namespace cmap.AotTests

open System
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

module Schemas =
    let address = schema<Address> {
        construct makeAddress
        field "street" _.Street
        field "city" _.City
    }

    let person = schema<Person> {
        construct makePerson
        field "id" _.Id
        field "name" _.Name
        field "home" _.Home address
    }

    let personId = Schema.int |> Schema.map PersonId (fun (PersonId id) -> id)

    let wrappedPerson = schema<WrappedPerson> {
        construct makeWrappedPerson
        field "id" _.Id personId
        field "tags" _.Tags (Schema.list Schema.string)
    }

module Program =
    let test name actual expected =
        if actual = expected then
            printfn "[PASS] %s" name
        else
            printfn "[FAIL] %s: Expected %A, got %A" name expected actual
            exit 1

    [<EntryPoint>]
    let main args =
        printfn "Running AOT Compatibility Tests..."

        // 1. Simple record
        let pCodec = Json.compile Schemas.person

        let p =
            { Id = 42
              Name = "AOT"
              Home = { Street = "Street"; City = "City" } }

        let pJson = Json.serialize pCodec p
        let pDecoded = Json.deserialize pCodec pJson
        test "Nested record round-trip" pDecoded p

        // 2. Mapped types
        let wpCodec = Json.compile Schemas.wrappedPerson

        let wp =
            { Id = PersonId 123
              Tags = [ "a"; "b" ] }

        let wpJson = Json.serialize wpCodec wp
        let wpDecoded = Json.deserialize wpCodec wpJson
        test "Mapped types round-trip" wpDecoded wp

        // 3. Lists
        let listCodec = Json.compile (Schema.list Schema.string)
        let l = [ "hello"; "aot" ]
        let lJson = Json.serialize listCodec l
        let lDecoded = Json.deserialize listCodec lJson
        test "List round-trip" lDecoded l

        printfn "All AOT tests passed successfully!"
        0
