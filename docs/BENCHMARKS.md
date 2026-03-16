# Benchmarks

This page tracks the current manual benchmark snapshot for `CodecMapper`.

Read these numbers as workload-shaped comparisons, not universal claims. They are useful for seeing where `CodecMapper` is already competitive and where the runtime still needs work.

## What this covers

The manual runner compares `CodecMapper` JSON encode and decode against:

- `System.Text.Json`
- `Newtonsoft.Json`

The current scenario matrix covers:

- `small-message`
- `person-batch-25`
- `person-batch-250`
- `escaped-articles-20`
- `telemetry-500`
- `person-batch-25-unknown-fields`

These numbers were measured locally on March 16, 2026 with:

```bash
dotnet run -c Release --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj
```

## Snapshot

| Scenario | CodecMapper serialize | STJ serialize | Newtonsoft serialize | CodecMapper deserialize | STJ deserialize | Newtonsoft deserialize | Brief explanation |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `small-message` | `519.5 ns` | `676.9 ns` | `1012.0 ns` | `990.1 ns` | `928.4 ns` | `1817.7 ns` | `CodecMapper` wins tiny-message serialize, while `STJ` still leads decode. |
| `person-batch-25` | `8.83 us` | `8.36 us` | `14.06 us` | `26.08 us` | `20.41 us` | `28.80 us` | Medium nested serialize remains close, but `STJ` holds a clearer decode lead than before. |
| `person-batch-250` | `86.93 us` | `78.18 us` | `125.44 us` | `247.16 us` | `190.27 us` | `277.88 us` | Larger nested batches are still competitive on serialize, but `STJ` has the throughput lead on decode. |
| `escaped-articles-20` | `46.00 us` | `33.87 us` | `49.79 us` | `80.78 us` | `63.08 us` | `78.27 us` | String-heavy payloads remain a clear weak spot, especially against `STJ`. |
| `telemetry-500` | `393.93 us` | `311.45 us` | `539.74 us` | `745.63 us` | `520.84 us` | `938.99 us` | Numeric-heavy payloads still need real optimization work, especially on decode. |
| `person-batch-25-unknown-fields` | `7.92 us` | `7.51 us` | `12.25 us` | `30.50 us` | `24.23 us` | `48.85 us` | Unknown-field decode improved, but `STJ` still has a noticeable lead. |

## Current reading

- `CodecMapper` is already competitive on small messages and stays reasonably close on medium nested-record serialize workloads.
- `System.Text.Json` still leads on string-heavy and numeric-heavy workloads.
- `Newtonsoft.Json` is slower across the whole current matrix.
- Decode on wider nested, numeric-heavy, and string-heavy payloads is still the most obvious performance gap.

## How to use this

- Use the manual runner for quick relative checks while iterating.
- Use the BenchmarkDotNet app when you need richer statistical output.
- Use the `perf` workflow when one scenario clearly regresses or becomes the dominant hot path.

Commands:

```bash
dotnet run -c Release --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj
dotnet run -c Release --project benchmarks/CodecMapper.Benchmarks/CodecMapper.Benchmarks.fsproj
```

For profiling guidance, see [How To Profile Benchmark Hot Paths](HOW_TO_PROFILE_BENCHMARK_HOT_PATHS.md).
