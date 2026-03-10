module JsonValueTests

open Xunit
open Swensen.Unquote
open CodecMapper
open TestCommon

[<Fact>]
let ``Raw JSON values round-trip dynamic objects and arrays`` () =
    let codec = Json.compile Schema.jsonValue

    let value =
        JObject [
            "kind", JString "dynamic"
            "metadata", JObject [ "x-rate-limit", JNumber "10"; "enabled", JBool true ]
            "payload", JArray [ JNumber "1"; JObject [ "nested", JNull ] ]
        ]

    let json = Json.serialize codec value
    let decoded = Json.deserialize codec json

    test
        <@
            json = "{\"kind\":\"dynamic\",\"metadata\":{\"x-rate-limit\":10,\"enabled\":true},\"payload\":[1,{\"nested\":null}]}"
        @>

    test <@ decoded = value @>

[<Fact>]
let ``Raw JSON values preserve tuple-like arrays without extra schema modeling`` () =
    let codec = Json.compile Schema.jsonValue

    let value = JArray [ JString "header"; JNumber "2"; JObject [ "ok", JBool true ] ]

    test <@ Json.deserialize codec (Json.serialize codec value) = value @>

[<Fact>]
let ``Raw JSON fallback exports an unconstrained JSON Schema document`` () =
    let actual = JsonSchema.generate Schema.jsonValue

    test <@ actual = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"title\":\"JsonValue\"}" @>

[<Fact>]
let ``Raw JSON fallback is rejected explicitly by XML codecs`` () =
    expectFailure "Schema.jsonValue is JSON-only" (fun () -> Xml.serialize (Xml.compile Schema.jsonValue) JNull)
