# How To Model Common Contract Patterns

Use this guide when you already know the kind of contract you want to model and need a copy-pasteable starting point.

All examples stay on the stable authored DSL:

```fsharp
Schema.define<'T>
|> Schema.construct ctor
|> Schema.field ...
|> Schema.build
```

## Basic record

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

let codec = Json.codec personSchema
```

## Nested record

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

Use `Schema.fieldWith` when the child value has its own explicit schema boundary.

## Validated wrapper

```fsharp
type UserId = UserId of int

module UserId =
    let create value =
        if value > 0 then Ok(UserId value)
        else Error "UserId must be positive"

    let value (UserId value) = value

type Account = { Id: UserId; Name: string }
let makeAccount id name = { Id = id; Name = name }

let userIdSchema =
    Schema.int
    |> Schema.tryMap UserId.create UserId.value

let accountSchema =
    Schema.define<Account>
    |> Schema.construct makeAccount
    |> Schema.fieldWith "id" _.Id userIdSchema
    |> Schema.field "name" _.Name
    |> Schema.build
```

If a wrapper rule repeats across multiple contracts, extract that `Schema.tryMap` pipeline into a named schema value and reuse it explicitly.

## Versioned contract

For config files or messages that evolve over time, use an explicit envelope:

```fsharp
type SettingsV2 = {
    Version: int
    Mode: string
    Region: string option
}

let makeSettingsV2 version mode region =
    { Version = version; Mode = mode; Region = region }

let settingsV2Schema =
    Schema.define<SettingsV2>
    |> Schema.construct makeSettingsV2
    |> Schema.fieldWith "version" _.Version (
        Schema.int
        |> Schema.tryMap
            (fun value ->
                if value > 0 then Ok value
                else Error "version must be positive")
            id
    )
    |> Schema.field "mode" _.Mode
    |> Schema.field "region" _.Region
    |> Schema.build
```

If you need defaults and omission policies, compose the field-policy helpers directly at the field boundary.

## Config contract

Compile the same authored schema into config-oriented projections when the wire surface is flat or YAML-shaped:

```fsharp
let keyValueCodec = KeyValue.codec settingsV2Schema
let yamlCodec = Yaml.codec settingsV2Schema
```

For a larger walkthrough of versioned configuration patterns, see [Config Contracts Guide](CONFIG_CONTRACTS.md).

## External JSON Schema import

Use the importer when another system owns the contract and you need a deterministic receive-side shape:

```fsharp
let report =
    JsonSchema.importWithReport """
    {
      "type": "object",
      "properties": {
        "kind": { "type": "string" },
        "count": { "type": "integer" }
      },
      "required": [ "kind", "count" ]
    }
    """

let codec = Json.codec report.Schema
```

Imported schemas target `Schema<JsonValue>`. Keep authored `Schema<'T>` values as the source of truth when you control the contract.

## C# contract bridge

If you already have existing C# contracts, import them through the bridge or author a new explicit schema through the C# facade:

```csharp
var userSchema =
    CSharpSchema.Record(() => new User())
        .Field("id", x => x.Id, (x, field) => x.Id = field)
        .Field("display_name", x => x.DisplayName, (x, field) => x.DisplayName = field)
        .Build();

var jsonCodec = CSharpSchema.Json(userSchema);
var yamlCodec = CSharpSchema.Yaml(userSchema);
```

For bridge import details and tradeoffs, see [How To Import Existing C# Contracts](HOW_TO_IMPORT_CSHARP_CONTRACTS.md) and [C# Attribute Bridge Design](CSHARP_ATTRIBUTE_BRIDGE.md).
