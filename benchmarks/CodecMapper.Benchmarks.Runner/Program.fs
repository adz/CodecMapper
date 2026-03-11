namespace CodecMapper.Benchmarks.Runner

open System
open System.Diagnostics
open CodecMapper.Benchmarks

type Measurement = { MeanNs: float; MeanAllocBytes: float }

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

                for _ in 1..iterations do
                    sink <- sink ^^^ guard (action ())

                sw.Stop()
                let afterAlloc = GC.GetAllocatedBytesForCurrentThread()

                let elapsedNs = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / float iterations
                let allocBytes = float (afterAlloc - beforeAlloc) / float iterations

                loop (round + 1) (timeTotal + elapsedNs) (allocTotal + allocBytes) sink

        loop 0 0.0 0.0 0

    type private ProfileOperation = Workloads.Workload -> unit -> int

    let private tryFindOperation operation : ProfileOperation option =
        match operation with
        | "stj-serialize" ->
            Some(fun (workload: Workloads.Workload) -> fun () -> workload.HashSerialized(workload.StjSerialize()))
        | "codecmapper-serialize" ->
            Some(fun (workload: Workloads.Workload) ->
                fun () -> workload.HashSerialized(workload.CodecMapperSerialize()))
        | "newtonsoft-serialize" ->
            Some(fun (workload: Workloads.Workload) ->
                fun () -> workload.HashSerialized(workload.NewtonsoftSerialize()))
        | "stj-deserialize" ->
            Some(fun (workload: Workloads.Workload) -> fun () -> workload.HashValue(workload.StjDeserialize()))
        | "codecmapper-deserialize-bytes" ->
            Some(fun (workload: Workloads.Workload) ->
                fun () -> workload.HashValue(workload.CodecMapperDeserializeBytes()))
        | "newtonsoft-deserialize" ->
            Some(fun (workload: Workloads.Workload) -> fun () -> workload.HashValue(workload.NewtonsoftDeserialize()))
        | _ -> None

    let private measureScenario (workload: Workloads.Workload) = [
        "STJ serialize", measure workload.SerializeIterations 3 workload.StjSerialize workload.HashSerialized
        "CodecMapper serialize",
        measure workload.SerializeIterations 3 workload.CodecMapperSerialize workload.HashSerialized
        "Newtonsoft serialize",
        measure (max 1 (workload.SerializeIterations / 2)) 3 workload.NewtonsoftSerialize workload.HashSerialized
        "STJ deserialize", measure workload.DeserializeIterations 3 workload.StjDeserialize workload.HashValue
        "CodecMapper deserialize bytes",
        measure workload.DeserializeIterations 3 workload.CodecMapperDeserializeBytes workload.HashValue
        "Newtonsoft deserialize",
        measure (max 1 (workload.DeserializeIterations / 2)) 3 workload.NewtonsoftDeserialize workload.HashValue
    ]

    let runAll () =
        printfn "Manual Release benchmark summary"
        printfn "Machine-specific numbers. Compare ratios more than absolutes."
        printfn ""

        for workload in Workloads.standard do
            printfn "Scenario: %s" workload.Name
            printfn "Description: %s" workload.Description
            printfn "Payload bytes: %d" workload.JsonSizeBytes
            printfn "Serialize iterations: %d" workload.SerializeIterations
            printfn "Deserialize iterations: %d" workload.DeserializeIterations

            for (name, result) in measureScenario workload do
                printfn "%s | %.1f ns/op | %.1f B/op" name result.MeanNs result.MeanAllocBytes

            printfn ""

        0

    ///
    /// Profile mode keeps targeting one scenario and one operation so `perf`
    /// output stays focused instead of mixing unrelated payload shapes.
    let runProfile operation iterations scenarioName recordCount =
        let workloadResult =
            match scenarioName with
            | Some "person-batch-legacy" -> Ok(Workloads.createLegacyPersonBatch recordCount)
            | Some name ->
                match Workloads.tryFind name with
                | Some workload -> Ok workload
                | None ->
                    let known = String.concat ", " Workloads.names
                    Error($"Unknown scenario '{name}'. Expected one of: {known}")
            | None -> Ok(Workloads.createLegacyPersonBatch recordCount)

        match workloadResult, tryFindOperation operation with
        | Error message, _ ->
            eprintfn "%s" message
            1
        | _, None ->
            eprintfn "Unknown operation '%s'." operation

            eprintfn
                "Expected one of: stj-serialize, codecmapper-serialize, newtonsoft-serialize, stj-deserialize, codecmapper-deserialize-bytes, newtonsoft-deserialize"

            1
        | Ok workload, Some runOperation ->
            let action = runOperation workload

            for _ in 1..1000 do
                action () |> ignore

            GC.Collect()
            GC.WaitForPendingFinalizers()
            GC.Collect()

            let sw = Stopwatch.StartNew()
            let mutable sink = 0

            for _ in 1..iterations do
                sink <- sink ^^^ action ()

            sw.Stop()

            printfn "Profiled %s" operation
            printfn "Scenario: %s" workload.Name
            printfn "Payload bytes: %d" workload.JsonSizeBytes
            printfn "Iterations: %d" iterations
            printfn "Elapsed: %.3f ms" sw.Elapsed.TotalMilliseconds
            printfn "Sink: %d" sink
            0

module Cli =
    type Command =
        | Summary
        | Profile of operation: string * iterations: int * scenarioName: string option * recordCount: int

    let private parseInt optionName (value: string) =
        match Int32.TryParse(value) with
        | true, parsed when parsed > 0 -> Ok parsed
        | _ -> Error $"Expected {optionName} to be a positive integer, but got '{value}'."

    ///
    /// Summary mode now runs a fixed scenario matrix. `--records` remains a
    /// profile-only escape hatch for the legacy nested-record benchmark.
    let parse argv =
        let rec loop command args =
            match command, args with
            | Summary, [] -> Ok command
            | Profile _, [] -> Ok command
            | Profile(operation, iterations, scenarioName, recordCount), "--iterations" :: value :: tail ->
                match parseInt "--iterations" value with
                | Ok parsed -> loop (Profile(operation, parsed, scenarioName, recordCount)) tail
                | Error message -> Error message
            | Profile(operation, iterations, scenarioName, recordCount), "--records" :: value :: tail ->
                match parseInt "--records" value with
                | Ok parsed -> loop (Profile(operation, iterations, scenarioName, parsed)) tail
                | Error message -> Error message
            | Profile(operation, iterations, _, recordCount), "--scenario" :: value :: tail ->
                loop (Profile(operation, iterations, Some value, recordCount)) tail
            | Summary, [ "profile"; operation ] -> Ok(Profile(operation, 200000, None, 50))
            | Summary, "profile" :: operation :: tail -> loop (Profile(operation, 200000, None, 50)) tail
            | _, option :: _ -> Error $"Unknown arguments starting at '{option}'."

        loop Summary (argv |> Array.toList)

module Program =
    [<EntryPoint>]
    let main argv =
        match Cli.parse argv with
        | Error message ->
            eprintfn "%s" message
            1
        | Ok Cli.Summary -> Runner.runAll ()
        | Ok(Cli.Profile(operation, iterations, scenarioName, recordCount)) ->
            Runner.runProfile operation iterations scenarioName recordCount
