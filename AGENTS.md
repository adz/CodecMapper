# Engineering Standards & Orientation: CodecMapper

This guide provides the architectural vision and mandatory standards for the `CodecMapper` project. All agents must adhere to these principles to maintain the library's unique value proposition.

## 1. Core Vision
`CodecMapper` is a high-performance, AOT-friendly JSON serialization library for F# that avoids code generation and complex generics. It is designed to be "Pure F#," making it compatible with both .NET Native AOT and Fable targets (JavaScript, Rust, Erlang, Python).

### Key Pillars
- **Data & Functions:** Prefer simple data structures (`JsonSource`) and higher-order functions (`Encoder<'T>`, `Decoder<'T>`) over complex class hierarchies or interfaces.
- **No Code Generation:** All serialization logic is explicitly defined using combinators, ensuring transparency and ease of debugging.
- **Zero-Dependency Core:** Avoid `System.Text.Json` or `System.Buffers` in core logic to ensure seamless cross-platform support via Fable.
- **Symmetry:** Every feature MUST support both encode and decode paths.

## 2. Engineering Standards
These standards represent the user's preferred style and architectural philosophy.

### Code Style & Documentation
- **The "Why" over "What":** Every function and non-obvious block must have a comment explaining its purpose and the rationale.
- **Visual Separation for Rationale:** Do NOT use a "Why:" prefix. Instead, add an **empty comment line** (`///`) before the rationale comment to clearly separate it from the code while maintaining a continuous comment block.
- **Expressive Conciseness:** Strive for concise code, but never at the expense of clarity. Use whitespace to "take up space" to express concepts clearly.
- **Docs move with code:** Every completed task must update the relevant inline API docs and user-facing docs in the same change. Do not leave new public behavior undocumented.
- **Docs follow Diataxis:** Organize user-facing docs by purpose: tutorials for learning, how-to guides for goal completion, technical reference for lookup, and explanations for design understanding.

### Technical Integrity & Performance
- **Zero-Trust Typing:** Never use `obj` or unsafe typing (`:?>`) in the public API.
- **Functional State Passing:** Use immutable state passing (e.g., returning `struct('T * JsonSource)`) instead of mutable `byref` types to maintain compatibility with F# generics across all platforms.
- **AOT & Fable Optimization:** Prioritize logic that compilers can easily inline or devirtualize. Avoid reflection entirely.

## 3. Testing & Validation
- **Unquote Assertions:** Use `Swensen.Unquote` for all assertions: `test <@ actual = expected @>`.
- **Round-Trip Testing:** Always include tests that verify a value can be serialized and then deserialized back to its original state.

## 4. Architectural Patterns
- **Decoder Pattern:** `JsonSource -> struct('T * JsonSource)`
- **Encoder Pattern:** `JsonWriter -> 'T -> unit`
- **Pipeline Blueprint:** Use `Schema.define<'T> |> Schema.construct ctor |> Schema.field ... |> Schema.build` to define symmetric mappings. This is the current stable DSL.

## 5. Current Findings & Edge Cases
- **Do not collapse `Schema.define` and `Schema.construct` without proving it compiles across the repo.** A direct `Schema.define makeCtor` style was attempted and rejected because F# either mis-inferred record targets when field names overlapped (`Id`, `Name`) or collapsed the constructor state to `obj`.
- **Keep `Json.compile` explicit.** Hiding compilation inside `serialize`/`deserialize` would either recompile on each call or require implicit caching, which is poor UX for a performance-oriented library.
- **Explicit nested/custom schemas currently use `Schema.fieldWith`.** Auto-resolution exists for primitives, lists, and arrays only. Future work may rename this, but the explicit-schema distinction is currently meaningful.
- **Benchmarks should use the same DSL as tests and docs.** Avoid introducing parallel schema-definition styles unless the repo deliberately adopts a second public API.
- **When changing parsers, expand tests before refactoring.** The JSON and XML parsers are handwritten and should be treated as deterministic state machines, not “best effort” parsers.
- **The XML surface is intentionally a small subset.** Current support is element-only XML with exact tags, escaped text, repeated `<item>` children for collections, and ignorable inter-element whitespace. Attributes, namespaces, mixed content, comments, CDATA, self-closing tags, and processing instructions are still out of scope.
- **Common built-in schemas are now broader, but still intentional.** Auto-resolution currently includes `int64`, `int16`, `byte`, `sbyte`, `uint32`, `uint16`, `uint64`, `float`, `decimal`, `char`, `Guid`, `DateTime`, `DateTimeOffset`, and `TimeSpan` in addition to the original primitives, lists, arrays, options, and mapping helpers.
- **Validated wrappers should use `Schema.tryMap`.** Use `Schema.map` for total projections and `Schema.tryMap` when the decode path needs a smart constructor such as `UserId.create : int -> Result<UserId, string>`.
- **JSON Schema work should prefer structural parsing over validator parity.** Lower incoming schemas into the strongest `CodecMapper` shape available, use validated wrapper types and `Schema.tryMap` for semantic rules, and only fall back to pre-validation or raw JSON representations for schema constructs that do not describe one deterministic parse shape.
- **Option support is explicit-value based.** `Schema.option` and `option<'T>` are supported, but missing fields still fail. `None` is represented as `null` in JSON and an empty element in XML.
- **Compatibility sentinels should stay representative, not exhaustive.** The AOT and Fable apps now cover both JSON and XML nested-record paths plus selected option and validated-mapping cases. Expand them when the supported surface changes materially.
- **The Fable story now has two checks.** `tests/CodecMapper.FableTests` still runs as a simple .NET sentinel app, and `scripts/check-fable-compat.sh` separately transpiles that project through the pinned Fable toolchain. Keep both checks working when changing generic/type-driven code.
- **BenchmarkDotNet now runs via the in-process emit toolchain.** This avoids child-project generation conflicts with the archived experimental clone under `benchmarks/CodecMapper/`, which also contains a `CodecMapper.Benchmarks.fsproj`. Keep the manual runner for quick snapshots and README numbers.
- **Project layout is now split by role.** Public libraries live under `src/`, executable and xUnit tests live under `tests/`, and benchmark apps live under `benchmarks/`. Keep new projects in the root that matches their purpose so tooling and docs discovery stay predictable.
