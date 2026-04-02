namespace CodecMapper.Benchmarks

open CodecMapper

module Json = CodecMapper.Json

type Address = { Street: string; City: string }

type Person = { Id: int; Name: string; Home: Address }

module Schemas =
    let address =
        Schema.record (fun street city -> { Street = street; City = city })
        |> Schema.field "Street" (fun (address: Address) -> address.Street)
        |> Schema.field "City" (fun (address: Address) -> address.City)
        |> Schema.build

    let person =
        Schema.record (fun id name home -> { Id = id; Name = name; Home = home })
        |> Schema.field "Id" (fun (person: Person) -> person.Id)
        |> Schema.field "Name" (fun (person: Person) -> person.Name)
        |> Schema.fieldWith "Home" (fun (person: Person) -> person.Home) address
        |> Schema.build

    ///
    /// The benchmark suite times batches of records so the published numbers
    /// reflect a more realistic payload than a single tiny object.
    let people = Schema.list person

module CodecMapperBench =
    let codec = Json.compile Schemas.people

    let serialize p = Json.serialize codec p
    let deserializeBytes bytes = Json.deserializeBytes codec bytes
