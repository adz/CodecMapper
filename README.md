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

That same authored path also covers explicit tagged unions, string-valued enums, message envelopes, and recursive case trees through `Schema.union`, `Schema.inlineUnion`, `Schema.envelope`, `Schema.stringEnum`, and `Schema.delay`.

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

Authored tagged unions stay on that same contract path instead of switching to a separate codegen or reflection model.

The core library stays focused on explicit schemas and handwritten runtimes. The separate bridge assembly exists for `.NET` interoperability with existing C# serializer contracts.

## Start here

- [Introduction](docs/INTRODUCTION.md)
- [Getting Started](docs/GETTING_STARTED.md)
- [How To Model A Basic Record](docs/HOW_TO_MODEL_A_BASIC_RECORD.md)
- [How To Model A Nested Record](docs/HOW_TO_MODEL_A_NESTED_RECORD.md)
- [How To Model A Validated Wrapper](docs/HOW_TO_MODEL_A_VALIDATED_WRAPPER.md)
- [How To Model A Recursive Tagged Union](docs/HOW_TO_MODEL_A_RECURSIVE_TAGGED_UNION.md)

Use these after the core authored path is clear:

- [How To Import Existing C# Contracts](docs/HOW_TO_IMPORT_CSHARP_CONTRACTS.md)
- [How To Export JSON Schema](docs/HOW_TO_EXPORT_JSON_SCHEMA.md)
- [Tagged Union Wire Shape Reference](docs/TAGGED_UNION_REFERENCE.md)
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

Latest local manual snapshot, measured on March 16, 2026:

| Scenario | CodecMapper serialize | STJ serialize | CodecMapper deserialize | STJ deserialize | Takeaway |
| --- | ---: | ---: | ---: | ---: | --- |
| `small-message` | `519.5 ns` | `676.9 ns` | `990.1 ns` | `928.4 ns` | `CodecMapper` still wins tiny-message serialize; `STJ` keeps a slight decode lead. |
| `person-batch-25` | `8.83 us` | `8.36 us` | `26.08 us` | `20.41 us` | Medium nested serialize stays close, but decode is not yet even. |
| `person-batch-250` | `86.93 us` | `78.18 us` | `247.16 us` | `190.27 us` | Larger nested batches remain competitive on serialize, while `STJ` leads decode throughput. |
| `escaped-articles-20` | `46.00 us` | `33.87 us` | `80.78 us` | `63.08 us` | String-heavy payloads are still a clear weak spot. |
| `telemetry-500` | `393.93 us` | `311.45 us` | `745.63 us` | `520.84 us` | Numeric-heavy flat payloads still need significant optimization work, especially on decode. |
| `person-batch-25-unknown-fields` | `7.92 us` | `7.51 us` | `30.50 us` | `24.23 us` | Unknown-field decode improved, but `STJ` still holds a noticeable lead. |

Those numbers are machine-specific. Compare ratios and workload shape more than the absolute values.
