module CommonTypeTests

open System
open Xunit
open Swensen.Unquote
open cmap
open TestCommon

let commonTypeSchema =
    Schema.define<CommonTypeRecord>
    |> Schema.construct makeCommonTypeRecord
    |> Schema.field "age" _.Age
    |> Schema.field "level" _.Level
    |> Schema.field "delta" _.Delta
    |> Schema.field "score" _.Score
    |> Schema.field "initial" _.Initial
    |> Schema.field "userId" _.UserId
    |> Schema.field "createdAt" _.CreatedAt
    |> Schema.field "updatedAt" _.UpdatedAt
    |> Schema.field "duration" _.Duration
    |> Schema.build

[<Fact>]
let ``Auto-resolved common types round-trip JSON`` () =
    let codec = Json.compile commonTypeSchema

    let value =
        {
            Age = 42s
            Level = 7uy
            Delta = -3y
            Score = 512us
            Initial = 'A'
            UserId = Guid.Parse("12345678-1234-1234-1234-123456789abc")
            CreatedAt = DateTime(2024, 10, 12, 8, 30, 45, DateTimeKind.Utc)
            UpdatedAt = DateTimeOffset(2024, 10, 12, 18, 0, 0, TimeSpan.FromHours(9.0))
            Duration = TimeSpan.FromMinutes(95.0)
        }

    let json = Json.serialize codec value
    let decoded = Json.deserialize codec json

    test <@ decoded = value @>

[<Fact>]
let ``Auto-resolved common types round-trip XML`` () =
    let codec = Xml.compile commonTypeSchema

    let value =
        {
            Age = 42s
            Level = 7uy
            Delta = -3y
            Score = 512us
            Initial = 'A'
            UserId = Guid.Parse("12345678-1234-1234-1234-123456789abc")
            CreatedAt = DateTime(2024, 10, 12, 8, 30, 45, DateTimeKind.Utc)
            UpdatedAt = DateTimeOffset(2024, 10, 12, 18, 0, 0, TimeSpan.FromHours(9.0))
            Duration = TimeSpan.FromMinutes(95.0)
        }

    let xml = Xml.serialize codec value
    let decoded = Xml.deserialize codec xml

    test <@ decoded = value @>

[<Fact>]
let ``Reject out-of-range common numeric decodes`` () =
    expectFailure "int16 value out of range" (fun () -> Json.deserialize (Json.compile Schema.int16) "32768")
    expectFailure "byte value out of range" (fun () -> Json.deserialize (Json.compile Schema.byte) "256")
    expectFailure "sbyte value out of range" (fun () -> Json.deserialize (Json.compile Schema.sbyte) "-129")
    expectFailure "uint16 value out of range" (fun () -> Json.deserialize (Json.compile Schema.uint16) "65536")
    expectFailure "char value must contain exactly one character" (fun () -> Json.deserialize (Json.compile Schema.char) "\"AB\"")
