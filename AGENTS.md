# Engineering Standards & Orientation: cmap

This guide provides the architectural vision and mandatory standards for the `cmap` project. All agents must adhere to these principles to maintain the library's unique value proposition.

## 1. Core Vision
`cmap` is a high-performance, AOT-friendly JSON serialization library for F# that avoids code generation and complex generics. It is designed to be "Pure F#," making it compatible with both .NET Native AOT and Fable targets (JavaScript, Rust, Erlang, Python).

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
- **Anonymous Record Blueprint:** Use `Schema.record<T> (fun x -> {| ... |})` to define symmetric mappings. This captures names, types, and getters in one go, which the compiler then uses to generate optimized codecs without runtime reflection.

