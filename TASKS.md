# Tasks

This file tracks the active forward-looking queue for `CodecMapper`.

Completed rename, benchmarking, parser, and first-pass C# bridge work now live in [docs/AGENT_NOTES.md](docs/AGENT_NOTES.md) and [AGENTS.md](AGENTS.md).

- [ ] **Task 15: Expand the C# attribute bridge beyond the initial import path**
  - Add explicit failure tests for unsupported converter, polymorphism, and extension-data attributes.
  - Decide whether mixed constructor-plus-setter classes should remain unsupported or gain a deterministic policy.
  - Add bridge examples to `GETTING_STARTED`.

- [ ] **Task 16: Add a code-generation path for imported C# contracts**
  - The runtime reflection importer is useful for migration, but code generation is the cleaner end state for AOT-sensitive users.
  - Explore emitting F# schema code or a source-generated `.NET` bridge layer from annotated C# types.

- [ ] **Task 17: Broaden common collection support where it helps C# interop**
  - Evaluate `IReadOnlyList<T>`, `ICollection<T>`, dictionaries, and enums.
  - Only add shapes that preserve symmetric encode/decode semantics cleanly.
