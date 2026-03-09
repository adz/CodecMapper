namespace CodecMapper.Benchmarks

open System
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Configs
open BenchmarkDotNet.Jobs
open BenchmarkDotNet.Running
open BenchmarkDotNet.Toolchains.InProcess.Emit

[<MemoryDiagnoser>]
type CompetitiveBenchmarks() =
    let person =
        { Id = 42
          Name = "Benchmark User"
          Home =
            { Street = "123 F# Way"
              City = "AOT City" } }

    let json =
        "{\"Home\":{\"City\":\"AOT City\",\"Street\":\"123 F# Way\"},\"Id\":42,\"Name\":\"Benchmark User\"}"

    let jsonBytes = System.Text.Encoding.UTF8.GetBytes(json)

    // --- Single Object ---

    [<Benchmark(Baseline = true)>]
    member _.STJ_Json_Serialize() = StjBench.serialize person

    [<Benchmark>]
    member _.CodecMapper_Json_Serialize() = CodecMapperBench.serialize person

    [<Benchmark>]
    member _.Newtonsoft_Json_Serialize() = NewtonsoftBench.serialize person

    [<Benchmark>]
    member _.STJ_Json_Deserialize() = StjBench.deserialize<Person> (json)

    [<Benchmark>]
    member _.CodecMapper_Json_Deserialize_Bytes() =
        CodecMapperBench.deserializeBytes jsonBytes

    [<Benchmark>]
    member _.Newtonsoft_Json_Deserialize() =
        NewtonsoftBench.deserialize<Person> (json)

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
