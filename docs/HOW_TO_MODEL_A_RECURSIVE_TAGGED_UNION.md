# How To Model A Recursive Tagged Union

Use `Schema.union` when the JSON/XML/YAML wire shape should be an explicit tagged object, and use `Schema.delay` when one of those cases needs to recurse back to the same schema.

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
            case1
                "leaf"
                (function
                | Leaf value -> Some value
                | _ -> None)
                Leaf
                string
            case1
                "branch"
                (function
                | Branch value -> Some value
                | _ -> None)
                Branch
                nodeSchema
        ])
```

`delay` is the recursive anchor. Without it, the schema would try to construct itself immediately and never finish.

## Compile once and reuse across formats

```fsharp
let jsonCodec = Json.compile nodeSchema
let xmlCodec = Xml.compile nodeSchema
let yamlCodec = Yaml.compile nodeSchema

let value = Branch(Branch(Leaf "ok"))

let json = Json.serialize jsonCodec value
let xml = Xml.serialize xmlCodec value
let yaml = Yaml.serialize yamlCodec value
```

For the value above, JSON uses the default wire field names:

```json
{"case":"branch","value":{"case":"branch","value":{"case":"leaf","value":"ok"}}}
```

## Choose the case helpers deliberately

Use these helpers:

- `case0` for a case with no payload
- `case1` for a case with exactly one payload value
- `union` for the default field names `"case"` and `"value"`
- `unionNamed` when another system expects different field names

Example with a payload-free case and custom field names:

```fsharp
type Status =
    | Pending
    | Failed of string

let statusSchema =
    unionNamed "kind" "details" [
        case0 "pending" Pending ((=) Pending)
        case1
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

They currently do not compile for:

- KeyValue

`JsonSchema.generate` can now export these authored tagged unions, including recursive shapes, using `oneOf`, `const`, and local `$defs` / `$ref`.
