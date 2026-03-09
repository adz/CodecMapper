namespace cmap.Benchmarks

open cmap

type Address = { Street: string; City: string }

type Person =
    { Id: int; Name: string; Home: Address }

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

module CmapBench =
    let codec = Json.compile Schemas.person

    let serialize p = Json.serialize codec p
    let deserializeBytes bytes = Json.deserializeBytes codec bytes
