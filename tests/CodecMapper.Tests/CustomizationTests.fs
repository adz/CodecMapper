module CustomizationTests

open Xunit
open Swensen.Unquote
open CodecMapper
open TestCommon

let userIdSchema = Schema.int |> Schema.tryMap UserId.create UserId.value

let accountSchema =
    Schema.define<Account>
    |> Schema.construct makeAccount
    |> Schema.fieldWith "id" _.Id userIdSchema
    |> Schema.field "name" _.Name
    |> Schema.build

[<Fact>]
let ``Smart-constructor wrapper round-trips JSON`` () =
    let codec = Json.compile accountSchema
    let value = { Id = UserId 42; Name = "Ada" }
    let json = Json.serialize codec value
    let decoded = Json.deserialize codec json

    test <@ decoded = value @>

[<Fact>]
let ``Smart-constructor wrapper round-trips XML`` () =
    let codec = Xml.compile accountSchema
    let value = { Id = UserId 42; Name = "Ada" }
    let xml = Xml.serialize codec value
    let decoded = Xml.deserialize codec xml

    test <@ decoded = value @>

[<Fact>]
let ``Smart-constructor wrapper rejects invalid decoded values`` () =
    let jsonCodec = Json.compile accountSchema
    let xmlCodec = Xml.compile accountSchema

    expectFailure "JSON decode error at $.id: Validation failed: UserId must be positive" (fun () ->
        Json.deserialize jsonCodec """{"id":0,"name":"Ada"}""")

    expectFailure "XML decode error at $/id: Validation failed: UserId must be positive" (fun () ->
        Xml.deserialize xmlCodec "<account><id>0</id><name>Ada</name></account>")

[<Fact>]
let ``Opt-in validated helpers reject invalid scalar values`` () =
    expectFailure "Validation failed: string must not be empty" (fun () ->
        Json.deserialize (Json.compile Schema.nonEmptyString) "\"\"")

    expectFailure "Validation failed: int must be positive" (fun () ->
        Json.deserialize (Json.compile Schema.positiveInt) "0")

    expectFailure "Validation failed: list must contain at least one item" (fun () ->
        Json.deserialize (Json.compile (Schema.nonEmptyList Schema.int)) "[]")

[<Fact>]
let ``Trimmed string helper normalizes on encode and decode`` () =
    let codec = Json.compile Schema.trimmedString

    test <@ Json.deserialize codec "\"  Ada  \"" = "Ada" @>
    test <@ Json.serialize codec "  Ada  " = "\"Ada\"" @>
