# Tasks

Active work only. Historical completed work lives in [notes/AGENT_NOTES.md](notes/AGENT_NOTES.md) and [AGENTS.md](AGENTS.md).

- [ ] **Task 42: Delete the old boxed schema engine completely**
  - Remove `ISchema`, `SchemaDefinition`, `SchemaField`, and `obj[] -> obj` record construction from the active implementation.
  - Move the public authored DSL to `Schema.*` over `Schema<'T>`.
  - Delete the old schema DSL instead of keeping compatibility shims.
  - Retarget `Json`, `Xml`, `Yaml`, `KeyValue`, and `JsonSchema` to compile from the new boxed contract IR directly.
  - Remove any `Lowering.lower` or legacy boxed-schema bridge from the runtime path.
  - Update tests, benchmarks, bridge code, and docs to the new surface.
  - Completion bar: there is no old boxed DSL left, publicly or internally.

- [ ] **Task 49: Review and improve the new DSL for DX**
  - After Task 42, review the new `Schema.*` surface for compactness, clarity, and maintainability.
  - Capture improvements in `PLAN-TO-IMPROVE-DSL`.
  - Do not implement that review directly in the same pass unless it is required to complete Task 42.

- [ ] **Task 37: Add structured decode error outputs for app boundaries**
  - Provide a structured error model that callers can use for REST responses, startup config failures, and message rejection logs.
  - Preserve the existing path-aware detail across JSON, XML, YAML, and KeyValue.
  - Keep the fast path cheap when callers still want exception-based failures.

- [ ] **Task 40: Add explicit unknown-field policy controls**
  - Support contract-level decisions for rejecting, allowing, or collecting unknown fields during decode.
  - Keep the default behavior conservative and aligned with explicit authored contracts.

- [ ] **Task 18: Add code generation modes for contracts**
  - Generate checked-in F# or C# contract code from CLR models, deterministic JSON examples, or imported contract inputs.
  - Keep generators in separate `.NET`-only projects so reflection-heavy tooling does not leak into the AOT/Fable-safe core.

- [ ] **Task 43: Improve the C# authoring story**
  - Expand the C# contract authoring DSL.
  - Add opt-in source generation layered on top of the stable runtime DSL.

- [ ] **Task 44: Add OpenAPI and schema-ecosystem guidance**
  - Document how exported JSON Schema can feed external OpenAPI or validation toolchains.
  - Evaluate whether a narrow OpenAPI bridge belongs in an opt-in package.

- [ ] **Task 45: Strengthen external-schema import and lowering**
  - Improve guidance and support for contracts that start from JSON Schema rather than authored `Schema<'T>`.

- [ ] **Task 46: Publish a JSON-first onboarding path**
  - Add a short path for developers who only care about JSON first.

- [ ] **Task 47: Expand contract-pattern coverage**
  - Add polished patterns for string enums, message envelopes, PATCH/update DTOs, event payloads, and layered config sections.
