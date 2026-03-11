# Tasks

This file tracks the active forward-looking queue for `CodecMapper`.

Completed rename, parser, bridge, compatibility, JSON Schema, docs, and projection work now lives in [notes/AGENT_NOTES.md](notes/AGENT_NOTES.md) and [AGENTS.md](AGENTS.md).

- **Completed archive**
- [x] Tasks `15`, `16`, `17`, `19`, `19a`, `20`, `20a`, `20b`, `20c`, `21`, `22`, `23`, `24`, `25`, `26`, `27`, and `28` are complete and documented in code, tests, and docs.

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
