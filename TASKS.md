# Tasks

Active work only. Historical completed work lives in [notes/AGENT_NOTES.md](notes/AGENT_NOTES.md) and [AGENTS.md](AGENTS.md).

- Recently completed: **Task 42** landed the boxed contract runtime and `Schema<'T>` surface. Keep the historical rationale, benchmark notes, and follow-up findings in [notes/AGENT_NOTES.md](notes/AGENT_NOTES.md) and [docs/PROFILE-REPORT-ANALYSIS-INSTRUCTIONAL.md](docs/PROFILE-REPORT-ANALYSIS-INSTRUCTIONAL.md).

- [ ] **Task 50: Run a narrow post-Task-42 performance follow-up**
  - Treat the current numbers as two separate problems: typed record decode overhead and handwritten parser hot loops.
  - Stay disciplined: no broad runtime rewrite until a narrower experiment wins clearly on the published scenario set.
  - First pass:
    - generalize the typed JSON record-decode lane beyond the benchmark-only hand-written shapes
    - profile string-heavy decode and serialize hotspots again after each step
    - identify whether the next worthwhile work is parser scanning, string handling, unknown-field skipping, or record assembly
  - Output:
    - refresh the benchmark notes with before/after numbers
    - either promote one proven optimization direction into production work or explicitly close the line of investigation

- [ ] **Task 49: Review and improve the new DSL for DX**
  - Review the new `Schema.*` surface for compactness, clarity, and maintainability after the immediate perf follow-up is settled.
  - Capture improvements in `PLAN-TO-IMPROVE-DSL`.
  - Do not fold speculative API cleanup into performance work unless it materially simplifies a measured hot path.

- [ ] **Task 37: Add structured decode error outputs for app boundaries**
  - Provide a structured error model that callers can use for REST responses, startup config failures, and message rejection logs.
  - Preserve the existing path-aware detail across JSON, XML, YAML, and KeyValue.
  - Keep the fast path cheap when callers still want exception-based failures.

- [ ] **Task 40: Add explicit unknown-field policy controls**
  - Support contract-level decisions for rejecting, allowing, or collecting unknown fields during decode.
  - Keep the default behavior conservative and aligned with explicit authored contracts.

- [ ] **Task 18: Add code generation modes for contracts**
  - Generate checked-in F# or C# contract code from CLR models, deterministic JSON examples, or imported contract inputs.
  - Keep generators in separate `.NET`-only projects so reflection-heavy tooling does not leak into the AOT/Fable-safe core.

- [ ] **Task 43: Improve the C# authoring story**
  - Expand the C# contract authoring DSL.
  - Add opt-in source generation layered on top of the stable runtime DSL.

- [ ] **Task 44: Add OpenAPI and schema-ecosystem guidance**
  - Document how exported JSON Schema can feed external OpenAPI or validation toolchains.
  - Evaluate whether a narrow OpenAPI bridge belongs in an opt-in package.

- [ ] **Task 45: Strengthen external-schema import and lowering**
  - Improve guidance and support for contracts that start from JSON Schema rather than authored `Schema<'T>`.

- [ ] **Task 46: Publish a JSON-first onboarding path**
  - Add a short path for developers who only care about JSON first.

- [ ] **Task 47: Expand contract-pattern coverage**
  - Add polished patterns for string enums, message envelopes, PATCH/update DTOs, event payloads, and layered config sections.
