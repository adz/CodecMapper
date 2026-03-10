module EnumInteropTests

open Xunit
open Swensen.Unquote
open CodecMapper
open TestCommon

let enumSchema =
    Schema.define<EnumRecord>
    |> Schema.construct makeEnumRecord
    |> Schema.field "status" _.Status
    |> Schema.build

[<Fact>]
let ``Auto-resolved enums round-trip JSON through their underlying numeric shape`` () =
    let codec = Json.compile enumSchema
    let value = { Status = OrderStatus.Suspended }

    let json = Json.serialize codec value
    let decoded = Json.deserialize codec json

    test <@ json = """{"status":2}""" @>
    test <@ decoded = value @>

[<Fact>]
let ``Auto-resolved enums round-trip XML through their underlying numeric shape`` () =
    let codec = Xml.compile enumSchema
    let value = { Status = OrderStatus.Active }

    let xml = Xml.serialize codec value
    let decoded = Xml.deserialize codec xml

    test <@ xml = "<enumrecord><status>1</status></enumrecord>" @>
    test <@ decoded = value @>
