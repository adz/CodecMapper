module FieldPolicyTests

open Swensen.Unquote
open Xunit
open CodecMapper

type ConfigRecord = {
    Mode: string
    Retries: int
    Labels: string list
}

let makeConfigRecord mode retries labels = {
    Mode = mode
    Retries = retries
    Labels = labels
}

let configSchema =
    Schema.define<ConfigRecord>
    |> Schema.construct makeConfigRecord
    |> Schema.fieldWith "mode" _.Mode (Schema.string |> Schema.missingAsValue "strict")
    |> Schema.fieldWith "retries" _.Retries (Schema.int |> Schema.missingAsValue 3)
    |> Schema.fieldWith "labels" _.Labels (Schema.list Schema.string |> Schema.missingAsValue [])
    |> Schema.build

type FlatConfigRecord = { Mode: string; Retries: int }

let makeFlatConfigRecord mode retries = { Mode = mode; Retries = retries }

let flatConfigSchema =
    Schema.define<FlatConfigRecord>
    |> Schema.construct makeFlatConfigRecord
    |> Schema.fieldWith "mode" _.Mode (Schema.string |> Schema.missingAsValue "strict")
    |> Schema.fieldWith "retries" _.Retries (Schema.int |> Schema.missingAsValue 3)
    |> Schema.build

type NullConfigRecord = { Mode: string; Region: string }

let makeNullConfigRecord mode region = { Mode = mode; Region = region }

let nullConfigSchema =
    Schema.define<NullConfigRecord>
    |> Schema.construct makeNullConfigRecord
    |> Schema.field "mode" _.Mode
    |> Schema.fieldWith "region" _.Region (Schema.string |> Schema.nullAsValue "global")
    |> Schema.build

type CollectionPolicyRecord = { Labels: string list }

let makeCollectionPolicyRecord labels = { Labels = labels }

let collectionPolicySchema =
    Schema.define<CollectionPolicyRecord>
    |> Schema.construct makeCollectionPolicyRecord
    |> Schema.fieldWith "labels" _.Labels (Schema.list Schema.string |> Schema.emptyCollectionAsValue [ "general" ])
    |> Schema.build

[<Fact>]
let ``Missing defaults apply in JSON without weakening explicit values`` () =
    let codec = Json.compile configSchema

    let value = Json.deserialize codec """{"mode":"lenient"}"""

    let expected = {
        Mode = "lenient"
        Retries = 3
        Labels = []
    }

    test <@ value = expected @>

[<Fact>]
let ``Missing defaults apply in XML`` () =
    let codec = Xml.compile configSchema

    let value = Xml.deserialize codec "<configrecord><mode>strict</mode></configrecord>"

    let expected = {
        Mode = "strict"
        Retries = 3
        Labels = []
    }

    test <@ value = expected @>

[<Fact>]
let ``Missing defaults apply in KeyValue for flat shapes`` () =
    let keyValueCodec = KeyValue.compile flatConfigSchema

    let fromKeyValue =
        KeyValue.deserialize keyValueCodec (Map.ofList [ "mode", "strict" ])

    test <@ fromKeyValue.Mode = "strict" @>
    test <@ fromKeyValue.Retries = 3 @>

[<Fact>]
let ``Missing defaults apply in Yaml for collection-bearing config shapes`` () =
    let yamlCodec = Yaml.compile configSchema

    let fromYaml = Yaml.deserialize yamlCodec "mode: strict"

    test <@ fromYaml.Mode = "strict" @>
    test <@ fromYaml.Retries = 3 @>
    test <@ fromYaml.Labels = [] @>

[<Fact>]
let ``Missing defaults do not change explicit encoding`` () =
    let jsonCodec = Json.compile configSchema
    let xmlCodec = Xml.compile configSchema

    let value = {
        Mode = "strict"
        Retries = 3
        Labels = []
    }

    test <@ Json.serialize jsonCodec value = """{"mode":"strict","retries":3,"labels":[]}""" @>

    test
        <@
            Xml.serialize xmlCodec value = "<configrecord><mode>strict</mode><retries>3</retries><labels></labels></configrecord>"
        @>

[<Fact>]
let ``Missing default fields are omitted from JSON Schema required`` () =
    let schema = JsonSchema.generate configSchema

    test <@ schema.Contains("\"required\":[\"mode\"]") = false @>
    test <@ schema.Contains("\"required\":[]") = false @>
    test <@ schema.Contains("\"retries\"") @>
    test <@ schema.Contains("\"labels\"") @>

[<Fact>]
let ``Null defaults apply in JSON and YAML without weakening explicit values`` () =
    let jsonCodec = Json.compile nullConfigSchema
    let yamlCodec = Yaml.compile nullConfigSchema

    let fromJson = Json.deserialize jsonCodec """{"mode":"strict","region":null}"""
    let fromYaml = Yaml.deserialize yamlCodec "mode: strict\nregion: null"
    let explicit = Json.deserialize jsonCodec """{"mode":"strict","region":"apac"}"""

    test <@ fromJson = { Mode = "strict"; Region = "global" } @>
    test <@ fromYaml = { Mode = "strict"; Region = "global" } @>
    test <@ explicit = { Mode = "strict"; Region = "apac" } @>

[<Fact>]
let ``Null defaults apply in XML through empty elements`` () =
    let codec = Xml.compile nullConfigSchema

    let value =
        Xml.deserialize codec "<nullconfigrecord><mode>strict</mode><region></region></nullconfigrecord>"

    test <@ value = { Mode = "strict"; Region = "global" } @>

[<Fact>]
let ``Null defaults stay required in JSON Schema`` () =
    let schema = JsonSchema.generate nullConfigSchema

    test <@ schema.Contains("\"required\":[\"mode\",\"region\"]") @>

[<Fact>]
let ``Empty collection defaults apply in JSON XML and YAML without weakening non-empty values`` () =
    let jsonCodec = Json.compile collectionPolicySchema
    let xmlCodec = Xml.compile collectionPolicySchema
    let yamlCodec = Yaml.compile collectionPolicySchema

    let fromJson = Json.deserialize jsonCodec """{"labels":[]}"""

    let fromXml =
        Xml.deserialize xmlCodec "<collectionpolicyrecord><labels></labels></collectionpolicyrecord>"

    let fromYaml = Yaml.deserialize yamlCodec "labels: []"
    let explicit = Json.deserialize jsonCodec """{"labels":["priority"]}"""

    test <@ fromJson = { Labels = [ "general" ] } @>
    test <@ fromXml = { Labels = [ "general" ] } @>
    test <@ fromYaml = { Labels = [ "general" ] } @>
    test <@ explicit = { Labels = [ "priority" ] } @>

[<Fact>]
let ``Empty collection defaults stay required in JSON Schema`` () =
    let schema = JsonSchema.generate collectionPolicySchema

    test <@ schema.Contains("\"required\":[\"labels\"]") @>
