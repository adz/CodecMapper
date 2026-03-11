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
    /// The benchmark matrix covers both common request-sized objects and
    /// larger batch-style payloads so one ratio does not dominate the story.
    [<ParamsSource("ScenarioNames")>]
    member val ScenarioName = "" with get, set

    member _.ScenarioNames = Workloads.names

    member private this.Current =
        match Workloads.tryFind this.ScenarioName with
        | Some workload -> workload
        | None -> failwithf "Unknown benchmark scenario '%s'." this.ScenarioName

    [<Benchmark(Baseline = true)>]
    member this.STJ_Json_Serialize() = this.Current.StjSerialize()

    [<Benchmark>]
    member this.CodecMapper_Json_Serialize() = this.Current.CodecMapperSerialize()

    [<Benchmark>]
    member this.Newtonsoft_Json_Serialize() = this.Current.NewtonsoftSerialize()

    [<Benchmark>]
    member this.STJ_Json_Deserialize() = this.Current.StjDeserialize()

    [<Benchmark>]
    member this.CodecMapper_Json_Deserialize_Bytes() =
        this.Current.CodecMapperDeserializeBytes()

    [<Benchmark>]
    member this.Newtonsoft_Json_Deserialize() = this.Current.NewtonsoftDeserialize()

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
