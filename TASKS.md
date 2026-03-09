# Tasks

This file tracks the active forward-looking queue for `CodecMapper`.

Completed rename, benchmarking, parser, and first-pass C# bridge work now live in [notes/AGENT_NOTES.md](notes/AGENT_NOTES.md) and [AGENTS.md](AGENTS.md).

- [x] **Task 15: Add extensive API documentation in code**
  - Add XML/API documentation at module and function level across the public surface.
  - Cover the schema DSL, JSON/XML compile and runtime functions, mapping helpers, and common built-in schemas.
  - Integrate the preserved CodecMapper logo into the primary docs surface so API docs and repository docs share the same branding.
  - Keep the docs aligned with the actual supported behavior and edge cases already recorded in `notes/AGENT_NOTES.md`.
  - *Done:* the core public surface now has inline XML/API docs across the structural schema types, byte/runtime primitives, schema DSL, built-in schemas, and JSON/XML compile and runtime entry points. The generated API docs now expose that coverage.

- [x] **Task 16: Add an API documentation generator**
  - Choose and wire an API doc tool that works well for F# and checked-in docs output.
  - Make generation reproducible and easy to run locally.
  - Ensure the generated docs can pick up the main repo branding assets, including the preserved logo.
  - Decide whether generated API docs should be committed or treated as build artifacts.
  - *Done:* `fsdocs` is now wired through `scripts/generate-api-docs.sh`, the landing page lives in `docs/index.md`, the preserved logo is part of the main docs tree, and generated output stays uncommitted under `output/`.

- [x] **Task 17: Expand the contract bridge surface**
  - Add `DataContract` / `DataMember` import support as another explicit bridge flavor.
  - Add explicit failure tests for unsupported converter, polymorphism, and extension-data attributes.
  - Decide whether mixed constructor-plus-setter classes should remain unsupported or gain a deterministic policy.
  - Add bridge examples to `GETTING_STARTED`.
  - *Done:* `DataContract` / `DataMember` import support now ships alongside the existing STJ and Newtonsoft importers. Unsupported converter, extension-data, polymorphism, and mixed-binding cases are now covered by explicit failures and tests, and bridge usage is documented in `GETTING_STARTED`.

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

- [ ] **Task 24: Split product, tests, and benchmarks into top-level folders**
  - Move runtime libraries under a dedicated top-level `src/` root.
  - Move test projects under a dedicated top-level `tests/` root.
  - Move benchmark and comparison runners under a dedicated top-level `benchmarks/` root.
  - Update solution/project references, scripts, hooks, and docs to match the new layout.

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

- [ ] **Task 23: Broaden field-policy controls for config-style contracts**
  - Decide whether missing/null/empty handling should grow beyond `Schema.missingAsNone` and `Schema.emptyStringAsNone`.
  - Evaluate empty-collection treatment, explicit defaults, and whether JSON/XML behavior should stay symmetric here.
  - Keep strict message-contract behavior as the default.

- [ ] **Task 25: Remove legacy comparison shims**
  - Remove `CodecMapper.LegacyShim` and any remaining compatibility glue for the archived experimental repo.
  - Decide whether old-vs-new comparison stays as a standalone manual benchmark or is retired entirely.
  - Clean up docs and benchmark notes that still describe the shim path.
