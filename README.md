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

It gives you one mapping model that can compile to JSON and XML codecs, with support for handwritten schemas in F# and migration paths from existing C# attribute-based contracts.

## Core shape

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
```

## What it covers

- explicit schema DSL for F#
- JSON and XML codecs from one schema
- common built-in primitives, options, collections, and time-based types
- validated mappings via `Schema.tryMap`
- bridge importers for `System.Text.Json`, `Newtonsoft.Json`, and `DataContract`

## Compatibility

- The core library is exercised by dedicated Native AOT and Fable sentinel apps under [tests/CodecMapper.AotTests](/home/adam/projects/cmap/tests/CodecMapper.AotTests) and [tests/CodecMapper.FableTests](/home/adam/projects/cmap/tests/CodecMapper.FableTests).
- CI runs both the .NET sentinel app and a real Fable transpilation check of the Fable sentinel project.
- The contract bridge in [src/CodecMapper.Bridge](/home/adam/projects/cmap/src/CodecMapper.Bridge) is `.NET`-only by design; the portable surface is the core schema/JSON/XML library in [src/CodecMapper](/home/adam/projects/cmap/src/CodecMapper).

## Docs

- [Getting started](docs/GETTING_STARTED.md)
- [Configuration contracts guide](docs/CONFIG_CONTRACTS.md)
- [C# attribute bridge design](docs/CSHARP_ATTRIBUTE_BRIDGE.md)
- [API docs](https://adz.github.io/CodecMapper/)

## Notes

- `Json.compile` is explicit by design. Compile once and reuse the resulting codec.
- The current benchmark numbers are machine-specific and published mainly as relative comparisons, not universal claims.
