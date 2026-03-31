namespace CodecMapper

open CodecMapper
/// Runner-specializable contract engine for authored `Codec<'T>` contracts.
///
/// This is the primary authoring surface.

type ICodecInfo =
    abstract member TargetType: System.Type

[<AbstractClass>]
type Schema<'T>() =
    interface ICodecInfo with
        member _.TargetType = typeof<'T>

type Codec<'T> = Schema<'T>

type IPrimitiveCodec =
    abstract member TargetType: System.Type

type IListCodec<'T> =
    abstract member Inner: Codec<'T>

type IArrayCodec<'T> =
    abstract member Inner: Codec<'T>

type IOptionCodec<'T> =
    abstract member Inner: Codec<'T>

type IDelayCodec<'T> =
    abstract member Factory: unit -> Codec<'T>

type IStringEnumCodec<'T> =
    abstract member Names: string array
    abstract member TryGetName: 'T -> string option
    abstract member ParseName: string -> 'T

type IRuntimeCodecShape =
    interface end

type IListCodecRuntime =
    inherit IRuntimeCodecShape
    abstract member InnerObj: obj

type IArrayCodecRuntime =
    inherit IRuntimeCodecShape
    abstract member InnerObj: obj

type IOptionCodecRuntime =
    inherit IRuntimeCodecShape
    abstract member InnerObj: obj

type IDelayCodecRuntime =
    inherit IRuntimeCodecShape
    abstract member FactoryObj: unit -> obj

type IStringEnumCodecRuntime =
    inherit IRuntimeCodecShape
    abstract member Names: string array
    abstract member TryGetNameObj: obj -> string option
    abstract member ParseNameObj: string -> obj

type IRawJsonValueCodec =
    inherit IRuntimeCodecShape

type IMissingAsNoneCodec<'T> =
    abstract member Inner: Codec<'T option>

type IMissingAsValueCodec<'T> =
    abstract member Value: 'T
    abstract member Inner: Codec<'T>

type INullAsValueCodec<'T> =
    abstract member Value: 'T
    abstract member Inner: Codec<'T>

type IEmptyCollectionAsValueCodec<'T> =
    abstract member Value: 'T
    abstract member Inner: Codec<'T>

type IEmptyStringAsNoneCodec =
    abstract member Inner: Codec<string option>

type IMappedCodec<'Inner, 'Outer> =
    abstract member Inner: Codec<'Inner>
    abstract member Decode: 'Inner -> 'Outer
    abstract member Encode: 'Outer -> 'Inner

type IMappedCodecRuntime =
    inherit IRuntimeCodecShape
    abstract member InnerObj: obj
    abstract member DecodeObj: obj -> obj
    abstract member EncodeObj: obj -> obj

type IRuntimeMissingWrapper =
    inherit IRuntimeCodecShape
    abstract member InnerObj: obj
    abstract member Kind: int
    abstract member ValueObj: obj

type ICodecSpecializer<'Target> =
    abstract member Specialize<'T> : Codec<'T> -> 'Target

type UnionCase<'Union> = {
    Name: string
    FieldType: System.Type option
    Codec: obj option
    Specialize: (obj -> obj) option
    TryGetValue: 'Union -> obj option
    Construct: obj option -> 'Union
}

type IUnionCodec<'Union> =
    abstract member DiscriminatorName: string
    abstract member ValueName: string
    abstract member Cases: UnionCase<'Union> list

type UnionCaseRuntime = {
    Name: string
    FieldType: System.Type option
    Codec: obj option
    TryGetValueObj: obj -> obj option
    ConstructObj: obj option -> obj
}

type IUnionCodecRuntime =
    inherit IRuntimeCodecShape
    abstract member DiscriminatorName: string
    abstract member ValueName: string
    abstract member CasesRuntime: UnionCaseRuntime list

type IInlineUnionCodec<'Union> =
    abstract member DiscriminatorName: string
    abstract member Cases: UnionCase<'Union> list

type IInlineUnionCodecRuntime =
    inherit IRuntimeCodecShape
    abstract member DiscriminatorName: string
    abstract member CasesRuntime: UnionCaseRuntime list

type Field<'Record> = {
    Name: string
    Codec: obj
    Get: 'Record -> obj
}

type FieldRuntime = {
    Name: string
    Codec: obj
    GetObj: obj -> obj
}

type RuntimeField = {
    Name: string
    TargetType: System.Type
    Codec: RuntimeSchema
    GetObj: obj -> obj
}

and RuntimeTaggedCase = {
    Name: string
    FieldType: System.Type option
    Codec: RuntimeSchema option
    TryGetValueObj: obj -> obj option
    ConstructObj: obj option -> obj
}

and RuntimeDefinition =
    | EPrimitive of System.Type
    | EStringEnum of names: string[] * tryGetName: (obj -> string option) * parseName: (string -> obj)
    | ERecord of System.Type * RuntimeField list * (obj[] -> obj)
    | EList of RuntimeSchema
    | EArray of RuntimeSchema
    | EOption of RuntimeSchema
    | EUnion of discriminatorName: string * valueName: string * RuntimeTaggedCase list
    | EInlineUnion of discriminatorName: string * RuntimeTaggedCase list
    | EDelay of (unit -> RuntimeSchema)
    | EMissingAsNone of RuntimeSchema
    | EMissingAsValue of obj * RuntimeSchema
    | ENullAsValue of obj * RuntimeSchema
    | EEmptyCollectionAsValue of obj * RuntimeSchema
    | EEmptyStringAsNone of RuntimeSchema
    | EMap of RuntimeSchema * (obj -> obj) * (obj -> obj)
    | ERawJsonValue

and RuntimeSchema = {
    TargetType: System.Type
    Definition: RuntimeDefinition
}

type IChainResult<'Record, 'CtorIn, 'CtorOut> =
    abstract member Value: obj

type IChainFactory<'Record> =
    abstract member OnEnd: unit -> IChainResult<'Record, 'Ctor, 'Ctor>

    abstract member OnField:
        name: string *
        getter: ('Record -> 'Field) *
        codec: Codec<'Field> *
        head: IChainResult<'Record, 'CtorIn, 'Field -> 'NextCtor> ->
            IChainResult<'Record, 'CtorIn, 'NextCtor>

    abstract member OnComplete<'Ctor> : ctor: 'Ctor * chain: IChainResult<'Record, 'Ctor, 'Record> -> Codec<'Record>

type IChainNode<'Record, 'CtorIn, 'CtorOut> =
    abstract member GetFields: int -> Field<'Record> list * int
    abstract member Apply: obj * obj array -> obj
    abstract member Build: IChainFactory<'Record> -> IChainResult<'Record, 'CtorIn, 'CtorOut>

type FieldsEnd<'Record, 'Ctor>() =
    interface IChainNode<'Record, 'Ctor, 'Ctor> with
        member _.GetFields(index) = [], index
        member _.Apply(ctor, _) = ctor
        member _.Build(factory) = factory.OnEnd()

type FieldsAppend<'Record, 'CtorIn, 'Field, 'NextCtor, 'Head
    when 'Head :> IChainNode<'Record, 'CtorIn, 'Field -> 'NextCtor>>
    (
        head: 'Head,
        name: string,
        getter: 'Record -> 'Field,
        codec: Codec<'Field>
    ) =

    interface IChainNode<'Record, 'CtorIn, 'NextCtor> with
        member _.GetFields(index) =
            let previousFields, nextIndex = head.GetFields(index)

            let field = {
                Name = name
                Codec = box codec
                Get = (fun record -> box (getter record))
            }

            previousFields @ [ field ], nextIndex + 1

        member _.Apply(ctor, values) =
            let headResult = head.Apply(ctor, values)
            let myIndex = head.GetFields(0) |> snd
            let typedCtor = headResult :?> ('Field -> 'NextCtor)
            box (typedCtor (values[myIndex] :?> 'Field))

        member _.Build(factory) =
            let headResult = head.Build(factory)
            factory.OnField(name, getter, codec, headResult)

type IMappingDefinition<'Record> =
    abstract member Fields: Field<'Record> list
    abstract member Create: obj array -> 'Record
    abstract member Specialize: IChainFactory<'Record> -> Codec<'Record>

type IMappingDefinitionRuntime =
    inherit IRuntimeCodecShape
    abstract member FieldsRuntime: FieldRuntime list
    abstract member CreateObj: obj array -> obj

type MappingDefinition<'Record, 'Ctor, 'Chain when 'Chain :> IChainNode<'Record, 'Ctor, 'Record>>
    (ctor: 'Ctor, chain: 'Chain) =
    inherit Codec<'Record>()

    member _.Ctor = ctor
    member _.Chain = chain

    interface IMappingDefinition<'Record> with
        member _.Fields = chain.GetFields(0) |> fst
        member _.Create(values) = chain.Apply(box ctor, values) :?> 'Record
        member _.Specialize(factory) =
            let result = chain.Build(factory)
            factory.OnComplete(ctor, result)

    interface IMappingDefinitionRuntime with
        member _.FieldsRuntime =
            (chain.GetFields(0) |> fst)
            |> List.map (fun field -> {
                Name = field.Name
                Codec = field.Codec
                GetObj = fun record -> field.Get (unbox record)
            })
        member _.CreateObj(values) = box (chain.Apply(box ctor, values) :?> 'Record)

type private UnionCodec<'Union>(discriminatorName: string, valueName: string, cases: UnionCase<'Union> list) =
    inherit Codec<'Union>()

    interface IUnionCodec<'Union> with
        member _.DiscriminatorName = discriminatorName
        member _.ValueName = valueName
        member _.Cases = cases

    interface IUnionCodecRuntime with
        member _.DiscriminatorName = discriminatorName
        member _.ValueName = valueName
        member _.CasesRuntime =
            cases
            |> List.map (fun case -> {
                Name = case.Name
                FieldType = case.FieldType
                Codec = case.Codec
                TryGetValueObj = fun candidate -> case.TryGetValue (unbox candidate)
                ConstructObj = fun value -> box (case.Construct value)
            })

type private InlineUnionCodec<'Union>(discriminatorName: string, cases: UnionCase<'Union> list) =
    inherit Codec<'Union>()

    interface IInlineUnionCodec<'Union> with
        member _.DiscriminatorName = discriminatorName
        member _.Cases = cases

    interface IInlineUnionCodecRuntime with
        member _.DiscriminatorName = discriminatorName
        member _.CasesRuntime =
            cases
            |> List.map (fun case -> {
                Name = case.Name
                FieldType = case.FieldType
                Codec = case.Codec
                TryGetValueObj = fun candidate -> case.TryGetValue (unbox candidate)
                ConstructObj = fun value -> box (case.Construct value)
            })

module Codec =
    type internal PrimitiveCodec<'T>() =
        inherit Codec<'T>()
        interface IPrimitiveCodec with
            member _.TargetType = typeof<'T>

    type internal ListCodec<'T>(inner: Codec<'T>) =
        inherit Codec<'T list>()
        interface IListCodec<'T> with
            member _.Inner = inner
        interface IListCodecRuntime with
            member _.InnerObj = box inner

    type internal ArrayCodec<'T>(inner: Codec<'T>) =
        inherit Codec<'T array>()
        interface IArrayCodec<'T> with
            member _.Inner = inner
        interface IArrayCodecRuntime with
            member _.InnerObj = box inner

    type internal OptionCodec<'T>(inner: Codec<'T>) =
        inherit Codec<'T option>()
        interface IOptionCodec<'T> with
            member _.Inner = inner
        interface IOptionCodecRuntime with
            member _.InnerObj = box inner

    type internal MappedCodec<'Inner, 'Outer>(inner: Codec<'Inner>, decode: 'Inner -> 'Outer, encode: 'Outer -> 'Inner) =
        inherit Codec<'Outer>()

        interface IMappedCodec<'Inner, 'Outer> with
            member _.Inner = inner
            member _.Decode value = decode value
            member _.Encode value = encode value
        interface IMappedCodecRuntime with
            member _.InnerObj = box inner
            member _.DecodeObj value = box (decode (unbox value))
            member _.EncodeObj value = box (encode (unbox value))

    type internal DelayCodec<'T>(factory: unit -> Codec<'T>) =
        inherit Codec<'T>()

        interface IDelayCodec<'T> with
            member _.Factory() = factory()
        interface IDelayCodecRuntime with
            member _.FactoryObj() = box (factory())

    type internal StringEnumCodec<'T>(names: string array, tryGetName: 'T -> string option, parseName: string -> 'T) =
        inherit Codec<'T>()

        interface IStringEnumCodec<'T> with
            member _.Names = names
            member _.TryGetName value = tryGetName value
            member _.ParseName name = parseName name
        interface IStringEnumCodecRuntime with
            member _.Names = names
            member _.TryGetNameObj value = tryGetName (unbox value)
            member _.ParseNameObj name = box (parseName name)

    type internal RawJsonValueCodec() =
        inherit Codec<JsonValue>()
        interface IRawJsonValueCodec

    type internal MissingAsNoneCodec<'T>(inner: Codec<'T option>) =
        inherit Codec<'T option>()
        interface IMissingAsNoneCodec<'T> with
            member _.Inner = inner
        interface IRuntimeMissingWrapper with
            member _.InnerObj = box inner
            member _.Kind = 0
            member _.ValueObj = null

    type internal MissingAsValueCodec<'T>(value: 'T, inner: Codec<'T>) =
        inherit Codec<'T>()
        interface IMissingAsValueCodec<'T> with
            member _.Value = value
            member _.Inner = inner
        interface IRuntimeMissingWrapper with
            member _.InnerObj = box inner
            member _.Kind = 1
            member _.ValueObj = box value

    type internal NullAsValueCodec<'T>(value: 'T, inner: Codec<'T>) =
        inherit Codec<'T>()
        interface INullAsValueCodec<'T> with
            member _.Value = value
            member _.Inner = inner
        interface IRuntimeMissingWrapper with
            member _.InnerObj = box inner
            member _.Kind = 2
            member _.ValueObj = box value

    type internal EmptyCollectionAsValueCodec<'T>(value: 'T, inner: Codec<'T>) =
        inherit Codec<'T>()
        interface IEmptyCollectionAsValueCodec<'T> with
            member _.Value = value
            member _.Inner = inner
        interface IRuntimeMissingWrapper with
            member _.InnerObj = box inner
            member _.Kind = 3
            member _.ValueObj = box value

    type internal EmptyStringAsNoneCodec(inner: Codec<string option>) =
        inherit Codec<string option>()
        interface IEmptyStringAsNoneCodec with
            member _.Inner = inner
        interface IRuntimeMissingWrapper with
            member _.InnerObj = box inner
            member _.Kind = 4
            member _.ValueObj = null

    let int: Codec<int> = PrimitiveCodec() :> Codec<int>
    let int64: Codec<int64> = PrimitiveCodec() :> Codec<int64>
    let uint32: Codec<uint32> = PrimitiveCodec() :> Codec<uint32>
    let uint64: Codec<uint64> = PrimitiveCodec() :> Codec<uint64>
    let float: Codec<float> = PrimitiveCodec() :> Codec<float>
    let decimal: Codec<decimal> = PrimitiveCodec() :> Codec<decimal>
    let string: Codec<string> = PrimitiveCodec() :> Codec<string>
    let bool: Codec<bool> = PrimitiveCodec() :> Codec<bool>
    let jsonValue: Codec<JsonValue> = RawJsonValueCodec() :> Codec<JsonValue>
    let list (inner: Codec<'T>) : Codec<'T list> = ListCodec inner :> Codec<'T list>
    let array (inner: Codec<'T>) : Codec<'T array> = ArrayCodec inner :> Codec<'T array>
    let option (inner: Codec<'T>) : Codec<'T option> = OptionCodec inner :> Codec<'T option>
    let delay (factory: unit -> Codec<'T>) : Codec<'T> = DelayCodec(factory) :> Codec<'T>
    let imap (decode: 'Inner -> 'Outer) (encode: 'Outer -> 'Inner) (inner: Codec<'Inner>) : Codec<'Outer> =
        MappedCodec(inner, decode, encode) :> Codec<'Outer>
    let map (decode: 'Inner -> 'Outer) (inner: Codec<'Inner>) : Codec<'Outer> =
        imap
            decode
            (fun _ -> failwith "Inverse mapping not provided for Codec.map. Use Codec.imap for symmetric codecs.")
            inner
    let tryMap (decode: 'Inner -> Result<'Outer, string>) (encode: 'Outer -> 'Inner) (inner: Codec<'Inner>) : Codec<'Outer> =
        imap
            (fun value ->
                match decode value with
                | Ok mapped -> mapped
                | Error message -> failwith message)
            encode
            inner
    let stringEnum (cases: (string * 'T) list) : Codec<'T> =
        let entries = cases |> List.toArray
        let names = entries |> Array.map fst

        StringEnumCodec(
            names,
            (fun candidate ->
                entries
                |> Array.tryPick (fun (name, value) -> if candidate = value then Some name else None)),
            (fun name ->
                entries
                |> Array.tryPick (fun (expectedName, value) -> if expectedName = name then Some value else None)
                |> Option.defaultWith (fun () -> failwithf "Unknown string enum value '%s'" name))
        )
        :> Codec<'T>
    let missingAsNone (inner: Codec<'T option>) : Codec<'T option> = MissingAsNoneCodec(inner) :> Codec<'T option>
    let missingAsValue (value: 'T) (inner: Codec<'T>) : Codec<'T> = MissingAsValueCodec(value, inner) :> Codec<'T>
    let nullAsValue (value: 'T) (inner: Codec<'T>) : Codec<'T> = NullAsValueCodec(value, inner) :> Codec<'T>
    let emptyCollectionAsValue (value: 'T) (inner: Codec<'T>) : Codec<'T> =
        EmptyCollectionAsValueCodec(value, inner) :> Codec<'T>
    let emptyStringAsNone (inner: Codec<string option>) : Codec<string option> =
        EmptyStringAsNoneCodec(inner) :> Codec<string option>
    let nonEmptyString: Codec<string> =
        string
        |> tryMap
            (fun value ->
                if System.String.IsNullOrEmpty(value) then
                    Error "string must not be empty"
                else
                    Ok value)
            id
    let trimmedString: Codec<string> = string |> imap (fun value -> value.Trim()) (fun value -> value.Trim())
    let positiveInt: Codec<int> =
        int
        |> tryMap
            (fun value -> if value > 0 then Ok value else Error "int must be positive")
            id
    let private rangedInt<'T>
        (typeName: string)
        (minValue: int)
        (maxValue: int)
        (convert: int -> 'T)
        (toInt: 'T -> int)
        : Codec<'T> =
        int
        |> imap
            (fun value ->
                if value < minValue || value > maxValue then
                    failwithf "%s value out of range: %d" typeName value

                convert value)
            toInt
    let int16: Codec<int16> =
        rangedInt
            "int16"
            (System.Convert.ToInt32(System.Int16.MinValue))
            (System.Convert.ToInt32(System.Int16.MaxValue))
            System.Convert.ToInt16
            System.Convert.ToInt32
    let byte: Codec<byte> = rangedInt "byte" 0 255 System.Convert.ToByte System.Convert.ToInt32
    let sbyte: Codec<sbyte> =
        rangedInt
            "sbyte"
            (System.Convert.ToInt32(System.SByte.MinValue))
            (System.Convert.ToInt32(System.SByte.MaxValue))
            System.Convert.ToSByte
            System.Convert.ToInt32
    let uint16: Codec<uint16> = rangedInt "uint16" 0 65535 System.Convert.ToUInt16 System.Convert.ToInt32
    let guid: Codec<System.Guid> =
        string
        |> imap
            System.Guid.Parse
            (fun value -> value.ToString("D"))
    let char: Codec<char> =
        string
        |> imap
            (fun value ->
                if value.Length <> 1 then
                    failwithf "char value must contain exactly one character, got %d" value.Length

                value[0])
            (fun value -> System.String([| value |]))
    let dateTime: Codec<System.DateTime> =
        string
        |> imap Core.parseDateTimeRoundtripInvariant (fun value -> value.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
    let dateTimeOffset: Codec<System.DateTimeOffset> =
        string
        |> imap Core.parseDateTimeOffsetRoundtripInvariant (fun value -> value.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
    let timeSpan: Codec<System.TimeSpan> =
        string
        |> imap Core.parseTimeSpanConstantInvariant (fun value -> value.ToString("c", System.Globalization.CultureInfo.InvariantCulture))
    let nonEmptyList (inner: Codec<'T>) : Codec<'T list> =
        list inner
        |> tryMap
            (fun values ->
                if List.isEmpty values then
                    Error "list must contain at least one item"
                else
                    Ok values)
            id
    let resizeArray (inner: Codec<'T>) : Codec<ResizeArray<'T>> =
        array inner |> imap ResizeArray (fun (items: ResizeArray<'T>) -> items.ToArray())
    let readOnlyList (inner: Codec<'T>) : Codec<System.Collections.Generic.IReadOnlyList<'T>> =
        array inner |> imap (fun items -> items :> System.Collections.Generic.IReadOnlyList<'T>) Seq.toArray
    let collection (inner: Codec<'T>) : Codec<System.Collections.Generic.ICollection<'T>> =
        array inner |> imap (fun items -> ResizeArray(items) :> System.Collections.Generic.ICollection<'T>) Seq.toArray

module Tagged =
    let tag (name: string) (value: 'Union) (matches: 'Union -> bool) : UnionCase<'Union> = {
        Name = name
        FieldType = None
        Codec = None
        Specialize = None
        TryGetValue = (fun candidate -> if matches candidate then Some null else None)
        Construct = (fun _ -> value)
    }

    let tagWith
        (name: string)
        (project: 'Union -> 'Field option)
        (inject: 'Field -> 'Union)
        (codec: Codec<'Field>)
        : UnionCase<'Union> =
        {
            Name = name
            FieldType = Some typeof<'Field>
            Codec = Some(box codec)
            Specialize =
                Some(fun specializer ->
                    box ((specializer :?> ICodecSpecializer<obj>).Specialize codec))
            TryGetValue = (fun candidate -> project candidate |> Option.map box)
            Construct =
                (fun value ->
                    value
                    |> Option.map unbox<'Field>
                    |> Option.map inject
                    |> Option.defaultWith (fun () -> failwithf "Union case '%s' requires a value" name))
        }

    let union (cases: UnionCase<'Union> list) : Codec<'Union> =
        UnionCodec<'Union>("case", "value", cases) :> Codec<'Union>

    let unionNamed (discriminatorName: string) (valueName: string) (cases: UnionCase<'Union> list) : Codec<'Union> =
        UnionCodec<'Union>(discriminatorName, valueName, cases) :> Codec<'Union>

    let inlineUnion (cases: UnionCase<'Union> list) : Codec<'Union> =
        InlineUnionCodec<'Union>("case", cases) :> Codec<'Union>

    let inlineUnionNamed (discriminatorName: string) (cases: UnionCase<'Union> list) : Codec<'Union> =
        InlineUnionCodec<'Union>(discriminatorName, cases) :> Codec<'Union>

    let message (name: string) (value: 'Union) (matches: 'Union -> bool) : UnionCase<'Union> = tag name value matches

    let messageWith
        (name: string)
        (project: 'Union -> 'Field option)
        (inject: 'Field -> 'Union)
        (codec: Codec<'Field>)
        : UnionCase<'Union> =
        tagWith name project inject codec

    let envelope (cases: UnionCase<'Union> list) : Codec<'Union> = unionNamed "type" "data" cases

    let envelopeNamed
        (discriminatorName: string)
        (valueName: string)
        (cases: UnionCase<'Union> list)
        : Codec<'Union> =
        unionNamed discriminatorName valueName cases

    let inlineEnvelope (cases: UnionCase<'Union> list) : Codec<'Union> = inlineUnionNamed "type" cases

    let inlineEnvelopeNamed (discriminatorName: string) (cases: UnionCase<'Union> list) : Codec<'Union> =
        inlineUnionNamed discriminatorName cases

type SchemaBuilder<'Record, 'Ctor, 'Current, 'Chain when 'Chain :> IChainNode<'Record, 'Ctor, 'Current>> = {
    Ctor: 'Ctor
    Chain: 'Chain
}

module internal RuntimeSchema =
    type private CodecObjRefComparer() =
        interface System.Collections.Generic.IEqualityComparer<obj> with
            member _.Equals(left, right) = obj.ReferenceEquals(left, right)
            member _.GetHashCode(value) = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value)

    let rec supportsInlinePayloadShape (codec: RuntimeSchema) =
        match codec.Definition with
        | ERecord _ -> true
        | EDelay factory -> supportsInlinePayloadShape (factory ())
        | EMissingAsNone inner
        | EMissingAsValue(_, inner)
        | ENullAsValue(_, inner)
        | EEmptyCollectionAsValue(_, inner)
        | EEmptyStringAsNone inner
        | EMap(inner, _, _) -> supportsInlinePayloadShape inner
        | _ -> false

    let toRuntimeSchema (codecObj: obj) : RuntimeSchema =
        let cache = System.Collections.Generic.Dictionary<obj, RuntimeSchema>(CodecObjRefComparer())

        let rec loop (codecObj: obj) : RuntimeSchema =
            match cache.TryGetValue(codecObj) with
            | true, runtimeSchema -> runtimeSchema
            | false, _ ->
                let targetType = (codecObj :?> ICodecInfo).TargetType

                let definition =
                    match codecObj with
                    | :? IPrimitiveCodec as primitive -> EPrimitive primitive.TargetType
                    | :? IRawJsonValueCodec -> ERawJsonValue
                    | :? IMappingDefinitionRuntime as mapping ->
                        let fields =
                            mapping.FieldsRuntime
                            |> List.map (fun field -> {
                                Name = field.Name
                                TargetType = (field.Codec :?> ICodecInfo).TargetType
                                Codec = loop field.Codec
                                GetObj = field.GetObj
                            })

                        ERecord(targetType, fields, mapping.CreateObj)
                    | :? IUnionCodecRuntime as unionCodec ->
                        let cases =
                            unionCodec.CasesRuntime
                            |> List.map (fun case -> {
                                Name = case.Name
                                FieldType = case.FieldType
                                Codec = case.Codec |> Option.map loop
                                TryGetValueObj = case.TryGetValueObj
                                ConstructObj = case.ConstructObj
                            })

                        EUnion(unionCodec.DiscriminatorName, unionCodec.ValueName, cases)
                    | :? IInlineUnionCodecRuntime as unionCodec ->
                        let cases =
                            unionCodec.CasesRuntime
                            |> List.map (fun case -> {
                                Name = case.Name
                                FieldType = case.FieldType
                                Codec = case.Codec |> Option.map loop
                                TryGetValueObj = case.TryGetValueObj
                                ConstructObj = case.ConstructObj
                            })

                        EInlineUnion(unionCodec.DiscriminatorName, cases)
                    | :? IListCodecRuntime as listCodec -> EList(loop listCodec.InnerObj)
                    | :? IArrayCodecRuntime as arrayCodec -> EArray(loop arrayCodec.InnerObj)
                    | :? IOptionCodecRuntime as optionCodec -> EOption(loop optionCodec.InnerObj)
                    | :? IDelayCodecRuntime as delayCodec -> EDelay(fun () -> loop (delayCodec.FactoryObj()))
                    | :? IMappedCodecRuntime as mapped -> EMap(loop mapped.InnerObj, mapped.DecodeObj, mapped.EncodeObj)
                    | :? IStringEnumCodecRuntime as stringEnum ->
                        EStringEnum(stringEnum.Names, stringEnum.TryGetNameObj, stringEnum.ParseNameObj)
                    | :? IRuntimeMissingWrapper as wrapped ->
                        let inner = loop wrapped.InnerObj

                        match wrapped.Kind with
                        | 0 -> EMissingAsNone inner
                        | 1 -> EMissingAsValue(wrapped.ValueObj, inner)
                        | 2 -> ENullAsValue(wrapped.ValueObj, inner)
                        | 3 -> EEmptyCollectionAsValue(wrapped.ValueObj, inner)
                        | 4 -> EEmptyStringAsNone inner
                        | _ -> failwithf "Unknown wrapper kind %d" wrapped.Kind
                    | _ -> failwithf "Codec %O cannot lower to runtime schema" (codecObj.GetType())

                let runtimeSchema = {
                    TargetType = targetType
                    Definition = definition
                }

                cache[codecObj] <- runtimeSchema
                runtimeSchema

        loop codecObj

    let toRuntime<'T> (codec: Codec<'T>) = toRuntimeSchema (box codec)

module internal SchemaRuntime =
    let rec supportsInlinePayloadShapeObj (codecObj: obj) =
        match codecObj with
        | :? IMappingDefinitionRuntime -> true
        | :? IDelayCodecRuntime as delayCodec -> supportsInlinePayloadShapeObj (delayCodec.FactoryObj())
        | :? IRuntimeMissingWrapper as wrapped -> supportsInlinePayloadShapeObj wrapped.InnerObj
        | :? IMappedCodecRuntime as mapped -> supportsInlinePayloadShapeObj mapped.InnerObj
        | _ -> false

    let rec tryInferMissingValueObj (codecObj: obj) : obj option =
        match codecObj with
        | :? IRuntimeMissingWrapper as wrapped ->
            match wrapped.Kind with
            | 0 -> Some(box None)
            | 1 -> Some wrapped.ValueObj
            | 2
            | 3
            | 4 -> tryInferMissingValueObj wrapped.InnerObj
            | _ -> failwithf "Unknown wrapper kind %d" wrapped.Kind
        | :? IMappedCodecRuntime as mapped ->
            tryInferMissingValueObj mapped.InnerObj |> Option.map mapped.DecodeObj
        | :? IDelayCodecRuntime as delayCodec -> tryInferMissingValueObj (delayCodec.FactoryObj())
        | _ -> None

module internal Inference =
    type private BuilderHelpers =
        static member MakeOption<'T>(innerCodecObj: obj) = box (Codec.option (unbox<Codec<'T>> innerCodecObj))
        static member MakeList<'T>(innerCodecObj: obj) = box (Codec.list (unbox<Codec<'T>> innerCodecObj))
        static member MakeArray<'T>(innerCodecObj: obj) = box (Codec.array (unbox<Codec<'T>> innerCodecObj))
        static member MakeReadOnlyList<'T>(innerCodecObj: obj) = box (Codec.readOnlyList (unbox<Codec<'T>> innerCodecObj))
        static member MakeCollection<'T>(innerCodecObj: obj) = box (Codec.collection (unbox<Codec<'T>> innerCodecObj))

        static member MakeEnum<'TEnum, 'TUnderlying when 'TUnderlying: struct and 'TUnderlying :> System.ValueType and 'TUnderlying: (new: unit -> 'TUnderlying)>
            (innerCodecObj: obj)
            =
            let innerCodec = unbox<Codec<'TUnderlying>> innerCodecObj

            box (
                Codec.imap
                    (fun value -> System.Enum.ToObject(typeof<'TEnum>, box value) :?> 'TEnum)
                    (fun (value: 'TEnum) ->
                        unbox<'TUnderlying>(
                            System.Convert.ChangeType(
                                value,
                                typeof<'TUnderlying>,
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                        ))
                    innerCodec
            )

    let rec tryResolve (fieldType: System.Type) : obj option =
        let getHelper name =
            typeof<BuilderHelpers>.GetMethod(name, System.Reflection.BindingFlags.Static ||| System.Reflection.BindingFlags.NonPublic)

        if fieldType = typeof<int> then Some(box Codec.int)
        elif fieldType = typeof<int64> then Some(box Codec.int64)
        elif fieldType = typeof<uint32> then Some(box Codec.uint32)
        elif fieldType = typeof<uint64> then Some(box Codec.uint64)
        elif fieldType = typeof<float> then Some(box Codec.float)
        elif fieldType = typeof<decimal> then Some(box Codec.decimal)
        elif fieldType = typeof<string> then Some(box Codec.string)
        elif fieldType = typeof<bool> then Some(box Codec.bool)
        elif fieldType = typeof<int16> then Some(box Codec.int16)
        elif fieldType = typeof<byte> then Some(box Codec.byte)
        elif fieldType = typeof<sbyte> then Some(box Codec.sbyte)
        elif fieldType = typeof<uint16> then Some(box Codec.uint16)
        elif fieldType = typeof<System.Guid> then Some(box Codec.guid)
        elif fieldType = typeof<char> then Some(box Codec.char)
        elif fieldType = typeof<System.DateTime> then Some(box Codec.dateTime)
        elif fieldType = typeof<System.DateTimeOffset> then Some(box Codec.dateTimeOffset)
        elif fieldType = typeof<System.TimeSpan> then Some(box Codec.timeSpan)
        elif fieldType = typeof<JsonValue> then Some(box Codec.jsonValue)
        elif fieldType.IsEnum then
            let underlyingType = System.Enum.GetUnderlyingType(fieldType)
            tryResolve underlyingType
            |> Option.map (fun innerCodec ->
                let methodInfo = getHelper "MakeEnum"
                methodInfo.MakeGenericMethod([| fieldType; underlyingType |]).Invoke(null, [| innerCodec |]))
        elif fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() = typedefof<option<_>> then
            let innerType = fieldType.GetGenericArguments()[0]
            tryResolve innerType
            |> Option.map (fun innerCodec ->
                let methodInfo = getHelper "MakeOption"
                methodInfo.MakeGenericMethod([| innerType |]).Invoke(null, [| innerCodec |]))
        elif fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() = typedefof<list<_>> then
            let innerType = fieldType.GetGenericArguments()[0]
            tryResolve innerType
            |> Option.map (fun innerCodec ->
                let methodInfo = getHelper "MakeList"
                methodInfo.MakeGenericMethod([| innerType |]).Invoke(null, [| innerCodec |]))
        elif fieldType.IsArray then
            let innerType = fieldType.GetElementType()
            tryResolve innerType
            |> Option.map (fun innerCodec ->
                let methodInfo = getHelper "MakeArray"
                methodInfo.MakeGenericMethod([| innerType |]).Invoke(null, [| innerCodec |]))
        elif fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() = typedefof<System.Collections.Generic.IReadOnlyList<_>> then
            let innerType = fieldType.GetGenericArguments()[0]
            tryResolve innerType
            |> Option.map (fun innerCodec ->
                let methodInfo = getHelper "MakeReadOnlyList"
                methodInfo.MakeGenericMethod([| innerType |]).Invoke(null, [| innerCodec |]))
        elif fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() = typedefof<System.Collections.Generic.ICollection<_>> then
            let innerType = fieldType.GetGenericArguments()[0]
            tryResolve innerType
            |> Option.map (fun innerCodec ->
                let methodInfo = getHelper "MakeCollection"
                methodInfo.MakeGenericMethod([| innerType |]).Invoke(null, [| innerCodec |]))
        else None

module Schema =
    let define<'Record> : SchemaBuilder<'Record, unit, unit, FieldsEnd<'Record, unit>> = {
        Ctor = ()
        Chain = FieldsEnd<'Record, unit>()
    }

    let construct
        (ctor: 'Ctor)
        (_builder: SchemaBuilder<'Record, unit, unit, FieldsEnd<'Record, unit>>)
        : SchemaBuilder<'Record, 'Ctor, 'Ctor, FieldsEnd<'Record, 'Ctor>> = {
        Ctor = ctor
        Chain = FieldsEnd<'Record, 'Ctor>()
    }

    let fieldWith
        (name: string)
        (getter: 'Record -> 'Field)
        (codec: Codec<'Field>)
        (builder: SchemaBuilder<'Record, 'Ctor, 'Field -> 'Next, 'Chain>)
        : SchemaBuilder<'Record, 'Ctor, 'Next, FieldsAppend<'Record, 'Ctor, 'Field, 'Next, 'Chain>>
        when 'Chain :> IChainNode<'Record, 'Ctor, 'Field -> 'Next> =
        {
            Ctor = builder.Ctor
            Chain = FieldsAppend(builder.Chain, name, getter, codec)
        }

    let field
        (name: string)
        (getter: 'Record -> 'Field)
        (builder: SchemaBuilder<'Record, 'Ctor, 'Field -> 'Next, 'Chain>)
        : SchemaBuilder<'Record, 'Ctor, 'Next, FieldsAppend<'Record, 'Ctor, 'Field, 'Next, 'Chain>>
        when 'Chain :> IChainNode<'Record, 'Ctor, 'Field -> 'Next> =
        match Inference.tryResolve typeof<'Field> with
        | Some codecObj -> fieldWith name getter (unbox<Codec<'Field>> codecObj) builder
        | None -> failwithf "Cannot infer codec for field '%s' of type %O" name typeof<'Field>

    let fieldInfer
        (name: string)
        (getter: 'Record -> 'Field)
        (builder: SchemaBuilder<'Record, 'Ctor, 'Field -> 'Next, 'Chain>)
        : SchemaBuilder<'Record, 'Ctor, 'Next, FieldsAppend<'Record, 'Ctor, 'Field, 'Next, 'Chain>>
        when 'Chain :> IChainNode<'Record, 'Ctor, 'Field -> 'Next> =
        field name getter builder

    let build
        (builder: SchemaBuilder<'Record, 'Ctor, 'Record, 'Chain>)
        : MappingDefinition<'Record, 'Ctor, 'Chain>
        when 'Chain :> IChainNode<'Record, 'Ctor, 'Record> =
        MappingDefinition(builder.Ctor, builder.Chain)
    let int = Codec.int
    let int64 = Codec.int64
    let uint32 = Codec.uint32
    let uint64 = Codec.uint64
    let float = Codec.float
    let decimal = Codec.decimal
    let string = Codec.string
    let bool = Codec.bool
    let int16 = Codec.int16
    let byte = Codec.byte
    let sbyte = Codec.sbyte
    let uint16 = Codec.uint16
    let guid = Codec.guid
    let char = Codec.char
    let dateTime = Codec.dateTime
    let dateTimeOffset = Codec.dateTimeOffset
    let timeSpan = Codec.timeSpan
    let jsonValue = Codec.jsonValue
    let list = Codec.list
    let nonEmptyList = Codec.nonEmptyList
    let array = Codec.array
    let resizeArray = Codec.resizeArray
    let readOnlyList = Codec.readOnlyList
    let collection = Codec.collection
    let option = Codec.option
    let delay = Codec.delay
    let map = Codec.imap
    let tryMap = Codec.tryMap
    let stringEnum = Codec.stringEnum
    let missingAsNone = Codec.missingAsNone
    let missingAsValue = Codec.missingAsValue
    let nullAsValue = Codec.nullAsValue
    let emptyCollectionAsValue = Codec.emptyCollectionAsValue
    let emptyStringAsNone = Codec.emptyStringAsNone
    let nonEmptyString = Codec.nonEmptyString
    let trimmedString = Codec.trimmedString
    let positiveInt = Codec.positiveInt
    let tag = Tagged.tag
    let tagWith = Tagged.tagWith
    let union = Tagged.union
    let unionNamed = Tagged.unionNamed
    let inlineUnion = Tagged.inlineUnion
    let inlineUnionNamed = Tagged.inlineUnionNamed
    let message = Tagged.message
    let messageWith = Tagged.messageWith
    let envelope = Tagged.envelope
    let envelopeNamed = Tagged.envelopeNamed
    let inlineEnvelope = Tagged.inlineEnvelope
    let inlineEnvelopeNamed = Tagged.inlineEnvelopeNamed

module Builder =
    let define<'Record> = Schema.define<'Record>
    let construct ctor builder = Schema.construct ctor builder
    let field name getter builder = Schema.field name getter builder
    let fieldWith name getter codec builder = Schema.fieldWith name getter codec builder
    let fieldInfer name getter builder = Schema.fieldInfer name getter builder
    let build builder = Schema.build builder
