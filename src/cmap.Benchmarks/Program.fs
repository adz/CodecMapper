namespace cmap.Benchmarks

open System
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Running

[<MemoryDiagnoser>]
type CompetitiveBenchmarks() =
    let person = { 
        Id = 42
        Name = "Benchmark User"
        Home = { Street = "123 F# Way"; City = "AOT City" } 
    }
    let json = "{\"Home\":{\"City\":\"AOT City\",\"Street\":\"123 F# Way\"},\"Id\":42,\"Name\":\"Benchmark User\"}"
    let jsonBytes = System.Text.Encoding.UTF8.GetBytes(json)

    // --- Single Object ---

    [<Benchmark(Baseline = true)>]
    member _.STJ_Json_Serialize() = StjBench.serialize person

    [<Benchmark>]
    member _.Cmap_Json_Serialize() = CmapBench.serialize person

    [<Benchmark>]
    member _.Newtonsoft_Json_Serialize() = NewtonsoftBench.serialize person

    [<Benchmark>]
    member _.STJ_Json_Deserialize() = StjBench.deserialize<Person>(json)

    [<Benchmark>]
    member _.Cmap_Json_Deserialize_Bytes() = CmapBench.deserializeBytes jsonBytes

    [<Benchmark>]
    member _.Newtonsoft_Json_Deserialize() = NewtonsoftBench.deserialize<Person>(json)

module Program =
    [<EntryPoint>]
    let main args =
        BenchmarkRunner.Run<CompetitiveBenchmarks>() |> ignore
        0
