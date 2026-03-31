# Introduction

`CodecMapper` is for cases where the wire schema should be written down on purpose instead of being inferred from CLR shape or serializer settings.

You author one `Schema<'T>`, then compile it into reusable format codecs.

That schema is the important idea in the library:

- it is explicit
- it drives both encode and decode
- it is reusable across formats
- it keeps schema changes visible in code review

## The core mental model

Most authored schemas follow one stable shape:

```fsharp
Schema.record ctor
|> Schema.field ...
|> Schema.build
```

Read that pipeline from top to bottom:

- `Schema.record ctor` says which value the schema describes and how decode rebuilds it
- each `Schema.field` maps one wire field to one domain field
- `Schema.build` finishes the authored schema

Then you compile that schema into a codec for the format boundary you need:

```fsharp
let codec = Json.compile personSchema
let json = Json.serialize codec person
let decoded = Json.deserialize codec json
```

## Why it feels different

`CodecMapper` is not trying to discover a schema from your record type.

Instead, the authored schema is the source of truth.

That makes it useful when:

- the wire shape matters and should stay reviewable
- JSON and XML should stay symmetric
- domain refinement should be explicit with `Schema.map` or `Schema.tryMap`
- tagged unions, inline tagged unions, and recursive case trees should stay explicit in normal schema code
- common string enums and message envelopes should still read like authored schemas rather than serializer magic
- AOT and Fable compatibility matter more than serializer magic

## The first path to learn

Start with one simple progression:

1. [Getting Started](GETTING_STARTED.md)
2. [How To Model A Basic Record](HOW_TO_MODEL_A_BASIC_RECORD.md)
3. [How To Model A Nested Record](HOW_TO_MODEL_A_NESTED_RECORD.md)
4. [How To Model A Validated Wrapper](HOW_TO_MODEL_A_VALIDATED_WRAPPER.md)
5. [How To Model A Recursive Tagged Union](HOW_TO_MODEL_A_RECURSIVE_TAGGED_UNION.md)

Take the C# bridge, JSON Schema, and config-specific guides only after that core model feels natural.
