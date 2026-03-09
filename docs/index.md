# CodecMapper

![CodecMapper logo](logo.png)

`CodecMapper` is a schema-first serialization library for F# focused on explicit contracts, symmetric encode/decode behavior, and portability across .NET AOT and Fable-oriented targets.

## Start Here

- [Getting Started](GETTING_STARTED.md)
- [Config Contracts Guide](CONFIG_CONTRACTS.md)
- [C# Attribute Bridge Design](CSHARP_ATTRIBUTE_BRIDGE.md)
- [API Reference](reference/index.html)

## Core Ideas

- Define one explicit schema and compile it into reusable codecs.
- Keep encode and decode semantics together, instead of scattering serializer settings across models.
- Treat wire contracts as versioned artifacts that can evolve deliberately.
- Prefer handwritten schema control over reflection-heavy runtime behavior.

## Current Surface

- Pipeline DSL for F# schema authoring
- JSON and XML codecs from the same schema
- Built-in support for common primitive, numeric, option, collection, and time-based types
- .NET-only bridge importers for `System.Text.Json`, `Newtonsoft.Json`, and `DataContract`

## Generate Docs

Run:

```bash
bash scripts/generate-api-docs.sh
```

That builds the content pages in `docs/` plus API reference pages for the core `CodecMapper` project into `output/`.
