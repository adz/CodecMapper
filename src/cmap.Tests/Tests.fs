module Tests

open Xunit
open Swensen.Unquote
open cmap

[<AutoOpen>]
module Domain =
    type Address = { Street: string; City: string }
    let makeAddress street city = { Street = street; City = city }

    type Person = { Id: int; Name: string; Home: Address }
    let makePerson id name home = { Id = id; Name = name; Home = home }

    type PersonId = PersonId of int
    type WrappedPerson = { Id: PersonId; Tags: string list }
    let makeWrappedPerson id tags = { Id = id; Tags = tags }

[<Fact>]
let ``Round-trip using Fluent DSL (CE)`` () =
    let addressSchema = schema<Address> {
        construct makeAddress
        field "street" _.Street
        field "city" _.City
    }

    let personSchema = schema<Person> {
        construct makePerson
        field "id" _.Id
        field "name" _.Name
        field "home" _.Home addressSchema
    }

    let codec = Json.compile personSchema

    let person =
        { Id = 42
          Name = "Adam"
          Home =
            { Street = "123 F# Lane"
              City = "Fluent City" } }

    let json = Json.serialize codec person
    let decoded = Json.deserialize codec json
    test <@ decoded = person @>

[<Fact>]
let ``One schema, multiple formats (JSON and XML)`` () =
    let addressSchema = schema<Address> {
        construct makeAddress
        field "street" _.Street
        field "city" _.City
    }

    let personSchema = schema<Person> {
        construct makePerson
        field "id" _.Id
        field "name" _.Name
        field "home" _.Home addressSchema
    }

    let person =
        { Id = 42
          Name = "Adam"
          Home =
            { Street = "123 F# Lane"
              City = "AOT City" } }

    // JSON 
    let jsonCodec = Json.compile personSchema
    let json = Json.serialize jsonCodec person
    test <@ json = "{\"id\":42,\"name\":\"Adam\",\"home\":{\"street\":\"123 F# Lane\",\"city\":\"AOT City\"}}" @>

    // XML
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

    let wrappedPersonSchema = schema<WrappedPerson> {
        construct makeWrappedPerson
        field "id" _.Id personIdSchema
        field "tags" _.Tags (Schema.list Schema.string)
    }

    let codec = Json.compile wrappedPersonSchema

    let p =
        { Id = PersonId 123
          Tags = [ "fsharp"; "aot" ] }

    let json = Json.serialize codec p
    let decoded = Json.deserialize codec json
    test <@ decoded = p @>

type CollectionRecord = { List: int list; Array: string array }
let makeCollectionRecord l a = { List = l; Array = a }

[<Fact>]
let ``Round-trip collections with auto-resolution`` () =
    let collectionSchema = schema<CollectionRecord> {
        construct makeCollectionRecord
        field "list" _.List
        field "array" _.Array
    }
    let codec = Json.compile collectionSchema
    let value = { List = [1; 2]; Array = [|"a"; "b"|] }
    let json = Json.serialize codec value
    let decoded = Json.deserialize codec json
    test <@ decoded = value @>

type LargeRecord = { F1: int; F2: string; F3: int }
let makeLargeRecord (args: obj[]) = 
    { F1 = unbox args.[0]; F2 = unbox args.[1]; F3 = unbox args.[2] }

[<Fact>]
let ``Round-trip using obj array escape hatch (Unlimited Arity)`` () =
    let largeSchema = schema<LargeRecord> {
        construct makeLargeRecord
        field "f1" _.F1
        field "f2" _.F2
        field "f3" _.F3
    }
    let codec = Json.compile largeSchema
    let value = { F1 = 1; F2 = "two"; F3 = 3 }
    let json = Json.serialize codec value
    let decoded = Json.deserialize codec json
    test <@ decoded = value @>
