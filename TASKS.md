# Tasks

This file tracks the active forward-looking queue for `CodecMapper`.

Completed rename, benchmarking, parser, and first-pass C# bridge work now live in [notes/AGENT_NOTES.md](notes/AGENT_NOTES.md) and [AGENTS.md](AGENTS.md).

- **Completed**
- [x] **Task 15:** Inline API docs now cover the public schema/runtime surface, and the generated docs expose that coverage.
- [x] **Task 16:** `fsdocs` is wired through `scripts/generate-api-docs.sh`; the landing page and branding live under `docs/`, and generated output stays uncommitted in `output/`.
- [x] **Task 17:** `DataContract` / `DataMember` bridge import now ships alongside STJ/Newtonsoft support, with explicit failure coverage and updated getting-started docs.
- [x] **Task 19:** `JsonSchema.generate` exports draft 2020-12 JSON Schema directly from `Schema<'T>`, with docs and tests covering record, primitive, array, option, and mapped/common-type shapes.
- [x] **Task 20a:** The AOT/Fable compatibility story is documented, and CI now runs both the .NET sentinel app and `scripts/check-fable-compat.sh`.
- [x] **Task 21:** Added [docs/CONFIG_CONTRACTS.md](docs/CONFIG_CONTRACTS.md) as the config-contract migration guide.
- [x] **Task 24:** Public libraries, tests, and benchmarks now live under `src/`, `tests/`, and `benchmarks/`, with tooling/docs updated to match.
- [x] **Task 25:** Removed the legacy shim path; the archived experimental repo remains under `benchmarks/CodecMapper/` for reference only.

- [ ] **Task 18: Add code generation modes for codecs**
  - Support generating codec/schema code from message-contract definitions.
  - Support generating codec/schema code from JSON examples or schema-like JSON inputs where the mapping is deterministic enough.
  - Support generating codec/schema code from CLR models.
  - Support both C# and F# records/classes as generator inputs.
  - Prefer readable checked-in output over opaque build-only generation.

- [x] **Task 19a:** JSON Schema import now has an explicit structural-lowering boundary, raw `Schema<JsonValue>` fallback for dynamic shapes, diagnostics through `importWithReport`, and docs/tests that keep unsupported keywords such as `dependentSchemas`, `not`, and deeper recursive composition clearly out of scope for now.

- [x] **Task 20:** Added array-backed support for `IReadOnlyList<T>` and `ICollection<T>`, the explicit `Schema.resizeArray` helper for concrete `ResizeArray<'T>` / `List<T>` models, and numeric-wire enum support. Direct dictionary support stays out of scope for now because it does not fit the current JSON/XML symmetry model as cleanly as the rest of the DSL.

- [x] **Task 20b:** CI now runs a pinned `Fable 5.0.0-rc.2` transpilation lane via `scripts/check-fable5-compat.sh`, the core float encoder uses a Fable-safe path instead of the unsupported round-trip `"R"` formatter, and both the stable `Fable 4` lane plus the new `Fable 5` lane pass against the shared compatibility sentinel.

- [x] **Task 20c:** Added flat `KeyValue` projection for flattened record/scalar config and environment-style contracts, plus a small YAML codec for config-style mappings, sequences, scalars, and `null`, all driven from the same authored `Schema<'T>` model with docs/tests/sentinel coverage. Full YAML feature parity and broader lossy normalization remain out of scope.

- [ ] **Task 22: Add a thin C# facade over schema authoring and execution**
  - Explore a fluent builder-pattern facade for C# over the existing schema model.
  - Keep the F# DSL canonical; the C# facade should be a wrapper, not a second implementation.
  - Evaluate where the facade helps and where bridge/codegen remains the better path.

- [ ] **Task 23: Broaden field-policy controls for config-style contracts**
  - Decide whether missing/null/empty handling should grow beyond `Schema.missingAsNone` and `Schema.emptyStringAsNone`.
  - Evaluate empty-collection treatment, explicit defaults, and whether JSON/XML behavior should stay symmetric here.
  - Keep strict message-contract behavior as the default.

- [x] **Task 26:** Docs are now organized around Diataxis: the core tutorial stays in `GETTING_STARTED`, JSON Schema docs are split across how-to/reference/explanation, C# contract import has its own how-to, and the docs landing page plus README now navigate by tutorial/how-to/reference/explanation with API docs as the reference anchor.

- [ ] **Task 27: Automate benchmark snapshot publishing**
  - Add a script that runs the stable benchmark path and emits a docs-friendly summary format.
  - Use that script to refresh the README/docs benchmark snapshot instead of editing numbers by hand.
  - Decide whether CI should publish benchmark artifacts, open a docs update, or only validate that the snapshot generator still runs.
  - Keep the benchmark story explicit about machine-specific numbers and comparable payload shape.
