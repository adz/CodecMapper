namespace CodecMapper.Benchmarks

open System
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Configs
open BenchmarkDotNet.Jobs
open BenchmarkDotNet.Running
open BenchmarkDotNet.Toolchains.InProcess.Emit

[<MemoryDiagnoser>]
type CompetitiveBenchmarks() =
    ///
    /// Benchmarking `100` records amortizes fixed serializer overhead and puts
    /// the comparisons closer to the payload sizes users actually send around.
    let people =
        [ 1..100 ]
        |> List.map (fun id -> {
            Id = id
            Name = $"Benchmark User {id}"
            Home = {
                Street = $"{id} F# Way"
                City = if id % 2 = 0 then "AOT City" else "Fable Town"
            }
        })

    let json = System.Text.Json.JsonSerializer.Serialize(people)

    let jsonBytes = System.Text.Encoding.UTF8.GetBytes(json)

    // --- Batch of 100 records ---

    [<Benchmark(Baseline = true)>]
    member _.STJ_Json_Serialize() = StjBench.serialize people

    [<Benchmark>]
    member _.CodecMapper_Json_Serialize() = CodecMapperBench.serialize people

    [<Benchmark>]
    member _.Newtonsoft_Json_Serialize() = NewtonsoftBench.serialize people

    [<Benchmark>]
    member _.STJ_Json_Deserialize() =
        StjBench.deserialize<Person list> (json)

    [<Benchmark>]
    member _.CodecMapper_Json_Deserialize_Bytes() =
        CodecMapperBench.deserializeBytes jsonBytes

    [<Benchmark>]
    member _.Newtonsoft_Json_Deserialize() =
        NewtonsoftBench.deserialize<Person list> (json)

module Program =
    [<EntryPoint>]
    let main argv =
        ///
        /// The archived experimental repo under `benchmarks/` contains another
        /// benchmark project with the same filename, which breaks the default
        /// child-project toolchain. Running in-process keeps BDN usable here
        /// without depending on repo layout.
        Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

        let config =
            ManualConfig
                .Create(DefaultConfig.Instance)
                .AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance))

        BenchmarkRunner.Run<CompetitiveBenchmarks>(config) |> ignore
        0
