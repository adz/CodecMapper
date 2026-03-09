# Getting Started with CodecMapper

Define mapping rules once and reuse them for both decoding and encoding. CodecMapper keeps decoding/encoding logic in one place so you can port domain models between different wire formats without symmetry drift.

## Why CodecMapper?

- **Symmetry:** Define the mapping once and get both `Decode` and `Encode` for free.
- **Native AOT Ready:** No reflection, no expression trees, no dynamic runtime code generation.
- **Streaming Support:** Built-in `Utf8JsonReader` traversal keeps decoding high-performance.
- **Type Safe:** The DSL is fully typed so mapping errors surface at compile time.

## Core Concepts

### The Codec

A `Codec<'T>` knows how to move a type `'T` to and from a wire format. You build codecs for records, discriminated unions, or primitive values and then reuse them wherever that type crosses the boundary.

### The DSL

The `codec` computation expression (CE) is the primary tool for building record mappings. Each line in a `codec` block follows a specific pattern.

#### Anatomy of a Mapping Line

A typical mapping line consists of four main elements:

```fsharp
// [Op]   [Codec]   [Wire Key]   [Getter]
linkVia  Codec.int   "user_id"    _.UserId
```

1.  **Operation (`Op`):** The instruction to the builder (e.g., `linkVia`, `linkViaDefault`, `linkViaOptional`).
2.  **Codec:** The strategy for converting the value (e.g., `Codec.int`, `Codec.string`, or a custom `Codec<'T>`).
3.  **Wire Key:** The name of the property in the wire format (e.g., `"user_id"` in JSON).
4.  **Getter:** A function that extracts the field from your domain record (e.g., `_.UserId`).

#### Compact Helpers

For common types, CodecMapper provides "sugar" that combines the Operation and Codec:

```fsharp
// [Sugar (Op+Codec)]  [Wire Key]  [Getter]
int                   "user_id"   _.UserId
```

#### The Constructor Pattern

Every `codec` block **must** begin with a `construct` line:

```fsharp
construct (fun id name -> { Id = id; Name = name })
```

The constructor must be a **curried function** whose parameters exactly match the order and types of the mapping lines that follow. If you have 3 mapping lines, your constructor must accept 3 arguments.

## Quick start

### Compact form

```fsharp
open CodecMapper.Core

type Person = { Id: int; Name: string }

let makePerson id name = { Id = id; Name = name }

let personCodec =
    codec {
        construct makePerson
        int "id" _.Id
        string "name" _.Name
    }
```

### Expanded form

```fsharp
let personCodecExplicit =
    codec {
        construct makePerson
        via Codec.int (mapField "id" _.Id)
        via Codec.string (mapField "name" _.Name)
    }
```

The compact helpers (`int`, `string`, etc.) are sugar, while `via` lets you plug in `mapField`-built `Relationship`s when you need to compose mappings programmatically.

## Before vs After

Before CodecMapper, teams keep DTO conversions next to serialization helpers:

```fsharp
type PersonDto = { Id: int; Name: string }

let toDto (person: Person) = { Id = person.Id; Name = person.Name }
let fromDto (dto: PersonDto) = { Id = dto.Id; Name = dto.Name }
```

That doubles the surface area that must stay in sync.

After:

```fsharp
let decoded = JsonRunner.decodeString personCodec """{"id":1,"name":"adam"}"""
let encoded = JsonRunner.encodeString personCodec { Id = 1; Name = "adam" }
```

CodecMapper keeps the conversion logic inside one codec, so runners,
tests, and adapters share that single definition.

## Error handling example

Decode APIs return `Result<'T, string>` so failures stay explicit and composable:

```fsharp
let parsePerson (json: string) =
    match JsonRunner.decodeString personCodec json with
    | Ok person -> Ok person
    | Error err -> Error $"Invalid Person payload: {err}"
```

This lets you surface actionable API errors without exception control flow.

## Failure messages

Common failure strings to expect:
- `Missing required key 'field'` when a required property is absent.
- `Key 'field': <inner error>` when a nested codec reports its own error.
- `Expected StartObject` when the incoming payload root is not an object.

These messages already describe the field and failure, so they map naturally to HTTP 400 responses or structured logs.

## Minor wire/domain variations

Wire fields often differ from domain names or optionality.

```fsharp
type User = {
    UserId: int
    DisplayName: string
    RetryCount: int
    Nickname: string option
}

let userCodec =
    codec {
        construct (fun userId displayName retry nickname ->
            { UserId = userId; DisplayName = displayName; RetryCount = retry; Nickname = nickname })

        int "user_id" _.UserId
        string "display_name" _.DisplayName

        linkViaDefault Codec.int 0 "retry_count" _.RetryCount
        linkViaOptional (Codec.option Codec.string) "nickname" _.Nickname
    }
```

## via vs linkVia

`via` and `linkVia` share the same internal behavior; the difference lies in how the relationship is supplied. Use `via` when you already have a `Relationship` from `mapField` and `linkVia` for inline convenience.

```fsharp
let personCodecMixed =
    codec {
        construct makePerson
        via Codec.string (mapField "name" _.Name)
        linkVia Codec.int "id" _.Id
    }
```

## Different codecs for different wire contracts

You can keep one domain type while exposing multiple contracts without introducing DTO classes for each integration.

```fsharp
type Account = {
    Id: int
    Name: string
    IsEnabled: bool
}

let partnerV1Codec =
    codec {
        construct (fun id name active -> { Id = id; Name = name; IsEnabled = active })
        int "id" _.Id
        string "full_name" _.Name
        bool "active" _.IsEnabled
    }

let partnerV2Codec =
    codec {
        construct (fun accountId profile status ->
            { Id = accountId; Name = profile; IsEnabled = (status = "enabled") })
        int "account_id" _.Id
        string "profile_name" _.Name
        linkVia Codec.string "status" (fun a -> if a.IsEnabled then "enabled" else "disabled")
    }
```

Both codecs map to `Account`, so the rest of your app stays unchanged while wire contracts vary.

## Migration story

The versioned example in `docs/examples/json-scenarios.md` decodes `version = "1" | "2" | "3"` responses into a single `CurrentProfile` domain while always encoding the normalized `version = "3"` payload.

```fsharp
let currentCodec =
    Codec.choiceAt "version"
        (fun _ -> "3")
        [ "1", v1Codec; "2", v2Codec; "3", v3Codec ]
```

Adding a new wire version is just another branch in the choice; the shared domain stays untouched.

## Runners

```fsharp
let fromString = JsonRunner.decodeString personCodec """{"id":1,"name":"adam"}"""
let fromFile = JsonRunner.decodeFile personCodec "person.json"

let jsonText = JsonRunner.encodeString personCodec { Id = 1; Name = "adam" }
let jsonBytes = JsonRunner.encodeBytes personCodec { Id = 1; Name = "adam" }
JsonRunner.encodeFile personCodec { Id = 1; Name = "adam" } "person-out.json"
```

`ObjectRunner.decode` is also available when migrating between CLR object shapes via the same codec rules.

## Composition

Codecs are composable. You can use one codec inside another.

 ```fsharp
type Team = { Name: string; Members: User list }

let teamCodec =
    codec {
        construct (fun name members -> { Name = name; Members = members })
        string "team_name" _.Name
        linkVia (Codec.list userCodec) "members" (fun t -> t.Members)
    }
```

## Field operations

```fsharp
type Profile = { Name: string; Nickname: string option; Retry: int }

let profileCodec =
    codec {
        construct (fun name nickname retry -> { Name = name; Nickname = nickname; Retry = retry })

        via Codec.string (mapField "name" _.Name)
        linkViaOptional (Codec.option Codec.string) "nickname" (fun profile -> profile.Nickname)
        linkViaDefault Codec.int 3 "retry" (fun profile -> profile.Retry)
    }
```

## Choice helpers

Use `choiceAt` when the wire tag key is just a string.

```fsharp
type Shape = Circle of float | Rect of float * float

let circleCodec = ...
let rectCodec = ...

let shapeCodec =
    Codec.choiceAt "type" (function Circle _ -> "circle" | Rect _ -> "rect") [
        "circle", circleCodec
        "rect", rectCodec
    ]
```

With `mapField`, you can still build manual `Relationship` instances when you need them, but the compact helpers above cover most use cases.

## Custom Types & Transformations

If your domain type doesn't match the wire type (e.g., a `DateTime` on the wire that should be a custom `Timestamp` type in your domain), you can use transformation combinators.

### imap (Isomorphic Map)

Use `imap` when you have a bidirectional transformation.

```fsharp
// Codec.string returns string, we transform it to/from a custom type
let userIdCodec = 
    Codec.string 
    |> Codec.imap (fun s -> UserId s) (fun (UserId s) -> s)

// Usage in a record
let userCodec = codec {
    construct (fun id -> { Id = id })
    linkVia userIdCodec "id" _.Id
}
```

### map (Decode-only Map)

Use `map` when you only need to transform during decoding. Note that encoding will fail if the inverse transformation is required but not provided.

```fsharp
let trimmedString = Codec.string |> Codec.map (fun s -> s.Trim())
```

## Collections

CodecMapper provides built-in generators for common F# and .NET collections.

| Collection Type | Generator | Base Codec |
| :--- | :--- | :--- |
| `List<'T>` | `Codec.list` | Any `Codec<'T>` |
| `Array<'T>` | `Codec.array` | Any `Codec<'T>` |
| `seq<'T>` | `Codec.seq` | Any `Codec<'T>` |
| `Set<'T>` | `Codec.set` | Any `Codec<'T>` |
| `ResizeArray<'T>`| `Codec.resizeArray` | Any `Codec<'T>` |
| `Map<'K, 'V>` | `Codec.fsharpMap` | `Codec<'K>` and `Codec<'V>` |

### Collection Example

```fsharp
type Project = {
    Name: string
    Tags: string Set
    Metadata: Map<string, string>
}

let projectCodec = codec {
    construct (fun name tags meta -> { Name = name; Tags = tags; Metadata = meta })
    string "name" _.Name
    linkVia (Codec.set Codec.string) "tags" _.Tags
    linkVia (Codec.fsharpMap Codec.string Codec.string) "metadata" _.Metadata
}
```

## Next Steps

- Explore more [JSON Scenarios](./examples/json-scenarios.md) for complex nested objects, optional fields, and versioning.
- View the [API Reference (GitHub Pages)](https://adz.github.io/CodecMapper/reference/index.html) for a full list of built-in codecs and DSL operations.
