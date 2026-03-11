# Typed Schema Interpreters

This note records one possible future architecture for `CodecMapper`: a typed interpreter layer for runtime codecs plus a reified layer for structural tooling such as JSON Schema export.

It is not a task request by itself. It is a design option, with explicit warnings about where previous exploration became vague or looped.

## Why this option exists

The current runtime keeps the public DSL pleasant, but it pays for that with internal erasure:

- `SchemaField.GetValue : obj -> obj`
- `SchemaDefinition.Record of System.Type * SchemaField[] * (obj[] -> obj)`
- `Builder<'T, 'Ctor>.App : obj[] -> int -> 'Ctor`

That design makes one generic backend easy to write across JSON, XML, YAML, bridge import, and JSON Schema work. It also forces hot JSON decode through boxed field storage and erased record reconstruction.

The typed-interpreter option exists because there is a real ceiling on how far the current erased record path can go.

## Core idea

Split schema use into two layers:

1. Typed interpretation
   Used for runtime encode/decode where preserving `'T` all the way through matters.

2. Reified structure
   Used for inspection-heavy consumers such as JSON Schema export, docs, import normalization, debugging, or bridge diagnostics.

The schema author still writes one DSL. Internally, that DSL must support both execution styles.

## What "typed all the way through" means

Today a record schema eventually becomes "an array of erased fields plus an erased constructor."

The typed version would instead preserve the field types in constructor state. Conceptually:

```fsharp
type HNil = HNil
type HCons<'Head, 'Tail> = HCons of 'Head * 'Tail
```

A record like:

```fsharp
type Person = { Id: int; Name: string; Home: Address }
```

would have a typed field state like:

```fsharp
HCons<int, HCons<string, HCons<Address, HNil>>>
```

or an equivalent typed constructor-state encoding.

The JSON decoder interpreter would decode:

- `int`
- then `string`
- then `Address`

and apply those directly to a typed constructor pipeline instead of storing them in `obj[]`.

## Likely representation styles

Two broad choices exist.

### 1. Finally-tagless / Church-encoded schema

The schema is represented by how it can be interpreted:

```fsharp
type Schema<'T> =
    abstract Run<'F> : ISchemaAlgebra<'F> -> 'F<'T>
```

This is the strongest option for typed runtime execution.

Pros:

- typed encoders/decoders stay typed end-to-end
- no erased field arrays required in the runtime interpreter
- naturally supports multiple format interpreters

Cons:

- hard to inspect structurally unless paired with reification
- F# inference and error messages can degrade quickly

### 2. Typed reified AST

The schema is stored as a typed syntax tree.

Pros:

- easier to inspect than pure final encoding
- tooling/export passes are more direct

Cons:

- harder to express ergonomically in F#
- still needs careful handling for heterogeneous fields

For this repo, a finally-tagless runtime layer plus separate reification is probably more realistic than a fully typed reified AST.

## Why a reified layer still matters

A pure final encoding is strong for execution but weak for inspection.

JSON Schema generation is inspection-heavy. It needs to answer questions like:

- is this a primitive, record, list, option, or wrapper?
- what are the field names?
- which fields are required?
- how do wrappers such as `missingAsNone` or `tryMap` affect the wire shape?

That is why a reified layer is still useful even if runtime codecs move to typed interpreters.

The likely split is:

- typed interpreter path for JSON/XML/YAML runtime codecs
- reified path for JSON Schema export, docs, inspection, bridge tooling

## Cross-format viability

This option does work across formats.

The same authored schema could feed interpreters for:

- JSON encode
- JSON decode
- XML encode
- XML decode
- YAML encode/decode where supported
- KeyValue projection
- JSON Schema export
- docs/reference rendering

That cross-format story is one of the strongest reasons to consider this approach. It preserves the library's "one schema, many boundaries" model.

## Why this is a major refactor

This is not a parser-only or benchmark-only change.

It would affect:

- the internal representation of `Schema<'T>`
- `Schema.define` / `Schema.construct` / `Schema.field` / `Schema.build`
- runtime compilers in JSON and XML
- bridge and importer code that expects a reified shape
- JSON Schema export code
- debugging and diagnostics surfaces that currently rely on structural inspection

The public DSL should ideally stay familiar, but the internals would change substantially.

## Why earlier exploration got stuck

The failure mode is predictable: the design is rich enough that an LLM can keep inventing alternate encodings without proving that any of them fit the repo's actual constraints.

The main loop hazards are:

- drifting into abstract type theory without touching the current DSL
- inventing HList machinery without mapping it back to JSON/XML/JSON Schema needs
- replacing inspection with pure final encoding and then getting stuck on export/import
- ignoring F# inference limits that already forced the current `Schema.construct` shape
- proposing codegen, reflection, or unsafe typing that violates project constraints

## Guidance for future LLM work

Any future agent exploring this option should follow these rules.

### Start from repo constraints

Before proposing changes, restate these constraints explicitly:

- no code generation
- no reflection-heavy runtime in the portable core
- symmetry across encode and decode
- Fable and AOT friendliness matter
- `Schema.construct` exists for real inference reasons
- JSON Schema export/import still needs structural inspection

If a proposal cannot satisfy those constraints, stop or narrow scope.

### Do not rewrite everything at once

The first useful milestone is not "replace the whole schema core."

The first useful milestone is:

- prove a typed JSON record runtime path for a narrow subset
- keep the current erased representation as fallback
- keep public DSL source compatibility

That gives a measurable checkpoint without blocking the whole repo on one rewrite.

### Separate execution from inspection

Any serious proposal must answer both:

1. how runtime encode/decode stays typed
2. how JSON Schema export and similar tooling still inspect structure

If the answer only solves one of those, it is incomplete.

### Require a concrete migration path

Each proposal should describe:

- what remains on the current erased path
- what subset gains a typed path first
- how compatibility is preserved
- what benchmark or test should improve if the step succeeds

### Ban vague success criteria

Future work must define success with explicit checks such as:

- benchmark `person-batch-25` or `telemetry-500` decode improves by a target ratio
- no regressions in `tests/CodecMapper.Tests`
- JSON Schema export still emits the same structural shape for a fixed test corpus

### Prefer thin vertical slices

A good first slice is:

- typed JSON runtime only
- records + primitives + nested records + lists
- current DSL unchanged
- erased fallback remains for unsupported wrappers or advanced import/export cases

A bad first slice is:

- redesign every schema shape simultaneously
- redesign bridge, JSON Schema, and all runtimes before proving one fast path

## Recommended staged plan if this is revived

1. Introduce an internal typed runtime representation for a narrow JSON subset.
2. Keep the current erased `SchemaDefinition` as the source used by tooling/export.
3. Teach `Json.compile` to choose the typed path when the schema matches the supported subset.
4. Benchmark nested-record and numeric-heavy decode against the current implementation.
5. Only then evaluate whether XML should gain the same typed interpreter path.
6. Only after runtime benefits are proven should the repo consider a deeper schema-core redesign.

## Decision guidance

This option is worth revisiting if:

- decode benchmarks stop improving materially with erased-path optimizations
- record reconstruction and boxing remain the dominant cost
- the repo is willing to absorb a medium-to-large internal refactor

This option is not worth revisiting yet if:

- current erased-path optimizations are still yielding meaningful wins
- the cost is in parser details rather than erased record reconstruction
- the team needs small safe steps more than architectural cleanup

## Current recommendation

Do not start with a full finally-tagless rewrite.

If this direction is revived, start with a hybrid:

- keep a reified layer for structure-aware tooling
- add a typed interpreter layer for runtime codecs
- prove it on JSON record decode first

That gives the repo the best chance of capturing the performance upside without losing the practical benefits of the current schema DSL.
