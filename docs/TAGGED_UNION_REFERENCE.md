# Tagged Union Wire Shape Reference

This page is for lookup once you already know the authored tagged-union API:

- `Schema.case0`
- `Schema.case1`
- `Schema.union`
- `Schema.unionNamed`
- `Schema.delay`

It describes the exact wire shapes currently emitted by the built-in codecs.

## Default field names

`Schema.union` uses these default wire field names:

- discriminator field: `case`
- payload field: `value`

Example schema:

```fsharp
open CodecMapper
open CodecMapper.Schema

type Status =
    | Pending
    | Failed of string

let statusSchema =
    union [
        case0 "pending" Pending ((=) Pending)
        case1
            "failed"
            (function Failed message -> Some message | _ -> None)
            Failed
            string
    ]
```

## JSON shape

Payload-free cases encode as an object with only the discriminator:

```json
{"case":"pending"}
```

Payload cases encode as an object with both fields:

```json
{"case":"failed","value":"boom"}
```

## XML shape

XML uses the schema-derived root element name, then nested discriminator and payload elements:

```xml
<status><case>pending</case></status>
```

```xml
<status><case>failed</case><value>boom</value></status>
```

## YAML shape

YAML projects the same JSON structure:

```yaml
case: pending
```

```yaml
case: failed
value: boom
```

## KeyValue shape

KeyValue flattens the same contract into dotted paths:

```text
case=pending
```

```text
case=failed
value=boom
```

Nested payloads keep extending the path:

```text
case=branch
value.case=branch
value.value.case=leaf
value.value.value=ok
```

## Custom field names with `unionNamed`

`Schema.unionNamed discriminatorName valueName` changes the wire field names without changing the authored case names.

Example:

```fsharp
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

That changes the wire shape like this.

JSON:

```json
{"kind":"failed","details":"boom"}
```

XML:

```xml
<status><kind>failed</kind><details>boom</details></status>
```

YAML:

```yaml
kind: failed
details: boom
```

KeyValue:

```text
kind=failed
details=boom
```

## Recursive unions with `delay`

`Schema.delay` lets a union point back to itself:

```fsharp
type RecursiveNode =
    | Leaf of string
    | Branch of RecursiveNode

let rec nodeSchema : Schema<RecursiveNode> =
    delay (fun () ->
        union [
            case1
                "leaf"
                (function Leaf value -> Some value | _ -> None)
                Leaf
                string
            case1
                "branch"
                (function Branch value -> Some value | _ -> None)
                Branch
                nodeSchema
        ])
```

That recursive authored contract currently compiles for:

- JSON
- XML
- YAML
- KeyValue

`JsonSchema.generate` also exports it as a structural schema using local `$defs` / `$ref`.

## Decode failure behavior

The codecs currently reject:

- unknown case names
- missing payload fields for payload cases
- stray payload keys for payload-free KeyValue cases

For KeyValue specifically, the payload-free case check matters because extra flattened keys would otherwise be easy to miss.
