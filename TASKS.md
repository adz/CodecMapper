# Tasks

This file tracks the active forward-looking queue for `CodecMapper`.

Completed rename, benchmarking, parser, and first-pass C# bridge work now live in [docs/AGENT_NOTES.md](docs/AGENT_NOTES.md) and [AGENTS.md](AGENTS.md).

- [ ] **Task 15: Add extensive API documentation in code**
  - Add XML/API documentation at module and function level across the public surface.
  - Cover the schema DSL, JSON/XML compile and runtime functions, mapping helpers, and common built-in schemas.
  - Keep the docs aligned with the actual supported behavior and edge cases already recorded in `docs/AGENT_NOTES.md`.

- [ ] **Task 16: Add an API documentation generator**
  - Choose and wire an API doc tool that works well for F# and checked-in docs output.
  - Make generation reproducible and easy to run locally.
  - Decide whether generated API docs should be committed or treated as build artifacts.

- [ ] **Task 17: Expand the contract bridge surface**
  - Add `DataContract` / `DataMember` import support as another explicit bridge flavor.
  - Add explicit failure tests for unsupported converter, polymorphism, and extension-data attributes.
  - Decide whether mixed constructor-plus-setter classes should remain unsupported or gain a deterministic policy.
  - Add bridge examples to `GETTING_STARTED`.
  - *Progress:* `DataContract` / `DataMember` import support is now implemented alongside the existing STJ and Newtonsoft importers. Remaining work is around unsupported/failure coverage, policy tightening, and docs/examples.

- [ ] **Task 18: Add code generation modes for codecs**
  - Support generating codec/schema code from message-contract definitions.
  - Support generating codec/schema code from JSON examples or schema-like JSON inputs where the mapping is deterministic enough.
  - Support generating codec/schema code from CLR models.
  - Support both C# and F# records/classes as generator inputs.
  - Prefer readable checked-in output over opaque build-only generation.

- [ ] **Task 19: Export JSON Schema from `Schema<'T>`**
  - Generate JSON Schema for message contracts and external validation/docs.
  - Base the export on `Schema<'T>` itself, not only on imported C# models.
  - Define how `Schema.map` / `Schema.tryMap` project to JSON Schema and where metadata is needed.

- [ ] **Task 20: Broaden common collection and interop type support**
  - Evaluate `IReadOnlyList<T>`, `ICollection<T>`, dictionaries, and enums.
  - Only add shapes that preserve symmetric encode/decode semantics cleanly.

- [x] **Task 21: Add a config contracts migration guide**
  - Write a guide for treating configuration as an explicit schema contract rather than an incidental serializer shape.
  - Show JSON as the canonical config format and XML as deprecated migration input only.
  - Show versioned config envelopes with an explicit `version` field and upgrade functions from older versions to the latest.
  - Present C# examples first, then F# examples.
  - Show how stronger domain modeling can be introduced over time, including options, discriminated unions, and a distinction between wire contracts and richer in-memory domain types.
  - *Done:* Added [docs/CONFIG_CONTRACTS.md](docs/CONFIG_CONTRACTS.md) with JSON-first guidance, versioned envelope examples, XML deprecation guidance, C#-first and F# follow-up examples, and a staged migration from wire contracts to richer domain models.

- [ ] **Task 22: Add a thin C# facade over schema authoring and execution**
  - Explore a fluent builder-pattern facade for C# over the existing schema model.
  - Keep the F# DSL canonical; the C# facade should be a wrapper, not a second implementation.
  - Evaluate where the facade helps and where bridge/codegen remains the better path.
