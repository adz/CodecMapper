# Tasks

This file tracks the active forward-looking queue for `CodecMapper`.

Completed rename, parser, bridge, compatibility, JSON Schema, docs, and projection work now lives in [notes/AGENT_NOTES.md](notes/AGENT_NOTES.md) and [AGENTS.md](AGENTS.md).

- [ ] **Task 18: Add code generation modes for codecs**
  - Support generating codec/schema code from message-contract definitions.
  - Support generating codec/schema code from JSON examples or schema-like JSON inputs where the mapping is deterministic enough.
  - Support generating codec/schema code from CLR models.
  - Support both C# and F# records/classes as generator inputs.
  - Prefer readable checked-in output over opaque build-only generation.
  - Keep the generator in a separate `.NET`-only project so reflection-heavy analysis and templates do not bleed into the AOT/Fable-safe core assembly.
  - Generate ordinary checked-in F# schema code rather than introducing a second runtime schema system.
  - Treat CLR-model analysis, JSON-example scaffolding, and imported-contract scaffolding as `.NET`-only tooling layered on top of the stable runtime DSL.
  - Keep generated output reviewable and copy-editable by users.

- [x] **Task 30:** Fixed the docs-site asset root by aligning `PackageProjectUrl` with the GitHub Pages URL instead of the repo URL, and hardened `scripts/generate-api-docs.sh` to clear stale `fsdocs` cache, build the doc assemblies first, and fail if generated output points theme/search assets at `github.com/adz/CodecMapper/...`.

- [x] **Task 29:** Split `src/CodecMapper/Library.fs` into explicit dependency-ordered files (`Core.fs`, `Schema.fs`, `Json.fs`, `JsonSchema.fs`, `Xml.fs`, `KeyValue.fs`, and `Yaml.fs`) and updated `CodecMapper.fsproj` to preserve the existing no-behavior-change compilation order.

- [x] **Task 31:** Improved explicit authoring ergonomics without adding reflection or a competing magic DSL by shipping compile aliases plus opt-in validated helpers such as `Schema.nonEmptyString`, `Schema.trimmedString`, `Schema.positiveInt`, and `Schema.nonEmptyList`, with matching docs and regression coverage.

- [x] **Task 32:** Added path-aware decode diagnostics across `Json`, `Xml`, `KeyValue`, and `Yaml`, including missing-field paths, collection indices/items, and `Schema.tryMap` validation context, with matching regression coverage in the unit test suite.

- [x] **Task 33:** Added a canonical contract-pattern guide covering basic records, nested records, validated wrappers, versioned contracts, config contracts, JSON Schema import, and the C# bridge, and linked it from the README and docs landing pages so the copy-paste patterns are easy to find.

- [x] **Task 35: Add property-based test coverage for codec laws**
  - Added `FsCheck.Xunit`-backed round-trip properties in `tests/CodecMapper.Tests` for representative nested-record, option, and collection schemas across both JSON and XML.
  - Kept the generators inside the supported deterministic surface so failures stay debuggable and align with the library's intentional JSON/XML subset.

- [x] **Task 36: Add repeatable profiling workflow for benchmark hot paths**
  - Add a repo-local profiling harness around the benchmark runner so CPU and allocation investigations are repeatable instead of one-off terminal sessions.
  - Prefer local tooling that exists on the machine today; `perf` is available, while `dotnet-trace` and `dotnet-counters` are not.
  - First slice landed: the benchmark runner now has a focused `profile` mode, and `scripts/profile-benchmark-hot-path.sh` captures `perf stat`, `perf.data`, injected JIT symbols, and a text report under `.artifacts/profiling/`.
  - Capture at least one checked-in workflow for JSON serialize and deserialize hot paths, with outputs that can be inspected as call stacks or folded into flamegraphs.
  - Keep the profiling entry points deterministic and parameterized so later optimization work can compare the same workload before and after changes.
  - Document how to rerun the profiling workflow and where generated artifacts land so it becomes part of normal performance work, not tribal knowledge.

- [ ] **Task 37: Add structured decode error outputs for app boundaries**
  - Provide a structured error model that callers can use for REST responses, startup config failures, and message rejection logs instead of relying only on formatted exception text.
  - Preserve the existing path-aware detail across JSON, XML, YAML, and KeyValue.
  - Keep the fast path cheap when callers still want exception-based decode failures.
  - Document how to map the structured errors into common HTTP and configuration-reporting workflows.

- [ ] **Task 38: Publish end-to-end REST and config integration guides**
  - Add one JSON-first HTTP guide that shows request decode, response encode, and decode-failure handling in a realistic endpoint flow.
  - Add one configuration-loading guide that shows layered environment/file input, explicit defaults, startup validation, and friendly failure reporting.
  - Keep the examples grounded in the existing stable DSL rather than introducing framework-specific schema systems.

- [ ] **Task 39: Improve union and enum authoring ergonomics for app contracts**
  - Add higher-level helpers for common string-enum, message-envelope, and public API union shapes so users do not have to hand-write projector/injector code for the most common cases.
  - Keep the explicit authored contract visible rather than hiding it behind reflection or attributes.
  - Cover JSON, XML, YAML, KeyValue, and JSON Schema export behavior for any new helpers.

- [ ] **Task 40: Add explicit unknown-field policy controls**
  - Support contract-level decisions for rejecting, allowing, or collecting unknown fields during decode.
  - Keep the default behavior conservative and symmetric with the library's explicit-contract goals.
  - Document how those policies apply differently for config-style inputs versus message/API boundaries.

- [ ] **Task 41: Add migration guidance from `System.Text.Json` and `Newtonsoft.Json`**
  - Show how to move one config type, one message contract, and one API DTO over incrementally without rewriting an entire system.
  - Explain where `CodecMapper` is a good fit and where the convention-based serializers still remain simpler.
  - Keep the guide focused on practical migration steps rather than abstract comparisons.

- [ ] **Task 42: Evaluate a non-erased typed codec path for hot-path performance**
  - Prototype a compile/runtime path that keeps more concrete type information instead of routing through erased `obj`-based schema nodes in the hottest encode/decode paths.
  - Measure whether that materially improves the current string-heavy and numeric-heavy benchmark weak spots without breaking AOT or Fable support.
  - Treat this as a benchmark-driven architecture decision, not an automatic rewrite.

- [ ] **Task 43: Improve the C# schema authoring DSL and add opt-in source generation**
  - Expand the C# schema authoring surface so common record/class/message contracts are less verbose to write by hand.
  - Add C# source-generation support in a separate opt-in project so generated contracts do not add reflection or Roslyn dependencies to the core runtime assembly.
  - Add F# source-generation or scaffolding support in a separate opt-in project for teams that want checked-in authored schema code from existing models or examples.
  - Keep both generators layered on top of the stable runtime DSL instead of creating a second runtime schema system.
  - Prefer readable generated output and reviewable partial adoption over opaque build-only magic.

- [ ] **Task 44: Add OpenAPI and schema-ecosystem guidance for REST users**
  - Document how exported JSON Schema can feed external API-description or validation toolchains without conflating `CodecMapper` with a full OpenAPI framework.
  - Evaluate whether a narrow OpenAPI bridge is worth adding as an opt-in package rather than bloating the core library.
  - Keep the primary authored contract in `Schema<'T>` even when downstream tooling wants OpenAPI-shaped output.

- [ ] **Task 45: Strengthen the external-schema import and lowering story**
  - Improve guidance and, where practical, implementation support for external contracts that start from JSON Schema rather than authored `Schema<'T>`.
  - Focus especially on the gap between authored recursive/discriminated contracts and imported schema-owned contracts.
  - Keep the raw-JSON fallback boundary explicit instead of pretending every external schema can lower into a typed authored contract.

- [ ] **Task 46: Publish a JSON-first onboarding path**
  - Add a short path for developers who only care about JSON at first and would otherwise bounce off the multi-format surface.
  - Keep the existing cross-format story intact, but lead with the smallest path to value for message and REST use cases.
  - Link that path from the README and docs landing pages.

- [ ] **Task 47: Expand contract-pattern coverage for common app shapes**
  - Add polished copy-paste patterns for string enums, message envelopes, PATCH/update DTOs, event payloads, and layered config sections with defaults.
  - Keep these patterns aligned with the stable DSL and existing docs structure rather than scattering them across ad hoc examples.
  - Cross-link the patterns with the error-handling and migration guides once those land.
