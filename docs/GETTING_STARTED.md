# Getting Started with `CodecMapper`

`CodecMapper` lets you define one schema and compile it into multiple codecs. The same mapping drives both encode and decode, so JSON and XML stay symmetric.

This tutorial is learning-oriented: it introduces the main schema DSL, how to read a schema definition, and the core compile-and-reuse workflow.

The portable core in `CodecMapper` is intended to stay usable from Native AOT and Fable-oriented targets. The separate `CodecMapper.Bridge` assembly is `.NET`-only because it imports CLR serializer metadata through reflection.

## Choose the right starting path

Start from the contract you already have:

- F# contract you control: author a `Schema<'T>` directly and compile it.
- New C# class you control: use `CSharpSchema` for setter-bound classes, then compile the result like any other schema.
- Existing C# contract already annotated for `System.Text.Json`, `Newtonsoft.Json`, or `DataContract`: import it through `CodecMapper.Bridge`.
- External JSON Schema document: keep that separate from authored schemas and use the JSON Schema import path only for receive-side `JsonValue` contracts.

That split matters because `CodecMapper` has two different extension stories:

- authored schemas: normal `Schema<'T>` values that compile into fast typed codecs
- imported contracts: bridge or JSON Schema flows that help you interoperate with contracts you do not want to rewrite immediately

## Core shape

The stable schema DSL is:

```fsharp
define<'T>
|> construct ctor
|> field ...
|> build
```

Compile explicitly and reuse the resulting codec:

```fsharp
let codec = Json.compile personSchema
let json = Json.serialize codec person
let roundTripped = Json.deserialize codec json
```

The format modules also expose `Json.codec`, `Xml.codec`, `Yaml.codec`, and `KeyValue.codec` as direct aliases for the corresponding `compile` functions. This tutorial uses the shorter `*.codec` form in the small examples to reduce noise, but the separate `compile` step is still the important performance habit: compile once, keep the codec, and reuse it.

## How to read a schema

Read the pipeline from top to bottom:

- `Schema.define<Person>` says which value the schema describes.
- `Schema.construct makePerson` says how decode rebuilds the value.
- `Schema.field "id" _.Id` means the wire field `"id"` maps to the `Id` field on the record.
- `Schema.fieldWith "home" _.Home addressSchema` means the field still maps to `Home`, but uses an explicit child schema instead of auto-resolution.

That is the mental model for the whole library: the schema is the wire contract written in the shape of the data.

## Quick start

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

let jsonCodec = Json.codec personSchema

let person = { Id = 1; Name = "Ada" }
let json = Json.serialize jsonCodec person
let decoded = Json.deserialize jsonCodec json

printfn "%s" json
printfn "%A" decoded
```

Output:

```text
{"id":1,"name":"Ada"}
{ Id = 1
  Name = "Ada" }
```

This tutorial keeps using the shorter `Json.codec` spelling in the small snippets:

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

let jsonCodec = Json.codec personSchema
```

In longer-lived application code, prefer keeping the explicit `Json.compile personSchema` shape when you want to emphasize that the codec should be created once and reused.

## Starting from C#

If you are using `CodecMapper` from a C#-heavy codebase, there are two intended paths.

For new setter-bound classes, author the schema directly with `CSharpSchema`:

```csharp
using CodecMapper;
using CodecMapper.Bridge;

public sealed class User
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

var schema =
    CSharpSchema.Record(() => new User())
        .Field("id", value => value.Id, (value, field) => value.Id = field)
        .Field("name", value => value.Name, (value, field) => value.Name = field)
        .Build();

var codec = CSharpSchema.Json(schema);
var json = codec.Serialize(new User { Id = 1, Name = "Ada" });
```

For existing attributed contracts, keep the existing annotations and import the contract instead:

```fsharp
open CodecMapper
open CodecMapper.Bridge

let schema =
    SystemTextJson.import<MyCompany.Contracts.User> BridgeOptions.defaults

let codec = Json.compile schema
```

Use the direct C# schema path when you own the contract and want `CodecMapper` to be the source of truth. Use the bridge when an existing serializer contract is already established and you need an incremental migration path.

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

let personSchema =
    define<Person>
    |> construct makePerson
    |> field "id" _.Id
    |> field "name" _.Name
    |> fieldWith "home" _.Home addressSchema
    |> build
```

The nested schema mirrors the nested data. `home` is not special serializer metadata; it is just another field whose value is described by `addressSchema`.

## Lists and arrays

Lists and arrays are first-class and auto-resolve inside record fields:

```fsharp
open CodecMapper.Schema

type Team = { Name: string; Tags: string list; Aliases: string array }
let makeTeam name tags aliases =
    { Name = name; Tags = tags; Aliases = aliases }

let teamSchema =
    define<Team>
    |> construct makeTeam
    |> field "name" _.Name
    |> field "tags" _.Tags
    |> field "aliases" _.Aliases
    |> build
```

You can also build collection schemas directly:

```fsharp
open CodecMapper.Schema

let namesSchema = list string
let aliasesSchema = array string
```

For .NET interop collection shapes, `IReadOnlyList<'T>` and `ICollection<'T>` also auto-resolve through the normal homogeneous array wire form.

If you need a concrete `ResizeArray<'T>` / `List<T>` in the model, keep that explicit:

```fsharp
open CodecMapper.Schema

let bufferSchema = resizeArray string
```

## Options

Options are explicit schemas too:

```fsharp
open CodecMapper.Schema

let maybeAgeSchema = option int
```

Inside records, `option<'T>` auto-resolves:

```fsharp
open CodecMapper.Schema

type Profile = { Nickname: string option; Age: int option }
let makeProfile nickname age = { Nickname = nickname; Age = age }

let profileSchema =
    define<Profile>
    |> construct makeProfile
    |> field "nickname" _.Nickname
    |> field "age" _.Age
    |> build
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

CLR enums also auto-resolve through their underlying numeric wire form. `CodecMapper` does not currently add a string-enum policy on top of that.

Example:

```fsharp
open System
open CodecMapper.Schema

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
    define<AuditRecord>
    |> construct makeAuditRecord
    |> field "userId" _.UserId
    |> field "createdAt" _.CreatedAt
    |> field "duration" _.Duration
    |> build
```

## Custom wrappers

Use `Schema.map` for total wrappers:

```fsharp
open CodecMapper.Schema

type PersonId = PersonId of int

let personIdSchema =
    int
    |> map PersonId (fun (PersonId value) -> value)
```

Read `Schema.map` as "the wire shape is still an `int`, but the in-memory value is `PersonId`."

Use `Schema.tryMap` for smart constructors that can reject invalid decoded values:

```fsharp
open CodecMapper.Schema

type UserId = UserId of int

module UserId =
    let create value =
        if value > 0 then
            Ok(UserId value)
        else
            Error "UserId must be positive"

    let value (UserId value) = value

let userIdSchema =
    int
    |> tryMap UserId.create UserId.value
```

Read `Schema.tryMap` as "decode the wire value first, then validate/refine it into a stronger domain type."

For common opt-in boundary rules, `Schema` also exposes small validated helpers:

```fsharp
open CodecMapper.Schema

let userNameSchema = nonEmptyString
let retryCountSchema = positiveInt
let tagsSchema = nonEmptyList string
let normalizedLabelSchema = trimmedString
```

These stay explicit authoring choices. They do not weaken the normal `Schema.string`, `Schema.int`, or `Schema.list` defaults.

That schema can then be used inside larger records:

```fsharp
open CodecMapper.Schema

type Account = { Id: UserId; Name: string }
let makeAccount id name = { Id = id; Name = name }

let accountSchema =
    define<Account>
    |> construct makeAccount
    |> fieldWith "id" _.Id userIdSchema
    |> field "name" _.Name
    |> build
```

## Multiple formats from one schema

Compile the same schema into JSON and XML:

```fsharp
let jsonCodec = Json.compile personSchema
let xmlCodec = Xml.compile personSchema

let person =
    {
        Id = 42
        Name = "Ada"
        Home = { Street = "Main"; City = "Adelaide" }
    }

let json = Json.serialize jsonCodec person
let personFromJson = Json.deserialize jsonCodec json

let xml = Xml.serialize xmlCodec person
let personFromXml = Xml.deserialize xmlCodec xml

printfn "%s" json
printfn "%A" personFromJson
printfn "%s" xml
printfn "%A" personFromXml
```

Output:

```text
{"id":42,"name":"Ada","home":{"street":"Main","city":"Adelaide"}}
{ Id = 42
  Name = "Ada"
  Home = { Street = "Main"
           City = "Adelaide" } }
<person><id>42</id><name>Ada</name><home><street>Main</street><city>Adelaide</city></home></person>
{ Id = 42
  Name = "Ada"
  Home = { Street = "Main"
           City = "Adelaide" } }
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
- Reach for `Schema.nonEmptyString`, `Schema.trimmedString`, `Schema.positiveInt`, and `Schema.nonEmptyList` when those boundary rules are part of the contract.
- Keep schemas in one place so JSON and XML stay aligned.
- Treat JSON Schema as a separate integration surface: export it from authored schemas, or import it for receive-side `JsonValue` contracts.
- Use [How To Export JSON Schema](HOW_TO_EXPORT_JSON_SCHEMA.md) when you need external schema documents from authored contracts.
- Use [How To Import Existing C# Contracts](HOW_TO_IMPORT_CSHARP_CONTRACTS.md) when you are starting from an existing C# serializer contract.
