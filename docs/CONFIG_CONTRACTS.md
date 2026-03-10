# Configuration As An Explicit Contract

This guide shows how to stop treating configuration as an incidental serializer shape and start treating it as an explicit contract.

The core idea is simple:

- define a schema for the wire format on purpose
- add an explicit `version` field
- read old versions and upgrade them forward
- write only the latest version
- standardize on JSON going forward

If you currently have a mix of JSON and XML config files, the recommendation here is:

- **JSON becomes the canonical config format**
- **XML is migration input only**
- **the application always writes the latest JSON form**

If you also need a flat key/value view for environment variables or app-settings style inputs, treat that as a projection of the same authored schema rather than as a second implicit contract.

If you need a human-edited text format without dropping back to serializer conventions, the same schema can also compile to the library's small YAML subset for mappings, sequences, scalars, and `null`.

## Why This Helps

Unversioned config files are brittle because:

- field changes are hard to track
- serializer behavior becomes the contract by accident
- migrations are implicit and easy to break
- XML and JSON drift apart over time

An explicit contract gives you:

- reviewable wire shape
- planned version changes
- controlled upgrade logic
- a clean boundary between wire format and domain model

## Flat Key/Value Projection

For environment variables or app-settings style surfaces, you can compile the same schema into flat `string,string` pairs:

```fsharp
let codec = KeyValue.compileUsing KeyValue.Options.environment yourConfigSchema

let values =
    KeyValue.serialize codec {
        ServiceUrl = "https://api.example.com"
        RetryCount = 3
        Mode = "strict"
    }
```

That produces keys like:

```text
SERVICEURL=https://api.example.com
RETRYCOUNT=3
MODE=strict
```

Use this for flat config-style boundaries only. Collections, raw JSON, and other non-flat shapes should stay on JSON or XML until there is an explicit normalization story.

## Explicit Missing Defaults

When a config field should fall back to a known value only when it is absent, keep that policy explicit in the schema:

```fsharp
type AppConfig =
    {
        Mode: string
        RetryCount: int
        Labels: string list
    }

let appConfigSchema =
    Schema.define<AppConfig>
    |> Schema.construct (fun mode retryCount labels ->
        {
            Mode = mode
            RetryCount = retryCount
            Labels = labels
        })
    |> Schema.fieldWith "mode" _.Mode (Schema.string |> Schema.missingAsValue "strict")
    |> Schema.fieldWith "retry_count" _.RetryCount (Schema.int |> Schema.missingAsValue 3)
    |> Schema.fieldWith "labels" _.Labels (Schema.list Schema.string |> Schema.missingAsValue [])
    |> Schema.build
```

That keeps the default local to the contract instead of smuggling it through serializer settings or post-deserialize mutation.

## Explicit Null And Empty Collection Policies

Some config boundaries treat an explicit `null` or an explicit empty collection as "use the contract default" rather than as a distinct payload state. Keep that normalization local to the field too:

```fsharp
type ServiceConfig =
    {
        Region: string
        Labels: string list
    }

let serviceConfigSchema =
    Schema.define<ServiceConfig>
    |> Schema.construct (fun region labels ->
        {
            Region = region
            Labels = labels
        })
    |> Schema.fieldWith "region" _.Region (Schema.string |> Schema.nullAsValue "global")
    |> Schema.fieldWith "labels" _.Labels (Schema.list Schema.string |> Schema.emptyCollectionAsValue [ "general" ])
    |> Schema.build
```

That means:

- missing fields still fail unless you also opt into `Schema.missingAsValue` or `Schema.missingAsNone`
- explicit `null` can map to a contract default with `Schema.nullAsValue`
- explicit `[]` can map to a contract default with `Schema.emptyCollectionAsValue`
- whitespace-only strings stay literal input; there is no implicit trimming or blank-string coercion beyond `Schema.emptyStringAsNone`

## YAML Projection

For hand-edited config files, the same schema can compile to YAML too:

```fsharp
let codec = Yaml.compile yourConfigSchema

let yaml =
    Yaml.serialize codec {
        ServiceUrl = "https://api.example.com"
        RetryCount = 3
        Mode = "strict"
    }
```

That produces a small config-oriented YAML shape such as:

```yaml
service_url: https://api.example.com
retry_count: 3
mode: strict
```

Current YAML scope is intentionally narrow:

- mappings
- sequences
- scalars and `null`
- quoted or plain strings

It does not aim at full YAML feature parity. Anchors, tags, multi-document streams, block scalars, and broader YAML syntax are still out of scope.

## Recommended Shape

Use an explicit versioned envelope:

```json
{
  "version": 2,
  "config": {
    "service_url": "https://api.example.com",
    "retry_count": 3,
    "mode": "strict"
  }
}
```

That is better than a flat object with implicit version inference.

Why:

- the version is visible
- the payload being versioned is explicit
- decoding and upgrading become easier to reason about

## C# First

Start with ordinary C# wire-contract classes if that is where your config already lives.

### Version 1

```csharp
using System.Runtime.Serialization;

[DataContract]
public sealed class AppConfigV1
{
    [DataMember(Name = "service_url", IsRequired = true)]
    public string ServiceUrl { get; set; } = "";

    [DataMember(Name = "retry_count", IsRequired = true)]
    public int RetryCount { get; set; }
}

[DataContract]
public sealed class VersionedConfigV1
{
    [DataMember(Name = "version", IsRequired = true)]
    public int Version { get; set; }

    [DataMember(Name = "config", IsRequired = true)]
    public AppConfigV1 Config { get; set; } = new();
}
```

### Version 2

```csharp
using System.Runtime.Serialization;

[DataContract]
public sealed class AppConfigV2
{
    [DataMember(Name = "service_url", IsRequired = true)]
    public string ServiceUrl { get; set; } = "";

    [DataMember(Name = "retry_count", IsRequired = true)]
    public int RetryCount { get; set; }

    [DataMember(Name = "mode", IsRequired = true)]
    public string Mode { get; set; } = "strict";
}

[DataContract]
public sealed class VersionedConfigV2
{
    [DataMember(Name = "version", IsRequired = true)]
    public int Version { get; set; }

    [DataMember(Name = "config", IsRequired = true)]
    public AppConfigV2 Config { get; set; } = new();
}
```

### Upgrade Path

Keep upgrades explicit:

```csharp
public static class ConfigUpgrades
{
    public static AppConfigV2 Upgrade(AppConfigV1 oldConfig) =>
        new()
        {
            ServiceUrl = oldConfig.ServiceUrl,
            RetryCount = oldConfig.RetryCount,
            Mode = "strict"
        };
}
```

Then the application flow is:

1. read `VersionedConfigV1` or `VersionedConfigV2`
2. upgrade older versions to `AppConfigV2`
3. run the application on `AppConfigV2`
4. write back only `VersionedConfigV2`

## F# Versioned Contracts

The same idea works cleanly in F#.

### Wire Contracts

```fsharp
type AppConfigV1 =
    {
        ServiceUrl: string
        RetryCount: int
    }

type AppConfigV2 =
    {
        ServiceUrl: string
        RetryCount: int
        Mode: string
    }

type VersionedConfig =
    | V1 of AppConfigV1
    | V2 of AppConfigV2
```

If you want the wire format to stay as an explicit envelope, you can model that directly too:

```fsharp
type VersionEnvelope<'T> =
    {
        Version: int
        Config: 'T
    }
```

Then deserialize by version and upgrade to the latest contract.

### Upgrade Functions

```fsharp
module ConfigUpgrades =
    let toV2 (oldConfig: AppConfigV1) : AppConfigV2 =
        {
            ServiceUrl = oldConfig.ServiceUrl
            RetryCount = oldConfig.RetryCount
            Mode = "strict"
        }
```

### Latest-Only Write Path

```fsharp
type CurrentConfig = AppConfigV2

let serializeCurrent (config: CurrentConfig) =
    { Version = 2; Config = config }
```

The important policy is:

- read many versions
- write one version

That stops config churn from spreading throughout the app.

## Schema As The Contract

With `CodecMapper`, the wire contract should be explicit in the schema, not inferred accidentally from serializer defaults.

A versioned envelope schema is the right place to make changes visible:

```fsharp
type AppConfigV2 =
    {
        ServiceUrl: string
        RetryCount: int
        Mode: string
    }

type VersionEnvelope<'T> =
    {
        Version: int
        Config: 'T
    }

module Schemas =
    let appConfigV2 =
        Schema.define<AppConfigV2>
        |> Schema.construct (fun serviceUrl retryCount mode ->
            {
                ServiceUrl = serviceUrl
                RetryCount = retryCount
                Mode = mode
            })
        |> Schema.field "service_url" _.ServiceUrl
        |> Schema.field "retry_count" _.RetryCount
        |> Schema.field "mode" _.Mode
        |> Schema.build
```

Then wrap that with a schema for the envelope:

```fsharp
module Schemas =
    let versionEnvelope inner =
        Schema.define<VersionEnvelope<'T>>
        |> Schema.construct (fun version config -> { Version = version; Config = config })
        |> Schema.field "version" _.Version
        |> Schema.fieldWith "config" _.Config inner
        |> Schema.build
```

The latest version should be the one you serialize.

## Move XML To Read-Only Migration

If XML exists today, deprecate it in stages.

### Recommended policy

1. Stop writing XML.
2. Keep XML read support only long enough to migrate existing installs.
3. Convert loaded XML immediately into the latest in-memory config.
4. Save back as latest-version JSON.
5. Remove XML support after the migration window.

### Why this is worth doing

- one canonical config format
- fewer tests
- fewer docs
- fewer hidden serializer mismatches

Do not keep XML and JSON as equal first-class config formats unless you have a hard external compatibility requirement.

## Separate Wire Contracts From Better Domain Models

A config file often starts with plain strings and ints because that is what legacy code or external tools expect.

That does not mean the application must stay modeled that way internally.

Use a staged approach:

### Stage 1: stable wire contract

```fsharp
type AppConfigV2 =
    {
        ServiceUrl: string
        RetryCount: int
        Mode: string
    }
```

### Stage 2: richer in-memory domain model

```fsharp
type Mode =
    | Strict
    | Lenient

type ServiceUrl = private ServiceUrl of string

type DomainConfig =
    {
        ServiceUrl: ServiceUrl
        RetryCount: int option
        Mode: Mode
    }
```

### Stage 3: explicit mapping between the two

```fsharp
module DomainConfig =
    let fromWire (config: AppConfigV2) : DomainConfig =
        {
            ServiceUrl = ServiceUrl config.ServiceUrl
            RetryCount =
                if config.RetryCount = 0 then None else Some config.RetryCount
            Mode =
                match config.Mode with
                | "strict" -> Strict
                | "lenient" -> Lenient
                | other -> failwithf "Unknown mode: %s" other
        }
```

This is the key migration idea:

- the wire contract remains explicit and stable
- the domain model gets better over time
- conversion between them is intentional and reviewable

## Options And DUs

Two modeling improvements usually pay off quickly.

### Options

If a value is genuinely optional in the application, model it as `option` in the domain.

You do not have to expose that immediately in the wire format. Legacy wire contracts can still use sentinel values or old fields while the domain becomes clearer first.

### Discriminated Unions

If a config field is really a closed set of modes, providers, or strategies, a DU is better than an unbounded string.

Example:

```fsharp
type AuthMode =
    | Anonymous
    | ApiKey
    | OAuth
```

That is much safer than:

```fsharp
type AppConfigV2 =
    {
        AuthMode: string
    }
```

Again, the wire contract can stay string-based at first while the domain moves to a DU through an explicit mapping layer.

## Practical Migration Pattern

A good working pattern is:

1. define versioned wire contracts
2. define schemas explicitly
3. read old versions
4. upgrade to latest wire contract
5. map latest wire contract to richer domain config
6. run the app on domain config
7. serialize only the latest wire contract as JSON

That gives you stable external contracts and steadily improving internal models.

## What To Avoid

Avoid these traps:

- one giant config record with many optional legacy fields
- unversioned config files
- serializer defaults becoming the contract by accident
- dual XML/JSON write paths
- mixing wire concerns and domain concerns in the same type forever

## Recommendation

For configuration and message-like contracts, prefer:

- explicit schemas
- explicit version envelopes
- latest-only write policy
- JSON as the canonical format
- wire-contract types separate from richer domain types

That is the foundation that makes later bridge/codegen/schema-export work make sense instead of becoming another layer of serializer guesswork.
