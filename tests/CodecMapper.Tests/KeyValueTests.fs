module KeyValueTests

open Swensen.Unquote
open Xunit
open CodecMapper
open TestCommon

let addressSchema =
    Schema.define<Address>
    |> Schema.construct makeAddress
    |> Schema.field "street" _.Street
    |> Schema.field "city" _.City
    |> Schema.build

let userIdSchema = Schema.int |> Schema.tryMap UserId.create UserId.value

let personSchema =
    Schema.define<Person>
    |> Schema.construct makePerson
    |> Schema.field "id" _.Id
    |> Schema.field "name" _.Name
    |> Schema.fieldWith "home" _.Home addressSchema
    |> Schema.build

let optionalSchema =
    Schema.define<OptionalRecord>
    |> Schema.construct makeOptionalRecord
    |> Schema.field "nickname" _.Nickname
    |> Schema.field "age" _.Age
    |> Schema.build

let accountSchema =
    Schema.define<Account>
    |> Schema.construct makeAccount
    |> Schema.fieldWith "id" _.Id userIdSchema
    |> Schema.field "name" _.Name
    |> Schema.build

type ListRecord = { Values: string list }
let makeListRecord values = { Values = values }

let listSchema =
    Schema.define<ListRecord>
    |> Schema.construct makeListRecord
    |> Schema.fieldWith "values" _.Values (Schema.list Schema.string)
    |> Schema.build

[<Fact>]
let ``KeyValue flattens nested records with dotted keys`` () =
    let codec = KeyValue.compile personSchema

    let value = {
        Id = 42
        Name = "Ada"
        Home = { Street = "Main"; City = "Adelaide" }
    }

    let encoded = KeyValue.serialize codec value
    let decoded = KeyValue.deserialize codec encoded

    let expected =
        Map.ofList [ "id", "42"; "name", "Ada"; "home.street", "Main"; "home.city", "Adelaide" ]

    test <@ encoded = expected @>
    test <@ decoded = value @>

[<Fact>]
let ``KeyValue omits None values and decodes missing option keys as None`` () =
    let codec = KeyValue.compile optionalSchema
    let value = { Nickname = None; Age = Some 42 }

    let encoded = KeyValue.serialize codec value
    let decoded = KeyValue.deserialize codec encoded

    test <@ encoded = Map.ofList [ "age", "42" ] @>
    test <@ decoded = value @>

[<Fact>]
let ``KeyValue supports env-style key naming through options`` () =
    let codec = KeyValue.compileUsing KeyValue.Options.environment personSchema

    let value = {
        Id = 7
        Name = "Lin"
        Home = { Street = "North"; City = "Perth" }
    }

    let encoded = KeyValue.serialize codec value

    let expected =
        Map.ofList [ "ID", "7"; "NAME", "Lin"; "HOME__STREET", "North"; "HOME__CITY", "Perth" ]

    test <@ encoded = expected @>

[<Fact>]
let ``KeyValue preserves validated wrapper mappings through scalar leaves`` () =
    let codec = KeyValue.compile accountSchema
    let value = { Id = UserId 7; Name = "Ada" }

    let encoded = KeyValue.serialize codec value
    let decoded = KeyValue.deserialize codec encoded

    test <@ encoded = Map.ofList [ "id", "7"; "name", "Ada" ] @>
    test <@ decoded = value @>

[<Fact>]
let ``KeyValue rejects collection schemas that do not flatten deterministically`` () =
    let error =
        Assert.Throws<System.Exception>(fun () -> KeyValue.compile listSchema |> ignore)

    test <@ error.Message.Contains("flattened record and scalar schemas") @>
