# JSON Schema in CodecMapper

`CodecMapper` should treat JSON Schema primarily as a description of parseable wire shape, not as a demand to mirror every validator feature in the decoder.

It also helps to keep two separate questions in mind:

- How do I publish or document a contract I already authored in `CodecMapper`?
- How do I receive an external contract that is owned by a JSON Schema document?

Those are different workflows, and `CodecMapper` keeps them separate on purpose.

That distinction matters because JSON Schema mixes two different concerns:

- structure: what JSON shape should be parsed
- validation: what rules make an already parsed value acceptable

`CodecMapper` is strongest when those concerns are separated.

## Structural parsing first

For normal message contracts, the preferred flow is:

1. express the wire shape with `Schema<'T>`
2. compile it into a codec
3. decode directly into a typed value
4. use validated wrapper types only where domain refinement is needed

That keeps the common path fast and predictable.

## Semantic refinement second

Many JSON Schema rules are better represented as type-level refinement than as a generic validator:

- a non-empty identifier string
- a positive integer ID
- a bounded percentage
- a record whose fields must agree with each other

Those rules can be enforced with `Schema.tryMap` and smart constructors. When decode succeeds, the result is not just "valid JSON"; it is a value whose type already excludes invalid states.

This is often more useful than generic validator parity because the failure messages can use domain language instead of generic schema terminology.

## Why not aim for full validator parity inside the codec

Trying to make the runtime decoder enforce every JSON Schema keyword would push `CodecMapper` away from its strengths:

- the common schema-authored path would get heavier
- branch-heavy schema logic would leak into the fast parser
- portability and clarity would suffer

That is the wrong tradeoff for a library built around explicit schemas, predictable encode/decode behavior, and AOT/Fable-friendly code.

## The fallback model

Some JSON Schemas do not describe one deterministic parse shape. Those cases need an explicit fallback rather than pretending they fit the normal DSL:

- dynamic-key objects
- tuple arrays
- recursive schemas
- ambiguous unions
- composition that needs normalization before parsing

For those cases, the intended direction is:

1. normalize or pre-validate only the small schema subset that affects parser choice
2. if necessary, fall back to `Schema.jsonValue` or a raw JSON DOM-style representation
3. refine from that raw representation into stronger domain types afterward

That keeps the common case simple without claiming that every JSON Schema can become a record-shaped `Schema<'T>`.

## Dynamic-shape receive paths

Keywords such as `oneOf`, `anyOf`, `if` / `then` / `else`, `patternProperties`, and `prefixItems` are best understood as dynamic-shape features.

They do not lower cleanly into the normal authored `Schema<'T>` record/list model. Instead, `CodecMapper` handles them on the receive side by:

1. parsing the payload into `JsonValue`
2. enforcing the imported JSON Schema rules over that raw shape
3. letting you refine the accepted raw value further if you need stronger domain types

That is why the advanced JSON Schema importer lives alongside the normal schema DSL instead of replacing it. For contracts you control, prefer explicit authored schemas. For external contracts with dynamic or branch-selected shapes, use the importer and treat the result as an advanced receive-side boundary.

For now, keywords such as `dependentSchemas`, `not`, and recursive-schema shapes stay explicitly outside the lowered authored-schema subset. They are reported as fallback or unsupported areas rather than being partially modeled.

## Practical guidance

When publishing messages:

- prefer explicit `Schema<'T>` contracts
- export JSON Schema from those contracts
- keep domain validation in refined types, not in ad hoc serializer settings

When receiving messages:

- lower the incoming JSON Schema into the strongest structural `CodecMapper` schema available
- use `JsonSchema.import` when you want an immediate `Schema<JsonValue>` based receive path
- use `JsonSchema.importWithReport` when you need to audit enforced versus fallback keywords
- read `NormalizedKeywords` when schema preprocessing such as `$ref` or `allOf` composition has been applied before import rules are built
- add custom `format` validators through `JsonSchema.ImportOptions.withFormat` when the schema relies on application-specific string semantics
- treat currently unsupported keywords such as `dependentSchemas` and `not` as explicit fallback diagnostics rather than as partially enforced rules
- refine semantic rules with smart constructors
- only use a validation or raw-JSON escape hatch when the schema itself prevents one deterministic parse model

That is the intended "parse, don't validate" stance for `CodecMapper`: parse as much structure as possible, validate only where structure alone is not enough, and keep those escape hatches narrow.
