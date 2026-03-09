# C# Attribute Bridge Design

This document defines the conservative `.NET`-only bridge for importing C# serializer metadata into `CodecMapper` schemas.

The bridge is for migration and interoperability. It is not part of the zero-reflection core.

## Goals

- Let C# users move incrementally from `System.Text.Json` or `Newtonsoft.Json` contracts into explicit `CodecMapper` schemas.
- Keep the imported surface aligned with `CodecMapper`'s current semantics instead of pretending every serializer feature has an equivalent.
- Make unsupported cases fail early and explicitly.

## Placement

- Keep this out of the core package.
- Put the runtime importer in a separate `.NET`-only package, for example:
  - `CodecMapper.Bridge.SystemTextJson`
  - `CodecMapper.Bridge.NewtonsoftJson`
- If code generation is added later, keep it separate again:
  - `CodecMapper.Bridge.Generator`

## Recommended API Shape

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

## Supported `System.Text.Json` Attributes

These are the attributes worth importing first:

- `JsonPropertyName`
  - Maps directly to the field name in `Schema.field` or `Schema.fieldWith`.
- `JsonIgnore`
  - Supported only for unconditional ignore.
  - Conditional ignore modes are serializer-policy concerns, not schema concerns.
- `JsonRequired`
  - Imported as presence metadata.
  - In current `CodecMapper` semantics this is mostly redundant, because missing fields already fail unless the schema explicitly models optionality.
- `JsonConstructor`
  - Used to choose the constructor when multiple public constructors exist.

Useful but not in the first bridge slice:

- `JsonInclude`
  - Only if the bridge intentionally supports fields or non-public setters/getters.
  - Do not imply support accidentally.

Explicitly unsupported in the first slice:

- `JsonConverter`
- `JsonDerivedType`
- `JsonPolymorphic`
- `JsonExtensionData`
- `JsonPropertyOrder`
- `JsonNumberHandling`
- enum string configuration via converter attributes

Why:

- These either require serializer-specific runtime behavior, polymorphism, hidden conversion rules, or order-sensitive formatting that `CodecMapper` does not currently model as schema semantics.

## Supported `Newtonsoft.Json` Attributes

These are the attributes worth importing first:

- `JsonProperty(PropertyName = ...)`
  - Maps to the wire field name.
- `JsonIgnore`
  - Maps to omitted schema fields.
- `JsonRequired`
  - Imported as presence metadata, with the same caveat as STJ.
- `JsonConstructor`
  - Used to choose the constructor when multiple public constructors exist.

Supported only in a reduced form:

- `JsonProperty(Required = ...)`
  - Accept only the cases that map cleanly to current `CodecMapper` semantics.
  - Do not claim parity with Newtonsoft's full null and missing-value behavior.

Explicitly unsupported in the first slice:

- `JsonConverter`
- `JsonObject(MemberSerialization.OptIn)`
- `JsonExtensionData`
- `NamingStrategyType`
- reference-preservation settings
- callback attributes
- type-name handling
- custom contract resolvers

## Constructor Policy

Constructor selection should be deterministic:

1. If exactly one supported serializer constructor attribute is present, use it.
2. Otherwise, if there is exactly one public constructor, use it.
3. Otherwise, fail with a clear error.

Parameter-to-member binding should be by CLR member name first, then serializer-renamed wire name only if that serializer explicitly supports it.

For `System.Text.Json`, the official docs note that `JsonPropertyName` does not affect parameter name matching for parameterized constructors.

## Member Policy

Import only members that meet all of these:

- public readable property, unless a future `JsonInclude` slice is explicitly enabled
- writable through the chosen constructor or a supported setter path
- schema can be resolved for the member type

Duplicate wire names should fail immediately.

## Schema Mapping Rules

The importer should build the same public schema shape that hand-written F# would produce:

- renamed members become `Schema.field "wire_name" ...`
- nested imported types recurse into imported schemas
- types already covered by built-in schemas use the existing `Schema.resolveSchema` path
- explicit per-member custom schema overrides, if added later, should layer on top rather than bypass imported metadata

## Unsupported Type Shapes In V1

Reject these up front:

- polymorphic object graphs
- catch-all extension data bags
- members that require custom converters
- members that rely on conditional ignore behavior
- dictionaries with special key conversion rules
- constructor graphs that cannot be matched deterministically

## Implementation Strategy

Phase 1 should be runtime reflection only and isolated from core.

- Inspect constructors, properties, and attributes.
- Build ordinary `Schema<'T>` values.
- Cache imported schemas per closed type to avoid repeated reflection.
- Keep failure messages specific enough that a C# user can hand-author the schema when import is not possible.

Phase 2 can add source generation or code generation.

That is the better long-term migration path for users who want AOT-friendly explicit schemas without runtime reflection.

## Non-Goals

- Do not emulate every serializer quirk.
- Do not hide unsupported behaviors behind partial success.
- Do not add reflection to `CodecMapper` core.
- Do not make the bridge the canonical authoring model for F#.

## Initial Recommendation

Start with:

- STJ: `JsonPropertyName`, `JsonIgnore`, `JsonRequired`, `JsonConstructor`
- Newtonsoft: `JsonProperty(PropertyName)`, `JsonIgnore`, `JsonRequired`, `JsonConstructor`

Everything else should fail closed until there is a schema-level story that is symmetric for encode and decode.
