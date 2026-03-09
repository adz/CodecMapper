module SchemaDslTests

open Xunit
open Swensen.Unquote
open CodecMapper
open TestCommon

[<Fact>]
let ``Round-trip using Pipeline DSL`` () =
    let addressSchema =
        Schema.define<Address>
        |> Schema.construct makeAddress
        |> Schema.field "street" _.Street
        |> Schema.field "city" _.City
        |> Schema.build

    let personSchema =
        Schema.define<Person>
        |> Schema.construct makePerson
        |> Schema.field "id" _.Id
        |> Schema.field "name" _.Name
        |> Schema.fieldWith "home" _.Home addressSchema
        |> Schema.build

    let codec = Json.compile personSchema

    let person =
        { Id = 42
          Name = "Adam"
          Home =
            { Street = "123 F# Lane"
              City = "Pipeline City" } }

    let json = Json.serialize codec person
    let decoded = Json.deserialize codec json
    test <@ decoded = person @>

[<Fact>]
let ``One schema, multiple formats (JSON and XML)`` () =
    let addressSchema =
        Schema.define<Address>
        |> Schema.construct makeAddress
        |> Schema.field "street" _.Street
        |> Schema.field "city" _.City
        |> Schema.build

    let personSchema =
        Schema.define<Person>
        |> Schema.construct makePerson
        |> Schema.field "id" _.Id
        |> Schema.field "name" _.Name
        |> Schema.fieldWith "home" _.Home addressSchema
        |> Schema.build

    let person =
        { Id = 42
          Name = "Adam"
          Home =
            { Street = "123 F# Lane"
              City = "AOT City" } }

    let jsonCodec = Json.compile personSchema
    let json = Json.serialize jsonCodec person
    test <@ json = "{\"id\":42,\"name\":\"Adam\",\"home\":{\"street\":\"123 F# Lane\",\"city\":\"AOT City\"}}" @>

    let xmlCodec = Xml.compile personSchema
    let xml = Xml.serialize xmlCodec person

    test
        <@
            xml = "<person><id>42</id><name>Adam</name><home><street>123 F# Lane</street><city>AOT City</city></home></person>"
        @>

[<Fact>]
let ``Round-trip list of strings JSON`` () =
    let listSchema = Schema.list Schema.string
    let codec = Json.compile listSchema

    let value = [ "a"; "b"; "c" ]
    let json = Json.serialize codec value
    test <@ json = "[\"a\",\"b\",\"c\"]" @>

    let decoded = Json.deserialize codec json
    test <@ decoded = value @>

[<Fact>]
let ``Round-trip mapped type (PersonId) JSON`` () =
    let personIdSchema = Schema.int |> Schema.map PersonId (fun (PersonId id) -> id)

    let wrappedPersonSchema =
        Schema.define<WrappedPerson>
        |> Schema.construct makeWrappedPerson
        |> Schema.fieldWith "id" _.Id personIdSchema
        |> Schema.fieldWith "tags" _.Tags (Schema.list Schema.string)
        |> Schema.build

    let codec = Json.compile wrappedPersonSchema

    let p =
        { Id = PersonId 123
          Tags = [ "fsharp"; "aot" ] }

    let json = Json.serialize codec p
    let decoded = Json.deserialize codec json
    test <@ decoded = p @>

[<Fact>]
let ``Round-trip collections with auto-resolution`` () =
    let collectionSchema =
        Schema.define<CollectionRecord>
        |> Schema.construct makeCollectionRecord
        |> Schema.field "list" _.List
        |> Schema.field "array" _.Array
        |> Schema.build

    let codec = Json.compile collectionSchema
    let value = { List = [ 1; 2 ]; Array = [| "a"; "b" |] }
    let json = Json.serialize codec value
    let decoded = Json.deserialize codec json
    test <@ decoded = value @>

[<Fact>]
let ``Round-trip using typed pipeline with 20 fields`` () =
    let largeSchema =
        Schema.define<LargeRecord>
        |> Schema.construct makeLargeRecord
        |> Schema.field "f1" _.F1
        |> Schema.field "f2" _.F2
        |> Schema.field "f3" _.F3
        |> Schema.field "f4" _.F4
        |> Schema.field "f5" _.F5
        |> Schema.field "f6" _.F6
        |> Schema.field "f7" _.F7
        |> Schema.field "f8" _.F8
        |> Schema.field "f9" _.F9
        |> Schema.field "f10" _.F10
        |> Schema.field "f11" _.F11
        |> Schema.field "f12" _.F12
        |> Schema.field "f13" _.F13
        |> Schema.field "f14" _.F14
        |> Schema.field "f15" _.F15
        |> Schema.field "f16" _.F16
        |> Schema.field "f17" _.F17
        |> Schema.field "f18" _.F18
        |> Schema.field "f19" _.F19
        |> Schema.field "f20" _.F20
        |> Schema.build

    let codec = Json.compile largeSchema

    let value =
        {
            F1 = 1
            F2 = 2
            F3 = 3
            F4 = 4
            F5 = 5
            F6 = 6
            F7 = 7
            F8 = 8
            F9 = 9
            F10 = 10
            F11 = 11
            F12 = 12
            F13 = 13
            F14 = 14
            F15 = 15
            F16 = 16
            F17 = 17
            F18 = 18
            F19 = 19
            F20 = 20
        }

    let json = Json.serialize codec value
    let decoded = Json.deserialize codec json
    test <@ decoded = value @>
