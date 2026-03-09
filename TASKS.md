# Tasks

This file tracks the active work queue for `cmap`.

Completed migration notes and DSL findings were moved into [docs/AGENT_NOTES.md](/home/adam/projects/cmap/docs/AGENT_NOTES.md) and [AGENTS.md](/home/adam/projects/cmap/AGENTS.md) so this file can stay focused on current work.

- [x] **Task 0: Rerun Benchmarks & Publish Results**
  - Run the current benchmark suite again using the pipeline-based schemas.
  - Capture the latest serialize/deserialize numbers for `cmap`, `System.Text.Json`, and `Newtonsoft.Json`.
  - Create or update the top-level project README with the benchmark summary and any important caveats about machine-specific results.

- [x] **Task 1: JSON Parser Hardening**
  - Add a broad deterministic test matrix for valid and invalid JSON inputs.
  - Cover empty structures, nesting, escapes, unicode escapes, numeric edge cases, malformed separators, trailing commas, duplicate keys, and deep nesting.
  - Make parser behavior explicit for ambiguous cases so the library is deterministic, not accidental.
  - *Done:* Coverage now lives in `src/cmap.Tests/JsonParserTests.fs`, with focused schema integration tests in `src/cmap.Tests/SchemaDslTests.fs`.
  - *Notes:* JSON now rejects trailing top-level content, leading-zero integers, malformed string escapes, incomplete bool literals, and over-depth unknown values. Unknown-field skipping is depth-bounded and deterministic, and `Schema.bool` is fully supported by `Json.compile`.

- [x] **Task 2: Expand XML Parsing**
  - Extend XML decode support beyond the current narrow subset.
  - Add symmetry tests for the expanded XML surface.
  - Document which XML constructs are intentionally supported and which are out of scope.
  - *Done:* XML now supports `int`, `string`, `bool`, records, `list`, `array`, and mapped schemas on both encode and decode.
  - *Notes:* The supported subset is element-only XML with exact tags, escaped text nodes, repeated `<item>` children for collections, and ignorable inter-element whitespace.

- [x] **Task 3: Common Schema Definitions**
  - Add first-class schema definitions or resolvers for common built-in types.
  - Prioritize types likely to appear in domain records: numeric variants, option-like shapes if feasible, dates/times, GUIDs, and other low-friction core types.
  - Keep the API AOT- and Fable-conscious.
  - *Done:* Added auto-resolved schemas for `int16`, `byte`, `sbyte`, `uint16`, `char`, `Guid`, `DateTime`, `DateTimeOffset`, and `TimeSpan`.
  - *Notes:* This keeps common domain identifiers and timestamps symmetric across JSON and XML without adding new parser branches beyond the integer core.

- [x] **Task 4: Richer Schema Customization**
  - Expand mapping/customization support for domain wrappers and special constructors such as `UserId`.
  - Prove encode/decode symmetry for these mappings.
  - Ensure the customization story remains explicit and discoverable in the public API.
  - *Done:* Added `Schema.tryMap` for smart constructors that can reject decoded values with explicit error messages.
  - *Notes:* `Schema.map` remains the total-function path, while `Schema.tryMap` covers validated wrappers such as positive `UserId` values.

- [x] **Task 5: Documentation Sweep**
  - Update `docs/GETTING_STARTED.md` to demonstrate the full supported feature set.
  - Include nested records, lists/arrays, mapped wrapper types, explicit nested schemas, multi-format compilation, and any new common-type support.
  - Keep examples aligned with the actual public DSL.
  - *Done:* `docs/GETTING_STARTED.md` now covers the stable pipeline DSL, nested schemas, collections, common built-in helpers, `Schema.map`, `Schema.tryMap`, explicit JSON/XML compilation, and the current XML subset.

- [x] **Task 6: Refresh Benchmark Snapshot**
  - Rerun the manual Release benchmark harness after the latest parser, XML, and schema-surface work.
  - Update the top-level README benchmark table with the latest machine-local numbers.
  - *Done:* README numbers were refreshed from the March 9, 2026 rerun of `src/cmap.Benchmarks.Runner`.

- [x] **Task 7: Add `null` and option support**
  - Teach JSON to parse and emit `null` deterministically.
  - Add `Schema.option` and auto-resolution for `option<'T>` where feasible.
  - Define the XML representation for optional values explicitly and test encode/decode symmetry.
  - *Done:* Added `Schema.option`, JSON `null` handling, and auto-resolution for `option<'T>`.
  - *Notes:* `None` is `null` in JSON and an empty wrapper element in XML. Missing fields still remain errors; option support is explicit-value based.

- [x] **Task 8: Broaden numeric support deliberately**
  - Decide which additional numeric shapes should be first-class: likely `int64`, `uint32`, `uint64`, `float`, and `decimal`.
  - Extend parser support only where semantics are clear and testable across JSON, XML, AOT, and Fable.
  - Keep failure behavior explicit for unsupported numeric forms rather than silently truncating.
  - *Done:* Added `int64`, `uint32`, `uint64`, `float`, and `decimal` schemas with JSON and XML encode/decode support.
  - *Notes:* JSON now tokenizes numeric literals explicitly, including fractional and exponent forms for `float` and `decimal`. Integer types still reject leading zeroes and fail explicitly on overflow.

- [x] **Task 9: Expand compatibility sentinels**
  - Bring the AOT and Fable sentinel projects closer to the real surface area.
  - Add coverage for XML where supported, common built-in schemas, and validated mappings where those targets can prove them.
  - Keep the sentinel apps small enough to stay diagnostic rather than becoming duplicate test suites.
  - *Done:* AOT now covers XML nested records, XML options, common built-in schemas, validated mappings, and extended numeric support. Fable now covers XML nested records, JSON options, and validated mappings.

- [x] **Task 10: Revisit benchmark tooling**
  - Investigate why BenchmarkDotNet's autogenerated project fails under the current SDK/project combination.
  - Restore the canonical benchmark path if possible, or document the failure mode and keep the manual runner as the supported fallback.
  - *Done:* Restored the benchmark project to a runnable `Exe` with a real BenchmarkDotNet entrypoint, then reproduced the remaining failure in the autogenerated child project.
  - *Notes:* On `.NET SDK 10.0.103` with `BenchmarkDotNet 0.15.8`, the child build still exits during `_GetProjectReferenceTargetFrameworkProperties` against `cmap.Benchmarks.fsproj` with `Build FAILED` and `0 Error(s)`. The manual Release runner remains the supported benchmark path.
