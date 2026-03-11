namespace CodecMapper.FablePackageTests

open System
open System.Collections.Generic
open CodecMapper

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

    type NumericRecord = {
        Total: int64
        Count: uint32
        Capacity: uint64
        Ratio: float
        Price: decimal
    }

    let makeNumericRecord total count capacity ratio price = {
        Total = total
        Count = count
        Capacity = capacity
        Ratio = ratio
        Price = price
    }

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

    let userId = Schema.int |> Schema.tryMap UserId.create UserId.value

    let account =
        Schema.define<Account>
        |> Schema.construct makeAccount
        |> Schema.fieldWith "id" _.Id userId
        |> Schema.field "name" _.Name
        |> Schema.build

    let numericRecord =
        Schema.define<NumericRecord>
        |> Schema.construct makeNumericRecord
        |> Schema.field "total" _.Total
        |> Schema.field "count" _.Count
        |> Schema.field "capacity" _.Capacity
        |> Schema.field "ratio" _.Ratio
        |> Schema.field "price" _.Price
        |> Schema.build

module Program =
    let private test name actual expected =
        if actual = expected then
            printfn "[PASS] %s" name
        else
            failwithf "%s: expected %A, got %A" name expected actual

    let private expectFailureContains name expectedError action =
        try
            action () |> ignore
            failwithf "%s: expected failure containing %s" name expectedError
        with error ->
            if not (error.Message.Contains(expectedError)) then
                failwithf "%s: expected %s, got %s" name expectedError error.Message

    [<EntryPoint>]
    let main _ =
        let personCodec = Json.compile Schemas.person

        let person = {
            Id = 42
            Name = "FablePackage"
            Home = { Street = "Main"; City = "Adelaide" }
        }

        let json = Json.serialize personCodec person
        let personDecoded = Json.deserialize personCodec json
        test "JSON package round-trip" personDecoded person

        let xmlCodec = Xml.compile Schemas.person
        let xml = Xml.serialize xmlCodec person
        let personXmlDecoded = Xml.deserialize xmlCodec xml
        test "XML package round-trip" personXmlDecoded person

        let yamlCodec = Yaml.compile Schemas.person
        let yaml = Yaml.serialize yamlCodec person
        let personYamlDecoded = Yaml.deserialize yamlCodec yaml
        test "Yaml package round-trip" personYamlDecoded person

        let keyValueCodec = KeyValue.compile Schemas.person
        let keyValue = KeyValue.serialize keyValueCodec person
        let personKeyValueDecoded = KeyValue.deserialize keyValueCodec keyValue
        test "KeyValue package round-trip" personKeyValueDecoded person

        let accountCodec = Json.compile Schemas.account
        let account = { Id = UserId 7; Name = "Ada" }
        let accountJson = Json.serialize accountCodec account
        let accountDecoded = Json.deserialize accountCodec accountJson
        test "Validated mapping package round-trip" accountDecoded account

        let numericCodec = Json.compile Schemas.numericRecord

        let numeric = {
            Total = 9_223_372_036_854_775_000L
            Count = 4_294_967_000u
            Capacity = 18_446_744_073_709_551_000UL
            Ratio = -12.5e3
            Price = 12345.6789M
        }

        let numericJson = Json.serialize numericCodec numeric
        let numericDecoded = Json.deserialize numericCodec numericJson
        test "Extended numeric package round-trip" numericDecoded numeric

        expectFailureContains "Out-of-range uint32 package rejection" "uint32 value out of range" (fun () ->
            Json.deserialize (Json.compile Schema.uint32) "4294967296")

        printfn "CodecMapper packaged consumer tests execution finished."
        0
