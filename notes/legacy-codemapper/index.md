# CodecMapper Documentation

Welcome to the CodecMapper documentation. CodecMapper is a symmetric mapping library for F# designed for performance and Native AOT compatibility.

## Sections

- **[Getting Started](./GETTING_STARTED.md)**: A high-level introduction to the library, its core concepts, and basic usage.
- **[API Reference](./reference/index.html)**: A detailed reference of all public modules, types, and functions.
- **[JSON Mapping Scenarios](./examples/json-scenarios.md)**: Practical examples of common API mapping patterns (GitHub, npm, Stripe, etc.).

## Key Features

- **Symmetry**: Define once, decode and encode with the same model.
- **Native AOT**: Zero reflection or expression trees at runtime.
- **Performance**: High-speed streaming decode path via `Utf8JsonReader`.
- **Type Safety**: Compile-time validation of your mapping logic.
