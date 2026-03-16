#nowarn "40"

module JsonSchemaTests

open Xunit
open Swensen.Unquote
open CodecMapper
open TestCommon

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

let optionalRecordSchema =
    Schema.define<OptionalRecord>
    |> Schema.construct makeOptionalRecord
    |> Schema.field "nickname" _.Nickname
    |> Schema.field "age" _.Age
    |> Schema.build

let optionalNicknameSchema =
    Schema.define<OptionalRecord>
    |> Schema.construct makeOptionalRecord
    |> Schema.fieldWith "nickname" _.Nickname (Schema.option Schema.string |> Schema.missingAsNone)
    |> Schema.field "age" _.Age
    |> Schema.build

let userIdSchema = Schema.int |> Schema.tryMap UserId.create UserId.value

let accountSchema =
    Schema.define<Account>
    |> Schema.construct makeAccount
    |> Schema.fieldWith "id" _.Id userIdSchema
    |> Schema.field "name" _.Name
    |> Schema.build

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

let createdDataSchema =
    Schema.define<CreatedData>
    |> Schema.construct makeCreatedData
    |> Schema.field "id" _.Id
    |> Schema.field "name" _.Name
    |> Schema.build

[<Fact>]
let ``Authored tagged unions export deterministic discriminator schema`` () =
    let schema =
        Schema.union [
            Schema.tag "none" None Option.isNone
            Schema.tagWith
                "some"
                id
                Some
                Schema.string
        ]

    let actual = JsonSchema.generate schema

    test <@ actual.Contains("\"oneOf\":[") @>
    test <@ actual.Contains("\"case\":{\"const\":\"none\"}") @>
    test <@ actual.Contains("\"case\":{\"const\":\"some\"}") @>
    test <@ actual.Contains("\"value\":{\"type\":\"string\"}") @>

[<Fact>]
let ``Recursive delayed unions export local defs and refs`` () =
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

    let actual = JsonSchema.generate nodeSchema

    test <@ actual.Contains("\"$defs\":{\"RecursiveNode\":") @>
    test <@ actual.Contains("\"oneOf\":[") @>
    test <@ actual.Contains("\"case\":{\"const\":\"leaf\"}") @>
    test <@ actual.Contains("\"case\":{\"const\":\"branch\"}") @>
    test <@ actual.Contains("\"$ref\":\"#/$defs/RecursiveNode\"") @>

[<Fact>]
let ``Inline tagged unions export merged object contracts`` () =
    let schema =
        Schema.inlineUnion [
            Schema.tag "ping" Ping ((=) Ping)
            Schema.tagWith
                "created"
                (function
                | Created payload -> Some payload
                | _ -> None)
                Created
                createdDataSchema
        ]

    let actual = JsonSchema.generate schema

    test <@ actual.Contains("\"oneOf\":[") @>
    test <@ actual.Contains("\"case\":{\"const\":\"ping\"}") @>
    test <@ actual.Contains("\"case\":{\"const\":\"created\"}") @>
    test <@ actual.Contains("\"id\":{\"type\":\"integer\"}") @>
    test <@ actual.Contains("\"name\":{\"type\":\"string\"}") @>
    test <@ not (actual.Contains("\"value\":")) @>

[<Fact>]
let ``String enums export JSON Schema enum values`` () =
    let schema =
        Schema.stringEnum [
            "strict", Strict
            "lenient", Lenient
            "off", Off
        ]

    let actual = JsonSchema.generate schema

    test <@ actual.Contains("\"type\":\"string\"") @>
    test <@ actual.Contains("\"enum\":[\"strict\",\"lenient\",\"off\"]") @>

[<Fact>]
let ``Envelope helpers export type and data field names`` () =
    let schema =
        Schema.envelope [
            Schema.message "ping" Ping ((=) Ping)
            Schema.messageWith
                "created"
                (function
                | Created payload -> Some payload
                | _ -> None)
                Created
                createdDataSchema
        ]

    let actual = JsonSchema.generate schema

    test <@ actual.Contains("\"type\":{\"const\":\"ping\"}") @>
    test <@ actual.Contains("\"type\":{\"const\":\"created\"}") @>
    test <@ actual.Contains("\"data\":{\"type\":\"object\"") @>

[<Fact>]
let ``Inline envelope helpers export merged payload properties under type discriminator`` () =
    let schema =
        Schema.inlineEnvelope [
            Schema.message "ping" Ping ((=) Ping)
            Schema.messageWith
                "created"
                (function
                | Created payload -> Some payload
                | _ -> None)
                Created
                createdDataSchema
        ]

    let actual = JsonSchema.generate schema

    test <@ actual.Contains("\"type\":{\"const\":\"created\"}") @>
    test <@ actual.Contains("\"id\":{\"type\":\"integer\"}") @>
    test <@ actual.Contains("\"name\":{\"type\":\"string\"}") @>
    test <@ not (actual.Contains("\"data\":")) @>

[<Fact>]
let ``Nested record schema exports deterministic object contract`` () =
    let actual = JsonSchema.generate personSchema

    test
        <@
            actual = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"title\":\"Person\",\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"integer\"},\"name\":{\"type\":\"string\"},\"home\":{\"type\":\"object\",\"title\":\"Address\",\"properties\":{\"street\":{\"type\":\"string\"},\"city\":{\"type\":\"string\"}},\"required\":[\"street\",\"city\"]}},\"required\":[\"id\",\"name\",\"home\"]}"
        @>

[<Fact>]
let ``Option fields stay required unless missing is explicitly allowed`` () =
    let actual = JsonSchema.generate optionalRecordSchema

    test <@ actual.Contains("\"required\":[\"nickname\",\"age\"]") @>
    test <@ actual.Contains("\"nickname\":{\"anyOf\":[{\"type\":\"string\"},{\"type\":\"null\"}]}") @>

[<Fact>]
let ``Missing-as-none removes property from required list`` () =
    let actual = JsonSchema.generate optionalNicknameSchema

    test <@ actual.Contains("\"required\":[\"age\"]") @>

[<Fact>]
let ``Mapped smart constructors export underlying wire shape`` () =
    let actual = JsonSchema.generate accountSchema

    test <@ actual.Contains("\"id\":{\"type\":\"integer\"}") @>

[<Fact>]
let ``Primitive root schemas export as valid top-level documents`` () =
    let actual = JsonSchema.generate Schema.int

    test
        <@
            actual = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"title\":\"Int32\",\"type\":\"integer\"}"
        @>

[<Fact>]
let ``Array root schemas export their items at the top level`` () =
    let actual = JsonSchema.generate (Schema.array Schema.string)

    test
        <@
            actual = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"title\":\"String[]\",\"type\":\"array\",\"items\":{\"type\":\"string\"}}"
        @>

[<Fact>]
let ``Top-level options export explicit null semantics`` () =
    let actual = JsonSchema.generate (Schema.option Schema.int)

    test
        <@
            actual = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"title\":\"FSharpOption`1\",\"anyOf\":[{\"type\":\"integer\"},{\"type\":\"null\"}]}"
        @>

[<Fact>]
let ``Common built-in schemas project to matching JSON Schema primitives`` () =
    let actual = JsonSchema.generate commonTypeSchema

    test <@ actual.Contains("\"age\":{\"type\":\"integer\"}") @>
    test <@ actual.Contains("\"level\":{\"type\":\"integer\"}") @>
    test <@ actual.Contains("\"delta\":{\"type\":\"integer\"}") @>
    test <@ actual.Contains("\"score\":{\"type\":\"integer\"}") @>
    test <@ actual.Contains("\"initial\":{\"type\":\"string\"}") @>
    test <@ actual.Contains("\"userId\":{\"type\":\"string\"}") @>
    test <@ actual.Contains("\"createdAt\":{\"type\":\"string\"}") @>
    test <@ actual.Contains("\"updatedAt\":{\"type\":\"string\"}") @>
    test <@ actual.Contains("\"duration\":{\"type\":\"string\"}") @>
