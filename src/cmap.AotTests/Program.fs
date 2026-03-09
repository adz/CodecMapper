namespace cmap.AotTests

open System
open cmap

type Address = { Street: string; City: string }
type Person = { Id: int; Name: string; Home: Address }

type PersonId = PersonId of int
type WrappedPerson = { Id: PersonId; Tags: string list }

module Schemas =
    let address = Schema.record<Address, _> (fun a -> {| Street = a.Street; City = a.City |})
    let person = Schema.recordWith<Person, _> 
                    (fun p -> {| Id = p.Id; Name = p.Name; Home = p.Home |})
                    (Map.ofList ["Home", address :> ISchema])
    
    let personId = Schema.int |> Schema.map PersonId (fun (PersonId id) -> id)
    let wrappedPerson = Schema.recordWith<WrappedPerson, _>
                            (fun p -> {| Id = p.Id; Tags = p.Tags |})
                            (Map.ofList ["Id", personId :> ISchema])

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
        let p = { Id = 42; Name = "AOT"; Home = { Street = "Street"; City = "City" } }
        let pJson = Json.serialize pCodec p
        let pDecoded = Json.deserialize pCodec pJson
        test "Nested record round-trip" pDecoded p

        // 2. Mapped types
        let wpCodec = Json.compile Schemas.wrappedPerson
        let wp = { Id = PersonId 123; Tags = ["a"; "b"] }
        let wpJson = Json.serialize wpCodec wp
        let wpDecoded = Json.deserialize wpCodec wpJson
        test "Mapped types round-trip" wpDecoded wp

        // 3. Lists
        let listCodec = Json.compile (Schema.list Schema.string)
        let l = ["hello"; "aot"]
        let lJson = Json.serialize listCodec l
        let lDecoded = Json.deserialize listCodec lJson
        test "List round-trip" lDecoded l

        printfn "All AOT tests passed successfully!"
        0
