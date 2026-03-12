# Agent Notes

This file records local findings that should survive across agent sessions.

## Current DSL

The stable schema DSL is:

```fsharp
let personSchema =
    Schema.define<Person>
    |> Schema.construct makePerson
    |> Schema.field "id" _.Id
    |> Schema.field "name" _.Name
    |> Schema.fieldWith "home" _.Home addressSchema
    |> Schema.build
```

Use this form in tests, docs, and benchmarks unless the public API changes deliberately.

## Common Type Surface

The library now auto-resolves these additional schema helpers:

- `Schema.int16`
- `Schema.int64`
- `Schema.byte`
- `Schema.sbyte`
- `Schema.uint32`
- `Schema.uint16`
- `Schema.uint64`
- `Schema.float`
- `Schema.decimal`
- `Schema.char`
- `Schema.guid`
- `Schema.dateTime`
- `Schema.dateTimeOffset`
- `Schema.timeSpan`

The narrower numeric types are range-checked wrappers over `Schema.int`. The identity and time-based types are string-backed so JSON and XML remain symmetric without extra handwritten token parsers.

## Option Semantics

- `Schema.option inner` is now first-class and `option<'T>` auto-resolves inside record fields.
- JSON uses `null` for `None`.
- XML uses an empty wrapper element for `None` and a nested `<some>...</some>` element for `Some`.
- Missing fields are still errors; option support is explicit-value semantics, not omit-on-missing semantics.
- There are now opt-in relaxations for config-style payloads:
  - `Schema.missingAsNone`
  - `Schema.emptyStringAsNone`
- Keep those wrappers explicit. `Schema.option` itself should stay strict so message-contract behavior does not silently weaken.

## Pipeline Findings

- `Schema.define` currently returns `Builder<'T, unit>`.
- `Schema.construct` is still necessary in practice.
- A one-step API such as `Schema.define makePerson` or `Schema.define<Person> makePerson` was explored and rejected.

The rejected forms failed for two different reasons:

- F# mis-inferred the record target when multiple record types shared field names such as `Id` and `Name`.
- Other variants caused the builder's constructor-state type to collapse to `obj`, which broke later `Schema.field` steps.

Those failures were observed in real repo call sites, not just toy examples.

## Compile UX

`Json.compile` and `Xml.compile` should remain explicit.

The recommended usage is:

```fsharp
let codec = Json.compile personSchema
let json = Json.serialize codec person
let person' = Json.deserialize codec json
```

This keeps compilation cost visible and avoids hidden recompilation or implicit caching.

## Custom Mapping

- `Schema.map` is the total-function customization hook.
- `Schema.tryMap` is the validated customization hook for smart constructors that return `Result<'T, string>`.
- Prefer `Schema.tryMap` over burying `failwith` inside a plain `Schema.map`, because it keeps decode-time validation explicit in the public API.

## JSON Schema Export

- `JsonSchema.generate` now exports a draft 2020-12 JSON Schema document directly from `Schema<'T>`.
- The export is structural: it reflects the JSON wire contract, not extra business rules buried inside mapping functions.
- `Schema.map` and `Schema.tryMap` therefore export their underlying wire shape.
- `Schema.option` exports as `anyOf [inner, null]`.
- `Schema.missingAsNone` removes the property from the enclosing object's `required` list but keeps the explicit value shape unchanged.
- Future JSON Schema import work should prefer a three-step ladder:
  - lower to normal `Schema<'T>` when the schema describes one deterministic wire shape
  - enforce semantic rules with validated wrapper types and `Schema.tryMap`
  - only fall back to pre-validation or a raw JSON DOM-style schema for dynamic-key, ambiguous-union, tuple-array, recursive, or similarly non-deterministic shapes
- `Schema.jsonValue` now exists as the explicit raw JSON fallback for those dynamic cases.
- `Schema.jsonValue` is intentionally JSON-only; `Xml.compile` fails explicitly rather than pretending there is a symmetric XML contract for arbitrary JSON.
- `JsonSchema.import` now builds `Schema<JsonValue>` for the currently supported receive-side subset by decoding raw JSON first and then validating the imported schema rules over that DOM.
- `JsonSchema.importWithReport` exposes enforced keywords, fallback keywords, and warnings so receive-side imports do not over-claim JSON Schema support.
- The currently enforced import subset now covers: `$defs`/local `$ref`, object-shaped `allOf`, `oneOf`, `anyOf`, `if` / `then` / `else`, `type`, `properties`, `required`, `items`, schema-valued `additionalProperties`, `patternProperties`, `propertyNames`, `prefixItems`, `contains`, `enum`, `const`, string length bounds, numeric bounds, `multipleOf`, collection/property count bounds, `pattern`, and `format` when a validator is configured.
- Local `$defs` and `$ref` normalization now work for the importer before rule generation.
- Object-shaped `allOf` composition is now normalized before rule generation, and `ImportReport.NormalizedKeywords` exposes that preprocessing explicitly.
- `JsonSchema.ImportOptions.defaults` currently covers `uuid` and `date-time`, and callers can add project-specific format validators with `JsonSchema.ImportOptions.withFormat`.
- The remaining unsupported importer surface is now narrower and mostly semantic: `dependentSchemas` and `not` still fall back rather than pretending to be enforced.

## Parser Notes

- The JSON parser is handwritten and should be hardened with deterministic input coverage before deeper refactors.
- The XML parser now supports `int`, `string`, `bool`, records, `list`, `array`, and `Schema.map`, but it is still a deliberately small XML subset.
- Supported XML is element-only: exact tags, escaped text nodes, repeated `<item>` children for collections, and ignorable inter-element whitespace.
- Out of scope for now: attributes, self-closing tags, namespaces, mixed content, comments, CDATA, and processing instructions.
- Parser work should prefer small state-machine steps plus exhaustive tests over “smart” generalized parsing.
- A future typed-interpreter architecture option is documented in `notes/TYPED_SCHEMA_INTERPRETERS.md`. Treat that note as a constrained design record, not as an implicit mandate to start a large rewrite.

## Test Coverage Notes

- `tests/CodecMapper.Tests/SchemaDslTests.fs` proves the typed pipeline with a 20-field round trip.
- `tests/CodecMapper.Tests/JsonParserTests.fs` holds the JSON compliance and adversarial cases.
- `tests/CodecMapper.Tests/XmlTests.fs` holds the XML subset round-trip and malformed-input coverage.
- `tests/CodecMapper.Tests/CSharpBridgeTests.fs` covers the first runtime import path from C# classes and serializer attributes.
- `tests/CodecMapper.AotTests/Program.fs` and `tests/CodecMapper.FableTests/Program.fs` are the compatibility sentinels. They now cover JSON and XML nested-record paths plus selected option/mapping/common-type cases.
- `scripts/check-fable-compat.sh` now transpiles `tests/CodecMapper.FableTests/` through the pinned Fable CLI, so the repo has a real transpilation smoke test instead of only a .NET-shaped sentinel.
- `benchmarks/CodecMapper.Benchmarks/CodecMapperBench.fs` now uses the pipeline DSL. Keep benchmark schemas aligned with public examples.

## Known Gaps

- The common-type surface is broader now, but it still does not cover every .NET numeric or framework type a C# migration story might want.
- The first `.NET`-only bridge implementation now exists in `src/CodecMapper.Bridge/`.
- It currently supports:
  - `System.Text.Json`: `JsonPropertyName`, `JsonIgnore`, `JsonRequired`, `JsonConstructor`
  - `Newtonsoft.Json`: `JsonProperty(PropertyName)`, `JsonIgnore`, `JsonRequired`, `JsonConstructor`
  - `DataContract`: `DataContract`, `DataMember(Name, IsRequired)`
  - constructor-bound classes
  - parameterless setter-bound classes
  - nested imported classes
  - arrays, `List<T>`, and `Nullable<T>`
- It currently rejects:
  - recursive graphs
  - polymorphism
  - extension data
  - converter attributes
  - mixed constructor-plus-setter binding
- Those unsupported paths are now pinned by dedicated tests in `tests/CodecMapper.Tests/CSharpBridgeTests.fs`, not just by design notes.
- `docs/CONFIG_CONTRACTS.md` now records the recommended config migration direction: explicit schema contracts, JSON as the canonical write format, XML as migration input only, explicit version envelopes, and a separation between wire contracts and richer domain models.

## Benchmarking Notes

- `benchmarks/CodecMapper.Benchmarks/CodecMapper.Benchmarks.fsproj` is now a runnable BenchmarkDotNet app again (`OutputType=Exe` plus a real entrypoint), so the failure is no longer at the top-level `dotnet run` step.
- `benchmarks/CodecMapper.Benchmarks/Program.fs` now forces BenchmarkDotNet onto `InProcessEmitToolchain` to avoid child-project generation entirely.
- The remaining warning during local runs is just Linux process-priority elevation failure (`Permission denied`), which does not stop benchmarks from executing.
- A manual Release runner was added in `benchmarks/CodecMapper.Benchmarks.Runner` to keep benchmark reporting moving while that tooling issue remains unresolved.
- Keep the manual Release runner for fast local snapshots and README updates; use BenchmarkDotNet when you specifically want richer statistical output.

## Formatting

- The repo now has `scripts/format.sh` and `scripts/format-check.sh`, both backed by the local `fantomas` tool manifest.
- The repo also has `scripts/generate-api-docs.sh`, which builds `fsdocs` output from the checked-in `docs/` content plus the public library XML comments under `src/`.
- The repo also has `scripts/check-fable-compat.sh`, which runs the pinned Fable CLI against `tests/CodecMapper.FableTests/`.
- GitHub Actions CI now lives in `.github/workflows/ci.yml` and is expected to stay aligned with those scripts:
  - `dotnet tool restore`
  - `dotnet restore CodecMapper.sln`
  - `bash scripts/format-check.sh`
  - `dotnet test tests/CodecMapper.Tests/CodecMapper.Tests.fsproj`
  - `dotnet run --project tests/CodecMapper.AotTests/CodecMapper.AotTests.fsproj`
  - `dotnet run --project tests/CodecMapper.FableTests/CodecMapper.FableTests.fsproj`
  - `bash scripts/check-fable-compat.sh`
  - `bash scripts/generate-api-docs.sh`
- That workflow now also deploys `output/` to GitHub Pages on pushes to `main`/`master`. The validation artifact (`api-docs`) and the Pages artifact should both come from the same generated `output/` tree.
- `scripts/install-git-hooks.sh` configures Git to use the versioned `.githooks` directory.
- `.githooks/pre-commit` runs the format check, so commits are expected to be `fantomas`-clean.
- The active Fantomas policy is intentionally conservative:
  - `fsharp_multiline_bracket_style = stroustrup`
  - `fsharp_max_line_length = 120`
  - `fsharp_newline_before_multiline_computation_expression = true`
  - `fsharp_keep_max_number_of_blank_lines = 1`
- Keep formatter changes small and policy-driven. If you change `.editorconfig`, rerun the formatter across the repo and review the resulting DSL/test readability before committing.
- The preserved CodecMapper logo now lives at `docs/logo.png` and is referenced from both `README.md` and `docs/index.md`. Treat that as the canonical branding asset path.
- `fsdocs` output is treated as a generated artifact, not checked-in source. Build it into `output/`, which is ignored by Git.
- The generated API docs are only as good as the XML comments in `src/`. If you change the public surface, update those comments in the same task instead of letting the reference drift.
- User-facing docs should be grouped by Diataxis purpose:
  - tutorials for first learning paths
  - how-to guides for specific tasks
  - technical reference for support matrices and API lookup
  - explanations for design tradeoffs and mental models

## Legacy CodecMapper Comparison

- Its published benchmark snapshot is not directly comparable to `CodecMapper`'s current README numbers because it benchmarks a 1000-record `Person list` payload, while `CodecMapper` currently publishes a small single-object benchmark.
- The old repo also fails to complete BenchmarkDotNet runs cleanly on this machine under `.NET SDK 10.0.103`; direct child-project builds still end with `Build FAILED` and `0 Error(s)`.
- Legacy branding assets from the archived experimental repo are preserved under `notes/legacy-codemapper/`.
