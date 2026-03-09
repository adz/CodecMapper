# Roadmap: cmap refactor

This file tracks the implementation of the "Schema-first" architecture for `cmap`.

- [x] **Task 1: Core Refactor**
  - Move `JsonWriter` and `JsonSource` into a target-agnostic module.
  - Define `IByteWriter` abstraction for cross-platform support.
  - *Success criteria:* Project compiles and existing benchmarks pass.

- [x] **Task 2: The Schema Blueprint**
  - Implement `Schema<'T>` types.
  - Implement `Schema.record<'T>` using anonymous record lambdas.
  - *Success criteria:* Codecs can be defined using the new DSL.

- [x] **Task 3: JSON Compiler (The Engine)**
  - Implement `Json.compile` to turn `Schema<'T>` into optimized byte-loops.
  - Use reflection *only once* during compilation.
  - *Success criteria:* Round-trip JSON tests pass for simple records.

- [x] **Task 4: Complex types & Transformations**
  - Support recursive schema resolution (Nested Records).
  - Implement optimized collection handling (Lists, Arrays).
  - Implement `Schema.map` for domain wrappers (e.g., `PersonId`).
  - *Success criteria:* Complex nested records and wrapped types round-trip successfully.

- [x] **Task 5: Multi-Format Proof (XML/Binary)**
  - Implement a basic `Xml.compile` or `Proto.compile` using the same `Schema`.
  - *Success criteria:* One schema produces multiple wire formats in tests.

- [x] **Task 6: Competitive Benchmarking**
  - Compare `cmap` against `System.Text.Json` (with and without Source Gen), `Newtonsoft.Json`, and `MessagePack-CSharp`.
  - *Success criteria:* Baseline performance report documented.

- [x] **Task 7: Optimization & Verification**
  - Iterate on compiler-generated code to reach/surpass manual "Final Boss" speeds.
  - Formally verify improvements against Task 6 baselines.
  - *Success criteria:* `cmap` maintains or extends its speed advantage.

- [x] **Task 8: AOT & Trimming Validation**
  - Create `src/cmap.AotTests`, a separate project with `<PublishAot>true</PublishAot>` and `<PublishTrimmed>true</PublishTrimmed>`.
  - Run a complete test run of all constructs inside this AOT-compiled binary.
  - *Success criteria:* Tests pass in a fully trimmed, Native AOT environment.

- [x] **Task 9: Fable Validation**
  - Verify Fable compilation for JS target.
  - *Success criteria:* Library runs successfully in a Node.js test environment.

- [x] **Task 10: Fluent DSL Implementation (Minimal)**
  - Implement a basic `SchemaBuilder` (CE) in `Library.fs`.
  - Support `construct` for simple constructors and `field` for primitive types.
  - *Success criteria:* Minimal test case passes using `schema { ... }`.

- [x] **Task 11: Test Migration & Full DSL Support**
  - Update `src/cmap.Tests`, `AotTests`, and `FableTests` to use the Fluent DSL.
  - Support `construct` for curried functions up to arity 8.
  - Support `field` with auto-resolution for lists and arrays.
  - Support explicit `field` for nested custom schemas.
  - *Success criteria:* All existing tests pass with the new DSL.

- [x] **Task 11b: Cleanup**
  - Remove deprecated `record` and `recordWith` functions from `Library.fs`.
  - Update `GETTING_STARTED.md` in `docs/` to reflect the new syntax.
  - *Success criteria:* Library is clean and documentation is accurate.

- [ ] ** Task 12: Parser hardening**
  - Ensure all the following:
  - Valid JSON Examples Your parser should successfully process these inputs:
        Empty object: {}
        Empty array: []
        Simple key-value pairs: {"key": "value", "number": 123, "boolean": true, "nullValue": null}
        Nested objects and arrays: {"person": {"name": "John", "age": 30}, "hobbies": ["reading", "gaming", "coding"]}
        Correctly escaped characters: {"path": "C:\\Users\\Name", "quote": "He said \\"Hello\\""}
        Unicode characters: {"greeting": "Hello, Wor\u006c\u0064!"}

  - Invalid JSON Examples (Edge Cases) Your parser should reject these inputs and handle the errors gracefully:
        Trailing commas: {"a": 1,} or [1, 2,]
        Unquoted keys: {key: "value"}
        Single quotes instead of double quotes: {'key': 'value'}
        Missing colons or commas: {"key" "value"} or {"key": "value" "key2": "value2"}
        Invalid data types/syntax: {"number": 0.123.456} or {"boolean": maybe}
        Mismatched brackets/braces: {"array": [1, 2]} or [{"key": "value"}]
        Deeply nested structures: Very deep nesting (e.g., [[[[...]]]]) to test for stack overflow vulnerabilities.
        Large numbers (precision issues): Numbers beyond the safe integer limit (2^53 - 1) if not handled as strings or specific BigInt types.
        Duplicate keys: {"role": "user", "role": "admin"} (behavior should be consistent or explicitly handled).
