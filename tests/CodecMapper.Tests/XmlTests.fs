module XmlTests

open Xunit
open Swensen.Unquote
open CodecMapper
open TestCommon

let addressSchema =
    Schema.record makeAddress
    |> Schema.field "street" (fun (address: Address) -> address.Street)
    |> Schema.field "city" (fun (address: Address) -> address.City)
    |> Schema.build

let personSchema =
    Schema.record makePerson
    |> Schema.field "id" (fun (person: Person) -> person.Id)
    |> Schema.field "name" (fun (person: Person) -> person.Name)
    |> Schema.fieldWith "home" (fun (person: Person) -> person.Home) addressSchema
    |> Schema.build

[<Fact>]
let ``Round-trip nested records XML`` () =
    let codec = Xml.compileSchema personSchema

    let value = {
        Id = 42
        Name = "Adam"
        Home = {
            Street = "123 F# Lane"
            City = "AOT City"
        }
    }

    let xml = Xml.serialize codec value
    let decoded = Xml.deserialize codec xml

    test <@ decoded = value @>

[<Fact>]
let ``Decode XML with inter-element whitespace`` () =
    let codec = Xml.compileSchema personSchema

    let xml =
        """
        <person>
          <id>42</id>
          <name>Adam</name>
          <home>
            <street>123 F# Lane</street>
            <city>AOT City</city>
          </home>
        </person>
        """

    let decoded = Xml.deserialize codec xml

    test
        <@
            decoded = {
                          Id = 42
                          Name = "Adam"
                          Home = {
                              Street = "123 F# Lane"
                              City = "AOT City"
                          }
                      }
        @>

[<Fact>]
let ``Round-trip escaped string content XML`` () =
    let codec = Xml.compileSchema Schema.string
    let value = """A & B <tag> "quoted" 'single'"""
    let xml = Xml.serialize codec value

    test <@ xml = """<string>A &amp; B &lt;tag&gt; &quot;quoted&quot; &apos;single&apos;</string>""" @>
    test <@ Xml.deserialize codec xml = value @>

[<Fact>]
let ``Round-trip collections and mapped wrappers XML`` () =
    let personIdSchema = Schema.int |> Schema.map PersonId (fun (PersonId id) -> id)

    let wrappedPersonSchema =
        Schema.record makeWrappedPerson
        |> Schema.fieldWith "id" (fun (person: WrappedPerson) -> person.Id) personIdSchema
        |> Schema.fieldWith "tags" (fun (person: WrappedPerson) -> person.Tags) (Schema.list Schema.string)
        |> Schema.build

    let codec = Xml.compileSchema wrappedPersonSchema

    let value = {
        Id = PersonId 123
        Tags = [ "fsharp"; "xml" ]
    }

    let xml = Xml.serialize codec value
    let decoded = Xml.deserialize codec xml

    test <@ decoded = value @>
    test <@ xml = "<wrappedperson><id>123</id><tags><item>fsharp</item><item>xml</item></tags></wrappedperson>" @>

[<Fact>]
let ``Round-trip bool and arrays XML`` () =
    let codec =
        Xml.compileSchema (
            Schema.record makeBoolArrayRecord
            |> Schema.field "enabled" _.Enabled
            |> Schema.field "aliases" _.Aliases
            |> Schema.build
        )

    let value = {
        Enabled = true
        Aliases = [| "one"; "two" |]
    }

    let xml = Xml.serialize codec value
    let decoded = Xml.deserialize codec xml

    test <@ decoded = value @>

[<Fact>]
let ``Reject mismatched XML tags deterministically`` () =
    let codec = Xml.compileSchema personSchema

    expectFailure "XML decode error at $/name: Expected <name>" (fun () ->
        Xml.deserialize
            codec
            "<person><id>42</id><title>Adam</title><home><street>123 F# Lane</street><city>AOT City</city></home></person>")

[<Fact>]
let ``Report nested XML element path for decode failures`` () =
    let codec = Xml.compileSchema personSchema

    expectFailure "XML decode error at $/home/street: Expected <street>" (fun () ->
        Xml.deserialize
            codec
            "<person><id>1</id><name>Ada</name><home><line1>Main</line1><city>Adelaide</city></home></person>")

[<Fact>]
let ``Reject trailing XML content after the root value`` () =
    let codec = Xml.compileSchema Schema.int

    expectFailure "XML decode error at $: Trailing content after top-level XML value" (fun () ->
        Xml.deserialize codec "<int>1</int><int>2</int>")
