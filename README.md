<h1>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/logo-mark.png">
    <source media="(prefers-color-scheme: light)" srcset="docs/logo-mark.png">
    <img src="docs/logo-mark.png" alt="CodecMapper logo" width="35" align="absmiddle">
  </picture>
  CodecMapper
</h1>

[![CI](https://github.com/adz/CodecMapper/actions/workflows/ci.yml/badge.svg)](https://github.com/adz/CodecMapper/actions/workflows/ci.yml)
[![Docs](https://img.shields.io/badge/docs-GitHub%20Pages-0a7ea4)](https://adz.github.io/CodecMapper/)
[![License](https://img.shields.io/github/license/adz/CodecMapper)](LICENSE.md)
[![Issues](https://img.shields.io/github/issues/adz/CodecMapper)](https://github.com/adz/CodecMapper/issues)
[![Last Commit](https://img.shields.io/github/last-commit/adz/CodecMapper)](https://github.com/adz/CodecMapper/commits/main)
[![Stars](https://img.shields.io/github/stars/adz/CodecMapper?style=social)](https://github.com/adz/CodecMapper/stargazers)

`CodecMapper` is a schema-first serialization library for F# focused on explicit wire contracts, symmetric encode/decode behavior, and execution that stays friendly to Native AOT and Fable-style targets.

It is for the cases where serializer attributes and implicit conventions stop being helpful: you want the wire shape to be visible in code, you want encode and decode to stay in sync, and you want the contract to read like the data it describes.

The core idea is simple: define one schema that mirrors your record shape, then compile it into reusable codecs.

That sits between two common extremes:

- Put serializer attributes directly on domain types and let wire concerns leak into the model.
- Introduce separate DTOs and mapping layers so the wire contract stays explicit, but now maintain extra types and conversion code.

`CodecMapper` keeps the useful part of the DTO approach, explicit contracts, without forcing a duplicate object model for every message shape. The schema is the contract, and it sits next to the data instead of behind a second translation layer.

## Why the schema feels different

```fsharp
open CodecMapper

type Address = { Street: string; City: string }
let makeAddress street city = { Street = street; City = city }

type Person = { Id: int; Name: string; Home: Address }
let makePerson id name home = { Id = id; Name = name; Home = home }

let addressSchema =
    Schema.define<Address>
    |> Schema.construct makeAddress
    |> Schema.field "street" _.Street
    |> Schema.field "city" _.City
    |> Schema.build

let personSchema =
    Schema.define<Person>
    |> Schema.construct makePerson
    |> Schema.field "id" _.Id
    |> Schema.field "name" _.Name
    |> Schema.fieldWith "home" _.Home addressSchema
    |> Schema.build

let codec = Json.compile personSchema
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

## Why this is useful

- The schema mirrors the data, so changes to the wire contract are visible in one place.
- Encode and decode come from the same definition, so drift is harder to introduce accidentally.
- `Json.compile` and `Xml.compile` reuse the same schema instead of making you maintain separate mappings.
- Domain refinement stays explicit through `Schema.map` and `Schema.tryMap` instead of being buried in serializer settings.
- Versioned message and config contracts stay deliberate because the wire shape is authored directly instead of inferred from whatever the current model happens to look like.
- Migration is easier to stage: keep the external contract stable, refine the in-memory domain behind `map` / `tryMap`, and only introduce DTOs when you genuinely need a separate transport model.

See [When models evolve](#when-models-evolve) for the concrete version of this tradeoff.

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

Other dimensions that matter:

- Format symmetry: `CodecMapper` is centered on one contract driving both JSON and XML, while most alternatives are JSON-only or serializer-specific.
- Model evolution: `CodecMapper` keeps contract edits explicit and supports domain refinement with `Schema.map` / `Schema.tryMap` before you reach for DTO duplication. See [When models evolve](#when-models-evolve).
- Tooling relationship: `CodecMapper` can export JSON Schema from the authored contract and also import external JSON Schema when you are adapting to another system.

If your question is "why not just use `System.Text.Json`?", the short answer is: use it when convention-based object serialization is enough. Use `CodecMapper` when you want the contract itself to be visible, reviewable, reusable, and stable across model evolution.

## Where it fits well

`CodecMapper` is strongest when the wire contract matters and you want it to stay explicit.

For message contracts:

- Define the exact payload shape once.
- Compile it into reusable codecs.
- Keep version changes visible in the schema instead of relying on serializer conventions.

For configuration contracts:

- Treat config as a real contract rather than as incidental object serialization.
- Keep migration and versioning logic deliberate.
- Use the schema as the stable boundary even if the in-memory domain gets richer over time.

For domain models:

- Keep the domain type close to the wire contract when that is useful.
- Use `Schema.map` and `Schema.tryMap` when the runtime model should be stronger than the serialized shape.
- Introduce separate DTOs only when the transport model genuinely needs to diverge.

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
type UserId = UserId of int

module UserId =
    let create value =
        if value > 0 then Ok(UserId value)
        else Error "UserId must be positive"

    let value (UserId value) = value

type Account = { Id: UserId; Name: string }
let makeAccount id name = { Id = id; Name = name }

let userIdSchema =
    Schema.int
    |> Schema.tryMap UserId.create UserId.value

let accountSchema =
    Schema.define<Account>
    |> Schema.construct makeAccount
    |> Schema.fieldWith "id" _.Id userIdSchema
    |> Schema.field "name" _.Name
    |> Schema.build
```

The JSON contract is still:

```json
{"id":42,"name":"Ada"}
```

The in-memory model is stronger, but you did not need a second DTO type just to keep that contract stable.

If the wire contract really changes, the schema changes with it in one obvious place:

```fsharp
type PersonV2 = { Id: int; Name: string; Email: string option }
let makePersonV2 id name email = { Id = id; Name = name; Email = email }

let personV2Schema =
    Schema.define<PersonV2>
    |> Schema.construct makePersonV2
    |> Schema.field "id" _.Id
    |> Schema.field "name" _.Name
    |> Schema.field "email" _.Email
    |> Schema.build
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
- Explicit field-policy helpers such as `Schema.missingAsNone` and `Schema.emptyStringAsNone`
- Domain refinement through `Schema.map` and `Schema.tryMap`
- JSON Schema export from authored `Schema<'T>` contracts
- JSON Schema import into `Schema<JsonValue>` for external dynamic receive-side contracts
- Raw JSON fallback via `Schema.jsonValue` for shapes that do not lower cleanly into the normal schema subset
- .NET-only bridge importers for `System.Text.Json`, `Newtonsoft.Json`, and `DataContract`

## Compatibility

- Shared compatibility coverage lives in [tests/CodecMapper.CompatibilitySentinel](/home/adam/projects/CodecMapper/tests/CodecMapper.CompatibilitySentinel), with thin Native AOT and Fable shell apps under [tests/CodecMapper.AotTests](/home/adam/projects/CodecMapper/tests/CodecMapper.AotTests) and [tests/CodecMapper.FableTests](/home/adam/projects/CodecMapper/tests/CodecMapper.FableTests).
- CI runs both the .NET sentinel app and a real Fable transpilation check of the Fable sentinel project.
- The contract bridge in [src/CodecMapper.Bridge](/home/adam/projects/CodecMapper/src/CodecMapper.Bridge) is `.NET`-only by design; the portable surface is the core schema/JSON/XML library in [src/CodecMapper](/home/adam/projects/CodecMapper/src/CodecMapper).

## Start here

- Read [Getting started](docs/GETTING_STARTED.md) for the core mental model and schema DSL.

## More docs

Tutorials:

- [Getting started](docs/GETTING_STARTED.md)

How-to guides:

- [How to export JSON Schema](docs/HOW_TO_EXPORT_JSON_SCHEMA.md)
- [How to import existing C# contracts](docs/HOW_TO_IMPORT_CSHARP_CONTRACTS.md)
- [Configuration contracts guide](docs/CONFIG_CONTRACTS.md)

Reference:

- [JSON Schema support reference](docs/JSON_SCHEMA_SUPPORT.md)
- [API docs](https://adz.github.io/CodecMapper/)

Explanations:

- [JSON Schema in CodecMapper](docs/JSON_SCHEMA_EXPLANATION.md)
- [C# attribute bridge design](docs/CSHARP_ATTRIBUTE_BRIDGE.md)

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

The benchmark suite compares `CodecMapper` JSON encode/decode against `System.Text.Json` and `Newtonsoft.Json` on the same small nested-object payload.

Latest local manual snapshot, measured on March 10, 2026.

<!-- benchmark-snapshot:start -->
Latest local manual snapshot, measured on March 10, 2026.

Encode, fastest to slowest:

| Library | Mean ns/op | Mean B/op |
| --- | ---: | ---: |
| CodecMapper | 489.2 | 512.0 |
| System.Text.Json | 830.1 | 504.2 |
| Newtonsoft.Json | 1137.9 | 1664.8 |

Decode, fastest to slowest:

| Library | Mean ns/op | Mean B/op |
| --- | ---: | ---: |
| CodecMapper deserialize bytes | 867.3 | 944.0 |
| System.Text.Json | 1073.7 | 896.0 |
| Newtonsoft.Json | 1742.7 | 3560.0 |

These numbers came from:

```bash
dotnet run -c Release --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj
```
<!-- benchmark-snapshot:end -->

## Notes

- `Json.compile` is explicit by design. Compile once and reuse the resulting codec.
- The current benchmark numbers are machine-specific and published mainly as relative comparisons, not universal claims.
