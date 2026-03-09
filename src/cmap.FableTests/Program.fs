namespace cmap.FableTests

open System
open cmap

[<AutoOpen>]
module Domain =
    type Address = { Street: string; City: string }
    let makeAddress street city = { Street = street; City = city }

    type Person = { Id: int; Name: string; Home: Address }
    let makePerson id name home = { Id = id; Name = name; Home = home }

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

module Program =
    let test name actual expected =
        // Simple comparison for Fable
        if sprintf "%A" actual = sprintf "%A" expected then
            printfn "[PASS] %s" name
        else
            printfn "[FAIL] %s: Expected %A, got %A" name expected actual

    [<EntryPoint>]
    let main args =
        printfn "Running Fable Compatibility Tests..."

        try
            let pCodec = Json.compile Schemas.person
            let p = { Id = 42; Name = "Fable"; Home = { Street = "Street"; City = "City" } }
            let pJson = Json.serialize pCodec p
            let pDecoded = Json.deserialize pCodec pJson
            test "Nested record round-trip" pDecoded p

            let listCodec = Json.compile (Schema.list Schema.string)
            let l = ["hello"; "fable"]
            let lJson = Json.serialize listCodec l
            let lDecoded = Json.deserialize listCodec lJson
            test "List round-trip" lDecoded l
            
            printfn "Fable tests execution finished."
        with
        | ex -> printfn "[ERROR] %s" ex.Message
        0
