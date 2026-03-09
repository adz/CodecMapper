# Getting Started with CodecMapper

Define mapping rules once and reuse them for both decoding and encoding. CodecMapper keeps decoding/encoding logic in one place so you can port domain models between different wire formats without symmetry drift.

## Why CodecMapper?

- **Symmetry:** Define the mapping once and get both `Decode` and `Encode` for free.
- **Native AOT Ready:** No reflection during execution, direct lambda constructors.
- **Fable Friendly:** Fully compatible with Fable for JS targets.
- **Type Safe:** The DSL is fully typed so mapping errors surface at compile time.

## Core Concepts

### The Schema

A `Schema<'T>` is an abstract blueprint for a type `'T`. It defines how to build the type and how to access its fields.

### The DSL

The pipeline builder is the primary tool for building record mappings.

#### The Constructor Pattern

Every schema starts with `Schema.define<'T>` and then pins a curried constructor with `Schema.construct`.

```fsharp
let makePerson id name = { Id = id; Name = name }

let personSchema =
    Schema.define<Person>
    |> Schema.construct makePerson
    |> Schema.field "id" _.Id
    |> Schema.field "name" _.Name
    |> Schema.build
```

#### Field Definition

The `field` operation maps a wire key to a record property.

```fsharp
Schema.field "user_id" _.UserId
```

For custom or nested types, provide the schema explicitly:

```fsharp
Schema.fieldWith "home" _.Home addressSchema
```

## Quick start

```fsharp
open cmap

type Person = { Id: int; Name: string }
let makePerson id name = { Id = id; Name = name }

let personSchema =
    Schema.define<Person>
    |> Schema.construct makePerson
    |> Schema.field "id" _.Id
    |> Schema.field "name" _.Name
    |> Schema.build

// Compile to JSON
let personCodec = Json.compile personSchema

let p = { Id = 1; Name = "Adam" }
let json = Json.serialize personCodec p
let decoded = Json.deserialize personCodec json
```

## Composition

Schemas are composable. You can use one schema inside another.

 ```fsharp
type Team = { Name: string; Members: Person list }
let makeTeam name members = { Name = name; Members = members }

let teamSchema =
    Schema.define<Team>
    |> Schema.construct makeTeam
    |> Schema.field "team_name" _.Name
    |> Schema.fieldWith "members" _.Members (Schema.list personSchema)
    |> Schema.build
```

## Multi-Format Support

The same schema can be used for different formats:

```fsharp
let jsonCodec = Json.compile personSchema
let xmlCodec = Xml.compile personSchema

let json = Json.serialize jsonCodec p
let xml = Xml.serialize xmlCodec p
```
