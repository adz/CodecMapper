# CodecMapper

[![CI](https://github.com/adz/CodecMapper/actions/workflows/ci.yml/badge.svg)](https://github.com/adz/CodecMapper/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/CodecMapper.svg)](https://www.nuget.org/packages/CodecMapper/)
[![Docs](https://img.shields.io/badge/docs-GitHub%20Pages-0a7ea4)](https://adz.github.io/CodecMapper/)
[![License](https://img.shields.io/github/license/adz/CodecMapper)](LICENSE.md)

`CodecMapper` is a schema-first serialization library for F# with native AOT and Fable compatibility.

It lets you define one schema and compile it into multiple codecs. The same mapping drives both encode and decode, so JSON and XML stay symmetric.

It's for cases where the wire contract should be explicit, reviewable, and reusable instead of being inferred from CLR shape or serializer settings.

## The idea

You author one `Schema<'T>` that describes the wire shape:

```fsharp
open CodecMapper
open CodecMapper.Schema

type Person = { Id: int; Name: string }
let makePerson id name = { Id = id; Name = name }

let personSchema =
    define<Person>
    |> construct makePerson
    |> field "id" _.Id
    |> field "name" _.Name
    |> build
```

Then you compile that schema into a reusable codec:

```fsharp
let codec = Json.compile personSchema

let person = { Id = 1; Name = "Ada" }
let json = Json.serialize codec person
let decoded = Json.deserialize codec json
```

That is the core model of the library:

- the schema is the contract
- encode and decode come from the same definition
- contract changes stay visible in one place

## Why use it

`CodecMapper` fits when:

- you want the wire contract to be authored explicitly
- JSON and XML should stay symmetric
- domain refinement should be explicit with `Schema.map` or `Schema.tryMap`
- Native AOT and Fable compatibility matter

It is not trying to replace convention-based serializers for every use case.

## Formats and scope

The same authored schema can compile into:

- JSON codecs
- XML codecs
- config-oriented YAML codecs
- flat KeyValue projections

The core library stays focused on explicit schemas and handwritten runtimes. The separate bridge assembly exists for `.NET` interoperability with existing C# serializer contracts.

## Start here

- [Introduction](docs/INTRODUCTION.md)
- [Getting Started](docs/GETTING_STARTED.md)
- [How To Model A Basic Record](docs/HOW_TO_MODEL_A_BASIC_RECORD.md)
- [How To Model A Nested Record](docs/HOW_TO_MODEL_A_NESTED_RECORD.md)
- [How To Model A Validated Wrapper](docs/HOW_TO_MODEL_A_VALIDATED_WRAPPER.md)

Use these after the core authored path is clear:

- [How To Import Existing C# Contracts](docs/HOW_TO_IMPORT_CSHARP_CONTRACTS.md)
- [How To Export JSON Schema](docs/HOW_TO_EXPORT_JSON_SCHEMA.md)
- [JSON Schema in CodecMapper](docs/JSON_SCHEMA_EXPLANATION.md)
- [API Reference](https://adz.github.io/CodecMapper/reference/index.html)

## Compatibility

`CodecMapper` is designed to stay usable from Native AOT and Fable-oriented targets. CI includes both in-repo compatibility sentinels and packaged-consumer Fable checks.

## Performance Status

Current status is mixed but clear:

- `CodecMapper` is competitive on small messages and medium nested-record workloads.
- `System.Text.Json` still leads on string-heavy and numeric-heavy payloads.
- `Newtonsoft.Json` trails both across the current manual scenario matrix.

The project ships both a manual scenario runner and a repeatable `perf` workflow for hot-path investigation:

- manual runner: `dotnet run -c Release --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj`
- profiling guide: [docs/HOW_TO_PROFILE_BENCHMARK_HOT_PATHS.md](docs/HOW_TO_PROFILE_BENCHMARK_HOT_PATHS.md)
- full benchmark page: [docs/BENCHMARKS.md](docs/BENCHMARKS.md)

Latest local manual snapshot, measured on March 11, 2026:

| Scenario | CodecMapper serialize | STJ serialize | CodecMapper deserialize | STJ deserialize | Takeaway |
| --- | ---: | ---: | ---: | ---: | --- |
| `small-message` | `3.0 us` | `3.6 us` | `6.9 us` | `5.2 us` | `CodecMapper` wins serialize on tiny payloads; `STJ` still leads deserialize. |
| `person-batch-25` | `76.1 us` | `68.5 us` | `152.2 us` | `152.5 us` | Medium nested decode is effectively even; serialize remains close. |
| `person-batch-250` | `436.0 us` | `386.9 us` | `1.303 ms` | `1.074 ms` | Larger nested batches are still competitive, but `STJ` leads on throughput. |
| `escaped-articles-20` | `236.4 us` | `192.9 us` | `410.7 us` | `325.8 us` | String-heavy payloads are a clear weak spot today. |
| `telemetry-500` | `1.984 ms` | `1.609 ms` | `3.981 ms` | `2.810 ms` | Numeric-heavy flat payloads still need real optimization work. |
| `person-batch-25-unknown-fields` | `40.4 us` | `39.3 us` | `158.9 us` | `129.4 us` | Unknown-field decode improved, but `STJ` still holds a noticeable lead. |

Those numbers are machine-specific. Compare ratios and workload shape more than the absolute values.
