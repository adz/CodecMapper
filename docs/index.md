# CodecMapper

![CodecMapper logo](logo.png)

`CodecMapper` is a schema-first serialization library for F# focused on explicit contracts, symmetric encode/decode behavior, and portability across .NET AOT and Fable-oriented targets.

## Tutorials

Tutorials are for first learning.

- [Getting Started](GETTING_STARTED.md)
  Learn the core schema DSL, the compile-and-reuse workflow, the C# starting paths, and where JSON Schema fits.

## How-To Guides

How-to guides are for goal-oriented tasks.

- [How To Model Common Contract Patterns](HOW_TO_MODEL_COMMON_CONTRACT_PATTERNS.md)
  Start from common record, wrapper, collection, and contract patterns.
- [Configuration As An Explicit Contract](CONFIG_CONTRACTS.md)
  Model versioned application configuration in JSON, YAML, XML, or key/value form.
- [How To Export JSON Schema](HOW_TO_EXPORT_JSON_SCHEMA.md)
  Generate JSON Schema documents from authored `Schema<'T>` contracts.
- [How To Import Existing C# Contracts](HOW_TO_IMPORT_CSHARP_CONTRACTS.md)
  Choose between `CSharpSchema`, the serializer-attribute bridge, and JSON Schema import for C#-heavy systems.
- [How To Profile Benchmark Hot Paths](HOW_TO_PROFILE_BENCHMARK_HOT_PATHS.md)
  Capture repeatable `perf` data for benchmarked JSON hot paths.

## Reference

Reference docs are for supported behavior and lookup.

- [JSON Schema Support Reference](JSON_SCHEMA_SUPPORT.md)
  Check the supported export/import surface and fallback boundaries.
- [API Reference](reference/index.html)
  Browse the public API surface generated from the inline docs.

## Explanations

Explanations are for design reasoning and mental models.

- [JSON Schema in CodecMapper](JSON_SCHEMA_EXPLANATION.md)
  Understand the structural parsing model and when raw JSON fallback is appropriate.

## Product Summary

- Author one explicit schema and compile it into reusable codecs.
- Keep encode and decode semantics together.
- Reuse the same contract across JSON and XML, with config-oriented YAML and KeyValue projections where appropriate.
- Keep dynamic JSON receive paths explicit through `Schema.jsonValue` and JSON Schema import boundaries.
