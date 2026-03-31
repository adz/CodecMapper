module TaggedUnionErrorTests

open Xunit
open CodecMapper
open CodecMapper.Schema
open TestCommon

//
// These tests are intentionally written as readable examples of the malformed
// payloads the authored tagged-union codecs reject across formats.
type private Status =
    | Pending
    | Ready of string

let private statusSchema =
    union [
        tag "pending" Pending ((=) Pending)
        tagWith
            "ready"
            (function
            | Ready message -> Some message
            | _ -> None)
            Ready
            string
    ]

let private jsonCodec = Json.compileSchema statusSchema
let private xmlCodec = Xml.compileSchema statusSchema
let private yamlCodec = Yaml.compileSchema statusSchema
let private keyValueCodec = KeyValue.compileSchema statusSchema

let private jsonUnknownCase = """{"case":"broken"}"""
let private jsonMissingPayload = """{"case":"ready"}"""
let private jsonUnexpectedPayload = """{"case":"pending","value":"unexpected"}"""

let private xmlUnknownCase = "<status><case>broken</case></status>"
let private xmlMissingPayload = "<status><case>ready</case></status>"

let private xmlUnexpectedPayload =
    "<status><case>pending</case><value>unexpected</value></status>"

let private yamlUnknownCase = "case: broken"
let private yamlMissingPayload = "case: ready"
let private yamlUnexpectedPayload = "case: pending\nvalue: unexpected"

let private keyValueUnknownCase = Map.ofList [ "case", "broken" ]
let private keyValueMissingPayload = Map.ofList [ "case", "ready" ]

let private keyValueUnexpectedPayload =
    Map.ofList [ "case", "pending"; "value", "unexpected" ]

//
// Inline tagged unions merge payload members into the same object scope as the
// discriminator. These tests keep that behavior readable enough to cite
// directly from the docs.
let private createdDataSchema =
    define<CreatedData>
    |> construct makeCreatedData
    |> field "id" _.Id
    |> field "name" _.Name
    |> build

let private eventSchema =
    inlineUnion [
        tag "ping" Ping ((=) Ping)
        tagWith
            "created"
            (function
            | Created payload -> Some payload
            | _ -> None)
            Created
            createdDataSchema
    ]

let private inlineJsonCodec = Json.compileSchema eventSchema
let private inlineXmlCodec = Xml.compileSchema eventSchema
let private inlineYamlCodec = Yaml.compileSchema eventSchema
let private inlineKeyValueCodec = KeyValue.compileSchema eventSchema

let private inlineJsonUnknownCase = """{"case":"broken","id":7,"name":"Ada"}"""
let private inlineJsonMissingPayloadField = """{"case":"created","id":7}"""
let private inlineJsonUnexpectedPayload = """{"case":"ping","id":7}"""

let private inlineXmlUnknownCase =
    "<event><case>broken</case><id>7</id><name>Ada</name></event>"

let private inlineXmlMissingPayloadField =
    "<event><case>created</case><id>7</id></event>"

let private inlineXmlUnexpectedPayload =
    "<event><case>ping</case><id>7</id></event>"

let private inlineYamlUnknownCase = "case: broken\nid: 7\nname: Ada"
let private inlineYamlMissingPayloadField = "case: created\nid: 7"
let private inlineYamlUnexpectedPayload = "case: ping\nid: 7"

let private inlineKeyValueUnknownCase =
    Map.ofList [ "case", "broken"; "id", "7"; "name", "Ada" ]

let private inlineKeyValueMissingPayloadField =
    Map.ofList [ "case", "created"; "id", "7" ]

let private inlineKeyValueUnexpectedPayload =
    Map.ofList [ "case", "ping"; "id", "7" ]

[<Fact>]
let ``JSON tagged unions reject unknown cases`` () =
    expectFailure "Unknown union case 'broken'" (fun () -> Json.deserialize jsonCodec jsonUnknownCase)

[<Fact>]
let ``JSON tagged unions reject missing payloads for payload cases`` () =
    expectFailure "Missing union payload 'value' for case 'ready'" (fun () ->
        Json.deserialize jsonCodec jsonMissingPayload)

[<Fact>]
let ``JSON tagged unions reject stray payloads for payload-free cases`` () =
    expectFailure "Union case 'pending' does not accept payload 'value'" (fun () ->
        Json.deserialize jsonCodec jsonUnexpectedPayload)

[<Fact>]
let ``XML tagged unions reject unknown cases`` () =
    expectFailure "Unknown union case 'broken'" (fun () -> Xml.deserialize xmlCodec xmlUnknownCase)

[<Fact>]
let ``XML tagged unions reject missing payloads for payload cases`` () =
    expectFailure "Expected <value>" (fun () -> Xml.deserialize xmlCodec xmlMissingPayload)

[<Fact>]
let ``XML tagged unions reject stray payloads for payload-free cases`` () =
    expectFailure "Union case 'pending' does not accept a <value> element" (fun () ->
        Xml.deserialize xmlCodec xmlUnexpectedPayload)

[<Fact>]
let ``YAML tagged unions reject unknown cases`` () =
    expectFailure "Unknown union case 'broken'" (fun () -> Yaml.deserialize yamlCodec yamlUnknownCase)

[<Fact>]
let ``YAML tagged unions reject missing payloads for payload cases`` () =
    expectFailure "Missing union payload 'value' for case 'ready'" (fun () ->
        Yaml.deserialize yamlCodec yamlMissingPayload)

[<Fact>]
let ``YAML tagged unions reject stray payloads for payload-free cases`` () =
    expectFailure "Union case 'pending' does not accept payload 'value'" (fun () ->
        Yaml.deserialize yamlCodec yamlUnexpectedPayload)

[<Fact>]
let ``KeyValue tagged unions reject unknown cases`` () =
    expectFailure "KeyValue decode error at $.case: Unknown union case 'broken'" (fun () ->
        KeyValue.deserialize keyValueCodec keyValueUnknownCase)

[<Fact>]
let ``KeyValue tagged unions reject missing payloads for payload cases`` () =
    expectFailure "KeyValue decode error at $.value: Missing required key 'value'" (fun () ->
        KeyValue.deserialize keyValueCodec keyValueMissingPayload)

[<Fact>]
let ``KeyValue tagged unions reject stray payloads for payload-free cases`` () =
    expectFailure "KeyValue decode error at $.value: Union case 'pending' does not accept key 'value'" (fun () ->
        KeyValue.deserialize keyValueCodec keyValueUnexpectedPayload)

[<Fact>]
let ``JSON inline tagged unions reject unknown cases`` () =
    expectFailure "Unknown union case 'broken'" (fun () -> Json.deserialize inlineJsonCodec inlineJsonUnknownCase)

[<Fact>]
let ``JSON inline tagged unions reject missing payload fields`` () =
    expectFailure "Missing required key 'name'" (fun () ->
        Json.deserialize inlineJsonCodec inlineJsonMissingPayloadField)

[<Fact>]
let ``JSON inline tagged unions reject stray payload fields for payload-free tags`` () =
    expectFailure "Union case 'ping' does not accept payload fields alongside 'case'" (fun () ->
        Json.deserialize inlineJsonCodec inlineJsonUnexpectedPayload)

[<Fact>]
let ``XML inline tagged unions reject unknown cases`` () =
    expectFailure "Unknown union case 'broken'" (fun () -> Xml.deserialize inlineXmlCodec inlineXmlUnknownCase)

[<Fact>]
let ``XML inline tagged unions reject missing payload fields`` () =
    expectFailure "Expected <name>" (fun () -> Xml.deserialize inlineXmlCodec inlineXmlMissingPayloadField)

[<Fact>]
let ``XML inline tagged unions reject stray payload fields for payload-free tags`` () =
    expectFailure "Union case 'ping' does not accept payload elements alongside <case>" (fun () ->
        Xml.deserialize inlineXmlCodec inlineXmlUnexpectedPayload)

[<Fact>]
let ``YAML inline tagged unions reject unknown cases`` () =
    expectFailure "Unknown union case 'broken'" (fun () -> Yaml.deserialize inlineYamlCodec inlineYamlUnknownCase)

[<Fact>]
let ``YAML inline tagged unions reject missing payload fields`` () =
    expectFailure "Missing required key 'name'" (fun () ->
        Yaml.deserialize inlineYamlCodec inlineYamlMissingPayloadField)

[<Fact>]
let ``YAML inline tagged unions reject stray payload fields for payload-free tags`` () =
    expectFailure "Union case 'ping' does not accept payload fields alongside 'case'" (fun () ->
        Yaml.deserialize inlineYamlCodec inlineYamlUnexpectedPayload)

[<Fact>]
let ``KeyValue inline tagged unions reject unknown cases`` () =
    expectFailure "KeyValue decode error at $.case: Unknown union case 'broken'" (fun () ->
        KeyValue.deserialize inlineKeyValueCodec inlineKeyValueUnknownCase)

[<Fact>]
let ``KeyValue inline tagged unions reject missing payload fields`` () =
    expectFailure "KeyValue decode error at $.name: Missing required key 'name'" (fun () ->
        KeyValue.deserialize inlineKeyValueCodec inlineKeyValueMissingPayloadField)

[<Fact>]
let ``KeyValue inline tagged unions reject stray payload fields for payload-free tags`` () =
    expectFailure
        "KeyValue decode error at $: Union case 'ping' does not accept payload fields alongside 'case'"
        (fun () -> KeyValue.deserialize inlineKeyValueCodec inlineKeyValueUnexpectedPayload)

[<Fact>]
let ``Inline tagged unions reject non-object payload schemas at compile time`` () =
    let invalidSchema =
        inlineUnion [
            tagWith
                "ready"
                (function
                | Ready message -> Some message
                | _ -> None)
                Ready
                string
        ]

    expectFailure "Inline union case 'ready' payload schema must be object-shaped" (fun () ->
        Json.compileSchema invalidSchema |> ignore)
