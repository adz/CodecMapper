namespace cmap.FableTests

open System
open cmap

[<AutoOpen>]
module Domain =
    type Address = { Street: string; City: string }
    let makeAddress street city = { Street = street; City = city }

    type Person = { Id: int; Name: string; Home: Address }
    let makePerson id name home = { Id = id; Name = name; Home = home }

    type UserId = UserId of int

    module UserId =
        let create value =
            if value > 0 then
                Ok(UserId value)
            else
                Error "UserId must be positive"

        let value (UserId value) = value

    type Account = { Id: UserId; Name: string }
    let makeAccount id name = { Id = id; Name = name }

    type OptionalRecord = { Nickname: string option; Age: int option }
    let makeOptionalRecord nickname age = { Nickname = nickname; Age = age }

module Schemas =
    let address = 
        Schema.define<Address>
        |> Schema.construct makeAddress
        |> Schema.field "street" _.Street
        |> Schema.field "city" _.City
        |> Schema.build

    let person = 
        Schema.define<Person>
        |> Schema.construct makePerson
        |> Schema.field "id" _.Id
        |> Schema.field "name" _.Name
        |> Schema.fieldWith "home" _.Home address
        |> Schema.build

    let optionalRecord =
        Schema.define<OptionalRecord>
        |> Schema.construct makeOptionalRecord
        |> Schema.field "nickname" _.Nickname
        |> Schema.field "age" _.Age
        |> Schema.build

    let userId = Schema.int |> Schema.tryMap UserId.create UserId.value

    let account =
        Schema.define<Account>
        |> Schema.construct makeAccount
        |> Schema.fieldWith "id" _.Id userId
        |> Schema.field "name" _.Name
        |> Schema.build

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

            let pXmlCodec = Xml.compile Schemas.person
            let pXml = Xml.serialize pXmlCodec p
            let pXmlDecoded = Xml.deserialize pXmlCodec pXml
            test "Nested record XML round-trip" pXmlDecoded p

            let listCodec = Json.compile (Schema.list Schema.string)
            let l = ["hello"; "fable"]
            let lJson = Json.serialize listCodec l
            let lDecoded = Json.deserialize listCodec lJson
            test "List round-trip" lDecoded l

            let optionalCodec = Json.compile Schemas.optionalRecord
            let optionalValue = { Nickname = Some "Fable"; Age = None }
            let optionalJson = Json.serialize optionalCodec optionalValue
            let optionalDecoded = Json.deserialize optionalCodec optionalJson
            test "Option round-trip" optionalDecoded optionalValue

            let accountCodec = Json.compile Schemas.account
            let accountValue = { Id = UserId 9; Name = "Fable" }
            let accountJson = Json.serialize accountCodec accountValue
            let accountDecoded = Json.deserialize accountCodec accountJson
            test "Validated mapping round-trip" accountDecoded accountValue
            
            printfn "Fable tests execution finished."
        with
        | ex -> printfn "[ERROR] %s" ex.Message
        0
