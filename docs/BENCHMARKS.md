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

These numbers were measured locally on March 11, 2026 with:

```bash
dotnet run -c Release --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj
```

## Snapshot

| Scenario | CodecMapper serialize | STJ serialize | Newtonsoft serialize | CodecMapper deserialize | STJ deserialize | Newtonsoft deserialize | Brief explanation |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `small-message` | `3.0 us` | `3.6 us` | `6.7 us` | `6.9 us` | `5.2 us` | `11.5 us` | `CodecMapper` wins tiny-message serialize, while `STJ` still leads decode. |
| `person-batch-25` | `76.1 us` | `68.5 us` | `130.1 us` | `152.2 us` | `152.5 us` | `150.2 us` | Medium nested decode is effectively even; serialize remains close. |
| `person-batch-250` | `436.0 us` | `386.9 us` | `670.5 us` | `1.303 ms` | `1.074 ms` | `1.627 ms` | Larger nested batches are still competitive, but `STJ` has the throughput lead. |
| `escaped-articles-20` | `236.4 us` | `192.9 us` | `288.0 us` | `410.7 us` | `325.8 us` | `404.9 us` | String-heavy payloads are a clear weak spot today. |
| `telemetry-500` | `1.984 ms` | `1.609 ms` | `2.814 ms` | `3.981 ms` | `2.810 ms` | `5.205 ms` | Numeric-heavy payloads still need real optimization work, especially on decode. |
| `person-batch-25-unknown-fields` | `40.4 us` | `39.3 us` | `68.9 us` | `158.9 us` | `129.4 us` | `273.9 us` | Unknown-field decode improved, but `STJ` still has a noticeable lead. |

## Current reading

- `CodecMapper` is already competitive on small messages and medium nested-record contracts.
- `System.Text.Json` still leads on string-heavy and numeric-heavy workloads.
- `Newtonsoft.Json` is slower across the whole current matrix.
- Decode on wider numeric and string-heavy payloads is still the most obvious performance gap.

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
