# How To Import Existing C# Contracts

Use the bridge when you already have C# models annotated for `System.Text.Json`, `Newtonsoft.Json`, or `DataContract` and want to import that wire contract into `CodecMapper`.

This is a migration path, not the canonical authoring style. For new F#-first contracts, prefer a handwritten `Schema<'T>`.

If you are authoring a new C# contract and want an explicit `CodecMapper` schema without serializer attributes, use the thin `CSharpSchema` facade instead of the bridge.

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

This is still a normal `Schema<T>` under the hood. The facade is just a thin wrapper over record-field definitions and the usual compile functions.

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

That means the rest of the workflow is the same as handwritten schemas.

## Know what the bridge is for

Use the bridge when:

- you are migrating an existing C# contract incrementally
- you need interoperability with an established serializer-based model
- you want to validate that an existing contract can be represented as a normal `CodecMapper` schema

Prefer handwritten schemas when:

- you control the contract already
- you want the contract to stay obvious in F#
- you want to avoid reflection in the authoring path

Prefer the C# facade when:

- you are defining a new setter-bound C# contract directly
- you want explicit `CodecMapper` schemas without serializer attributes
- you want the bridge and codegen paths to stay optional rather than foundational

## Supported bridge surface

Current support is intentionally conservative:

- constructor-bound classes
- parameterless setter-bound classes
- nested imported classes
- arrays, `List<T>`, `IReadOnlyList<T>`, `ICollection<T>`, `Nullable<T>`, and numeric-wire enums
- rename/ignore/required metadata for `System.Text.Json`, `Newtonsoft.Json`, and `DataContract`

## Explicitly unsupported

These fail closed instead of being partially emulated:

- custom converter attributes
- extension-data bags
- polymorphic contracts
- recursive graphs
- classes that mix constructor-bound and setter-bound members

For the C# facade specifically, current support is narrower:

- parameterless setter-bound classes
- explicit `Field` and `FieldWith` bindings
- normal JSON/XML/KeyValue/YAML compile helpers after `Build()`

Constructor-bound C# authoring is still better served by the bridge or future code generation.

For the reasoning behind those boundaries, see [C# Attribute Bridge Design](CSHARP_ATTRIBUTE_BRIDGE.md).
