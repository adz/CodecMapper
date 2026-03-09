# CodecMapper

<p align="center">
  <img src="docs/img/logo.png" alt="CodecMapper Logo" width="200" />
</p>

![CI Status](https://github.com/adz/CodecMapper/actions/workflows/dotnet.yml/badge.svg)
![Version](https://img.shields.io/badge/version-0.1.0--preview-blue)

Define mapping rules once. Use them for both decode and encode.

CodecMapper helps when your domain model and wire payloads do not match perfectly, but you still want:
- one canonical domain type
- explicit mapping rules you can reason about
- no reflection-heavy runtime magic
- Native AOT-friendly behavior

Today, CodecMapper ships with a high-performance JSON runner (`JsonRunner`), an object projection runner (`ObjectRunner`), and a dictionary runner (`DictionaryRunner`), all powered by the same codec definitions.

## Why this is useful

Most integration code drifts because teams maintain two separate implementations:
- parse wire -> domain
- serialize domain -> wire

CodecMapper keeps those in one place. That reduces regressions, DTO sprawl, and "we forgot to update the serializer" incidents.

It is particularly useful when you need to:
- keep a stable domain model while wire formats evolve
- support multiple external contracts for the same domain type
- normalize messy incoming data while still emitting clean outbound payloads

## Documentation

- [Getting Started Guide](./docs/GETTING_STARTED.md)
- [API Reference (GitHub Pages)](https://adz.github.io/CodecMapper/reference/index.html)
- [JSON Scenarios & Examples](./docs/examples/json-scenarios.md)

To view documentation locally with working links:
```bash
./scripts/serve-docs.sh
```
Then open [http://localhost:8080](http://localhost:8080).

## Benchmark snapshot (1000-record nested list)

As of **March 8, 2026**, measured with `BenchmarkDotNet` `ShortRun` on:
- Fedora Linux 43
- Intel Core i5-10310U
- .NET SDK 10.0.103 / Runtime 10.0.3

Payload benchmarked: `Person list` with 1000 records, nested `Home` record, and `Tags` array.

### Decode (means sorted ascending)

| Method | Mean (CPU) | Allocated | Gen0 / Gen1 / Gen2 |
| :--- | ---: | ---: | ---: |
| **CodecMapper_Decode_Stream** | **1.187 ms** | **682.28 KB** | 111.3281 / 109.3750 / - |
| SystemTextJson_Decode | 1.367 ms | 696.40 KB | 160.1563 / 66.4063 / - |
| **CodecMapper_Decode_Doc** | **1.527 ms** | **682.35 KB** | 111.3281 / 109.3750 / - |
| Newtonsoft_Decode | 2.168 ms | 1,421.16 KB | 339.8438 / 144.5313 / - |
| FSharpData_Decode | 2.870 ms | 2,777.75 KB | 546.8750 / 339.8438 / - |
| Farse_Decode | 2.965 ms | 2,484.54 KB | 402.3438 / 398.4375 / - |
| FSharpSystemTextJson_Decode | 3.204 ms | 656.86 KB | 140.6250 / 70.3125 / - |
| ThothJsonNet_Decode | 8.721 ms | 5,232.51 KB | 859.3750 / 843.7500 / 156.2500 |
| Fleece_Decode | 37.683 ms | 21,601.65 KB | 5307.6923 / 923.0769 / 384.6154 |

### Encode (means sorted ascending)

| Method | Mean (CPU) | Allocated | Gen0 / Gen1 / Gen2 |
| :--- | ---: | ---: | ---: |
| SystemTextJson_Encode | 685.0 μs | 266.86 KB | 83.0078 / 83.0078 / 83.0078 |
| **CodecMapper_Encode** | **694.2 μs** | **637.81 KB** | 166.0156 / 166.0156 / 166.0156 |
| Newtonsoft_Encode | 1,140.3 μs | 696.62 KB | 111.3281 / 82.0313 / 82.0313 |
| FSharpSystemTextJson_Encode | 1,191.6 μs | 633.75 KB | 164.0625 / 82.0313 / 82.0313 |
| FSharpData_Encode | 2,015.9 μs | 2,291.22 KB | 332.0313 / 324.2188 / 140.6250 |
| Farse_Encode | 2,033.9 μs | 2,798.50 KB | 496.0938 / 332.0313 / 82.0313 |
| ThothJsonNet_Encode | 5,675.9 μs | 4,689.41 KB | 789.0625 / 656.2500 / 78.1250 |
| Fleece_Encode | 22,098.4 μs | 13,091.13 KB | 2062.5000 / 750.0000 / 406.2500 |

Notes:
- These are `ShortRun` results (directional, not publication-grade precision).
- Results are machine/runtime dependent.
- `Fleece` restores via legacy framework assets on `net10.0`; keep that in mind when interpreting results.
- The benchmark project runs decode and encode suites separately and sorts the tables by `Mean` via `DefaultOrderer(SummaryOrderPolicy.FastestToSlowest)`.

To rerun:

```bash
dotnet run -c Release --project benchmarks/CodecMapper.Benchmarks/CodecMapper.Benchmarks.fsproj
```
