# Getting Started with `cmap`

`cmap` lets you define one schema and compile it into multiple codecs. The same mapping drives both encode and decode, so JSON and XML stay symmetric.

## Core shape

The stable schema DSL is:

```fsharp
Schema.define<'T>
|> Schema.construct ctor
|> Schema.field ...
|> Schema.build
```

Compile explicitly and reuse the resulting codec:

```fsharp
let codec = Json.compile personSchema
let json = Json.serialize codec person
let roundTripped = Json.deserialize codec json
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

let jsonCodec = Json.compile personSchema

let person = { Id = 1; Name = "Ada" }
let json = Json.serialize jsonCodec person
let decoded = Json.deserialize jsonCodec json
```

## Nested records and explicit child schemas

Use `Schema.fieldWith` when the nested type has its own schema:

```fsharp
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
```

## Lists and arrays

Lists and arrays are first-class and auto-resolve inside record fields:

```fsharp
type Team = { Name: string; Tags: string list; Aliases: string array }
let makeTeam name tags aliases =
    { Name = name; Tags = tags; Aliases = aliases }

let teamSchema =
    Schema.define<Team>
    |> Schema.construct makeTeam
    |> Schema.field "name" _.Name
    |> Schema.field "tags" _.Tags
    |> Schema.field "aliases" _.Aliases
    |> Schema.build
```

You can also build collection schemas directly:

```fsharp
let namesSchema = Schema.list Schema.string
let aliasesSchema = Schema.array Schema.string
```

## Options

Options are explicit schemas too:

```fsharp
let maybeAgeSchema = Schema.option Schema.int
```

Inside records, `option<'T>` auto-resolves:

```fsharp
type Profile = { Nickname: string option; Age: int option }
let makeProfile nickname age = { Nickname = nickname; Age = age }

let profileSchema =
    Schema.define<Profile>
    |> Schema.construct makeProfile
    |> Schema.field "nickname" _.Nickname
    |> Schema.field "age" _.Age
    |> Schema.build
```

Current wire representation is explicit rather than omitted:

- JSON `None` encodes as `null`
- XML `None` encodes as an empty element such as `<age></age>`
- missing fields are still treated as errors

## Common built-in types

These helpers are available directly and also auto-resolve inside records:

- `Schema.int`
- `Schema.int16`
- `Schema.byte`
- `Schema.sbyte`
- `Schema.uint16`
- `Schema.bool`
- `Schema.char`
- `Schema.string`
- `Schema.guid`
- `Schema.dateTime`
- `Schema.dateTimeOffset`
- `Schema.timeSpan`

Example:

```fsharp
open System

type AuditRecord =
    {
        UserId: Guid
        CreatedAt: DateTime
        Duration: TimeSpan
    }

let makeAuditRecord userId createdAt duration =
    {
        UserId = userId
        CreatedAt = createdAt
        Duration = duration
    }

let auditSchema =
    Schema.define<AuditRecord>
    |> Schema.construct makeAuditRecord
    |> Schema.field "userId" _.UserId
    |> Schema.field "createdAt" _.CreatedAt
    |> Schema.field "duration" _.Duration
    |> Schema.build
```

## Custom wrappers

Use `Schema.map` for total wrappers:

```fsharp
type PersonId = PersonId of int

let personIdSchema =
    Schema.int
    |> Schema.map PersonId (fun (PersonId value) -> value)
```

Use `Schema.tryMap` for smart constructors that can reject invalid decoded values:

```fsharp
type UserId = UserId of int

module UserId =
    let create value =
        if value > 0 then
            Ok(UserId value)
        else
            Error "UserId must be positive"

    let value (UserId value) = value

let userIdSchema =
    Schema.int
    |> Schema.tryMap UserId.create UserId.value
```

That schema can then be used inside larger records:

```fsharp
type Account = { Id: UserId; Name: string }
let makeAccount id name = { Id = id; Name = name }

let accountSchema =
    Schema.define<Account>
    |> Schema.construct makeAccount
    |> Schema.fieldWith "id" _.Id userIdSchema
    |> Schema.field "name" _.Name
    |> Schema.build
```

## Multiple formats from one schema

Compile the same schema into JSON and XML:

```fsharp
let jsonCodec = Json.compile personSchema
let xmlCodec = Xml.compile personSchema

let json = Json.serialize jsonCodec person
let personFromJson = Json.deserialize jsonCodec json

let xml = Xml.serialize xmlCodec person
let personFromXml = Xml.deserialize xmlCodec xml
```

## XML subset

The XML codec intentionally supports a small, explicit subset:

- element tags only
- escaped text nodes
- repeated `<item>` children for lists and arrays
- ignorable whitespace between elements

Still out of scope:

- attributes
- namespaces
- mixed content
- comments
- CDATA
- self-closing tags
- processing instructions

## Practical guidance

- Compile once and reuse codecs.
- Prefer `Schema.field` when the type auto-resolves cleanly.
- Use `Schema.fieldWith` when the nested or wrapped type has an explicit schema.
- Use `Schema.tryMap` when decode needs validation.
- Keep schemas in one place so JSON and XML stay aligned.
