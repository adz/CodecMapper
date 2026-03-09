namespace CodecMapper.Benchmarks.Runner

open System
open System.Diagnostics
open System.Text
open Newtonsoft.Json
open CodecMapper

type Address = { Street: string; City: string }

type Person =
    { Id: int; Name: string; Home: Address }

module Schemas =
    let address =
        Schema.define<Address>
        |> Schema.construct (fun street city -> { Street = street; City = city })
        |> Schema.field "Street" _.Street
        |> Schema.field "City" _.City
        |> Schema.build

    let person =
        Schema.define<Person>
        |> Schema.construct (fun id name home -> { Id = id; Name = name; Home = home })
        |> Schema.field "Id" _.Id
        |> Schema.field "Name" _.Name
        |> Schema.fieldWith "Home" _.Home address
        |> Schema.build

module Bench =
    let private stjOptions = System.Text.Json.JsonSerializerOptions()
    let private cmapCodec = Json.compile Schemas.person

    let person =
        { Id = 42
          Name = "Benchmark User"
          Home =
            { Street = "123 F# Way"
              City = "AOT City" } }

    let json =
        "{\"Home\":{\"City\":\"AOT City\",\"Street\":\"123 F# Way\"},\"Id\":42,\"Name\":\"Benchmark User\"}"

    let jsonBytes = Encoding.UTF8.GetBytes(json)

    let stjSerialize () =
        System.Text.Json.JsonSerializer.Serialize(person, stjOptions)

    let cmapSerialize () = Json.serialize cmapCodec person
    let newtonsoftSerialize () = JsonConvert.SerializeObject(person)

    let stjDeserialize () =
        System.Text.Json.JsonSerializer.Deserialize<Person>(json, stjOptions)

    let cmapDeserializeBytes () =
        Json.deserializeBytes cmapCodec jsonBytes

    let newtonsoftDeserialize () =
        JsonConvert.DeserializeObject<Person>(json)

type Measurement =
    { MeanNs: float; MeanAllocBytes: float }

module Runner =
    let private measure iterations rounds action guard =
        let rec loop round timeTotal allocTotal sinkSeed =
            if round = rounds then
                { MeanNs = timeTotal / float rounds
                  MeanAllocBytes = allocTotal / float rounds }
            else
                GC.Collect()
                GC.WaitForPendingFinalizers()
                GC.Collect()

                let beforeAlloc = GC.GetAllocatedBytesForCurrentThread()
                let sw = Stopwatch.StartNew()
                let mutable sink = sinkSeed

                for _ in 1..iterations do
                    sink <- sink ^^^ guard (action ())

                sw.Stop()
                let afterAlloc = GC.GetAllocatedBytesForCurrentThread()

                let elapsedNs = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / float iterations

                let allocBytes = float (afterAlloc - beforeAlloc) / float iterations

                loop (round + 1) (timeTotal + elapsedNs) (allocTotal + allocBytes) sink

        loop 0 0.0 0.0 0

    let private hashString (value: string) = value.Length

    let private hashPerson (value: Person) =
        value.Id ^^^ value.Name.Length ^^^ value.Home.City.Length

    let run () =
        printfn "Manual Release benchmark summary"
        printfn "Machine-specific numbers. Compare ratios more than absolutes."
        printfn ""

        let results =
            [ "STJ serialize", measure 200000 5 Bench.stjSerialize hashString
              "CodecMapper serialize", measure 200000 5 Bench.cmapSerialize hashString
              "Newtonsoft serialize", measure 100000 5 Bench.newtonsoftSerialize hashString
              "STJ deserialize", measure 200000 5 Bench.stjDeserialize hashPerson
              "CodecMapper deserialize bytes", measure 200000 5 Bench.cmapDeserializeBytes hashPerson
              "Newtonsoft deserialize", measure 100000 5 Bench.newtonsoftDeserialize hashPerson ]

        for (name, result) in results do
            printfn "%s | %.1f ns/op | %.1f B/op" name result.MeanNs result.MeanAllocBytes

        0

module Program =
    [<EntryPoint>]
    let main _ = Runner.run ()
