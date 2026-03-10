module YamlTests

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

let collectionSchema =
    Schema.define<CollectionRecord>
    |> Schema.construct makeCollectionRecord
    |> Schema.field "list" _.List
    |> Schema.field "array" _.Array
    |> Schema.build

[<Fact>]
let ``Yaml round-trips nested records`` () =
    let codec = Yaml.compile personSchema

    let value = {
        Id = 42
        Name = "Ada"
        Home = { Street = "Main"; City = "Adelaide" }
    }

    let yaml = Yaml.serialize codec value
    let decoded = Yaml.deserialize codec yaml

    test <@ yaml = "id: 42\nname: Ada\nhome:\n  street: Main\n  city: Adelaide" @>
    test <@ decoded = value @>

[<Fact>]
let ``Yaml preserves explicit null option values`` () =
    let codec = Yaml.compile optionalSchema
    let value = { Nickname = None; Age = Some 42 }

    let yaml = Yaml.serialize codec value
    let decoded = Yaml.deserialize codec yaml

    test <@ yaml = "nickname: null\nage: 42" @>
    test <@ decoded = value @>

[<Fact>]
let ``Yaml round-trips collection shapes through block sequences`` () =
    let codec = Yaml.compile collectionSchema

    let value = {
        List = [ 1; 2 ]
        Array = [| "a"; "b" |]
    }

    let yaml = Yaml.serialize codec value
    let decoded = Yaml.deserialize codec yaml

    test <@ yaml = "list:\n  - 1\n  - 2\narray:\n  - a\n  - b" @>
    test <@ decoded = value @>

[<Fact>]
let ``Yaml deserializes hand-authored sequences of objects`` () =
    let codec = Yaml.compile (Schema.list personSchema)

    let yaml =
        "- id: 1\n  name: Ada\n  home:\n    street: Main\n    city: Adelaide\n- id: 2\n  name: Lin\n  home:\n    street: North\n    city: Perth"

    let decoded = Yaml.deserialize codec yaml

    let expected = [
        {
            Id = 1
            Name = "Ada"
            Home = { Street = "Main"; City = "Adelaide" }
        }
        {
            Id = 2
            Name = "Lin"
            Home = { Street = "North"; City = "Perth" }
        }
    ]

    test <@ decoded = expected @>
