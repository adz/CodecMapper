module TestCommon

open System
open Swensen.Unquote

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

    type OptionalRecord =
        {
            Nickname: string option
            Age: int option
        }

    let makeOptionalRecord nickname age =
        {
            Nickname = nickname
            Age = age
        }

    type CollectionRecord = { List: int list; Array: string array }
    let makeCollectionRecord l a = { List = l; Array = a }

    type BoolArrayRecord = { Enabled: bool; Aliases: string array }
    let makeBoolArrayRecord enabled aliases = { Enabled = enabled; Aliases = aliases }

    type CommonTypeRecord =
        {
            Age: int16
            Level: byte
            Delta: sbyte
            Score: uint16
            Initial: char
            UserId: Guid
            CreatedAt: DateTime
            UpdatedAt: DateTimeOffset
            Duration: TimeSpan
        }

    let makeCommonTypeRecord age level delta score initial userId createdAt updatedAt duration =
        {
            Age = age
            Level = level
            Delta = delta
            Score = score
            Initial = initial
            UserId = userId
            CreatedAt = createdAt
            UpdatedAt = updatedAt
            Duration = duration
        }

    type IdOnly = { Id: int }
    let makeIdOnly id = { Id = id }

    type LargeRecord =
        {
            F1: int
            F2: int
            F3: int
            F4: int
            F5: int
            F6: int
            F7: int
            F8: int
            F9: int
            F10: int
            F11: int
            F12: int
            F13: int
            F14: int
            F15: int
            F16: int
            F17: int
            F18: int
            F19: int
            F20: int
        }

    let makeLargeRecord f1 f2 f3 f4 f5 f6 f7 f8 f9 f10 f11 f12 f13 f14 f15 f16 f17 f18 f19 f20 =
        {
            F1 = f1
            F2 = f2
            F3 = f3
            F4 = f4
            F5 = f5
            F6 = f6
            F7 = f7
            F8 = f8
            F9 = f9
            F10 = f10
            F11 = f11
            F12 = f12
            F13 = f13
            F14 = f14
            F15 = f15
            F16 = f16
            F17 = f17
            F18 = f18
            F19 = f19
            F20 = f20
        }

let expectFailure (expectedFragment: string) f =
    try
        f () |> ignore
        failwith "Expected failure"
    with ex ->
        test <@ ex.Message.Contains(expectedFragment) @>
