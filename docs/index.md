# CodecMapper

![CodecMapper logo](logo.png)

`CodecMapper` is a schema-first serialization library for F# focused on explicit contracts, symmetric encode/decode behavior, and portability across .NET AOT and Fable-oriented targets.

## Tutorials

- [Getting Started](GETTING_STARTED.md)

Use tutorials when you are learning the library for the first time.

## How-To Guides

- [How To Export JSON Schema](HOW_TO_EXPORT_JSON_SCHEMA.md)
- [Config Contracts Guide](CONFIG_CONTRACTS.md)

Use how-to guides when you already know what you want to accomplish.

## Reference

- [JSON Schema Support Reference](JSON_SCHEMA_SUPPORT.md)
- [API Reference](reference/index.html)

Use reference docs when you need exact supported behavior or API lookup.

## Explanations

- [JSON Schema in CodecMapper](JSON_SCHEMA_EXPLANATION.md)
- [C# Attribute Bridge Design](CSHARP_ATTRIBUTE_BRIDGE.md)

Use explanations when you want the reasoning behind the design.

## Core Ideas

- Define one explicit schema and compile it into reusable codecs.
- Keep encode and decode semantics together, instead of scattering serializer settings across models.
- Treat wire contracts as versioned artifacts that can evolve deliberately.
- Prefer handwritten schema control over reflection-heavy runtime behavior.

## Current Surface

- Pipeline DSL for F# schema authoring
- JSON and XML codecs from the same schema
- Built-in support for common primitive, numeric, option, collection, and time-based types
- Raw JSON fallback via `Schema.jsonValue` for dynamic imported JSON shapes
- .NET-only bridge importers for `System.Text.Json`, `Newtonsoft.Json`, and `DataContract`

## Compatibility

- `CodecMapper` keeps the portable codec surface in the core [CodecMapper](reference/codecmapper.html) namespace and isolates the `.NET`-only import story in [CodecMapper.Bridge](reference/codecmapper-bridge.html).
- Native AOT and Fable compatibility are guarded by dedicated sentinel apps in `tests/CodecMapper.AotTests/` and `tests/CodecMapper.FableTests/`.
- CI runs the Fable sentinel twice: once as a normal `.NET` executable and once through the Fable compiler as a transpilation smoke test.
