# Tagged Union Wire Shape Reference

This page is for lookup once you already know the authored tagged-union API:

- `Schema.tag`
- `Schema.tagWith`
- `Schema.union`
- `Schema.unionNamed`
- `Schema.inlineUnion`
- `Schema.inlineUnionNamed`
- `Schema.message`
- `Schema.messageWith`
- `Schema.envelope`
- `Schema.envelopeNamed`
- `Schema.inlineEnvelope`
- `Schema.inlineEnvelopeNamed`
- `Schema.delay`

It describes the exact wire shapes currently emitted by the built-in codecs.

## Default field names

`Schema.union` uses these default wire field names:

- discriminator field: `case`
- payload field: `value`

Example schema:

```fsharp
open CodecMapper

type Status =
    | Pending
    | Failed of string

let statusSchema =
    Schema.union [
        Schema.tag "pending" Pending ((=) Pending)
        Schema.tagWith
            "failed"
            (function Failed message -> Some message | _ -> None)
            Failed
            Schema.string
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

KeyValue flattens the same schema into dotted paths:

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
    Schema.unionNamed "kind" "details" [
        Schema.tag "pending" Pending ((=) Pending)
        Schema.tagWith
            "failed"
            (function Failed message -> Some message | _ -> None)
            Failed
            Schema.string
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

## Inline payload fields with `inlineUnion`

`Schema.inlineUnion` keeps the discriminator field, but merges payload members
 into the same object level instead of nesting them under a separate payload
 field.

Example:

```fsharp
type CreatedData = { Id: int; Name: string }
type Event =
    | Ping
    | Created of CreatedData

let makeCreatedData id name = { Id = id; Name = name }

let createdDataSchema =
    Schema.define<CreatedData>
    |> Schema.construct makeCreatedData
    |> Schema.field "id" _.Id
    |> Schema.field "name" _.Name
    |> Schema.build

let eventSchema =
    Schema.inlineUnion [
        Schema.tag "ping" Ping ((=) Ping)
        Schema.tagWith
            "created"
            (function Created payload -> Some payload | _ -> None)
            Created
            createdDataSchema
    ]
```

JSON:

```json
{"case":"created","id":7,"name":"Ada"}
```

XML:

```xml
<event><case>created</case><id>7</id><name>Ada</name></event>
```

YAML:

```yaml
case: created
id: 7
name: Ada
```

KeyValue:

```text
case=created
id=7
name=Ada
```

Inline payload schemas must be object-shaped so the payload can contribute
 named members cleanly across all formats. Record schemas are the intended
 fit here.

## Custom discriminator names with `inlineUnionNamed`

`Schema.inlineUnionNamed discriminatorName` changes only the discriminator
 field name for the inline shape.

JSON:

```json
{"kind":"created","id":7,"name":"Ada"}
```

## Message and envelope helpers

For message and event schemas, the helper names can read more directly than
 the generic tagged-union names:

- `message` is the same authored tag shape as `tag`
- `messageWith` is the same authored tag shape as `tagWith`
- `envelope` uses `"type"` / `"data"` field names by default
- `inlineEnvelope` uses `"type"` and inlines payload members next to it

Example envelope:

```fsharp
let eventSchema =
    Schema.envelope [
        Schema.message "ping" Ping ((=) Ping)
        Schema.messageWith
            "created"
            (function Created payload -> Some payload | _ -> None)
            Created
            createdDataSchema
    ]
```

JSON:

```json
{"type":"created","data":{"id":7,"name":"Ada"}}
```

Inline envelope JSON:

```json
{"type":"created","id":7,"name":"Ada"}
```

## Recursive unions with `delay`

`Schema.delay` lets a union point back to itself:

```fsharp
type RecursiveNode =
    | Leaf of string
    | Branch of RecursiveNode

let rec nodeSchema : Schema<RecursiveNode> =
    Schema.delay (fun () ->
        Schema.union [
            Schema.tagWith
                "leaf"
                (function Leaf value -> Some value | _ -> None)
                Leaf
                Schema.string
            Schema.tagWith
                "branch"
                (function Branch value -> Some value | _ -> None)
                Branch
                nodeSchema
        ])
```

That recursive authored schema currently compiles for:

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
- unknown inline case names
- missing inline payload fields for payload tags
- stray inline payload fields for payload-free tags

For KeyValue specifically, the payload-free case check matters because extra flattened keys would otherwise be easy to miss.

For readable, executable examples of malformed payloads and the expected error messages across JSON, XML, YAML, and KeyValue, see [`tests/CodecMapper.Tests/TaggedUnionErrorTests.fs`](../tests/CodecMapper.Tests/TaggedUnionErrorTests.fs).
