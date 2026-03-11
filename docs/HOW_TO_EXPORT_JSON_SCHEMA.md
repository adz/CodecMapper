# How To Export JSON Schema

Use `JsonSchema.generate` when you want a JSON Schema document for the JSON wire contract already described by a `Schema<'T>`.

This is the authored-schema path. It is separate from JSON Schema import:

- export starts from your typed `Schema<'T>`
- import starts from an external JSON Schema document and returns `Schema<JsonValue>`

Keep those two workflows separate when you design your integration boundary.

## Export a schema

```fsharp
open CodecMapper

type Person = { Id: int; Name: string }
let makePerson id name = { Id = id; Name = name }

let personSchema =
    Schema.define<Person>
    |> Schema.construct makePerson
    |> Schema.field "id" _.Id
    |> Schema.field "name" _.Name
    |> Schema.build

let jsonSchema = JsonSchema.generate personSchema
```

`jsonSchema` is a compact draft 2020-12 JSON Schema document as a string.

## Export validated wrapper types

`Schema.map` and `Schema.tryMap` export the underlying wire shape, not the domain-only refinement rule:

```fsharp
type UserId = UserId of int

module UserId =
    let create value =
        if value > 0 then Ok(UserId value)
        else Error "UserId must be positive"

    let value (UserId value) = value

let userIdSchema =
    Schema.int
    |> Schema.tryMap UserId.create UserId.value

let schemaText = JsonSchema.generate userIdSchema
```

That schema still exports as an integer contract because the JSON wire value is still an integer.

## Export optional fields

`Schema.option` keeps explicit `null` semantics:

```fsharp
let maybeAgeSchema = Schema.option Schema.int
let schemaText = JsonSchema.generate maybeAgeSchema
```

That exports as `anyOf` with the inner schema plus `null`.

If you use `Schema.missingAsNone` inside a record field, the field is removed from the enclosing object's `required` list, but its value shape stays the same.

## Know what is and is not exported

`JsonSchema.generate` exports the structural JSON contract:

- primitive types
- arrays
- objects with properties and required fields
- nullable option shapes
- mapped wrapper types as their underlying wire form

It does not infer extra validation keywords from smart constructors or arbitrary business rules. If your type enforces domain constraints through `Schema.tryMap`, keep doing that on decode; the exported JSON Schema remains the structural contract.

## Use the raw fallback for non-deterministic imported shapes

If an imported schema cannot be lowered into a normal record/array/primitive contract, use `Schema.jsonValue` as the escape hatch:

```fsharp
let codec = Json.compile Schema.jsonValue
```

That keeps the dynamic case explicit instead of weakening the common typed path. `Schema.jsonValue` is intended for JSON-only fallback scenarios such as dynamic-key objects, tuple-like arrays, or schemas that need a separate normalization step before stronger typing is possible.

## Import a JSON Schema for receive-side validation

If you are receiving payloads from an external JSON Schema contract, import it into `Schema<JsonValue>`:

```fsharp
let imported =
    JsonSchema.import """{
        "type":"object",
        "properties":{
            "id":{"type":"integer"},
            "name":{"type":"string"}
        },
        "required":["id"]
    }"""

let codec = Json.compile imported
let value = Json.deserialize codec """{"id":42,"name":"Ada"}"""
```

This path preserves the incoming JSON shape as `JsonValue`. It enforces the supported structural subset and leaves unsupported branch-heavy features on the raw JSON fallback path.

This is not a round-trip back into a typed authored schema. It is a receive-side integration boundary for external schema-owned contracts.

If you need to know what was enforced, use `JsonSchema.importWithReport`:

```fsharp
let report = JsonSchema.importWithReport schemaText
let codec = Json.compile report.Schema
```

That report exposes enforced keywords, fallback keywords, and warnings from local `$ref` normalization.

It also exposes `NormalizedKeywords`, which is where keywords such as `$ref` and `allOf` show up after schema preprocessing.

Fallback keywords are intentional diagnostics, not silent downgrades. For example, if an imported
schema uses unsupported keywords such as `dependentSchemas` or `not` alongside supported keywords
such as `type` or `minLength`, the supported sibling rules still enforce normally while the
unsupported keyword is reported in `FallbackKeywords`.

## Supply a custom `format` validator

Use `JsonSchema.importUsing` or `JsonSchema.importWithReportUsing` when your schema uses a project-specific `format`:

```fsharp
let options =
    JsonSchema.ImportOptions.defaults
    |> JsonSchema.ImportOptions.withFormat "upper-code" (fun value ->
        if value.ToUpperInvariant() = value then
            Ok()
        else
            Error "String did not match the upper-code format")

let codec =
    Json.compile (
        JsonSchema.importUsing options """{"type":"string","format":"upper-code"}"""
    )
```

The built-in defaults already cover `uuid` and `date-time`. Add custom validators only for formats your application actually relies on.

## Handle advanced dynamic-shape schemas

For external receive-side schemas that use keywords such as `oneOf`, `anyOf`, `if` / `then` / `else`, `patternProperties`, or `prefixItems`, keep the boundary explicit:

```fsharp
let report = JsonSchema.importWithReport schemaText
let codec = Json.compile report.Schema
```

That path parses into `JsonValue` and enforces the supported dynamic-shape keywords over the raw JSON structure. It is appropriate for external contracts you do not control. For contracts you author yourself, prefer normal explicit `Schema<'T>` values.

Example:

```fsharp
open CodecMapper

let schemaText =
    """{
        "type":"object",
        "propertyNames":{"pattern":"^[a-z-]+$"},
        "patternProperties":{
            "^x-":{"type":"integer"}
        },
        "additionalProperties":{"type":"string"}
    }"""

let report = JsonSchema.importWithReport schemaText
let codec = Json.compile report.Schema

let value = Json.deserialize codec """{"x-rate":1,"name":"Ada"}"""

printfn "%A" report.EnforcedKeywords
printfn "%A" value
```

Output:

```text
["type"; "additionalProperties"; "patternProperties"; "propertyNames"]
JObject
  [("x-rate", JNumber "1"); ("name", JString "Ada")]
```

This is still a `JsonValue` receive path, not a lowered record schema. The importer is enforcing the dynamic object rules over the raw JSON shape.

For unsupported keywords that are intentionally out of scope for now, inspect `FallbackKeywords` explicitly:

```fsharp
let report =
    JsonSchema.importWithReport
        """{
            "type":"string",
            "minLength":2,
            "not":{"const":"blocked"}
        }"""

printfn "%A" report.EnforcedKeywords
printfn "%A" report.FallbackKeywords
```

Output:

```text
["type"; "minLength"]
["not"]
```

That means the supported sibling rules still enforce, while `not` remains on the fallback boundary instead of being partially modeled.
