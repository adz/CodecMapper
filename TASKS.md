# Tasks

This file tracks the active forward-looking queue for `CodecMapper`.

Completed rename, parser, bridge, compatibility, JSON Schema, docs, and projection work now lives in [notes/AGENT_NOTES.md](notes/AGENT_NOTES.md) and [AGENTS.md](AGENTS.md).

- **Completed archive**
- [x] Tasks `15`, `16`, `17`, `19`, `19a`, `20`, `20a`, `20b`, `20c`, `21`, `22`, `24`, `25`, and `26` are complete and documented in code, tests, and docs.

- [ ] **Task 18: Add code generation modes for codecs**
  - Support generating codec/schema code from message-contract definitions.
  - Support generating codec/schema code from JSON examples or schema-like JSON inputs where the mapping is deterministic enough.
  - Support generating codec/schema code from CLR models.
  - Support both C# and F# records/classes as generator inputs.
  - Prefer readable checked-in output over opaque build-only generation.

- [x] **Task 23:** Added `Schema.missingAsValue`, `Schema.nullAsValue`, and `Schema.emptyCollectionAsValue` for config-style defaults while keeping strict missing-field behavior as the default and leaving whitespace-only strings untouched unless a schema opts into `Schema.emptyStringAsNone`.

- [x] **Task 27:** Added `scripts/generate-benchmark-snapshot.sh` to run the stable benchmark runner, emit docs-friendly markdown, and refresh the README benchmark snapshot block; CI now validates the generator in `--stdout-only` mode so the automation is exercised without rewriting docs on CI machines.
