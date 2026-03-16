#nowarn "40"

module SchemaDslTests

open Xunit
open Swensen.Unquote
open CodecMapper
open CodecMapper.Schema
open TestCommon

[<Fact>]
let ``Round-trip using Pipeline DSL`` () =
    let addressSchema =
        Schema.define<Address>
        |> Schema.construct makeAddress
        |> Schema.field "street" _.Street
        |> Schema.field "city" _.City
        |> Schema.build

    let personSchema =
        Schema.define<Person>
        |> Schema.construct makePerson
        |> Schema.field "id" _.Id
        |> Schema.field "name" _.Name
        |> Schema.fieldWith "home" _.Home addressSchema
        |> Schema.build

    let codec = Json.compile personSchema

    let person = {
        Id = 42
        Name = "Adam"
        Home = {
            Street = "123 F# Lane"
            City = "Pipeline City"
        }
    }

    let json = Json.serialize codec person
    let decoded = Json.deserialize codec json
    test <@ decoded = person @>

[<Fact>]
let ``One schema, multiple formats (JSON and XML)`` () =
    let addressSchema =
        Schema.define<Address>
        |> Schema.construct makeAddress
        |> Schema.field "street" _.Street
        |> Schema.field "city" _.City
        |> Schema.build

    let personSchema =
        Schema.define<Person>
        |> Schema.construct makePerson
        |> Schema.field "id" _.Id
        |> Schema.field "name" _.Name
        |> Schema.fieldWith "home" _.Home addressSchema
        |> Schema.build

    let person = {
        Id = 42
        Name = "Adam"
        Home = {
            Street = "123 F# Lane"
            City = "AOT City"
        }
    }

    let jsonCodec = Json.compile personSchema
    let json = Json.serialize jsonCodec person
    test <@ json = "{\"id\":42,\"name\":\"Adam\",\"home\":{\"street\":\"123 F# Lane\",\"city\":\"AOT City\"}}" @>

    let xmlCodec = Xml.compile personSchema
    let xml = Xml.serialize xmlCodec person

    test
        <@
            xml = "<person><id>42</id><name>Adam</name><home><street>123 F# Lane</street><city>AOT City</city></home></person>"
        @>

[<Fact>]
let ``Round-trip list of strings JSON`` () =
    let listSchema = Schema.list Schema.string
    let codec = Json.compile listSchema

    let value = [ "a"; "b"; "c" ]
    let json = Json.serialize codec value
    test <@ json = "[\"a\",\"b\",\"c\"]" @>

    let decoded = Json.deserialize codec json
    test <@ decoded = value @>

[<Fact>]
let ``buildAndCompile composes build and compile across formats`` () =
    let person = {
        Id = 7
        Name = "Alias"
        Home = {
            Street = "Codec Street"
            City = "Adelaide"
        }
    }

    let jsonCodec =
        Schema.define<Person>
        |> Schema.construct makePerson
        |> Schema.field "id" _.Id
        |> Schema.field "name" _.Name
        |> Schema.fieldWith
            "home"
            _.Home
            (Schema.define<Address>
             |> Schema.construct makeAddress
             |> Schema.field "street" _.Street
             |> Schema.field "city" _.City
             |> Schema.build)
        |> Json.buildAndCompile

    let xmlCodec =
        Schema.define<Person>
        |> Schema.construct makePerson
        |> Schema.field "id" _.Id
        |> Schema.field "name" _.Name
        |> Schema.fieldWith
            "home"
            _.Home
            (Schema.define<Address>
             |> Schema.construct makeAddress
             |> Schema.field "street" _.Street
             |> Schema.field "city" _.City
             |> Schema.build)
        |> Xml.buildAndCompile

    let yamlCodec =
        Schema.define<Person>
        |> Schema.construct makePerson
        |> Schema.field "id" _.Id
        |> Schema.field "name" _.Name
        |> Schema.fieldWith
            "home"
            _.Home
            (Schema.define<Address>
             |> Schema.construct makeAddress
             |> Schema.field "street" _.Street
             |> Schema.field "city" _.City
             |> Schema.build)
        |> Yaml.buildAndCompile

    let keyValueCodec =
        Schema.define<Person>
        |> Schema.construct makePerson
        |> Schema.field "id" _.Id
        |> Schema.field "name" _.Name
        |> Schema.fieldWith
            "home"
            _.Home
            (Schema.define<Address>
             |> Schema.construct makeAddress
             |> Schema.field "street" _.Street
             |> Schema.field "city" _.City
             |> Schema.build)
        |> KeyValue.buildAndCompile

    let json = Json.serialize jsonCodec person
    let xml = Xml.serialize xmlCodec person
    let yaml = Yaml.serialize yamlCodec person
    let keyValue = KeyValue.serialize keyValueCodec person

    test <@ Json.deserialize jsonCodec json = person @>
    test <@ Xml.deserialize xmlCodec xml = person @>
    test <@ Yaml.deserialize yamlCodec yaml = person @>
    test <@ KeyValue.deserialize keyValueCodec keyValue = person @>

[<Fact>]
let ``Round-trip mapped type (PersonId) JSON`` () =
    let personIdSchema = Schema.int |> Schema.map PersonId (fun (PersonId id) -> id)

    let wrappedPersonSchema =
        Schema.define<WrappedPerson>
        |> Schema.construct makeWrappedPerson
        |> Schema.fieldWith "id" _.Id personIdSchema
        |> Schema.fieldWith "tags" _.Tags (Schema.list Schema.string)
        |> Schema.build

    let codec = Json.compile wrappedPersonSchema

    let p = {
        Id = PersonId 123
        Tags = [ "fsharp"; "aot" ]
    }

    let json = Json.serialize codec p
    let decoded = Json.deserialize codec json
    test <@ decoded = p @>

[<Fact>]
let ``Round-trip collections with auto-resolution`` () =
    let collectionSchema =
        Schema.define<CollectionRecord>
        |> Schema.construct makeCollectionRecord
        |> Schema.field "list" _.List
        |> Schema.field "array" _.Array
        |> Schema.build

    let codec = Json.compile collectionSchema

    let value = {
        List = [ 1; 2 ]
        Array = [| "a"; "b" |]
    }

    let json = Json.serialize codec value
    let decoded = Json.deserialize codec json
    test <@ decoded = value @>

[<Fact>]
let ``Round-trip using typed pipeline with 20 fields`` () =
    let largeSchema =
        Schema.define<LargeRecord>
        |> Schema.construct makeLargeRecord
        |> Schema.field "f1" _.F1
        |> Schema.field "f2" _.F2
        |> Schema.field "f3" _.F3
        |> Schema.field "f4" _.F4
        |> Schema.field "f5" _.F5
        |> Schema.field "f6" _.F6
        |> Schema.field "f7" _.F7
        |> Schema.field "f8" _.F8
        |> Schema.field "f9" _.F9
        |> Schema.field "f10" _.F10
        |> Schema.field "f11" _.F11
        |> Schema.field "f12" _.F12
        |> Schema.field "f13" _.F13
        |> Schema.field "f14" _.F14
        |> Schema.field "f15" _.F15
        |> Schema.field "f16" _.F16
        |> Schema.field "f17" _.F17
        |> Schema.field "f18" _.F18
        |> Schema.field "f19" _.F19
        |> Schema.field "f20" _.F20
        |> Schema.build

    let codec = Json.compile largeSchema

    let value = {
        F1 = 1
        F2 = 2
        F3 = 3
        F4 = 4
        F5 = 5
        F6 = 6
        F7 = 7
        F8 = 8
        F9 = 9
        F10 = 10
        F11 = 11
        F12 = 12
        F13 = 13
        F14 = 14
        F15 = 15
        F16 = 16
        F17 = 17
        F18 = 18
        F19 = 19
        F20 = 20
    }

    let json = Json.serialize codec value
    let decoded = Json.deserialize codec json
    test <@ decoded = value @>

[<Fact>]
let ``Recursive tagged union round-trips JSON XML and YAML`` () =
    let rec nodeSchema : Schema<RecursiveNode> =
        Schema.delay (fun () ->
            Schema.union [
                Schema.tagWith
                    "leaf"
                    (function
                    | Leaf value -> Some value
                    | _ -> None)
                    Leaf
                    Schema.string
                Schema.tagWith
                    "branch"
                    (function
                    | Branch value -> Some value
                    | _ -> None)
                    Branch
                    nodeSchema
            ])

    let value = Branch(Branch(Leaf "ok"))
    let jsonCodec = Json.compile nodeSchema
    let xmlCodec = Xml.compile nodeSchema
    let yamlCodec = Yaml.compile nodeSchema

    let json = Json.serialize jsonCodec value
    let xml = Xml.serialize xmlCodec value
    let yaml = Yaml.serialize yamlCodec value

    test <@ json = """{"case":"branch","value":{"case":"branch","value":{"case":"leaf","value":"ok"}}}""" @>

    test <@ xml = "<recursivenode><case>branch</case><value><case>branch</case><value><case>leaf</case><value>ok</value></value></value></recursivenode>" @>

    test <@ Json.deserialize jsonCodec json = value @>
    test <@ Xml.deserialize xmlCodec xml = value @>
    test <@ Yaml.deserialize yamlCodec yaml = value @>

[<Fact>]
let ``Inline tagged unions round-trip across JSON XML YAML and KeyValue`` () =
    let payloadSchema =
        Schema.define<CreatedData>
        |> Schema.construct makeCreatedData
        |> Schema.field "id" _.Id
        |> Schema.field "name" _.Name
        |> Schema.build

    let schema =
        Schema.inlineUnion [
            Schema.tag "ping" Ping ((=) Ping)
            Schema.tagWith
                "created"
                (function
                | Created value -> Some value
                | _ -> None)
                Created
                payloadSchema
        ]

    let value = Created { Id = 7; Name = "Ada" }

    let jsonCodec = Json.compile schema
    let xmlCodec = Xml.compile schema
    let yamlCodec = Yaml.compile schema
    let keyValueCodec = KeyValue.compile schema

    let json = Json.serialize jsonCodec value
    let xml = Xml.serialize xmlCodec value
    let yaml = Yaml.serialize yamlCodec value
    let keyValue = KeyValue.serialize keyValueCodec value

    test <@ json = """{"case":"created","id":7,"name":"Ada"}""" @>
    test <@ xml = "<event><case>created</case><id>7</id><name>Ada</name></event>" @>
    test <@ yaml.Contains("case: created") @>
    test <@ yaml.Contains("id: 7") @>
    test <@ yaml.Contains("name: Ada") @>
    test <@ keyValue = Map.ofList [ "case", "created"; "id", "7"; "name", "Ada" ] @>

    test <@ Json.deserialize jsonCodec json = value @>
    test <@ Xml.deserialize xmlCodec xml = value @>
    test <@ Yaml.deserialize yamlCodec yaml = value @>
    test <@ KeyValue.deserialize keyValueCodec keyValue = value @>

[<Fact>]
let ``String enums round-trip across JSON XML YAML and KeyValue`` () =
    let modeSchema =
        Schema.stringEnum [
            "strict", Strict
            "lenient", Lenient
            "off", Off
        ]

    let configSchema =
        Schema.define<ModeConfig>
        |> Schema.construct makeModeConfig
        |> Schema.fieldWith "mode" _.Mode modeSchema
        |> Schema.build

    let value = Lenient
    let jsonCodec = Json.compile modeSchema
    let xmlCodec = Xml.compile modeSchema
    let yamlCodec = Yaml.compile modeSchema
    let keyValueCodec = KeyValue.compile configSchema

    let json = Json.serialize jsonCodec value
    let xml = Xml.serialize xmlCodec value
    let yaml = Yaml.serialize yamlCodec value
    let keyValue = KeyValue.serialize keyValueCodec { Mode = value }

    test <@ json = "\"lenient\"" @>
    test <@ xml = "<mode>lenient</mode>" @>
    test <@ yaml = "lenient" @>
    test <@ keyValue = Map.ofList [ "mode", "lenient" ] @>

    test <@ Json.deserialize jsonCodec json = value @>
    test <@ Xml.deserialize xmlCodec xml = value @>
    test <@ Yaml.deserialize yamlCodec yaml = value @>
    test <@ KeyValue.deserialize keyValueCodec keyValue = { Mode = value } @>

[<Fact>]
let ``Envelope helpers use type and data field names across formats`` () =
    let payloadSchema =
        Schema.define<CreatedData>
        |> Schema.construct makeCreatedData
        |> Schema.field "id" _.Id
        |> Schema.field "name" _.Name
        |> Schema.build

    let schema =
        Schema.envelope [
            Schema.message "ping" Ping ((=) Ping)
            Schema.messageWith
                "created"
                (function
                | Created payload -> Some payload
                | _ -> None)
                Created
                payloadSchema
        ]

    let value = Created { Id = 7; Name = "Ada" }
    let jsonCodec = Json.compile schema
    let xmlCodec = Xml.compile schema
    let yamlCodec = Yaml.compile schema
    let keyValueCodec = KeyValue.compile schema

    let json = Json.serialize jsonCodec value
    let xml = Xml.serialize xmlCodec value
    let yaml = Yaml.serialize yamlCodec value
    let keyValue = KeyValue.serialize keyValueCodec value

    test <@ json = """{"type":"created","data":{"id":7,"name":"Ada"}}""" @>
    test <@ xml = "<event><type>created</type><data><id>7</id><name>Ada</name></data></event>" @>
    test <@ yaml.Contains("type: created") @>
    test <@ yaml.Contains("data:") @>
    test <@ keyValue = Map.ofList [ "type", "created"; "data.id", "7"; "data.name", "Ada" ] @>

    test <@ Json.deserialize jsonCodec json = value @>
    test <@ Xml.deserialize xmlCodec xml = value @>
    test <@ Yaml.deserialize yamlCodec yaml = value @>
    test <@ KeyValue.deserialize keyValueCodec keyValue = value @>

[<Fact>]
let ``Inline envelope helpers inline payload fields next to type`` () =
    let payloadSchema =
        Schema.define<CreatedData>
        |> Schema.construct makeCreatedData
        |> Schema.field "id" _.Id
        |> Schema.field "name" _.Name
        |> Schema.build

    let schema =
        Schema.inlineEnvelope [
            Schema.message "ping" Ping ((=) Ping)
            Schema.messageWith
                "created"
                (function
                | Created payload -> Some payload
                | _ -> None)
                Created
                payloadSchema
        ]

    let value = Created { Id = 7; Name = "Ada" }
    let jsonCodec = Json.compile schema
    let xmlCodec = Xml.compile schema
    let yamlCodec = Yaml.compile schema
    let keyValueCodec = KeyValue.compile schema

    let json = Json.serialize jsonCodec value
    let xml = Xml.serialize xmlCodec value
    let yaml = Yaml.serialize yamlCodec value
    let keyValue = KeyValue.serialize keyValueCodec value

    test <@ json = """{"type":"created","id":7,"name":"Ada"}""" @>
    test <@ xml = "<event><type>created</type><id>7</id><name>Ada</name></event>" @>
    test <@ yaml.Contains("type: created") @>
    test <@ yaml.Contains("id: 7") @>
    test <@ yaml.Contains("name: Ada") @>
    test <@ keyValue = Map.ofList [ "type", "created"; "id", "7"; "name", "Ada" ] @>

    test <@ Json.deserialize jsonCodec json = value @>
    test <@ Xml.deserialize xmlCodec xml = value @>
    test <@ Yaml.deserialize yamlCodec yaml = value @>
    test <@ KeyValue.deserialize keyValueCodec keyValue = value @>

[<Fact>]
let ``Pipeline DSL can use opened Schema module at file scope`` () =
    let addressSchema =
        define<Address>
        |> construct makeAddress
        |> field "street" _.Street
        |> field "city" _.City
        |> build

    let personSchema =
        define<Person>
        |> construct makePerson
        |> field "id" _.Id
        |> field "name" _.Name
        |> fieldWith "home" _.Home addressSchema
        |> build

    let codec = Json.compile personSchema

    let person = {
        Id = 12
        Name = "Open"
        Home = { Street = "Short"; City = "DSL" }
    }

    test <@ Json.deserialize codec (Json.serialize codec person) = person @>
