# Tasks

This file tracks the active work queue for `cmap`.

Completed migration notes and DSL findings were moved into [docs/AGENT_NOTES.md](/home/adam/projects/cmap/docs/AGENT_NOTES.md) and [AGENTS.md](/home/adam/projects/cmap/AGENTS.md) so this file can stay focused on current work.

- [x] **Task 0: Rerun Benchmarks & Publish Results**
  - Run the current benchmark suite again using the pipeline-based schemas.
  - Capture the latest serialize/deserialize numbers for `cmap`, `System.Text.Json`, and `Newtonsoft.Json`.
  - Create or update the top-level project README with the benchmark summary and any important caveats about machine-specific results.

- [ ] **Task 1: JSON Parser Hardening**
  - Add a broad deterministic test matrix for valid and invalid JSON inputs.
  - Cover empty structures, nesting, escapes, unicode escapes, numeric edge cases, malformed separators, trailing commas, duplicate keys, and deep nesting.
  - Make parser behavior explicit for ambiguous cases so the library is deterministic, not accidental.
  - *Progress:* Escaped strings, unicode escapes, duplicate-key behavior, malformed-object rejection, and safer unknown-value skipping are now covered in `src/cmap.Tests/Tests.fs`.

- [ ] **Task 2: Expand XML Parsing**
  - Extend XML decode support beyond the current narrow subset.
  - Add symmetry tests for the expanded XML surface.
  - Document which XML constructs are intentionally supported and which are out of scope.

- [ ] **Task 3: Common Schema Definitions**
  - Add first-class schema definitions or resolvers for common built-in types.
  - Prioritize types likely to appear in domain records: numeric variants, option-like shapes if feasible, dates/times, GUIDs, and other low-friction core types.
  - Keep the API AOT- and Fable-conscious.

- [ ] **Task 4: Richer Schema Customization**
  - Expand mapping/customization support for domain wrappers and special constructors such as `UserId`.
  - Prove encode/decode symmetry for these mappings.
  - Ensure the customization story remains explicit and discoverable in the public API.

- [ ] **Task 5: Documentation Sweep**
  - Update `docs/GETTING_STARTED.md` to demonstrate the full supported feature set.
  - Include nested records, lists/arrays, mapped wrapper types, explicit nested schemas, multi-format compilation, and any new common-type support.
  - Keep examples aligned with the actual public DSL.
