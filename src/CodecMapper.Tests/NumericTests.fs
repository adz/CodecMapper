module NumericTests

open Xunit
open Swensen.Unquote
open CodecMapper
open TestCommon

let numericSchema =
    Schema.define<NumericRecord>
    |> Schema.construct makeNumericRecord
    |> Schema.field "total" _.Total
    |> Schema.field "count" _.Count
    |> Schema.field "capacity" _.Capacity
    |> Schema.field "ratio" _.Ratio
    |> Schema.field "price" _.Price
    |> Schema.build

[<Fact>]
let ``Extended numeric types round-trip JSON`` () =
    let codec = Json.compile numericSchema

    let value =
        { Total = 9_223_372_036_854_775_000L
          Count = 4_294_967_000u
          Capacity = 18_446_744_073_709_551_000UL
          Ratio = -12.5e3
          Price = 12345.6789M }

    let json = Json.serialize codec value
    let decoded = Json.deserialize codec json

    test <@ decoded = value @>

[<Fact>]
let ``Extended numeric types round-trip XML`` () =
    let codec = Xml.compile numericSchema

    let value =
        { Total = 9_223_372_036_854_775_000L
          Count = 4_294_967_000u
          Capacity = 18_446_744_073_709_551_000UL
          Ratio = -12.5e3
          Price = 12345.6789M }

    let xml = Xml.serialize codec value
    let decoded = Xml.deserialize codec xml

    test <@ decoded = value @>

[<Fact>]
let ``Extended numeric decoders reject out-of-range values`` () =
    expectFailure "int64 value out of range" (fun () ->
        Json.deserialize (Json.compile Schema.int64) "9223372036854775808")

    expectFailure "uint32 value out of range" (fun () -> Json.deserialize (Json.compile Schema.uint32) "4294967296")

    expectFailure "uint64 value out of range" (fun () ->
        Json.deserialize (Json.compile Schema.uint64) "18446744073709551616")

[<Fact>]
let ``Float and decimal support JSON fractional and exponent forms`` () =
    let floatCodec = Json.compile Schema.float
    let decimalCodec = Json.compile Schema.decimal

    test <@ Json.deserialize floatCodec "1.25e2" = 125.0 @>
    test <@ Json.deserialize decimalCodec "-12.50" = -12.50M @>
