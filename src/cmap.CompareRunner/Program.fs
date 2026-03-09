module cmap.CompareRunner

open System
open System.Diagnostics
open System.Text
open System.Text.Json
open Newtonsoft.Json
open cmap
open CodecMapper.Core

module CmapJson = cmap.Json

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

type LegacyAddress = { City: string; PostCode: string }

type LegacyPerson =
    {
        Id: int
        Name: string
        IsActive: bool
        Score: float
        Home: LegacyAddress
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

let makeLegacyAddress city postCode : LegacyAddress = { City = city; PostCode = postCode }

let makeLegacyPerson id name isActive score home tags : LegacyPerson =
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

module LegacySchemas =
    let address =
        codec {
            construct makeLegacyAddress
            string "City" _.City
            string "PostCode" _.PostCode
        }

    let person =
        codec {
            construct makeLegacyPerson
            int "Id" _.Id
            string "Name" _.Name
            bool "IsActive" _.IsActive
            float "Score" _.Score
            linkVia (Codec.sub address) "Home" _.Home
            linkVia (Codec.array Codec.string) "Tags" _.Tags
        }

    let people = Codec.list person

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

    let legacyPeople: LegacyPerson list =
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

    let stjOptions = JsonSerializerOptions()
    let json : string = System.Text.Json.JsonSerializer.Serialize(sharedPeople, stjOptions)
    let jsonBytes : byte[] = Encoding.UTF8.GetBytes(json)

module Bench =
    let private currentCodec = CmapJson.compile CurrentSchemas.people

    let currentEncode () = CmapJson.serialize currentCodec Payload.currentPeople
    let currentDecodeBytes () = CmapJson.deserializeBytes currentCodec Payload.jsonBytes

    let legacyEncode () = CodecMapper.Core.JsonRunner.encodeString LegacySchemas.people Payload.legacyPeople

    let legacyDecodeDoc () =
        use doc = JsonDocument.Parse(Payload.json : string)
        CodecMapper.Core.JsonRunner.decodeDoc LegacySchemas.people doc.RootElement

    let legacyDecodeStream () =
        let mutable reader = Utf8JsonReader(Payload.jsonBytes)
        reader.Read() |> ignore
        CodecMapper.Core.JsonRunner.decodeReader LegacySchemas.people &reader

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
              "cmap encode", measure 1000 5 Bench.currentEncode hashString
              "CodecMapper encode", measure 1000 5 Bench.legacyEncode hashString
              "Newtonsoft encode", measure 500 5 Bench.newtonsoftEncode hashString
              "STJ decode", measure 1000 5 Bench.stjDecode hashSharedOption
              "cmap decode bytes", measure 1000 5 Bench.currentDecodeBytes hashCurrentList
              "CodecMapper decode doc", measure 1000 5 Bench.legacyDecodeDoc hashLegacyResult
              "CodecMapper decode stream", measure 1000 5 Bench.legacyDecodeStream hashLegacyResult
              "Newtonsoft decode", measure 500 5 Bench.newtonsoftDecode hashSharedOption ]

        for (name, result) in results do
            printfn "%s | %.1f ns/op | %.1f B/op" name result.MeanNs result.MeanAllocBytes

        0

module Program =
    [<EntryPoint>]
    let main _ = Runner.run ()
