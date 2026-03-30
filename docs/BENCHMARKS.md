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

These numbers were measured locally on March 31, 2026 with:

```bash
dotnet run -c Release --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj
```

## Snapshot

| Scenario | CodecMapper serialize | STJ serialize | Newtonsoft serialize | CodecMapper deserialize | STJ deserialize | Newtonsoft deserialize | Brief explanation |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `small-message` | `526.5 ns` | `715.0 ns` | `1022.0 ns` | `641.6 ns` | `907.5 ns` | `1646.7 ns` | `CodecMapper` still wins both directions on the tiny-message case. |
| `person-batch-25` | `8.33 us` | `7.37 us` | `13.66 us` | `24.29 us` | `18.22 us` | `24.88 us` | Medium nested workloads still trail `STJ`, but remain comfortably ahead of `Newtonsoft.Json`. |
| `person-batch-250` | `84.95 us` | `69.09 us` | `117.13 us` | `229.36 us` | `168.44 us` | `246.47 us` | Larger nested batches still trail `STJ`, but stay ahead of `Newtonsoft.Json`. |
| `escaped-articles-20` | `43.45 us` | `30.85 us` | `47.34 us` | `98.00 us` | `59.33 us` | `73.78 us` | String-heavy payloads remain a clear weak spot, especially on decode. |
| `telemetry-500` | `413.04 us` | `298.93 us` | `554.83 us` | `516.87 us` | `513.80 us` | `921.54 us` | Numeric-heavy decode is effectively tied with `STJ` on this run, while serialize still trails. |
| `person-batch-25-unknown-fields` | `9.05 us` | `7.85 us` | `13.13 us` | `30.93 us` | `25.00 us` | `47.76 us` | Unknown-field decode is still in range, but not especially close to `STJ`. |

## Current reading

- `CodecMapper` still wins the tiny-message case.
- `System.Text.Json` still leads on most medium-to-large serialize and decode workloads.
- `Newtonsoft.Json` is slower across the whole current matrix.
- Decode on string-heavy payloads is still the most obvious gap, while numeric-heavy decode is now roughly at parity with `STJ` on this machine.

## Why `CodecMapper` is still not faster than `STJ` overall

- `System.Text.Json` still has the edge on the large hot loops that dominate these scenarios: UTF-8 string escaping, object-property writing, and bulk array traversal.
- `CodecMapper` now has a strong specialized record-decode path for common authored JSON records, but it still spends more work than `STJ` on per-field dispatch and general composability hooks.
- The current wins line up with that implementation shape: the smallest message case is excellent, numeric-heavy decode can reach parity, but string-heavy and serialize-heavy scenarios still favor `STJ`.
- The benchmark matrix does not point to one remaining giant bottleneck. It points to several medium costs where `STJ` has lower-level primitives and less abstraction overhead in the hottest loops.

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
