# Getting Started with `CodecMapper`

This tutorial teaches the main schema-authoring path first.

The goal is simple:

1. define one schema
2. compile it once
3. serialize and deserialize with the same schema

Leave the C# bridge, JSON Schema import, and other side paths until this flow feels natural.

## The smallest complete example

```fsharp
open CodecMapper
type Person = { Id: int; Name: string }
let makePerson id name = { Id = id; Name = name }

let personSchema =
    Schema.record makePerson
    |> Schema.field "id" _.Id
    |> Schema.field "name" _.Name
    |> Schema.build

let codec = Json.compile personSchema

let person = { Id = 1; Name = "Ada" }
let json = Json.serialize codec person
let decoded = Json.deserialize codec json
```

That is the normal shape of the library:

- author a schema
- compile it into a codec
- reuse that codec for both directions

## How to read the schema

Read the builder pipeline from top to bottom:

- `Schema.record makePerson` says which value the schema describes and how decode rebuilds it
- `Schema.field "id" _.Id` maps the wire field `"id"` to the record field `Id`
- `Schema.build` finishes the schema

The important idea is that the schema is explicit code, not a hint to a serializer.

## What compilation means

The authored schema is still just data about the wire shape. `Json.compile` turns that definition into a reusable codec:

```fsharp
let codec = Json.compile personSchema
```

That explicit step matters because `CodecMapper` is designed for reuse. You compile once, then serialize and deserialize many values with the same codec.

If the schema is only being authored inline at the end of a short example, `Json.buildAndCompile` is a convenience:

```fsharp
let codec =
    Schema.record makePerson
    |> Schema.field "id" _.Id
    |> Schema.field "name" _.Name
    |> Json.buildAndCompile
```

Use that helper for small inline examples. Keep `Json.compile personSchema` when the schema has a name, is reused, or is referenced by other schemas.

## The next step: nested data

A child record usually gets its own schema:

```fsharp
type Address = { Street: string; City: string }
let makeAddress street city = { Street = street; City = city }

type Person = { Id: int; Name: string; Home: Address }
let makePerson id name home = { Id = id; Name = name; Home = home }

let addressSchema =
    Schema.record makeAddress
    |> Schema.field "street" _.Street
    |> Schema.field "city" _.City
    |> Schema.build

let personSchema =
    Schema.record makePerson
    |> Schema.field "id" _.Id
    |> Schema.field "name" _.Name
    |> Schema.fieldWith "home" _.Home addressSchema
    |> Schema.build
```

`Schema.fieldWith` marks an explicit schema boundary for the child value.

## The next step: stronger domain types

If the wire value is simple but the in-memory value should be validated, refine the schema with `Schema.tryMap`:

```fsharp
type UserId = UserId of int

module UserId =
    let create value =
        if value > 0 then Ok(UserId value)
        else Error "UserId must be positive"

    let value (UserId value) = value

let userIdSchema =
    Schema.int
    |> Schema.tryMap UserId.create UserId.value
```

That keeps the wire schema simple while making the domain type stricter.

## Where to go next

Take the next pages in this order:

1. [How To Model A Basic Record](HOW_TO_MODEL_A_BASIC_RECORD.md)
2. [How To Model A Nested Record](HOW_TO_MODEL_A_NESTED_RECORD.md)
3. [How To Model A Validated Wrapper](HOW_TO_MODEL_A_VALIDATED_WRAPPER.md)
4. [How To Model A Versioned Schema](HOW_TO_MODEL_A_VERSIONED_CONTRACT.md)
5. [How To Model A Recursive Tagged Union](HOW_TO_MODEL_A_RECURSIVE_TAGGED_UNION.md)

Once the schema-authoring path is clear:

- use [How To Import Existing C# Contracts](HOW_TO_IMPORT_CSHARP_CONTRACTS.md) for bridge or C# facade work
- use [How To Export JSON Schema](HOW_TO_EXPORT_JSON_SCHEMA.md) for outward schema documents
- use [JSON Schema in CodecMapper](JSON_SCHEMA_EXPLANATION.md) when you need the design reasoning
