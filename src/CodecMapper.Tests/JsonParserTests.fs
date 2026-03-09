module JsonParserTests

open Xunit
open Swensen.Unquote
open CodecMapper
open TestCommon

let idSchema =
    Schema.define<IdOnly>
    |> Schema.construct makeIdOnly
    |> Schema.field "id" _.Id
    |> Schema.build

let idCodec = Json.compile idSchema

let quoted value = "\"" + value + "\""

[<Fact>]
let ``Round-trip escaped strings JSON`` () =
    let codec = Json.compile Schema.string
    let value = "He said \"Hello\"\nC:\\temp\\file.txt"
    let json = Json.serialize codec value

    test <@ json = quoted """He said \"Hello\"\nC:\\temp\\file.txt""" @>

    let decoded = Json.deserialize codec json
    test <@ decoded = value @>

[<Fact>]
let ``Decode unicode escape string JSON`` () =
    let codec = Json.compile Schema.string
    let decoded = Json.deserialize codec (quoted """Hello, Wor\u006c\u0064!""")
    test <@ decoded = "Hello, World!" @>

[<Fact>]
let ``Round-trip bool JSON`` () =
    let codec = Json.compile Schema.bool

    test <@ Json.serialize codec true = "true" @>
    test <@ Json.serialize codec false = "false" @>
    test <@ Json.deserialize codec "true" = true @>
    test <@ Json.deserialize codec "false" = false @>

[<Fact>]
let ``Reject malformed string escape sequences`` () =
    let codec = Json.compile Schema.string

    expectFailure "Invalid escape sequence" (fun () -> Json.deserialize codec (quoted """bad\xescape"""))
    expectFailure "Invalid unicode escape" (fun () -> Json.deserialize codec (quoted """bad\u12xz"""))
    expectFailure "Invalid unicode escape" (fun () -> Json.deserialize codec (quoted """bad\u123"""))
    expectFailure "Unterminated unicode escape" (fun () -> Json.deserialize codec "\"bad\\u12")
    expectFailure "Unterminated string" (fun () -> Json.deserialize codec "\"unterminated")

[<Fact>]
let ``Reject trailing content after top-level JSON value`` () =
    let intCodec = Json.compile Schema.int
    let stringCodec = Json.compile Schema.string

    expectFailure "Trailing content after top-level JSON value" (fun () -> Json.deserialize intCodec "1 2")
    expectFailure "Trailing content after top-level JSON value" (fun () -> Json.deserialize stringCodec (quoted "ok" + "[]"))

[<Fact>]
let ``Unknown nested object with braces inside strings is skipped deterministically`` () =
    let value = Json.deserialize idCodec """{"extra":{"text":"{[still text]}"},"id":42}"""
    test <@ value = { Id = 42 } @>

[<Fact>]
let ``Unknown nested array with delimiter-like strings is skipped deterministically`` () =
    let json = """{"extra":["}","]","comma,inside",{"inner":"[{still text}]"}],"id":42}"""
    let value = Json.deserialize idCodec json
    test <@ value = { Id = 42 } @>

[<Fact>]
let ``Duplicate keys keep the last value`` () =
    let value = Json.deserialize idCodec """{"id":1,"id":2}"""
    test <@ value = { Id = 2 } @>

[<Fact>]
let ``Escaped property names match decoded schema field names`` () =
    let value = Json.deserialize idCodec """{"i\u0064":42}"""
    test <@ value = { Id = 42 } @>

[<Fact>]
let ``Reject trailing commas in objects and arrays`` () =
    let listCodec = Json.compile (Schema.list Schema.int)

    expectFailure "Expected \"" (fun () -> Json.deserialize idCodec """{"id":1,}""")
    expectFailure "Expected digit" (fun () -> Json.deserialize listCodec "[1,2,]")

[<Fact>]
let ``Reject malformed object syntax`` () =
    expectFailure "Expected \"" (fun () -> Json.deserialize idCodec "{id:1}")
    expectFailure "Expected :" (fun () -> Json.deserialize idCodec """{"id" 1}""")
    expectFailure "Expected , or }" (fun () -> Json.deserialize idCodec """{"id":1 "x":2}""")

[<Fact>]
let ``Reject numbers with leading zeroes`` () =
    let codec = Json.compile Schema.int
    expectFailure "Leading zeroes are not allowed" (fun () -> Json.deserialize codec "01")

[<Fact>]
let ``Skip unknown fields with escaped quotes and backslashes deterministically`` () =
    let json = """{"extra":"\\\\\"quoted\\\\\" text","id":42}"""
    let value = Json.deserialize idCodec json
    test <@ value = { Id = 42 } @>

[<Fact>]
let ``Reject missing required keys`` () =
    expectFailure "Missing required key: id" (fun () -> Json.deserialize idCodec "{}")

[<Fact>]
let ``Reject empty and whitespace-only payloads`` () =
    let intCodec = Json.compile Schema.int
    let stringCodec = Json.compile Schema.string

    expectFailure "Unexpected end of input" (fun () -> Json.deserialize intCodec "")
    expectFailure "Unexpected end of input" (fun () -> Json.deserialize intCodec "   ")
    expectFailure "Expected \"" (fun () -> Json.deserialize stringCodec "   ")

[<Fact>]
let ``Reject truncated containers`` () =
    let listCodec = Json.compile (Schema.list Schema.int)

    expectFailure "Expected , or }" (fun () -> Json.deserialize idCodec """{"id":1""")
    expectFailure "Expected , or ]" (fun () -> Json.deserialize listCodec "[1,2")

[<Fact>]
let ``Decode empty containers`` () =
    let listCodec = Json.compile (Schema.list Schema.int)
    let emptyList = Json.deserialize listCodec "[]"

    test <@ emptyList = [] @>

[<Fact>]
let ``Reject unsupported numeric formats deterministically`` () =
    let codec = Json.compile Schema.int

    expectFailure "Expected digit" (fun () -> Json.deserialize codec "-")
    expectFailure "Trailing content after top-level JSON value" (fun () -> Json.deserialize codec "1.0")
    expectFailure "Trailing content after top-level JSON value" (fun () -> Json.deserialize codec "1e3")
    expectFailure "Expected digit" (fun () -> Json.deserialize codec "+1")
    expectFailure "Leading zeroes are not allowed" (fun () -> Json.deserialize codec "-01")

[<Fact>]
let ``Reject incomplete and invalid bool literals`` () =
    let codec = Json.compile Schema.bool

    expectFailure "Expected true or false" (fun () -> Json.deserialize codec "tru")
    expectFailure "Expected true or false" (fun () -> Json.deserialize codec "fals")
    expectFailure "Expected true or false" (fun () -> Json.deserialize codec "null")

[<Fact>]
let ``Skip deeply nested unknown values and still decode target field`` () =
    let nestedUnknown =
        String.replicate 256 """{"x":"""
        + "0"
        + String.replicate 256 "}"

    let json = $"""{{"extra":{nestedUnknown},"id":42}}"""
    let value = Json.deserialize idCodec json
    test <@ value = { Id = 42 } @>

[<Fact>]
let ``Reject unknown values beyond the maximum JSON nesting depth`` () =
    let nestedUnknown =
        String.replicate 300 """{"x":"""
        + "0"
        + String.replicate 300 "}"

    let json = $"""{{"extra":{nestedUnknown},"id":42}}"""

    expectFailure
        "Maximum JSON nesting depth exceeded"
        (fun () -> Json.deserialize idCodec json)
