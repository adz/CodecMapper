# CodecMapper Namespace

The `CodecMapper` namespace contains the core schema model and the JSON/XML compilation entry points.

## Main Areas

- `Schema`
  Builds explicit schemas and provides the built-in primitive, collection, option, and mapping helpers.
- `Json`
  Compiles a schema into a reusable JSON codec.
- `Xml`
  Compiles the same schema model into a reusable XML codec.
- `Core`
  Exposes the low-level byte source and writer primitives used by the runtime.

## Recommended Entry Points

- Start with [Getting Started](../GETTING_STARTED.md) for handwritten schema authoring.
- Use `Json.compile` or `Xml.compile` once per schema and reuse the resulting codec.
- Use `Schema.tryMap` when decode should enforce a smart constructor or validation rule.
