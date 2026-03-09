module CodecMapper.CompareRunner

open System
open System.Diagnostics
open System.Text
open System.Text.Json
open Newtonsoft.Json
open CodecMapper
open CodecMapper.Core
open CodecMapper.LegacyShim

module CurrentJson = CodecMapper.Json

type CurrentAddress = { City: string; PostCode: string }

type CurrentPerson =
    {
        Id: int
        Name: string
        IsActive: bool
        Score: float
        Home: CurrentAddress
        Tags: string array
    }

type SharedAddress = { City: string; PostCode: string }

type SharedPerson =
    {
        Id: int
        Name: string
        IsActive: bool
        Score: float
        Home: SharedAddress
        Tags: string array
    }

let makeCurrentAddress city postCode : CurrentAddress = { City = city; PostCode = postCode }

let makeCurrentPerson id name isActive score home tags : CurrentPerson =
    {
        Id = id
        Name = name
        IsActive = isActive
        Score = score
        Home = home
        Tags = tags
    }

module CurrentSchemas =
    let address =
        Schema.define<CurrentAddress>
        |> Schema.construct makeCurrentAddress
        |> Schema.field "City" _.City
        |> Schema.field "PostCode" _.PostCode
        |> Schema.build

    let person =
        Schema.define<CurrentPerson>
        |> Schema.construct makeCurrentPerson
        |> Schema.field "Id" _.Id
        |> Schema.field "Name" _.Name
        |> Schema.field "IsActive" _.IsActive
        |> Schema.field "Score" _.Score
        |> Schema.fieldWith "Home" _.Home address
        |> Schema.field "Tags" _.Tags
        |> Schema.build

    let people = Schema.list person

module Payload =
    let sharedPeople: SharedPerson list =
        [ for i in 1 .. 1000 ->
              {
                  Id = i
                  Name = $"User-{i}"
                  IsActive = i % 2 = 0
                  Score = float i * 1.25
                  Home =
                    {
                        City = "Adelaide"
                        PostCode = sprintf "50%02d" (i % 100)
                    }
                  Tags = [| "alpha"; "beta"; $"tag-{i % 10}" |]
              } ]

    let currentPeople: CurrentPerson list =
        sharedPeople
        |> List.map (fun person ->
            {
                Id = person.Id
                Name = person.Name
                IsActive = person.IsActive
                Score = person.Score
                Home =
                    {
                        City = person.Home.City
                        PostCode = person.Home.PostCode
                    }
                Tags = Array.copy person.Tags
            })

    let legacyPeople = Fixtures.createPeople 1000

    let stjOptions = JsonSerializerOptions()
    let json : string = System.Text.Json.JsonSerializer.Serialize(sharedPeople, stjOptions)
    let jsonBytes : byte[] = Encoding.UTF8.GetBytes(json)

module Bench =
    let private currentCodec = CurrentJson.compile CurrentSchemas.people

    let currentEncode () = CurrentJson.serialize currentCodec Payload.currentPeople
    let currentDecodeBytes () = CurrentJson.deserializeBytes currentCodec Payload.jsonBytes

    let legacyEncode () = LegacyJson.encodePeople Payload.legacyPeople
    let legacyDecodeDoc () = LegacyJson.decodePeopleDoc Payload.json
    let legacyDecodeStream () = LegacyJson.decodePeopleStream Payload.jsonBytes

    let stjEncode () = System.Text.Json.JsonSerializer.Serialize(Payload.sharedPeople, Payload.stjOptions)

    let stjDecode () =
        System.Text.Json.JsonSerializer.Deserialize<SharedPerson list>(Payload.json, Payload.stjOptions)

    let newtonsoftEncode () = JsonConvert.SerializeObject(Payload.sharedPeople)
    let newtonsoftDecode () = JsonConvert.DeserializeObject<SharedPerson list>(Payload.json)

type Measurement =
    {
        MeanNs: float
        MeanAllocBytes: float
    }

module Runner =
    let private measure iterations rounds action guard =
        let rec loop round timeTotal allocTotal sinkSeed =
            if round = rounds then
                {
                    MeanNs = timeTotal / float rounds
                    MeanAllocBytes = allocTotal / float rounds
                }
            else
                GC.Collect()
                GC.WaitForPendingFinalizers()
                GC.Collect()

                let beforeAlloc = GC.GetAllocatedBytesForCurrentThread()
                let sw = Stopwatch.StartNew()
                let mutable sink = sinkSeed

                for _ in 1 .. iterations do
                    sink <- sink ^^^ guard (action ())

                sw.Stop()
                let afterAlloc = GC.GetAllocatedBytesForCurrentThread()

                let elapsedNs = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / float iterations
                let allocBytes = float (afterAlloc - beforeAlloc) / float iterations

                loop (round + 1) (timeTotal + elapsedNs) (allocTotal + allocBytes) sink

        loop 0 0.0 0.0 0

    let private hashString (value: string) = value.Length

    let private hashSharedList (value: SharedPerson list) =
        value.Length ^^^ value.Head.Id ^^^ value.Head.Home.City.Length

    let private hashCurrentList (value: CurrentPerson list) =
        value.Length ^^^ value.Head.Id ^^^ value.Head.Home.City.Length

    let private hashLegacyResult (value: Result<LegacyPerson list, string>) =
        match value with
        | Ok people -> people.Length ^^^ people.Head.Id ^^^ people.Head.Home.City.Length
        | Error message -> failwith message

    let private hashSharedOption (value: SharedPerson list) =
        if isNull (box value) then
            failwith "Unexpected null decode result"

        hashSharedList value

    let run () =
        printfn "Shared comparison benchmark summary"
        printfn "Payload: 1000-record nested list with bool, float, and string array fields."
        printfn "Machine-specific numbers. Compare relative behavior on this machine."
        printfn ""

        let results =
            [ "STJ encode", measure 1000 5 Bench.stjEncode hashString
              "Current CodecMapper encode", measure 1000 5 Bench.currentEncode hashString
              "Legacy CodecMapper encode", measure 1000 5 Bench.legacyEncode hashString
              "Newtonsoft encode", measure 500 5 Bench.newtonsoftEncode hashString
              "STJ decode", measure 1000 5 Bench.stjDecode hashSharedOption
              "Current CodecMapper decode bytes", measure 1000 5 Bench.currentDecodeBytes hashCurrentList
              "Legacy CodecMapper decode doc", measure 1000 5 Bench.legacyDecodeDoc hashLegacyResult
              "Legacy CodecMapper decode stream", measure 1000 5 Bench.legacyDecodeStream hashLegacyResult
              "Newtonsoft decode", measure 500 5 Bench.newtonsoftDecode hashSharedOption ]

        for (name, result) in results do
            printfn "%s | %.1f ns/op | %.1f B/op" name result.MeanNs result.MeanAllocBytes

        0

module Program =
    [<EntryPoint>]
    let main _ = Runner.run ()
