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

- [x] **Task 29:** Split `src/CodecMapper/Library.fs` into explicit dependency-ordered files (`Core.fs`, `Schema.fs`, `Json.fs`, `JsonSchema.fs`, `Xml.fs`, `KeyValue.fs`, and `Yaml.fs`) and updated `CodecMapper.fsproj` to preserve the existing no-behavior-change compilation order.

- [ ] **Task 31: Ergonomics without reflection**
  - Keep the runtime DSL explicit and AOT/Fable-safe; do not introduce a second magic authoring path.
  - Add small authoring helpers where they make the current DSL read smaller, such as compile aliases and more built-in validated/domain combinators.
  - Prefer expanding safe static auto-resolution over making `fieldWith` implicit through runtime metadata.
  - Keep `fieldWith` for true schema boundaries such as validated wrappers, imported contracts, and explicit child schemas.

- [ ] **Task 32: Improve decode diagnostics**
  - Make decode failures consistently report path, expected shape, actual token/value, and validation/wrapper context.
  - Align JSON, XML, KeyValue, YAML, and imported JSON Schema errors around the same mental model.
  - Treat debuggability as a core ergonomics feature for the explicit schema approach.

- [ ] **Task 33: Ship canonical pattern docs**
  - Add copy-pasteable reference patterns for basic records, nested records, validated wrappers, versioned contracts, config contracts, JSON Schema import, and the C# bridge.
  - Keep the examples aligned with the stable `Schema.define |> Schema.construct |> ... |> Schema.build` DSL.
  - Make the “small explicit DSL” and compile-once workflow easy to discover from README and docs landing pages.

- [ ] **Task 34: Keep Task 18 focused on build-time code generation**
  - Generate ordinary checked-in F# schema code rather than introducing a second runtime schema system.
  - Treat CLR-model analysis, JSON-example scaffolding, and imported-contract scaffolding as `.NET`-only tooling layered on top of the stable runtime DSL.
  - Keep generated output reviewable and copy-editable by users.

- [ ] **Task 30: Fix the published docs site asset loading**
  - Reproduce the generated `fsdocs` output locally and identify why theme/search assets are being loaded from blocked cross-origin URLs.
  - Make the published site self-contained or otherwise serve its JS/CSS assets from paths that work on GitHub Pages without CORS failures.
  - Add a verification step so docs generation catches broken asset references before publishing.

- [x] **Task 30:** Fixed the docs-site asset root by aligning `PackageProjectUrl` with the GitHub Pages URL instead of the repo URL, and hardened `scripts/generate-api-docs.sh` to clear stale `fsdocs` cache, build the doc assemblies first, and fail if generated output points theme/search assets at `github.com/adz/CodecMapper/...`.
