namespace cmap.FableTests

open System
open cmap

type Address = { Street: string; City: string }
type Person = { Id: int; Name: string; Home: Address }

module Schemas =
    let address = Schema.record<Address, _> (fun a -> {| Street = a.Street; City = a.City |})
    let person = Schema.recordWith<Person, _> 
                    (fun p -> {| Id = p.Id; Name = p.Name; Home = p.Home |})
                    (Map.ofList ["Home", Schema.unwrap address])

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
