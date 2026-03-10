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

- [ ] **Task 19a: Finish JSON Schema import boundary and fallback story**
  - Keep the current structural-lowering-first design: deterministic shapes lower into normal schemas; branch-heavy or dynamic shapes stay on `Schema<JsonValue>`.
  - Preserve the fast path for authored schemas and normal imported record/array/primitive contracts.
  - Convert the remaining open questions into explicit policy:
    `dependentSchemas` and `not` remain unsupported and therefore stay on raw `JsonValue` fallback with `importWithReport` diagnostics rather than partial enforcement.
  - Document that recursive references and any future deeper composition semantics also remain outside the lowered-schema subset unless there is a deterministic normalization story.
  - Capture the acceptance criteria in docs and tests so the fallback boundary is deliberate:
    unsupported keywords must appear in `FallbackKeywords`, supported keywords must stay in `EnforcedKeywords` or `NormalizedKeywords`, and unsupported keywords must not silently alter the fast path for authored schemas.
  - Current baseline:
    `Schema.jsonValue`, `JsonSchema.import`, `JsonSchema.importWithReport`, format validators, local `$ref` normalization, object-shaped `allOf`, `oneOf`, `anyOf`, `if` / `then` / `else`, `type`, `properties`, `required`, `items`, schema-valued `additionalProperties`, `patternProperties`, `propertyNames`, `prefixItems`, `contains`, `enum`, `const`, string and numeric bounds, collection/property count bounds, `pattern`, and configured `format` validation are already implemented and covered.

- [ ] **Task 20: Broaden common collection and interop type support**
  - Evaluate `IReadOnlyList<T>`, `ICollection<T>`, dictionaries, and enums.
  - Only add shapes that preserve symmetric encode/decode semantics cleanly.

- [ ] **Task 20b: Add an explicit Fable 5 compatibility baseline**
  - Decide whether the repo should pin `Fable 5` directly or run a second compatibility lane against that version.
  - Add a stable `Fable 5` transpilation check once the version choice is deliberate and repeatable in CI.

- [ ] **Task 22: Add a thin C# facade over schema authoring and execution**
  - Explore a fluent builder-pattern facade for C# over the existing schema model.
  - Keep the F# DSL canonical; the C# facade should be a wrapper, not a second implementation.
  - Evaluate where the facade helps and where bridge/codegen remains the better path.

- [ ] **Task 23: Broaden field-policy controls for config-style contracts**
  - Decide whether missing/null/empty handling should grow beyond `Schema.missingAsNone` and `Schema.emptyStringAsNone`.
  - Evaluate empty-collection treatment, explicit defaults, and whether JSON/XML behavior should stay symmetric here.
  - Keep strict message-contract behavior as the default.

- [ ] **Task 26: Reorganize docs around Diataxis**
  - Classify each user-facing document as a tutorial, how-to guide, technical reference, or explanation.
  - Restructure the docs landing page and README links around that purpose-based navigation.
  - Add missing JSON Schema docs in the right categories rather than treating all prose as a single getting-started guide.
  - Keep API docs as the reference anchor and ensure new feature work adds docs in the appropriate category.

- [ ] **Task 27: Automate benchmark snapshot publishing**
  - Add a script that runs the stable benchmark path and emits a docs-friendly summary format.
  - Use that script to refresh the README/docs benchmark snapshot instead of editing numbers by hand.
  - Decide whether CI should publish benchmark artifacts, open a docs update, or only validate that the snapshot generator still runs.
  - Keep the benchmark story explicit about machine-specific numbers and comparable payload shape.
