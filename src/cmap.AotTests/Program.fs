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

    type AuditRecord =
        {
            UserId: Guid
            CreatedAt: DateTime
            Duration: TimeSpan
        }

    let makeAuditRecord userId createdAt duration =
        {
            UserId = userId
            CreatedAt = createdAt
            Duration = duration
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
            {
                UserId = Guid.Parse("12345678-1234-1234-1234-123456789abc")
                CreatedAt = DateTime(2024, 10, 12, 8, 30, 45, DateTimeKind.Utc)
                Duration = TimeSpan.FromMinutes(95.0)
            }

        let auditJson = Json.serialize auditCodec audit
        let auditDecoded = Json.deserialize auditCodec auditJson
        test "Common type round-trip" auditDecoded audit

        // 5. Smart-constructor mappings
        let accountCodec = Json.compile Schemas.account
        let account = { Id = UserId 7; Name = "AOT" }
        let accountJson = Json.serialize accountCodec account
        let accountDecoded = Json.deserialize accountCodec accountJson
        test "Validated mapping round-trip" accountDecoded account

        printfn "All AOT tests passed successfully!"
        0
