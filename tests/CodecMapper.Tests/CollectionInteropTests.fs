module CollectionInteropTests

open System.Collections.Generic
open Xunit
open Swensen.Unquote
open CodecMapper
open TestCommon

let interopCollectionSchema =
    Schema.define<InteropCollectionRecord>
    |> Schema.construct makeInteropCollectionRecord
    |> Schema.fieldWith "buffer" _.Buffer (Schema.resizeArray Schema.string)
    |> Schema.field "names" _.Names
    |> Schema.field "scores" _.Scores
    |> Schema.build

[<Fact>]
let ``Auto-resolved .NET interop collections round-trip JSON`` () =
    let codec = Json.compile interopCollectionSchema

    let value = {
        Buffer = ResizeArray([ "a"; "b" ])
        Names = ResizeArray([ "Ada"; "Lin" ]) :> IReadOnlyList<string>
        Scores = ResizeArray([ 1; 2; 3 ]) :> ICollection<int>
    }

    let json = Json.serialize codec value
    let decoded = Json.deserialize codec json

    test <@ json = """{"buffer":["a","b"],"names":["Ada","Lin"],"scores":[1,2,3]}""" @>
    test <@ Seq.toList decoded.Buffer = [ "a"; "b" ] @>
    test <@ Seq.toList decoded.Names = [ "Ada"; "Lin" ] @>
    test <@ Seq.toList decoded.Scores = [ 1; 2; 3 ] @>

[<Fact>]
let ``Auto-resolved .NET interop collections round-trip XML`` () =
    let codec = Xml.compile interopCollectionSchema

    let expectedXml =
        "<interopcollectionrecord><buffer><item>x</item><item>y</item></buffer><names><item>Quinn</item><item>Rory</item></names><scores><item>5</item><item>8</item></scores></interopcollectionrecord>"

    let value = {
        Buffer = ResizeArray([ "x"; "y" ])
        Names = ResizeArray([ "Quinn"; "Rory" ]) :> IReadOnlyList<string>
        Scores = ResizeArray([ 5; 8 ]) :> ICollection<int>
    }

    let xml = Xml.serialize codec value
    let decoded = Xml.deserialize codec xml

    test <@ xml = expectedXml @>

    test <@ Seq.toList decoded.Buffer = [ "x"; "y" ] @>
    test <@ Seq.toList decoded.Names = [ "Quinn"; "Rory" ] @>
    test <@ Seq.toList decoded.Scores = [ 5; 8 ] @>
