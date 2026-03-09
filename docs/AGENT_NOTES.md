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

## Parser Notes

- The JSON parser is handwritten and should be hardened with deterministic input coverage before deeper refactors.
- The XML parser now supports `int`, `string`, `bool`, records, `list`, `array`, and `Schema.map`, but it is still a deliberately small XML subset.
- Supported XML is element-only: exact tags, escaped text nodes, repeated `<item>` children for collections, and ignorable inter-element whitespace.
- Out of scope for now: attributes, self-closing tags, namespaces, mixed content, comments, CDATA, and processing instructions.
- Parser work should prefer small state-machine steps plus exhaustive tests over “smart” generalized parsing.

## Test Coverage Notes

- `src/CodecMapper.Tests/SchemaDslTests.fs` proves the typed pipeline with a 20-field round trip.
- `src/CodecMapper.Tests/JsonParserTests.fs` holds the JSON compliance and adversarial cases.
- `src/CodecMapper.Tests/XmlTests.fs` holds the XML subset round-trip and malformed-input coverage.
- `src/CodecMapper.Tests/CSharpBridgeTests.fs` covers the first runtime import path from C# classes and serializer attributes.
- `src/CodecMapper.AotTests/Program.fs` and `src/CodecMapper.FableTests/Program.fs` are the compatibility sentinels. They now cover JSON and XML nested-record paths plus selected option/mapping/common-type cases.
- `src/CodecMapper.Benchmarks/CodecMapperBench.fs` now uses the pipeline DSL. Keep benchmark schemas aligned with public examples.

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
- `docs/CONFIG_CONTRACTS.md` now records the recommended config migration direction: explicit schema contracts, JSON as the canonical write format, XML as migration input only, explicit version envelopes, and a separation between wire contracts and richer domain models.

## Benchmarking Notes

- `src/CodecMapper.Benchmarks/CodecMapper.Benchmarks.fsproj` is now a runnable BenchmarkDotNet app again (`OutputType=Exe` plus a real entrypoint), so the failure is no longer at the top-level `dotnet run` step.
- The archived experimental clone under `benchmarks/CodecMapper/` also contains a `CodecMapper.Benchmarks.fsproj`, which makes BenchmarkDotNet's default child-project generator ambiguous once both repos exist in the same workspace.
- `src/CodecMapper.Benchmarks/Program.fs` now forces BenchmarkDotNet onto `InProcessEmitToolchain` to avoid child-project generation entirely.
- The remaining warning during local runs is just Linux process-priority elevation failure (`Permission denied`), which does not stop benchmarks from executing.
- A manual Release runner was added in `src/CodecMapper.Benchmarks.Runner` to keep benchmark reporting moving while that tooling issue remains unresolved.
- Keep the manual Release runner for fast local snapshots and README updates; use BenchmarkDotNet when you specifically want richer statistical output.

## Legacy CodecMapper Comparison

- The previous `CodecMapper` repo is cloned locally at `benchmarks/CodecMapper/` for reference only and is ignored by this repo's Git metadata.
- Its published benchmark snapshot is not directly comparable to `CodecMapper`'s current README numbers because it benchmarks a 1000-record `Person list` payload, while `CodecMapper` currently publishes a small single-object benchmark.
- The old repo also fails to complete BenchmarkDotNet runs cleanly on this machine under `.NET SDK 10.0.103`; direct child-project builds still end with `Build FAILED` and `0 Error(s)`.
- Any serious comparison between the old repo and current `CodecMapper` needs a shared manual harness on the same payload, not a README-to-README comparison.
- `src/CodecMapper.CompareRunner/` is that shared harness. Latest local run on the 1000-record nested-list payload:
  - `STJ encode` `674487.8 ns/op`
  - `CodecMapper encode` `769925.5 ns/op`
  - old `CodecMapper encode` `781210.3 ns/op`
  - `STJ decode` `1422827.2 ns/op`
  - `CodecMapper decode bytes` `1971350.4 ns/op`
  - old `CodecMapper decode stream` `2132364.1 ns/op`
- To keep that harness buildable after the repo rename, the local archived clone under `benchmarks/CodecMapper/` is expected to be built as `CodecMapper.CPExperiment.dll` so it does not collide with the current `CodecMapper.dll`.
- Current `CodecMapper` is slightly faster than the old repo on that shared payload, but the old repo allocates less. Do not frame the rename decision as a simple performance win for the old implementation.
- Legacy branding assets from the archived experimental repo are preserved under `docs/legacy-codemapper/`.
