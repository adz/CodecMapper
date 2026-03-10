module PropertyTests

open System
open FsCheck
open FsCheck.Xunit
open CodecMapper
open TestCommon

let private addressSchema =
    Schema.define<Address>
    |> Schema.construct makeAddress
    |> Schema.field "street" _.Street
    |> Schema.field "city" _.City
    |> Schema.build

let private personSchema =
    Schema.define<Person>
    |> Schema.construct makePerson
    |> Schema.field "id" _.Id
    |> Schema.field "name" _.Name
    |> Schema.fieldWith "home" _.Home addressSchema
    |> Schema.build

let private optionalRecordSchema =
    Schema.define<OptionalRecord>
    |> Schema.construct makeOptionalRecord
    |> Schema.field "nickname" _.Nickname
    |> Schema.field "age" _.Age
    |> Schema.build

let private collectionSchema =
    Schema.define<CollectionRecord>
    |> Schema.construct makeCollectionRecord
    |> Schema.field "list" _.List
    |> Schema.field "array" _.Array
    |> Schema.build

type private PropertyArbitraries =
    static member private SafeText() : Arbitrary<string> =
        let safeChar: Gen<char> =
            FsCheck.FSharp.Gen.elements ([ 'a' .. 'z' ] @ [ 'A' .. 'Z' ] @ [ '0' .. '9' ] @ [ ' '; '-'; '_'; '.' ])

        let generator: Gen<string> =
            FsCheck.FSharp.GenBuilder.gen {
                let! length = FsCheck.FSharp.Gen.choose (0, 24)
                let! chars = FsCheck.FSharp.Gen.arrayOfLength length safeChar
                return String(chars: char array)
            }

        FsCheck.FSharp.Arb.fromGen generator

    static member Address() : Arbitrary<Address> =
        let safeText: Gen<string> = PropertyArbitraries.SafeText().Generator

        let generator: Gen<Address> =
            FsCheck.FSharp.GenBuilder.gen {
                let! street = safeText
                let! city = safeText
                return { Street = street; City = city }
            }

        FsCheck.FSharp.Arb.fromGen generator

    static member Person() : Arbitrary<Person> =
        let safeText: Gen<string> = PropertyArbitraries.SafeText().Generator
        let addressGen: Gen<Address> = PropertyArbitraries.Address().Generator

        let generator: Gen<Person> =
            FsCheck.FSharp.GenBuilder.gen {
                let! id = FsCheck.FSharp.Gen.choose (-5000, 5000)
                let! name = safeText
                let! home = addressGen
                return { Id = id; Name = name; Home = home }
            }

        FsCheck.FSharp.Arb.fromGen generator

    static member OptionalRecord() : Arbitrary<OptionalRecord> =
        let safeText: Gen<string> = PropertyArbitraries.SafeText().Generator

        let safeOption (generator: Gen<'T>) : Gen<'T option> =
            FsCheck.FSharp.Gen.frequency (
                [
                    1, FsCheck.FSharp.Gen.constant None
                    3, FsCheck.FSharp.Gen.map Some generator
                ]
            )

        let generator: Gen<OptionalRecord> =
            FsCheck.FSharp.GenBuilder.gen {
                let! nickname = safeOption safeText
                let! age = safeOption (FsCheck.FSharp.Gen.choose (-5000, 5000))
                return { Nickname = nickname; Age = age }
            }

        FsCheck.FSharp.Arb.fromGen generator

    static member CollectionRecord() : Arbitrary<CollectionRecord> =
        let safeText: Gen<string> = PropertyArbitraries.SafeText().Generator

        let generator: Gen<CollectionRecord> =
            FsCheck.FSharp.GenBuilder.gen {
                let! values = FsCheck.FSharp.Gen.listOf (FsCheck.FSharp.Gen.choose (-100, 100))
                let! aliases = FsCheck.FSharp.Gen.map List.toArray (FsCheck.FSharp.Gen.listOf safeText)
                return { List = values; Array = aliases }
            }

        FsCheck.FSharp.Arb.fromGen generator

let private jsonPersonCodec = Json.compile personSchema
let private xmlPersonCodec = Xml.compile personSchema
let private jsonOptionalCodec = Json.compile optionalRecordSchema
let private xmlOptionalCodec = Xml.compile optionalRecordSchema
let private jsonCollectionCodec = Json.compile collectionSchema
let private xmlCollectionCodec = Xml.compile collectionSchema

[<Property(Arbitrary = [| typeof<PropertyArbitraries> |], MaxTest = 100)>]
let ``Person round-trips through JSON`` (value: Person) =
    Json.deserialize jsonPersonCodec (Json.serialize jsonPersonCodec value) = value

[<Property(Arbitrary = [| typeof<PropertyArbitraries> |], MaxTest = 100)>]
let ``Person round-trips through XML`` (value: Person) =
    Xml.deserialize xmlPersonCodec (Xml.serialize xmlPersonCodec value) = value

[<Property(Arbitrary = [| typeof<PropertyArbitraries> |], MaxTest = 100)>]
let ``Optional records round-trip through JSON`` (value: OptionalRecord) =
    Json.deserialize jsonOptionalCodec (Json.serialize jsonOptionalCodec value) = value

[<Property(Arbitrary = [| typeof<PropertyArbitraries> |], MaxTest = 100)>]
let ``Optional records round-trip through XML`` (value: OptionalRecord) =
    Xml.deserialize xmlOptionalCodec (Xml.serialize xmlOptionalCodec value) = value

[<Property(Arbitrary = [| typeof<PropertyArbitraries> |], MaxTest = 100)>]
let ``Collection records round-trip through JSON`` (value: CollectionRecord) =
    Json.deserialize jsonCollectionCodec (Json.serialize jsonCollectionCodec value) = value

[<Property(Arbitrary = [| typeof<PropertyArbitraries> |], MaxTest = 100)>]
let ``Collection records round-trip through XML`` (value: CollectionRecord) =
    Xml.deserialize xmlCollectionCodec (Xml.serialize xmlCollectionCodec value) = value
