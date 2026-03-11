# C# Attribute Bridge Design

This note records the bridge design boundaries for importing C# serializer metadata into `CodecMapper`.

It is an internal architecture note, not part of the user-facing docs set. User guidance lives in `docs/HOW_TO_IMPORT_CSHARP_CONTRACTS.md`.

## Goals

- Let C# users move incrementally from `System.Text.Json`, `Newtonsoft.Json`, or `DataContract` contracts into explicit `CodecMapper` schemas.
- Keep the imported surface aligned with `CodecMapper` semantics instead of implying serializer-feature parity.
- Make unsupported cases fail early and explicitly.

## Placement

- Keep the bridge out of the core portable package.
- Keep runtime import in the `.NET`-only bridge assembly.
- Keep any future code generation path separate again from the runtime importer.

## Recommended API shape

Runtime reflection importer:

```fsharp
module CodecMapper.Bridge.SystemTextJson =
    val import<'T> : BridgeOptions -> Schema<'T>

module CodecMapper.Bridge.NewtonsoftJson =
    val import<'T> : BridgeOptions -> Schema<'T>
```

Suggested shared options:

```fsharp
type NamingPolicy =
    | Exact
    | CamelCase
    | SnakeCaseLower
    | SnakeCaseUpper
    | KebabCaseLower
    | KebabCaseUpper

type BridgeOptions =
    {
        DefaultNaming: NamingPolicy
        IncludeFields: bool
        RespectNullableAnnotations: bool
    }
```

`DefaultNaming` exists because many C# projects rely on serializer options rather than per-member attributes, and those global options are not visible from type metadata alone.

## Supported attribute slices

Initial `System.Text.Json` slice:

- `JsonPropertyName`
- unconditional `JsonIgnore`
- `JsonRequired`
- `JsonConstructor`

Initial `Newtonsoft.Json` slice:

- `JsonProperty(PropertyName = ...)`
- `JsonIgnore`
- `JsonRequired`
- `JsonConstructor`

Initial `DataContract` slice:

- data-member rename and required metadata that maps cleanly into normal schema fields

## Unsupported areas

Fail closed on:

- custom converters
- extension-data bags
- polymorphic contracts
- reference-preservation settings
- callback attributes
- conditional ignore behavior
- naming strategy features that do not map to one fixed schema

## Constructor policy

Constructor selection should be deterministic:

1. If exactly one supported serializer constructor attribute is present, use it.
2. Otherwise, if there is exactly one public constructor, use it.
3. Otherwise, fail with a clear error.

For `System.Text.Json`, constructor parameter matching should stay aligned with CLR member names rather than renamed wire names.

## Member policy

Import only members that are:

- publicly readable
- writable through the chosen constructor or a supported setter path
- representable through the existing schema system

Duplicate wire names should fail immediately.

## Why the bridge is conservative

The bridge should build ordinary `Schema<'T>` values, not a serializer-emulation layer. That keeps the imported result usable across the normal `CodecMapper` surface and avoids inventing a second contract system.

## Future direction

If the bridge grows, the better long-term path is likely source generation or explicit code generation rather than pushing more runtime reflection and serializer-specific behavior into the importer.
