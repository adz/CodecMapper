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

- [ ] **Task 29: Split the core library into dependency-ordered files**
  - Refactor `src/CodecMapper/Library.fs` into stable module files such as core/schema, JSON, JSON Schema, XML, and config projections.
  - Keep the split as a no-behavior-change refactor first; do not redesign the API while moving code.
  - Preserve the AOT/Fable-safe core assembly boundary and keep F# file ordering explicit in the project file.

- [ ] **Task 30: Fix the published docs site asset loading**
  - Reproduce the generated `fsdocs` output locally and identify why theme/search assets are being loaded from blocked cross-origin URLs.
  - Make the published site self-contained or otherwise serve its JS/CSS assets from paths that work on GitHub Pages without CORS failures.
  - Add a verification step so docs generation catches broken asset references before publishing.

- [x] **Task 30:** Fixed the docs-site asset root by aligning `PackageProjectUrl` with the GitHub Pages URL instead of the repo URL, and hardened `scripts/generate-api-docs.sh` to clear stale `fsdocs` cache, build the doc assemblies first, and fail if generated output points theme/search assets at `github.com/adz/CodecMapper/...`.
