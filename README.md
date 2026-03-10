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
let decoded = Json.deserialize codec json

printfn "%s" json
printfn "%A" decoded
```

That schema reads almost like the data constructor:

- `Schema.define<Person>` says which value you are describing
- `Schema.construct makePerson` says how to rebuild it during decode
- each `Schema.field` names one wire field and points at the matching record field
- `Schema.fieldWith` says "this field has its own explicit child schema"

The result is not hidden serializer behavior. It is the contract itself, written in normal F#.

Output:

```text
{"id":42,"name":"Ada","home":{"street":"Main","city":"Adelaide"}}
{ Id = 42
  Name = "Ada"
  Home = { Street = "Main"
           City = "Adelaide" } }
```

## Why this is useful

- The schema mirrors the data, so changes to the wire contract are visible in one place.
- Encode and decode come from the same definition, so drift is harder to introduce accidentally.
- `Json.compile` and `Xml.compile` reuse the same schema instead of making you maintain separate mappings.
- Domain refinement stays explicit through `Schema.map` and `Schema.tryMap` instead of being buried in serializer settings.

## What it covers

- explicit schema DSL for F#
- JSON and XML codecs from one schema
- common built-in primitives, options, collections, and time-based types
- validated mappings via `Schema.tryMap`
- raw JSON fallback via `Schema.jsonValue` for dynamic imported shapes
- bridge importers for `System.Text.Json`, `Newtonsoft.Json`, and `DataContract`

## Compatibility

- The core library is exercised by dedicated Native AOT and Fable sentinel apps under [tests/CodecMapper.AotTests](/home/adam/projects/cmap/tests/CodecMapper.AotTests) and [tests/CodecMapper.FableTests](/home/adam/projects/cmap/tests/CodecMapper.FableTests).
- CI runs both the .NET sentinel app and a real Fable transpilation check of the Fable sentinel project.
- The contract bridge in [src/CodecMapper.Bridge](/home/adam/projects/cmap/src/CodecMapper.Bridge) is `.NET`-only by design; the portable surface is the core schema/JSON/XML library in [src/CodecMapper](/home/adam/projects/cmap/src/CodecMapper).

## Start here

- Read [Getting started](docs/GETTING_STARTED.md) for the core mental model and schema DSL.

## More docs

- Tutorials:

- [Getting started](docs/GETTING_STARTED.md)

- How-to guides:

- [How to export JSON Schema](docs/HOW_TO_EXPORT_JSON_SCHEMA.md)
- [Configuration contracts guide](docs/CONFIG_CONTRACTS.md)

- Reference:

- [JSON Schema support reference](docs/JSON_SCHEMA_SUPPORT.md)
- [API docs](https://adz.github.io/CodecMapper/)

- Explanations:

- [JSON Schema in CodecMapper](docs/JSON_SCHEMA_EXPLANATION.md)
- [C# attribute bridge design](docs/CSHARP_ATTRIBUTE_BRIDGE.md)

## Notes

- `Json.compile` is explicit by design. Compile once and reuse the resulting codec.
- The current benchmark numbers are machine-specific and published mainly as relative comparisons, not universal claims.
