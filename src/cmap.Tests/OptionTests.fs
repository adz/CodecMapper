module OptionTests

open Xunit
open Swensen.Unquote
open cmap
open TestCommon

let optionalRecordSchema =
    Schema.define<OptionalRecord>
    |> Schema.construct makeOptionalRecord
    |> Schema.field "nickname" _.Nickname
    |> Schema.field "age" _.Age
    |> Schema.build

[<Fact>]
let ``Option values round-trip JSON`` () =
    let codec = Json.compile optionalRecordSchema

    let someValue =
        {
            Nickname = Some "Ada"
            Age = Some 42
        }

    let noneValue =
        {
            Nickname = None
            Age = None
        }

    test <@ Json.serialize codec someValue = """{"nickname":"Ada","age":42}""" @>
    test <@ Json.serialize codec noneValue = """{"nickname":null,"age":null}""" @>
    test <@ Json.deserialize codec """{"nickname":"Ada","age":42}""" = someValue @>
    test <@ Json.deserialize codec """{"nickname":null,"age":null}""" = noneValue @>

[<Fact>]
let ``Top-level option values round-trip JSON`` () =
    let codec = Json.compile (Schema.option Schema.int)

    test <@ Json.serialize codec (Some 42) = "42" @>
    test <@ Json.serialize codec None = "null" @>
    test <@ Json.deserialize codec "42" = Some 42 @>
    test <@ Json.deserialize codec "null" = None @>

[<Fact>]
let ``Option values round-trip XML`` () =
    let codec = Xml.compile optionalRecordSchema

    let someValue =
        {
            Nickname = Some "Ada"
            Age = Some 42
        }

    let noneValue =
        {
            Nickname = None
            Age = None
        }

    let someXml = "<optionalrecord><nickname><some>Ada</some></nickname><age><some>42</some></age></optionalrecord>"
    let noneXml = "<optionalrecord><nickname></nickname><age></age></optionalrecord>"

    test <@ Xml.serialize codec someValue = someXml @>
    test <@ Xml.serialize codec noneValue = noneXml @>
    test <@ Xml.deserialize codec someXml = someValue @>
    test <@ Xml.deserialize codec noneXml = noneValue @>

[<Fact>]
let ``Missing option fields remain an error`` () =
    let jsonCodec = Json.compile optionalRecordSchema
    let xmlCodec = Xml.compile optionalRecordSchema

    expectFailure "Missing required key: age" (fun () -> Json.deserialize jsonCodec """{"nickname":null}""")
    expectFailure "Expected <age>" (fun () -> Xml.deserialize xmlCodec "<optionalrecord><nickname></nickname></optionalrecord>")
