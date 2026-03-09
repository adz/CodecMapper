# CodecMapper

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/logo-mark.png">
  <source media="(prefers-color-scheme: light)" srcset="docs/logo-mark.png">
  <img src="docs/logo-mark.png" alt="CodecMapper logo" width="72">
</picture>

`CodecMapper` is a schema-first serialization library for F# focused on explicit wire contracts, symmetric encode/decode behavior, and AOT-friendly execution.

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

## Docs

- [Getting started](docs/GETTING_STARTED.md)
- [Configuration contracts guide](docs/CONFIG_CONTRACTS.md)
- [C# attribute bridge design](docs/CSHARP_ATTRIBUTE_BRIDGE.md)
- [API docs](https://adz.github.io/CodecMapper/)

## Notes

- `Json.compile` is explicit by design. Compile once and reuse the resulting codec.
- The current benchmark numbers are machine-specific and published mainly as relative comparisons, not universal claims.
