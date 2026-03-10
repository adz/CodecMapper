# Getting Started with `CodecMapper`

`CodecMapper` lets you define one schema and compile it into multiple codecs. The same mapping drives both encode and decode, so JSON and XML stay symmetric.

This tutorial is learning-oriented: it introduces the main schema DSL and the core compile-and-reuse workflow.

The portable core in `CodecMapper` is intended to stay usable from Native AOT and Fable-oriented targets. The separate `CodecMapper.Bridge` assembly is `.NET`-only because it imports CLR serializer metadata through reflection.

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
open CodecMapper

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

If you prefer a shorter-looking DSL, open `CodecMapper.Schema` and call the schema steps directly:

```fsharp
open CodecMapper
open CodecMapper.Schema

type Person = { Id: int; Name: string }
let makePerson id name = { Id = id; Name = name }

let personSchema =
    define<Person>
    |> construct makePerson
    |> field "id" _.Id
    |> field "name" _.Name
    |> build
```

If you want a shorter qualified style without opening the module for the whole file, use a module alias:

```fsharp
module S = CodecMapper.Schema

let personSchema =
    S.define<Person>
    |> S.construct makePerson
    |> S.field "id" _.Id
    |> S.field "name" _.Name
    |> S.build
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
- `Schema.int64`
- `Schema.int16`
- `Schema.byte`
- `Schema.sbyte`
- `Schema.uint32`
- `Schema.uint16`
- `Schema.uint64`
- `Schema.float`
- `Schema.decimal`
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

## JSON Schema export

You can export the JSON wire contract described by any `Schema<'T>`:

```fsharp
let jsonSchema = JsonSchema.generate accountSchema
```

The exported document targets JSON Schema draft 2020-12 and follows the same structural rules as `Json.compile`:

- record schemas become object schemas with `properties` and `required`
- `Schema.option` exports as `anyOf` with the inner type plus `null`
- `Schema.missingAsNone` removes that property from `required`
- `Schema.map` and `Schema.tryMap` export the underlying wire shape, not domain-only validation rules

For example, a smart-constructor wrapper over `Schema.int` still exports as an integer contract because the JSON wire format is still an integer.

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

## Import existing C# contracts

If you already have C# models annotated for `System.Text.Json`, `Newtonsoft.Json`, or `DataContract`, you can import a `CodecMapper` schema instead of rewriting the contract by hand.

F#:

```fsharp
open CodecMapper
open CodecMapper.Bridge

let userSchema =
    SystemTextJson.import<MyCompany.Contracts.User> BridgeOptions.defaults

let codec = Json.compile userSchema
```

C# model:

```csharp
public sealed class User
{
    [JsonConstructor]
    public User(int id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    [JsonPropertyName("user_id")]
    public int Id { get; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; }
}
```

Supported bridge surface today:

- constructor-bound classes
- parameterless setter-bound classes
- nested imported classes
- arrays, `List<T>`, and `Nullable<T>`
- `System.Text.Json`, `Newtonsoft.Json`, and `DataContract` rename/ignore/required metadata

Explicitly unsupported today:

- custom converter attributes
- extension-data bags
- polymorphic contracts
- recursive graphs
- classes that mix constructor-bound and setter-bound members

The bridge is a migration/bootstrap path. Once you know the imported shape is correct, code generation or a handwritten schema is still the cleaner long-term contract.

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
