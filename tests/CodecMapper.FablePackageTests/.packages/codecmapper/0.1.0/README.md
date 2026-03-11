# CodecMapper

[![CI](https://github.com/adz/CodecMapper/actions/workflows/ci.yml/badge.svg)](https://github.com/adz/CodecMapper/actions/workflows/ci.yml)
[![Docs](https://img.shields.io/badge/docs-GitHub%20Pages-0a7ea4)](https://adz.github.io/CodecMapper/)
[![License](https://img.shields.io/github/license/adz/CodecMapper)](LICENSE.md)
[![Issues](https://img.shields.io/github/issues/adz/CodecMapper)](https://github.com/adz/CodecMapper/issues)
[![Last Commit](https://img.shields.io/github/last-commit/adz/CodecMapper)](https://github.com/adz/CodecMapper/commits/main)
[![Stars](https://img.shields.io/github/stars/adz/CodecMapper?style=social)](https://github.com/adz/CodecMapper/stargazers)

CodecMapper is a schema-first serialization library for F# focused on explicit wire contracts,
symmetric encode/decode behavior, and portability to Native AOT and Fable-style targets.

It's for cases where serializer attributes and implicit conventions stop being helpful. 
You define one schema that mirrors the wire shape, then compile it into reusable codecs.

## Why the schema feels different

```fsharp
open CodecMapper
open CodecMapper.Schema

type Address = { Street: string; City: string }
let makeAddress street city = { Street = street; City = city }

type Person = { Id: int; Name: string; Home: Address }
let makePerson id name home = { Id = id; Name = name; Home = home }

let addressSchema =
    define<Address>
    |> construct makeAddress
    |> field "street" _.Street
    |> field "city" _.City
    |> build

let codec =
    define<Person>
    |> construct makePerson
    |> field "id" _.Id
    |> field "name" _.Name
    |> fieldWith "home" _.Home addressSchema
    |> Json.buildAndCompile
let person =
    {
        Id = 42
        Name = "Ada"
        Home = { Street = "Main"; City = "Adelaide" }
    }

let json = Json.serialize codec person
printfn "%s" json
// {"id":42,"name":"Ada","home":{"street":"Main","city":"Adelaide"}}

let decoded = Json.deserialize codec json
printfn "%A" decoded
// { Id = 42
//   Name = "Ada"
//   Home = { Street = "Main"
//            City = "Adelaide" } }
```

That schema reads almost like the data constructor:

- `Schema.define<Person>` says which value you are describing
- `Schema.construct makePerson` says how to rebuild it during decode
- each `Schema.field` names one wire field and points at the matching record field
- `Schema.fieldWith` says "this field has its own explicit child schema"

The result is not hidden serializer behavior. It is the contract itself, written in normal F#.

If the schema only exists inline at the end of the authoring pipeline, `Json.buildAndCompile`, `Xml.buildAndCompile`, `Yaml.buildAndCompile`, and `KeyValue.buildAndCompile` make that terminal step easier to scan. Keep `Json.compile personSchema` when the named schema is reused.

## Why use it

- The schema mirrors the data, so changes to the wire contract are visible in one place.
- Encode and decode come from the same definition, so drift is harder to introduce accidentally.
- `Json.compile` and `Xml.compile` reuse the same schema instead of making you maintain separate mappings.
- Domain refinement stays explicit through `Schema.map` and `Schema.tryMap` instead of being buried in serializer settings.
- Versioned message and config contracts stay deliberate because the wire shape is authored directly.

## Why not just use X?

`CodecMapper` is not trying to replace every serializer. It is for the cases where explicit contracts matter more than convention-driven convenience.

| Option | AOT | Fable | Style | Best fit |
| --- | --- | --- | --- | --- |
| `System.Text.Json` | Good with source generation | No | CLR-shape and attributes | General-purpose .NET serialization |
| `Newtonsoft.Json` | Weaker | No | CLR-shape and attributes | Flexible JSON-heavy .NET apps |
| `Thoth.Json` | N/A | Strong | Explicit JSON codecs | F# apps that want JSON-only explicit codecs |
| `Fleece` / `Chiron` | Varies | Limited | F# JSON mapping | F#-first JSON mapping |
| DTOs + manual mapping | Strong | Strong | Explicit but duplicated | Strict transport/domain separation |
| JSON Schema-first | Varies | Varies | External schema owned | Integrating with schema-owned systems |
| `CodecMapper` | Strong | Strong | Authored schema contract | Explicit message/config contracts across JSON/XML |

Use `System.Text.Json` when convention-based object serialization is enough. Use `CodecMapper` when you want the contract itself to be visible, reviewable, reusable, and stable across model evolution.

## Where it fits well

`CodecMapper` is strongest when the wire contract matters and you want it to stay explicit.

- Message contracts: define the payload shape once and keep changes visible in the schema.
- Config contracts: treat configuration as a versioned boundary instead of incidental object serialization.
- Domain refinement: use `Schema.map` and `Schema.tryMap` when the runtime model should be stronger than the serialized shape.

## How JSON Schema fits in

JSON Schema is a useful companion, but it is not the center of the library.

```text
                 external schema docs
                        ^
                        |
                 JsonSchema.generate
                        |
running app <-> Schema<'T> <-> Json.compile / Xml.compile
                        |
                 JsonSchema.import
                        |
                        v
             external schema-owned inputs
```

The authored `Schema<'T>` is the source of truth. You do not generate app code from JSON Schema in the normal path, and JSON Schema does not replace the schema DSL. CodecMapper sits in the middle: it drives the running codecs you use in the app, and it can also project outward to formal schema documents or receive external schema-owned contracts.

- Author normal `Schema<'T>` values first when you control the contract.
- Export JSON Schema from those authored contracts when other systems need a formal schema document.
- Import external JSON Schema into `Schema<JsonValue>` when you are receiving a dynamic or externally-owned contract.

That keeps the normal authored path simple while still giving you an integration story for external schema-driven systems.

For exact JSON Schema capabilities and fallback boundaries, see [JSON Schema support reference](docs/JSON_SCHEMA_SUPPORT.md) and [How to export JSON Schema](docs/HOW_TO_EXPORT_JSON_SCHEMA.md).

## When models evolve

One of the main benefits over convention-based serializers is that model evolution becomes explicit.

If your domain gets richer but the wire contract does not need to change yet, keep the same wire shape and refine it:

```fsharp
open CodecMapper.Schema

type UserId = UserId of int

module UserId =
    let create value =
        if value > 0 then Ok(UserId value)
        else Error "UserId must be positive"

    let value (UserId value) = value

type Account = { Id: UserId; Name: string }
let makeAccount id name = { Id = id; Name = name }

let userIdSchema =
    int
    |> tryMap UserId.create UserId.value

let accountSchema =
    define<Account>
    |> construct makeAccount
    |> fieldWith "id" _.Id userIdSchema
    |> field "name" _.Name
    |> build
```

The JSON contract is still:

```json
{"id":42,"name":"Ada"}
```

The in-memory model is stronger, but you did not need a second DTO type just to keep that contract stable.

If the wire contract really changes, the schema changes with it in one obvious place:

```fsharp
open CodecMapper.Schema

type PersonV2 = { Id: int; Name: string; Email: string option }
let makePersonV2 id name email = { Id = id; Name = name; Email = email }

let personV2Schema =
    define<PersonV2>
    |> construct makePersonV2
    |> field "id" _.Id
    |> field "name" _.Name
    |> field "email" _.Email
    |> build
```

That does not silently "pick up" the new field just because the record changed. You add it deliberately to the schema, so the contract review point is explicit.

Compared with DTO-heavy designs, the difference is:

- You still get an explicit wire contract.
- You do not automatically pay for duplicate transport types and mapping code.
- When you really do need a separate transport model, you can still introduce one on purpose instead of by default.

## What it covers

- Core schema DSL for explicit record, collection, option, and wrapper contracts in F#
- Reusable JSON and XML codecs compiled from the same schema
- Flat key/value projection for config and environment-style contracts
- A small YAML codec for config-style mappings, sequences, and scalars
- A thin C# facade for setter-bound schema authoring and codec compilation
- A handwritten parser/runtime in the core library rather than a thin wrapper over `System.Text.Json`
- Built-in support for common numeric, enum, string, boolean, GUID, time-based, and collection interop types
- Explicit field-policy helpers such as `Schema.missingAsNone`, `Schema.missingAsValue`, `Schema.nullAsValue`, `Schema.emptyCollectionAsValue`, and `Schema.emptyStringAsNone`
- Domain refinement through `Schema.map` and `Schema.tryMap`
- JSON Schema export from authored `Schema<'T>` contracts
- JSON Schema import into `Schema<JsonValue>` for external dynamic receive-side contracts
- Raw JSON fallback via `Schema.jsonValue` for shapes that do not lower cleanly into the normal schema subset
- .NET-only bridge importers for `System.Text.Json`, `Newtonsoft.Json`, and `DataContract`

## Compatibility

- Shared compatibility coverage lives in `tests/CodecMapper.CompatibilitySentinel`, with thin Native AOT and Fable shell apps under `tests/CodecMapper.AotTests` and `tests/CodecMapper.FableTests`.
- CI runs both the in-repo Fable sentinel and a packaged-consumer Fable transpilation check against the locally packed `CodecMapper` NuGet.
- The shared sentinel now includes selected invalid and out-of-range numeric cases, so the portability story covers failure behavior as well as happy-path round-trips.

## Performance Work

When benchmark numbers move, profile before changing the runtime. The repo now includes a repeatable `perf` workflow for the manual benchmark runner in [docs/HOW_TO_PROFILE_BENCHMARK_HOT_PATHS.md](docs/HOW_TO_PROFILE_BENCHMARK_HOT_PATHS.md).
- The contract bridge in `src/CodecMapper.Bridge` is `.NET`-only by design; the portable surface is the core schema/JSON/XML library in `src/CodecMapper`.

## Docs

- Start with [Getting started](docs/GETTING_STARTED.md).
- Use the [contract pattern index](docs/HOW_TO_MODEL_COMMON_CONTRACT_PATTERNS.md) when you need a quick jump page.
- Copy from [How to model a basic record](docs/HOW_TO_MODEL_A_BASIC_RECORD.md), [how to model a nested record](docs/HOW_TO_MODEL_A_NESTED_RECORD.md), [how to model a validated wrapper](docs/HOW_TO_MODEL_A_VALIDATED_WRAPPER.md), or [how to model a versioned contract](docs/HOW_TO_MODEL_A_VERSIONED_CONTRACT.md).
- Use [Configuration contracts guide](docs/CONFIG_CONTRACTS.md) for versioned config shapes.
- Use [How to export JSON Schema](docs/HOW_TO_EXPORT_JSON_SCHEMA.md) and [JSON Schema support reference](docs/JSON_SCHEMA_SUPPORT.md) for schema interchange.
- Use [How to import existing C# contracts](docs/HOW_TO_IMPORT_CSHARP_CONTRACTS.md) for the bridge/facade story.
- Browse the [API docs](https://adz.github.io/CodecMapper/).

## Benchmarks

`CodecMapper` still carries benchmark coverage and comparison runners. The published numbers should be read as machine-specific snapshots, not universal claims.

For quick local comparisons, use the manual Release runner:

```bash
dotnet run -c Release --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj
```

For BenchmarkDotNet output, use:

```bash
dotnet run -c Release --project benchmarks/CodecMapper.Benchmarks/CodecMapper.Benchmarks.fsproj
```

The benchmark suite compares `CodecMapper` JSON encode/decode against `System.Text.Json` and `Newtonsoft.Json` across a deterministic scenario matrix that covers small messages, nested-record batches, string-heavy payloads, numeric-heavy telemetry, and decode paths with ignored unknown fields.

<!-- benchmark-snapshot:start -->
Latest local manual scenario-matrix snapshot, measured on March 11, 2026.

The manual runner now covers six deterministic workloads:

- `small-message`: one shallow command-sized object
- `person-batch-25`: medium nested-record API-style batch
- `person-batch-250`: larger nested-record throughput batch
- `escaped-articles-20`: string-heavy records with escapes and nested authors
- `telemetry-500`: numeric-heavy objects with float, decimal, and wider integers
- `person-batch-25-unknown-fields`: receive-side decode with ignored extra fields

Headline observations from the latest local run:

- The latest optimization pass moved `CodecMapper` ahead on `small-message` serialize (`1.87 us` vs `2.25 us`) while keeping tiny-message decode in the same general range.
- `CodecMapper` stayed effectively even with `System.Text.Json` on `person-batch-25` deserialize (`94.6 us` vs `94.7 us`) and remained competitive on `person-batch-250` serialize (`390.8 us` vs `370.8 us`).
- `System.Text.Json` still leads on the string-heavy `escaped-articles-20` workload, especially on deserialize.
- `System.Text.Json` also still leads the largest numeric-heavy `telemetry-500` case, which means the JSON runtime still has meaningful throughput and allocation work left on wide numeric batches.
- The unknown-field decode path improved, but `System.Text.Json` still holds a modest lead on `person-batch-25-unknown-fields` deserialize (`125.8 us` vs `132.5 us`).
- Both `CodecMapper` and `System.Text.Json` stayed well ahead of `Newtonsoft.Json` across every workload in this local matrix.

These numbers came from:

```bash
dotnet run -c Release --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj
```
<!-- benchmark-snapshot:end -->

## Notes

- `Json.compile` is explicit by design. Compile once and reuse the resulting codec.
- The current benchmark numbers are machine-specific and published mainly as relative comparisons, not universal claims.
