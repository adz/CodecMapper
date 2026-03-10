# How To Import Existing C# Contracts

Use the bridge when you already have C# models annotated for `System.Text.Json`, `Newtonsoft.Json`, or `DataContract` and want to import that wire contract into `CodecMapper`.

This is a migration path, not the canonical authoring style. For new F#-first contracts, prefer a handwritten `Schema<'T>`.

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

## Supported bridge surface

Current support is intentionally conservative:

- constructor-bound classes
- parameterless setter-bound classes
- nested imported classes
- arrays, `List<T>`, and `Nullable<T>`
- rename/ignore/required metadata for `System.Text.Json`, `Newtonsoft.Json`, and `DataContract`

## Explicitly unsupported

These fail closed instead of being partially emulated:

- custom converter attributes
- extension-data bags
- polymorphic contracts
- recursive graphs
- classes that mix constructor-bound and setter-bound members

For the reasoning behind those boundaries, see [C# Attribute Bridge Design](CSHARP_ATTRIBUTE_BRIDGE.md).
