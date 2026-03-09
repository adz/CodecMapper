namespace CodecMapper.AotTests

open System
open CodecMapper

[<AutoOpen>]
module Domain =
    type Address = { Street: string; City: string }
    let makeAddress street city = { Street = street; City = city }

    type Person =
        { Id: int; Name: string; Home: Address }

    let makePerson id name home = { Id = id; Name = name; Home = home }

    type PersonId = PersonId of int
    type WrappedPerson = { Id: PersonId; Tags: string list }
    let makeWrappedPerson id tags = { Id = id; Tags = tags }

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

    type OptionalRecord =
        { Nickname: string option
          Age: int option }

    let makeOptionalRecord nickname age = { Nickname = nickname; Age = age }

    type NumericRecord =
        { Total: int64
          Count: uint32
          Capacity: uint64
          Ratio: float
          Price: decimal }

    let makeNumericRecord total count capacity ratio price =
        { Total = total
          Count = count
          Capacity = capacity
          Ratio = ratio
          Price = price }

    type AuditRecord =
        { UserId: Guid
          CreatedAt: DateTime
          Duration: TimeSpan }

    let makeAuditRecord userId createdAt duration =
        { UserId = userId
          CreatedAt = createdAt
          Duration = duration }

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

    let personId = Schema.int |> Schema.map PersonId (fun (PersonId id) -> id)

    let wrappedPerson =
        Schema.define<WrappedPerson>
        |> Schema.construct makeWrappedPerson
        |> Schema.fieldWith "id" _.Id personId
        |> Schema.fieldWith "tags" _.Tags (Schema.list Schema.string)
        |> Schema.build

    let userId = Schema.int |> Schema.tryMap UserId.create UserId.value

    let account =
        Schema.define<Account>
        |> Schema.construct makeAccount
        |> Schema.fieldWith "id" _.Id userId
        |> Schema.field "name" _.Name
        |> Schema.build

    let optionalRecord =
        Schema.define<OptionalRecord>
        |> Schema.construct makeOptionalRecord
        |> Schema.field "nickname" _.Nickname
        |> Schema.field "age" _.Age
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

    let auditRecord =
        Schema.define<AuditRecord>
        |> Schema.construct makeAuditRecord
        |> Schema.field "userId" _.UserId
        |> Schema.field "createdAt" _.CreatedAt
        |> Schema.field "duration" _.Duration
        |> Schema.build

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

        let pXmlCodec = Xml.compile Schemas.person
        let pXml = Xml.serialize pXmlCodec p
        let pXmlDecoded = Xml.deserialize pXmlCodec pXml
        test "Nested record XML round-trip" pXmlDecoded p

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

        // 4. Common built-in schema helpers
        let auditCodec = Json.compile Schemas.auditRecord

        let audit =
            { UserId = Guid.Parse("12345678-1234-1234-1234-123456789abc")
              CreatedAt = DateTime(2024, 10, 12, 8, 30, 45, DateTimeKind.Utc)
              Duration = TimeSpan.FromMinutes(95.0) }

        let auditJson = Json.serialize auditCodec audit
        let auditDecoded = Json.deserialize auditCodec auditJson
        test "Common type round-trip" auditDecoded audit

        // 5. Smart-constructor mappings
        let accountCodec = Json.compile Schemas.account
        let account = { Id = UserId 7; Name = "AOT" }
        let accountJson = Json.serialize accountCodec account
        let accountDecoded = Json.deserialize accountCodec accountJson
        test "Validated mapping round-trip" accountDecoded account

        // 6. Option support
        let optionalCodec = Json.compile Schemas.optionalRecord

        let optionalValue = { Nickname = Some "AOT"; Age = None }

        let optionalJson = Json.serialize optionalCodec optionalValue
        let optionalDecoded = Json.deserialize optionalCodec optionalJson
        test "Option round-trip" optionalDecoded optionalValue

        let optionalXmlCodec = Xml.compile Schemas.optionalRecord
        let optionalXml = Xml.serialize optionalXmlCodec optionalValue
        let optionalXmlDecoded = Xml.deserialize optionalXmlCodec optionalXml
        test "Option XML round-trip" optionalXmlDecoded optionalValue

        // 7. Extended numeric support
        let numericCodec = Json.compile Schemas.numericRecord

        let numeric =
            { Total = 9_223_372_036_854_775_000L
              Count = 4_294_967_000u
              Capacity = 18_446_744_073_709_551_000UL
              Ratio = -12.5e3
              Price = 12345.6789M }

        let numericJson = Json.serialize numericCodec numeric
        let numericDecoded = Json.deserialize numericCodec numericJson
        test "Extended numeric round-trip" numericDecoded numeric

        printfn "All AOT tests passed successfully!"
        0
