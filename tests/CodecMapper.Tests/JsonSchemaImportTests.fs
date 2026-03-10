module JsonSchemaImportTests

open Xunit
open Swensen.Unquote
open CodecMapper
open TestCommon

[<Fact>]
let ``Imported primitive schema validates integer instances`` () =
    let codec = Json.compile (JsonSchema.import """{"type":"integer"}""")

    test <@ Json.deserialize codec "42" = JNumber "42" @>

    expectFailure "Expected integer" (fun () -> Json.deserialize codec "\"nope\"")

[<Fact>]
let ``Imported object schema validates required fields and additionalProperties false`` () =
    let schemaText =
        """{
            "type":"object",
            "properties":{
                "id":{"type":"integer"},
                "name":{"type":"string"}
            },
            "required":["id"],
            "additionalProperties":false
        }"""

    let codec = Json.compile (JsonSchema.import schemaText)

    let value = Json.deserialize codec """{"id":1,"name":"Ada"}"""

    test <@ value = JObject [ "id", JNumber "1"; "name", JString "Ada" ] @>

    expectFailure "Missing required property: id" (fun () -> Json.deserialize codec """{"name":"Ada"}""")

    expectFailure "Unexpected property: extra" (fun () -> Json.deserialize codec """{"id":1,"extra":true}""")

[<Fact>]
let ``Imported array schema validates items recursively`` () =
    let codec =
        Json.compile (JsonSchema.import """{"type":"array","items":{"type":"boolean"}}""")

    test <@ Json.deserialize codec """[true,false]""" = JArray [ JBool true; JBool false ] @>

    expectFailure "Expected boolean" (fun () -> Json.deserialize codec """[true,3]""")

[<Fact>]
let ``Imported enum and const rules are enforced over raw JSON values`` () =
    let enumCodec = Json.compile (JsonSchema.import """{"enum":["red","green"]}""")
    let constCodec = Json.compile (JsonSchema.import """{"const":{"kind":"fixed"}}""")

    test <@ Json.deserialize enumCodec "\"red\"" = JString "red" @>
    test <@ Json.deserialize constCodec """{"kind":"fixed"}""" = JObject [ "kind", JString "fixed" ] @>

    expectFailure "enum" (fun () -> Json.deserialize enumCodec "\"blue\"")

    expectFailure "const" (fun () -> Json.deserialize constCodec """{"kind":"other"}""")

[<Fact>]
let ``Imported string and numeric constraints are enforced when supported`` () =
    let stringCodec =
        Json.compile (JsonSchema.import """{"type":"string","minLength":2,"maxLength":4}""")

    let numberCodec =
        Json.compile (JsonSchema.import """{"type":"number","minimum":0,"exclusiveMaximum":10,"multipleOf":2}""")

    test <@ Json.deserialize stringCodec "\"Ada\"" = JString "Ada" @>
    test <@ Json.deserialize numberCodec "8" = JNumber "8" @>

    expectFailure "at least 2" (fun () -> Json.deserialize stringCodec "\"A\"")

    expectFailure "at most 4" (fun () -> Json.deserialize stringCodec "\"Atlas\"")

    expectFailure "at least 0" (fun () -> Json.deserialize numberCodec "-2")

    expectFailure "less than 10" (fun () -> Json.deserialize numberCodec "10")

    expectFailure "multiple of 2" (fun () -> Json.deserialize numberCodec "3")

[<Fact>]
let ``Imported pattern constraints are enforced for strings`` () =
    let codec =
        Json.compile (JsonSchema.import """{"type":"string","pattern":"^[A-Z]+$"}""")

    test <@ Json.deserialize codec "\"ABC\"" = JString "ABC" @>

    expectFailure "match pattern" (fun () -> Json.deserialize codec "\"Abc\"")

[<Fact>]
let ``Imported built-in formats are enforced when configured by defaults`` () =
    let codec = Json.compile (JsonSchema.import """{"type":"string","format":"uuid"}""")

    test
        <@
            Json.deserialize codec "\"12345678-1234-1234-1234-123456789abc\"" = JString
                "12345678-1234-1234-1234-123456789abc"
        @>

    expectFailure "uuid format" (fun () -> Json.deserialize codec "\"not-a-guid\"")

[<Fact>]
let ``Imported custom format validators can be supplied by the caller`` () =
    let options =
        JsonSchema.ImportOptions.defaults
        |> JsonSchema.ImportOptions.withFormat "upper-code" (fun value ->
            if value.ToUpperInvariant() = value then
                Ok()
            else
                Error "String did not match the upper-code format")

    let codec =
        Json.compile (JsonSchema.importUsing options """{"type":"string","format":"upper-code"}""")

    test <@ Json.deserialize codec "\"ABC\"" = JString "ABC" @>

    expectFailure "upper-code format" (fun () -> Json.deserialize codec "\"Abc\"")

[<Fact>]
let ``Imported collection size constraints are enforced when supported`` () =
    let arrayCodec =
        Json.compile (JsonSchema.import """{"type":"array","minItems":1,"maxItems":2}""")

    let objectCodec =
        Json.compile (JsonSchema.import """{"type":"object","minProperties":1,"maxProperties":2}""")

    test <@ Json.deserialize arrayCodec "[1]" = JArray [ JNumber "1" ] @>
    test <@ Json.deserialize objectCodec """{"a":1}""" = JObject [ "a", JNumber "1" ] @>

    expectFailure "at least 1" (fun () -> Json.deserialize arrayCodec "[]")

    expectFailure "at most 2" (fun () -> Json.deserialize arrayCodec "[1,2,3]")

    expectFailure "at least 1" (fun () -> Json.deserialize objectCodec "{}")

    expectFailure "at most 2" (fun () -> Json.deserialize objectCodec """{"a":1,"b":2,"c":3}""")

[<Fact>]
let ``Imported dynamic object keywords validate keys and unknown properties`` () =
    let codec =
        Json.compile (
            JsonSchema.import
                """{
                    "type":"object",
                    "propertyNames":{"pattern":"^[a-z-]+$"},
                    "patternProperties":{
                        "^x-":{"type":"integer"}
                    },
                    "additionalProperties":{"type":"string"}
                }"""
        )

    test
        <@
            Json.deserialize codec """{"x-rate":1,"name":"Ada"}""" = JObject [
                "x-rate", JNumber "1"
                "name", JString "Ada"
            ]
        @>

    expectFailure "Property name X-Rate" (fun () -> Json.deserialize codec """{"X-Rate":1}""")

    expectFailure "Property x-rate: Expected integer" (fun () -> Json.deserialize codec """{"x-rate":"bad"}""")

    expectFailure "Property flag: Expected string" (fun () -> Json.deserialize codec """{"flag":true}""")

[<Fact>]
let ``Imported tuple-like arrays and contains are enforced`` () =
    let codec =
        Json.compile (
            JsonSchema.import
                """{
                    "type":"array",
                    "prefixItems":[
                        {"type":"string"},
                        {"type":"integer"}
                    ],
                    "items":false,
                    "contains":{"type":"integer"}
                }"""
        )

    test <@ Json.deserialize codec """["hdr",2]""" = JArray [ JString "hdr"; JNumber "2" ] @>

    expectFailure "Array item 1: Expected integer" (fun () -> Json.deserialize codec """["hdr","bad"]""")

    expectFailure "beyond prefixItems" (fun () -> Json.deserialize codec """["hdr",2,true]""")

[<Fact>]
let ``Imported conditional schemas enforce then and else branches`` () =
    let codec =
        Json.compile (
            JsonSchema.import
                """{
                    "if":{"type":"object","properties":{"kind":{"const":"adult"}}},
                    "then":{"type":"object","required":["age"]},
                    "else":{"type":"object","required":["guardian"]}
                }"""
        )

    test
        <@
            Json.deserialize codec """{"kind":"adult","age":42}""" = JObject [
                "kind", JString "adult"
                "age", JNumber "42"
            ]
        @>

    test
        <@
            Json.deserialize codec """{"kind":"child","guardian":"Ada"}""" = JObject [
                "kind", JString "child"
                "guardian", JString "Ada"
            ]
        @>

    expectFailure "Missing required property: age" (fun () -> Json.deserialize codec """{"kind":"adult"}""")

    expectFailure "Missing required property: guardian" (fun () -> Json.deserialize codec """{"kind":"child"}""")

[<Fact>]
let ``Imported oneOf and anyOf branches are validated over raw values`` () =
    let oneOfCodec =
        Json.compile (
            JsonSchema.import
                """{
                    "oneOf":[
                        {"type":"string"},
                        {"type":"integer"}
                    ]
                }"""
        )

    let anyOfCodec =
        Json.compile (
            JsonSchema.import
                """{
                    "anyOf":[
                        {"type":"string","pattern":"^[A-Z]+$"},
                        {"type":"integer","minimum":10}
                    ]
                }"""
        )

    test <@ Json.deserialize oneOfCodec "\"Ada\"" = JString "Ada" @>
    test <@ Json.deserialize oneOfCodec "42" = JNumber "42" @>
    test <@ Json.deserialize anyOfCodec "\"ABC\"" = JString "ABC" @>
    test <@ Json.deserialize anyOfCodec "12" = JNumber "12" @>

    expectFailure "any oneOf branch" (fun () -> Json.deserialize oneOfCodec "true")

    expectFailure "any anyOf branch" (fun () -> Json.deserialize anyOfCodec "\"abc\"")

[<Fact>]
let ``Imported local defs and refs are normalized into enforced rules`` () =
    let report =
        JsonSchema.importWithReport
            """{
                "$defs":{
                    "userId":{"type":"integer"}
                },
                "type":"object",
                "properties":{
                    "id":{"$ref":"#/$defs/userId"}
                },
                "required":["id"]
            }"""

    let codec = Json.compile report.Schema

    test <@ report.EnforcedKeywords |> List.contains "$defs" @>
    test <@ report.EnforcedKeywords |> List.contains "$ref" @>
    test <@ report.NormalizedKeywords |> List.contains "$ref" @>
    test <@ report.Warnings = [] @>
    test <@ Json.deserialize codec """{"id":42}""" = JObject [ "id", JNumber "42" ] @>

    expectFailure "Property id: Expected integer" (fun () -> Json.deserialize codec """{"id":"nope"}""")

[<Fact>]
let ``Import report exposes fallback keywords for unsupported schema features`` () =
    let report =
        JsonSchema.importWithReport
            """{
                "dependentSchemas":{
                    "legacy":{"required":["next"]}
                }
            }"""

    test <@ report.FallbackKeywords |> List.contains "dependentSchemas" @>
    test <@ report.NormalizedKeywords = [] @>
    test <@ report.Warnings = [] @>

[<Fact>]
let ``Imported allOf schemas are normalized and enforced together`` () =
    let report =
        JsonSchema.importWithReport
            """{
                "allOf":[
                    {
                        "type":"object",
                        "properties":{"id":{"type":"integer"}},
                        "required":["id"]
                    },
                    {
                        "type":"object",
                        "properties":{"name":{"type":"string","minLength":2}},
                        "required":["name"]
                    }
                ]
            }"""

    let codec = Json.compile report.Schema

    test <@ report.EnforcedKeywords |> List.contains "allOf" @>
    test <@ report.NormalizedKeywords |> List.contains "allOf" @>
    test <@ report.FallbackKeywords |> List.contains "allOf" |> not @>
    test <@ Json.deserialize codec """{"id":1,"name":"Ada"}""" = JObject [ "id", JNumber "1"; "name", JString "Ada" ] @>

    expectFailure "Missing required property: name" (fun () -> Json.deserialize codec """{"id":1}""")

    expectFailure "Property name: String length must be at least 2" (fun () ->
        Json.deserialize codec """{"id":1,"name":"A"}""")

[<Fact>]
let ``Import report warns when no format validator is configured`` () =
    let report =
        JsonSchema.importWithReportUsing JsonSchema.ImportOptions.empty """{"type":"string","format":"uuid"}"""

    test <@ report.EnforcedKeywords |> List.contains "format" @>

    test
        <@
            report.Warnings
            |> List.exists (fun warning -> warning.Contains("No validator configured"))
        @>

[<Fact>]
let ``Unsupported branch-shaping keywords fall back to raw JSON acceptance`` () =
    let codec =
        Json.compile (JsonSchema.import """{"oneOf":[{"type":"string"},{"type":"integer"}]}""")

    test <@ Json.deserialize codec "\"x\"" = JString "x" @>
    test <@ Json.deserialize codec "5" = JNumber "5" @>
