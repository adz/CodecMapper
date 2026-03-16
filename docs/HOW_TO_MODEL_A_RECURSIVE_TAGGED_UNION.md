# How To Model A Recursive Tagged Union

Use `Schema.union` when the JSON/XML/YAML/KeyValue wire shape should be an explicit tagged contract with a separate payload field. Use `Schema.inlineUnion` when payload members should sit next to the discriminator at the same level. Use `Schema.delay` when one of those tags needs to recurse back to the same schema.

This is the authored-schema path for recursive tree-like contracts. The wire contract stays explicit:

- one discriminator field that chooses the case
- one payload field for single-value cases
- the same schema reused for nested values

## Model a recursive tree

```fsharp
open CodecMapper
open CodecMapper.Schema

type RecursiveNode =
    | Leaf of string
    | Branch of RecursiveNode

let rec nodeSchema : Schema<RecursiveNode> =
    delay (fun () ->
        union [
            tagWith
                "leaf"
                (function
                | Leaf value -> Some value
                | _ -> None)
                Leaf
                string
            tagWith
                "branch"
                (function
                | Branch value -> Some value
                | _ -> None)
                Branch
                nodeSchema
        ])
```

`delay` is the recursive anchor. Without it, the schema would try to construct itself immediately and never finish.

F# may warn about recursive object initialization (`FS0040`) when you author a recursive schema value with `let rec`. That warning is expected for this pattern. `Schema.delay` is the part that keeps the schema construction itself well-founded.

## Compile once and reuse across formats

```fsharp
let jsonCodec = Json.compile nodeSchema
let xmlCodec = Xml.compile nodeSchema
let yamlCodec = Yaml.compile nodeSchema
let keyValueCodec = KeyValue.compile nodeSchema

let value = Branch(Branch(Leaf "ok"))

let json = Json.serialize jsonCodec value
let xml = Xml.serialize xmlCodec value
let yaml = Yaml.serialize yamlCodec value
let keyValues = KeyValue.serialize keyValueCodec value
```

For the value above, JSON uses the default wire field names:

```json
{"case":"branch","value":{"case":"branch","value":{"case":"leaf","value":"ok"}}}
```

KeyValue flattens the same authored shape into dotted paths:

```text
case=branch
value.case=branch
value.value.case=leaf
value.value.value=ok
```

## Choose the case helpers deliberately

Use these helpers:

- `tag` for a tag without payload
- `tagWith` for a tag with one payload value
- `union` for the default field names `"case"` and `"value"`
- `unionNamed` when another system expects different field names
- `inlineUnion` when payload members should be merged next to the discriminator
- `inlineUnionNamed` when that inline shape needs a custom discriminator name

If you need the exact emitted JSON, XML, YAML, or KeyValue shapes, see [Tagged Union Wire Shape Reference](TAGGED_UNION_REFERENCE.md).

If you want concrete malformed payload examples and the exact failure messages the codecs are expected to produce, see [`tests/CodecMapper.Tests/TaggedUnionErrorTests.fs`](../tests/CodecMapper.Tests/TaggedUnionErrorTests.fs).

Example with a payload-free case and custom field names:

```fsharp
type Status =
    | Pending
    | Failed of string

let statusSchema =
    unionNamed "kind" "details" [
        tag "pending" Pending ((=) Pending)
        tagWith
            "failed"
            (function Failed message -> Some message | _ -> None)
            Failed
            string
    ]
```

That JSON shape becomes:

```json
{"kind":"failed","details":"boom"}
```

## Know the current boundaries

Recursive tagged unions currently compile for:

- JSON
- XML
- YAML
- KeyValue

`JsonSchema.generate` can also export these authored tagged unions, including recursive shapes, using `oneOf`, `const`, and local `$defs` / `$ref`.
