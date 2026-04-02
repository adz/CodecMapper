# CodecMapper

[![CI](https://github.com/adz/CodecMapper/actions/workflows/ci.yml/badge.svg)](https://github.com/adz/CodecMapper/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/CodecMapper.svg)](https://www.nuget.org/packages/CodecMapper/)
[![Docs](https://img.shields.io/badge/docs-GitHub%20Pages-0a7ea4)](https://adz.github.io/CodecMapper/)
[![License](https://img.shields.io/github/license/adz/CodecMapper)](LICENSE.md)

`CodecMapper` is a schema-first serialization library for F# with native AOT and Fable compatibility.

It lets you author one schema and compile it into multiple codecs. The same mapping drives both encode and decode, so JSON and XML stay symmetric.

It's for cases where the wire schema should be explicit, reviewable, and reusable instead of being inferred from CLR shape or serializer settings.

## The idea

You author one `Schema<'T>` that describes the wire shape:

```fsharp
open CodecMapper

type Person = { Id: int; Name: string }
let makePerson id name = { Id = id; Name = name }

let personSchema =
    Schema.record makePerson
    |> Schema.field "id" _.Id
    |> Schema.field "name" _.Name
    |> Schema.build
```

Then you compile that schema into a reusable format codec:

```fsharp
let codec = Json.compile personSchema

let person = { Id = 1; Name = "Ada" }
let json = Json.serialize codec person
let decoded = Json.deserialize codec json
```

That is the core model of the library:

- the schema is explicit code
- encode and decode come from the same definition
- schema changes stay visible in one place

That same authored path also covers explicit tagged unions, string-valued enums, message envelopes, and recursive case trees through `Tagged.union`, `Tagged.inlineUnion`, `Tagged.envelope`, `Schema.stringEnum`, and `Schema.delay`.

## Why use it

`CodecMapper` fits when:

- you want the wire schema to be authored explicitly
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

Authored tagged unions stay on that same schema path instead of switching to a separate codegen or reflection model.

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

- `CodecMapper` is strongest on the smallest message path and can match `STJ` on the current numeric-heavy decode case.
- `System.Text.Json` still leads on most medium-to-large serialize and decode workloads.
- `Newtonsoft.Json` trails both across the current manual scenario matrix.

The project ships both a manual scenario runner and a repeatable `perf` workflow for hot-path investigation:

- manual runner: `dotnet run -c Release --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj`
- profiling guide: [docs/HOW_TO_PROFILE_BENCHMARK_HOT_PATHS.md](docs/HOW_TO_PROFILE_BENCHMARK_HOT_PATHS.md)
- full benchmark page: [docs/BENCHMARKS.md](docs/BENCHMARKS.md)

Latest local manual snapshot, measured on April 2, 2026:

| Scenario | CodecMapper serialize | STJ serialize | CodecMapper deserialize | STJ deserialize | Takeaway |
| --- | ---: | ---: | ---: | ---: | --- |
| `small-message` | `441.8 ns` | `627.1 ns` | `644.6 ns` | `889.1 ns` | `CodecMapper` still leads both directions on the tiny-message case. |
| `person-batch-25` | `7.96 us` | `7.29 us` | `27.13 us` | `20.84 us` | Medium nested workloads still trail `STJ`, but remain ahead of `Newtonsoft.Json` on decode. |
| `person-batch-250` | `83.89 us` | `71.53 us` | `294.50 us` | `217.70 us` | Larger nested batches still trail `STJ`, and the decode gap widened on this run. |
| `escaped-articles-20` | `45.97 us` | `34.71 us` | `115.38 us` | `66.25 us` | String-heavy payloads remain a clear weak spot, especially on decode. |
| `telemetry-500` | `421.70 us` | `317.53 us` | `559.46 us` | `556.73 us` | Numeric-heavy decode is still roughly tied with `STJ`, while serialize trails. |
| `person-batch-25-unknown-fields` | `8.56 us` | `7.63 us` | `34.38 us` | `29.37 us` | Unknown-field decode is still in range, but not especially close to `STJ`. |

Those numbers are machine-specific. Compare ratios and workload shape more than the absolute values.
