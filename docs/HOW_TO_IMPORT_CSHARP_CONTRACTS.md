# How To Import Existing C# Contracts

Use this guide when you already have C# models and need a clear path into `CodecMapper`.

There are three distinct cases:

- new C# classes you control: author a schema directly with `CSharpSchema`
- existing attributed contracts: import them with `CodecMapper.Bridge`
- external JSON Schema documents: do not use the bridge; use the JSON Schema import path instead

That split keeps the user-facing story simple:

- `CSharpSchema` is for explicit schema authoring from C#
- the bridge is for migration from serializer attributes
- JSON Schema import is for external schema-owned receive paths

## Choose the pathway

Choose the path that matches the contract you already have:

- Use `CSharpSchema` when the class is setter-bound and you want `CodecMapper` to be the source of truth.
- Use `SystemTextJson.import`, `NewtonsoftJson.import`, or `DataContracts.import` when the class is already annotated and that attribute contract is the source of truth.
- Use `JsonSchema.import` when the source of truth is a JSON Schema document rather than a CLR type.

If you control the C# model and do not need serializer attribute compatibility, prefer `CSharpSchema`. It is simpler, more explicit, and avoids reflection in the authoring path.

## Author a schema directly from C#

For setter-bound classes, the facade wraps the normal `Schema<'T>` model with a mutable fluent builder:

```csharp
using CodecMapper;
using CodecMapper.Bridge;

public sealed class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
}

public sealed class User
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    public Address Home { get; set; } = new();
}

var addressSchema =
    CSharpSchema.Record(() => new Address())
        .Field("street", value => value.Street, (value, field) => value.Street = field)
        .Field("city", value => value.City, (value, field) => value.City = field)
        .Build();

var userSchema =
    CSharpSchema.Record(() => new User())
        .Field("id", value => value.Id, (value, field) => value.Id = field)
        .Field("display_name", value => value.DisplayName, (value, field) => value.DisplayName = field)
        .FieldWith("home", value => value.Home, (value, field) => value.Home = field, addressSchema)
        .Build();

var jsonCodec = CSharpSchema.Json(userSchema);
var keyValueCodec = CSharpSchema.KeyValue(userSchema);
```

The resulting schema is a normal `Schema<T>`. Compile it into JSON, XML, YAML, or KeyValue codecs the same way you would from F#.

Use this path when:

- you are writing a new C# contract
- the class has a parameterless constructor and writable properties
- you want the wire contract to stay explicit in code instead of inferred from serializer attributes

## Import an existing attributed contract

Use the bridge when the contract is already described by serializer metadata and you want to preserve that wire shape while moving into `CodecMapper`.

## Import a `System.Text.Json` contract

F#:

```fsharp
open CodecMapper
open CodecMapper.Bridge

let schema =
    SystemTextJson.import<MyCompany.Contracts.User> BridgeOptions.defaults

let codec = Json.compile schema
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

This is the common incremental migration path for constructor-bound immutable C# models.

## Import a `Newtonsoft.Json` contract

```fsharp
open CodecMapper
open CodecMapper.Bridge

let schema =
    NewtonsoftJson.import<MyCompany.Contracts.User> BridgeOptions.defaults

let codec = Json.compile schema
```

## Import a `DataContract` contract

```fsharp
open CodecMapper
open CodecMapper.Bridge

let schema =
    DataContracts.import<MyCompany.Contracts.User> BridgeOptions.defaults

let codec = Json.compile schema
```

## Compile and round-trip the imported schema

Once imported, the bridge result is just a normal `Schema<'T>`:

```fsharp
let codec = Json.compile schema
let json = Json.serialize codec value
let decoded = Json.deserialize codec json

printfn "%s" json
printfn "%A" decoded
```

From that point on, use the imported schema the same way you would use a handwritten schema.

## Know what each C# option is for

Use `CSharpSchema` when:

- you are defining a new setter-bound C# class
- you want an explicit `CodecMapper` schema without serializer attributes
- you want the contract definition to stay visible in your own codebase

Use the bridge when:

- you are migrating an existing serializer-based contract incrementally
- you need compatibility with an established `System.Text.Json`, `Newtonsoft.Json`, or `DataContract` model
- the class is constructor-bound and the serializer attributes already define the wire shape

Use JSON Schema import instead when:

- the source of truth is a JSON Schema document instead of a CLR type
- the receive path is dynamic or branch-heavy
- you expect the imported result to stay `Schema<JsonValue>` rather than a typed CLR schema

## Supported bridge surface

The bridge supports the contract shapes that map cleanly into normal `CodecMapper` schemas:

- constructor-bound classes
- parameterless setter-bound classes
- nested imported classes
- arrays, `List<T>`, `IReadOnlyList<T>`, `ICollection<T>`, `Nullable<T>`, and numeric-wire enums
- rename, ignore, required, and constructor-selection metadata for `System.Text.Json`, `Newtonsoft.Json`, and `DataContract`

This is enough for many existing API and config models, but it is intentionally not serializer-feature-complete.

## Edge cases to check before you choose the bridge

These cases are worth checking early because they decide whether the bridge is the right fit:

- Multiple constructors:
  the bridge needs one deterministic construction path. Use an explicit serializer constructor attribute or a single public constructor.
- Renamed constructor parameters:
  `System.Text.Json` still matches constructor parameters by CLR member name, not by `JsonPropertyName`. Keep the constructor parameter names aligned with the underlying members.
- Duplicate wire names:
  if two members map to the same wire field after renaming, import fails.
- Mixed constructor and setter ownership:
  classes that partly decode through constructor parameters and partly through setters are rejected.
- Custom converters or serializer-only policies:
  attribute-driven conversion logic does not become part of a normal `CodecMapper` schema. Those cases fail closed.
- Extension-data and polymorphic contracts:
  keep those outside the bridge path unless the library gains an explicit schema-level model for them.

For the C# facade specifically, the supported surface is narrower:

- parameterless setter-bound classes
- explicit `Field` and `FieldWith` bindings
- normal JSON/XML/KeyValue/YAML compile helpers after `Build()`

Constructor-bound C# authoring is better served by the bridge.

## Practical examples

Choose `CSharpSchema` for a new explicit contract:

```csharp
var schema =
    CSharpSchema.Record(() => new User())
        .Field("id", value => value.Id, (value, field) => value.Id = field)
        .Field("name", value => value.Name, (value, field) => value.Name = field)
        .Build();
```

Choose the bridge for an existing immutable serializer contract:

```fsharp
let schema =
    SystemTextJson.import<MyCompany.Contracts.User> BridgeOptions.defaults
```

Choose JSON Schema import for an external schema-owned boundary:

```fsharp
let imported = JsonSchema.import schemaText
let codec = Json.compile imported
```

That last path is intentionally different: it produces a `Schema<JsonValue>` receive boundary, not a typed CLR contract import.
