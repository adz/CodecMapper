# JSON Schema Support Reference

This page describes the current `CodecMapper` JSON Schema model and the intended direction for future import work.

## Export support from `Schema<'T>`

`JsonSchema.generate` currently exports these structural shapes directly:

- `integer`
- `number`
- `string`
- `boolean`
- `null`
- homogeneous arrays via `items`
- objects with `properties`
- object `required`
- nullable values via `anyOf [inner, null]`

These `Schema` features map directly:

- `Schema.int`, `Schema.int64`, `Schema.int16`, `Schema.byte`, `Schema.sbyte`, `Schema.uint32`, `Schema.uint16`, `Schema.uint64` -> `integer`
- `Schema.float`, `Schema.decimal` -> `number`
- `Schema.string`, `Schema.char`, `Schema.guid`, `Schema.dateTime`, `Schema.dateTimeOffset`, `Schema.timeSpan` -> `string`
- `Schema.bool` -> `boolean`
- `Schema.list inner`, `Schema.array inner` -> `array`
- record schemas -> `object`
- `Schema.option inner` -> `anyOf [inner, null]`
- `Schema.missingAsNone` -> omit that property from the enclosing object's `required`
- `Schema.map`, `Schema.tryMap` -> the underlying wire shape

## Not currently exported as JSON Schema constraints

`CodecMapper` does not currently emit:

- string constraints such as `pattern`, `minLength`, `maxLength`, `format`
- numeric constraints such as `minimum`, `maximum`, `multipleOf`
- object constraints such as `additionalProperties`, `patternProperties`, `propertyNames`
- array constraints such as `prefixItems`, `contains`, `minItems`, `maxItems`, `uniqueItems`
- `enum`, `const`
- `$defs`, `$ref`
- `oneOf`, `allOf`, conditional keywords, or discriminator-style composition beyond nullable option shapes

## Import direction

Future JSON Schema import work should follow this ladder:

1. Lower the incoming schema into the strongest deterministic `CodecMapper` shape available.
2. Use validated wrapper types and `Schema.tryMap` for semantic rules that are better expressed in domain types.
3. Only use pre-validation or a raw JSON fallback for schema features that change parse shape or cannot be lowered deterministically.

`JsonSchema.import` now returns `Schema<JsonValue>`. It decodes through `Schema.jsonValue` first, then enforces the supported JSON Schema subset over that raw JSON DOM.

`JsonSchema.importWithReport` returns the imported schema plus diagnostics about:

- enforced keywords
- normalized keywords
- fallback keywords
- warnings such as unresolved or cyclic local refs

Currently enforced during import:

- local `$defs` and `$ref`
- `allOf` normalization for object-shaped schema composition
- `oneOf`, `anyOf`
- `if` / `then` / `else`
- `type`
- `properties`
- `required`
- `items`
- `additionalProperties: false`
- `additionalProperties` as a schema
- `patternProperties`, `propertyNames`
- `prefixItems`, `contains`
- `enum`
- `const`
- `minLength`, `maxLength`
- `minimum`, `maximum`, `exclusiveMinimum`, `exclusiveMaximum`, `multipleOf`
- `minItems`, `maxItems`
- `minProperties`, `maxProperties`
- `pattern`
- `format` when a validator is configured

Currently not enforced and treated as raw-fallback areas:

- `dependentSchemas`
- `not`

## Raw JSON fallback

`Schema.jsonValue` is the explicit escape hatch for imported JSON Schema shapes that do not fit the normal DSL without ambiguity.

It currently supports:

- arbitrary-key objects
- heterogeneous arrays
- nested combinations of raw JSON values

It is intentionally JSON-only. XML compilation fails explicitly rather than pretending there is a symmetric XML DOM contract for arbitrary JSON.

## Advanced dynamic-shape keywords

Some receive-side JSON Schema features are supported only through the raw `JsonValue` import path because they describe dynamic or branch-selected shapes rather than one fixed record schema.

These include:

- branch selection: `oneOf`, `anyOf`, `if` / `then` / `else`
- dynamic-key objects: `patternProperties`, `propertyNames`, schema-valued `additionalProperties`
- tuple-like arrays: `prefixItems`
- array membership checks: `contains`

That support is intentional, but it is different from the normal authored `Schema<'T>` path:

- the payload is still parsed as `JsonValue`
- the schema keywords are enforced over that parsed raw value
- you should document these receive-side contracts as advanced or dynamic-shape scenarios, not as the preferred path for schemas you author yourself

## When `Schema.tryMap` is enough

Many JSON Schema rules are better treated as semantic refinement after structural parsing:

- patterned strings
- domain identifiers
- closed string or numeric sets
- cross-field invariants on records

In those cases, parse first and refine into a stronger type. That keeps `CodecMapper` aligned with "parse, don't validate" while still rejecting invalid states.

## When a fallback is needed

These shapes do not fit the current `Schema<'T>` model directly:

- arbitrary-key objects
- `patternProperties`
- heterogeneous tuple arrays
- recursive schemas
- ambiguous unions without a deterministic discriminator
- composition that must be normalized before a single parse shape is known

The intended fallback for those cases is:

- a pre-validation or normalization step for branch-shaping schema logic
- or `Schema.jsonValue` when the payload cannot reasonably lower into records, arrays, and primitives

The common explicit-schema path should remain the fast path. Dynamic JSON fallbacks must not penalize normal record, array, and primitive codecs.
