#nowarn "40"

module TaggedUnionErrorTests

open Xunit
open CodecMapper
open CodecMapper.Schema
open TestCommon

///
/// These tests are intentionally written as readable examples of the malformed
/// payloads the authored tagged-union codecs reject across formats.
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

let private jsonCodec = Json.compile statusSchema
let private xmlCodec = Xml.compile statusSchema
let private yamlCodec = Yaml.compile statusSchema
let private keyValueCodec = KeyValue.compile statusSchema

let private jsonUnknownCase = """{"case":"broken"}"""
let private jsonMissingPayload = """{"case":"ready"}"""
let private jsonUnexpectedPayload = """{"case":"pending","value":"unexpected"}"""

let private xmlUnknownCase = "<status><case>broken</case></status>"
let private xmlMissingPayload = "<status><case>ready</case></status>"
let private xmlUnexpectedPayload = "<status><case>pending</case><value>unexpected</value></status>"

let private yamlUnknownCase = "case: broken"
let private yamlMissingPayload = "case: ready"
let private yamlUnexpectedPayload = "case: pending\nvalue: unexpected"

let private keyValueUnknownCase = Map.ofList [ "case", "broken" ]
let private keyValueMissingPayload = Map.ofList [ "case", "ready" ]
let private keyValueUnexpectedPayload = Map.ofList [ "case", "pending"; "value", "unexpected" ]

[<Fact>]
let ``JSON tagged unions reject unknown cases`` () =
    expectFailure "Unknown union case 'broken'" (fun () ->
        Json.deserialize jsonCodec jsonUnknownCase)

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
    expectFailure "Unknown union case 'broken'" (fun () ->
        Xml.deserialize xmlCodec xmlUnknownCase)

[<Fact>]
let ``XML tagged unions reject missing payloads for payload cases`` () =
    expectFailure "Expected <value>" (fun () ->
        Xml.deserialize xmlCodec xmlMissingPayload)

[<Fact>]
let ``XML tagged unions reject stray payloads for payload-free cases`` () =
    expectFailure "Union case 'pending' does not accept a <value> element" (fun () ->
        Xml.deserialize xmlCodec xmlUnexpectedPayload)

[<Fact>]
let ``YAML tagged unions reject unknown cases`` () =
    expectFailure "Unknown union case 'broken'" (fun () ->
        Yaml.deserialize yamlCodec yamlUnknownCase)

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
