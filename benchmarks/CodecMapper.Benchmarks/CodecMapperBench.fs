namespace CodecMapper.Benchmarks

open CodecMapper

type Address = { Street: string; City: string }

type Person = { Id: int; Name: string; Home: Address }

module Schemas =
    let address =
        Schema.define<Address>
        |> Schema.construct (fun street city -> { Street = street; City = city })
        |> Schema.field "Street" _.Street
        |> Schema.field "City" _.City
        |> Schema.build

    let person =
        Schema.define<Person>
        |> Schema.construct (fun id name home -> { Id = id; Name = name; Home = home })
        |> Schema.field "Id" _.Id
        |> Schema.field "Name" _.Name
        |> Schema.fieldWith "Home" _.Home address
        |> Schema.build

    ///
    /// The benchmark suite times batches of records so the published numbers
    /// reflect a more realistic payload than a single tiny object.
    let people = Schema.list person

module CodecMapperBench =
    let codec = Json.compile Schemas.people

    let serialize p = Json.serialize codec p
    let deserializeBytes bytes = Json.deserializeBytes codec bytes
