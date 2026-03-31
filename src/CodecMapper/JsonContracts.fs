namespace CodecMapper

open System.Text
open CodecMapper

module Json =
    type JsonSource = JsonBackend.JsonSource
    type Decoder<'T> = JsonBackend.Decoder<'T>
    type Codec<'T> = JsonBackend.Codec<'T>

    let private writeEscapedString (writer: IByteWriter) (value: string) =
        JsonBackend.Runtime.writeEscapedString writer value

    type private JsonSpecializedCodec<'T>(compiled: Codec<'T>) =
        inherit CodecMapper.Codec<'T>()
        member _.Compiled = compiled

    type private RuntimeJsonCodec = {
        EncodeObj: IByteWriter -> obj -> unit
        DecodeObj: JsonSource -> struct (obj * JsonSource)
    }

    type private FieldJsonCodec<'T> = {
        Encode: IByteWriter -> 'T -> unit
        Decode: JsonSource -> struct ('T * JsonSource)
        MissingValue: 'T option
    }

    type private IJsonChain<'Record, 'CtorIn, 'CtorOut> =
        abstract member IsEmpty: bool
        abstract member ResetStreamState: unit -> unit
        abstract member TryCollectStreamField: string * JsonSource -> struct (bool * JsonSource)
        abstract member ApplyCollectedStream: 'CtorIn -> 'CtorOut
        abstract member EncodeFieldsJson: IByteWriter * 'Record -> unit

    type private JsonChainResult<'Record, 'CtorIn, 'CtorOut>(chain: IJsonChain<'Record, 'CtorIn, 'CtorOut>) =
        interface IChainResult<'Record, 'CtorIn, 'CtorOut> with
            member _.Value = box chain

    type private JsonFieldsEnd<'Record, 'Ctor>() =
        interface IJsonChain<'Record, 'Ctor, 'Ctor> with
            member _.IsEmpty = true
            member _.ResetStreamState() = ()
            member _.TryCollectStreamField(_, src) = struct (false, src)
            member _.ApplyCollectedStream(ctor) = ctor
            member _.EncodeFieldsJson(_, _) = ()

    type private JsonFieldsAppend<'Record, 'CtorIn, 'Field, 'NextCtor, 'Head
        when 'Head :> IJsonChain<'Record, 'CtorIn, 'Field -> 'NextCtor>>
        (
            head: 'Head,
            fieldName: string,
            getter: 'Record -> 'Field,
            codec: FieldJsonCodec<'Field>
        ) =

        let mutable collectedValue: 'Field voption = ValueNone

        interface IJsonChain<'Record, 'CtorIn, 'NextCtor> with
            member _.IsEmpty = false

            member _.ResetStreamState() =
                head.ResetStreamState()
                collectedValue <- ValueNone

            member _.TryCollectStreamField(name, src) =
                if name = fieldName then
                    try
                        let struct (value, nextSrc) = codec.Decode src
                        collectedValue <- ValueSome value
                        struct (true, nextSrc)
                    with
                    | :? JsonBackend.JsonDecodeException as decodeEx ->
                        raise (
                            JsonBackend.JsonDecodeException(
                                JsonBackend.Property fieldName :: decodeEx.Path,
                                decodeEx.Detail,
                                decodeEx
                            )
                        )
                    | ex ->
                        raise (JsonBackend.JsonDecodeException([ JsonBackend.Property fieldName ], ex.Message, ex))
                else
                    head.TryCollectStreamField(name, src)

            member _.ApplyCollectedStream(ctor) =
                let ctorForField = head.ApplyCollectedStream(ctor)

                match collectedValue with
                | ValueSome value -> ctorForField value
                | ValueNone ->
                    match codec.MissingValue with
                    | Some value -> ctorForField value
                    | None ->
                        raise (
                            JsonBackend.JsonDecodeException(
                                [ JsonBackend.Property fieldName ],
                                sprintf "Missing required key '%s'" fieldName,
                                null
                            )
                        )

            member _.EncodeFieldsJson(writer, record) =
                head.EncodeFieldsJson(writer, record)

                if not head.IsEmpty then
                    writer.WriteByte(byte ',')

                writeEscapedString writer fieldName
                writer.WriteByte(byte ':')
                codec.Encode writer (getter record)

    and private JsonChainFactory<'Record>() =
        interface IChainFactory<'Record> with
            member _.OnEnd() =
                JsonChainResult<'Record, 'Ctor, 'Ctor>(JsonFieldsEnd<'Record, 'Ctor>()) :> IChainResult<_, _, _>

            member _.OnField(name, getter, codec, head) =
                let headChain = head.Value :?> IJsonChain<'Record, 'CtorIn, 'Field -> 'NextCtor>
                let fieldCodec = JsonCompiler.CompileField codec

                let chain =
                    JsonFieldsAppend<'Record, 'CtorIn, 'Field, 'NextCtor, _>(headChain, name, getter, fieldCodec)

                JsonChainResult<'Record, 'CtorIn, 'NextCtor>(chain) :> IChainResult<_, _, _>

            member _.OnComplete<'Ctor>(ctor, chainResult) =
                let chain = chainResult.Value :?> IJsonChain<'Record, 'Ctor, 'Record>

                let compiled: Codec<'Record> = {
                    Encode =
                        (fun writer value ->
                            writer.WriteByte(byte '{')
                            chain.EncodeFieldsJson(writer, value)
                            writer.WriteByte(byte '}'))
                    Decode =
                        (fun src ->
                            let mutable current = JsonBackend.Runtime.skipWhitespace src
                            let data = current.Data

                            if current.Offset >= data.Length || data[current.Offset] <> byte '{' then
                                failwith "Expected {"

                            current <- JsonBackend.Runtime.skipWhitespace (current.Advance(1))
                            chain.ResetStreamState()

                            if current.Offset < data.Length && data[current.Offset] = byte '}' then
                                current <- current.Advance(1)
                            else
                                let mutable looping = true

                                while looping do
                                    let struct (name, afterKey) = JsonBackend.Runtime.stringDecoder current
                                    let valueSrc =
                                        JsonBackend.Runtime.skipWhitespace
                                            (JsonBackend.Runtime.advancePastColon afterKey)

                                    let struct (matched, nextSrc) = chain.TryCollectStreamField(name, valueSrc)

                                    let afterValue =
                                        JsonBackend.Runtime.skipWhitespace
                                            (if matched then nextSrc else JsonBackend.Runtime.skipValue valueSrc)

                                    if afterValue.Offset < afterValue.Data.Length && afterValue.Data[afterValue.Offset] = byte ',' then
                                        current <- JsonBackend.Runtime.skipWhitespace (afterValue.Advance(1))
                                    elif
                                        afterValue.Offset < afterValue.Data.Length
                                        && afterValue.Data[afterValue.Offset] = byte '}'
                                    then
                                        current <- afterValue.Advance(1)
                                        looping <- false
                                    else
                                        failwith "Expected , or }"

                            struct (chain.ApplyCollectedStream(ctor), current))
                }

                JsonSpecializedCodec<'Record>(compiled) :> CodecMapper.Codec<'Record>

    and private JsonCompiler =
        static member private WrapFieldCodec<'T>(codec: Codec<'T>) : FieldJsonCodec<'T> = {
            Encode = codec.Encode
            Decode = codec.Decode
            MissingValue = None
        }

        static member private InferMissingValue<'T>(codec: CodecMapper.Codec<'T>) : 'T option =
            SchemaRuntime.tryInferMissingValueObj (box codec) |> Option.map unbox<'T>

        static member Compile<'T>(codec: CodecMapper.Codec<'T>) : Codec<'T> =
            match box codec with
            | :? JsonSpecializedCodec<'T> as specialized -> specialized.Compiled
            | :? IMappingDefinition<'T> as mapping ->
                let specialized = mapping.Specialize(JsonChainFactory<'T>())

                match specialized with
                | :? JsonSpecializedCodec<'T> as specialized -> specialized.Compiled
                | _ -> failwith "Unexpected JSON specialization result"
            | :? IUnionCodec<'T> as unionCodec ->
                JsonCompiler.CompileUnion unionCodec |> box |> unbox<Codec<'T>>
            | :? IInlineUnionCodec<'T> as inlineUnionCodec ->
                JsonCompiler.CompileInlineUnion inlineUnionCodec |> box |> unbox<Codec<'T>>
            | :? IDelayCodec<'T> as delayCodec ->
                JsonCompiler.CompileDelay delayCodec |> box |> unbox<Codec<'T>>
            | :? IMappedCodec<_, 'T> as mappedCodec ->
                JsonCompiler.CompileMapped mappedCodec |> box |> unbox<Codec<'T>>
            | :? IListCodec<_> as listCodec ->
                JsonCompiler.CompileList listCodec |> box |> unbox<Codec<'T>>
            | :? IArrayCodec<_> as arrayCodec ->
                JsonCompiler.CompileArray arrayCodec |> box |> unbox<Codec<'T>>
            | :? IOptionCodec<_> as optionCodec ->
                JsonCompiler.CompileOption optionCodec |> box |> unbox<Codec<'T>>
            | _ -> JsonBackend.compile codec

        static member CompileField<'T>(codec: CodecMapper.Codec<'T>) : FieldJsonCodec<'T> =
            match box codec with
            | :? IMissingAsNoneCodec<_> as wrapped ->
                let inner = JsonCompiler.CompileField wrapped.Inner
                {
                    Encode = inner.Encode
                    Decode = inner.Decode
                    MissingValue = Some(unbox None)
                }
                |> box |> unbox<FieldJsonCodec<'T>>
            | :? IMissingAsValueCodec<'T> as wrapped ->
                let inner = JsonCompiler.CompileField wrapped.Inner
                {
                    Encode = inner.Encode
                    Decode = inner.Decode
                    MissingValue = Some wrapped.Value
                }
            | :? INullAsValueCodec<'T> as wrapped ->
                let inner = JsonCompiler.CompileField wrapped.Inner
                {
                    Encode = inner.Encode
                    Decode =
                        (fun src ->
                            let current = JsonBackend.Runtime.skipWhitespace src
                            let data = current.Data

                            if current.Offset < data.Length && data[current.Offset] = byte 'n' then
                                let next = JsonBackend.Runtime.nullDecoder current
                                struct (wrapped.Value, next)
                            else
                                inner.Decode src)
                    MissingValue = inner.MissingValue
                }
            | :? IEmptyCollectionAsValueCodec<'T> as wrapped ->
                let inner = JsonCompiler.CompileField wrapped.Inner
                {
                    Encode = inner.Encode
                    Decode =
                        (fun src ->
                            let struct (value, next) = inner.Decode src
                            if Core.isEmptyCollectionValue value then
                                struct (wrapped.Value, next)
                            else
                                struct (value, next))
                    MissingValue = inner.MissingValue
                }
            | :? IEmptyStringAsNoneCodec as wrapped ->
                let inner = JsonCompiler.CompileField wrapped.Inner
                {
                    Encode = inner.Encode
                    Decode =
                        (fun src ->
                            let current = JsonBackend.Runtime.skipWhitespace src
                            let data = current.Data

                            if current.Offset < data.Length && data[current.Offset] = byte '"' then
                                let struct (text, next) = JsonBackend.Runtime.stringDecoder src
                                if text = "" then
                                    struct (None, next)
                                else
                                    inner.Decode src
                            else
                                inner.Decode src)
                    MissingValue = inner.MissingValue
                }
                |> box |> unbox<FieldJsonCodec<'T>>
            | _ ->
                let compiled = JsonBackend.compile codec
                {
                    Encode = compiled.Encode
                    Decode = compiled.Decode
                    MissingValue = JsonCompiler.InferMissingValue codec
                }

        static member private CompileList<'Item>(listCodec: IListCodec<'Item>) : Codec<'Item list> =
            let itemCodec = JsonCompiler.Compile listCodec.Inner

            {
                Encode =
                    (fun writer values ->
                        writer.WriteByte(byte '[')
                        let mutable first = true

                        for value in values do
                            if not first then
                                writer.WriteByte(byte ',')

                            itemCodec.Encode writer value
                            first <- false

                        writer.WriteByte(byte ']'))
                Decode =
                    (fun src ->
                        let mutable current = JsonBackend.Runtime.skipWhitespace src

                        if current.Offset >= current.Data.Length || current.Data[current.Offset] <> byte '[' then
                            failwith "Expected ["

                        current <- JsonBackend.Runtime.skipWhitespace (current.Advance(1))
                        let values = ResizeArray<'Item>()
                        let mutable continueLoop = true

                        if current.Offset < current.Data.Length && current.Data[current.Offset] = byte ']' then
                            current <- current.Advance(1)
                            continueLoop <- false

                        while continueLoop do
                            let struct (value, nextSrc) = itemCodec.Decode current
                            values.Add(value)
                            let nextSrc = JsonBackend.Runtime.skipWhitespace nextSrc

                            if nextSrc.Offset < nextSrc.Data.Length && nextSrc.Data[nextSrc.Offset] = byte ',' then
                                current <- JsonBackend.Runtime.skipWhitespace (nextSrc.Advance(1))
                            elif nextSrc.Offset < nextSrc.Data.Length && nextSrc.Data[nextSrc.Offset] = byte ']' then
                                current <- nextSrc.Advance(1)
                                continueLoop <- false
                            else
                                failwith "Expected , or ]"

                        struct (List.ofSeq values, current))
            }

        static member private CompileMapped<'Inner, 'Outer>(mappedCodec: IMappedCodec<'Inner, 'Outer>) : Codec<'Outer> =
            let innerCodec = JsonCompiler.Compile mappedCodec.Inner

            {
                Encode = (fun writer value -> innerCodec.Encode writer (mappedCodec.Encode value))
                Decode =
                    (fun src ->
                        let struct (value, next) = innerCodec.Decode src
                        struct (mappedCodec.Decode value, next))
            }

        static member private CompileDelay<'T>(delayCodec: IDelayCodec<'T>) : Codec<'T> =
            let compiled = lazy (JsonCompiler.Compile(delayCodec.Factory()))

            {
                Encode = (fun writer value -> compiled.Value.Encode writer value)
                Decode = (fun src -> compiled.Value.Decode src)
            }

        static member private CompileArray<'Item>(arrayCodec: IArrayCodec<'Item>) : Codec<'Item array> =
            let itemCodec = JsonCompiler.Compile arrayCodec.Inner

            {
                Encode =
                    (fun writer values ->
                        writer.WriteByte(byte '[')
                        let mutable first = true

                        for value in values do
                            if not first then
                                writer.WriteByte(byte ',')

                            itemCodec.Encode writer value
                            first <- false

                        writer.WriteByte(byte ']'))
                Decode =
                    (fun src ->
                        let mutable current = JsonBackend.Runtime.skipWhitespace src

                        if current.Offset >= current.Data.Length || current.Data[current.Offset] <> byte '[' then
                            failwith "Expected ["

                        current <- JsonBackend.Runtime.skipWhitespace (current.Advance(1))
                        let values = ResizeArray<'Item>()
                        let mutable continueLoop = true

                        if current.Offset < current.Data.Length && current.Data[current.Offset] = byte ']' then
                            current <- current.Advance(1)
                            continueLoop <- false

                        while continueLoop do
                            let struct (value, nextSrc) = itemCodec.Decode current
                            values.Add(value)
                            let nextSrc = JsonBackend.Runtime.skipWhitespace nextSrc

                            if nextSrc.Offset < nextSrc.Data.Length && nextSrc.Data[nextSrc.Offset] = byte ',' then
                                current <- JsonBackend.Runtime.skipWhitespace (nextSrc.Advance(1))
                            elif nextSrc.Offset < nextSrc.Data.Length && nextSrc.Data[nextSrc.Offset] = byte ']' then
                                current <- nextSrc.Advance(1)
                                continueLoop <- false
                            else
                                failwith "Expected , or ]"

                        struct (values.ToArray(), current))
            }

        static member private CompileOption<'Item>(optionCodec: IOptionCodec<'Item>) : Codec<'Item option> =
            let itemCodec = JsonCompiler.Compile optionCodec.Inner

            {
                Encode =
                    (fun writer value ->
                        match value with
                        | Some inner -> itemCodec.Encode writer inner
                        | None -> writer.WriteString("null"))
                Decode =
                    (fun src ->
                        let current = JsonBackend.Runtime.skipWhitespace src
                        let data = current.Data

                        if current.Offset < data.Length && data[current.Offset] = byte 'n' then
                            let next = JsonBackend.Runtime.nullDecoder current
                            struct (None, next)
                        else
                            let struct (value, next) = itemCodec.Decode src
                            struct (Some value, next))
            }

        static member private CompileUnion<'Union>(unionCodec: IUnionCodec<'Union>) : Codec<'Union> =
            let specializer =
                { new ICodecSpecializer<obj> with
                    member _.Specialize<'T>(codec: CodecMapper.Codec<'T>) =
                        let compiled = JsonCompiler.Compile codec

                        box {
                            EncodeObj = (fun writer value -> compiled.Encode writer (unbox value))
                            DecodeObj =
                                (fun src ->
                                    let struct (value, next) = compiled.Decode src
                                    struct (box value, next))
                        } }

            let compiledCases =
                unionCodec.Cases
                |> List.map (fun case ->
                    case,
                    (case.Specialize
                     |> Option.map (fun specialize -> specialize (box specializer) :?> RuntimeJsonCodec)))

            let rawJsonCodec = JsonBackend.compile Schema.jsonValue

            {
                Encode =
                    (fun writer value ->
                        match
                            compiledCases
                            |> List.tryPick (fun (case, payloadCodec) ->
                                case.TryGetValue value |> Option.map (fun fieldValue -> case, payloadCodec, fieldValue))
                        with
                        | Some(case, payloadCodec, fieldValue) ->
                            writer.WriteByte(byte '{')
                            writeEscapedString writer unionCodec.DiscriminatorName
                            writer.WriteByte(byte ':')
                            writeEscapedString writer case.Name

                            match payloadCodec with
                            | Some payloadCodec ->
                                writer.WriteByte(byte ',')
                                writeEscapedString writer unionCodec.ValueName
                                writer.WriteByte(byte ':')
                                payloadCodec.EncodeObj writer fieldValue
                            | None -> ()

                            writer.WriteByte(byte '}')
                        | None -> failwithf "No union case matched value for type %O" typeof<'Union>)
                Decode =
                    (fun src ->
                        let struct (rawValue, next) = JsonBackend.Runtime.jsonValueDecoder src

                        match rawValue with
                        | JObject properties ->
                            let tryFind name =
                                properties |> List.tryFind (fun (key, _) -> key = name) |> Option.map snd

                            let caseName =
                                match tryFind unionCodec.DiscriminatorName with
                                | Some(JString value) -> value
                                | Some _ ->
                                    failwithf "Union discriminator '%s' must be a string" unionCodec.DiscriminatorName
                                | None -> failwithf "Missing union discriminator '%s'" unionCodec.DiscriminatorName

                            match compiledCases |> List.tryFind (fun (case, _) -> case.Name = caseName) with
                            | Some(case, payloadCodec) ->
                                match payloadCodec with
                                | None ->
                                    match tryFind unionCodec.ValueName with
                                    | Some _ ->
                                        failwithf
                                            "Union case '%s' does not accept payload '%s'"
                                            caseName
                                            unionCodec.ValueName
                                    | None -> struct (case.Construct None, next)
                                | Some payloadCodec ->
                                    let payload =
                                        match tryFind unionCodec.ValueName with
                                        | Some value -> value
                                        | None ->
                                            failwithf
                                                "Missing union payload '%s' for case '%s'"
                                                unionCodec.ValueName
                                                caseName

                                    let writer = ResizableBuffer.Create(4096)

                                    try
                                        rawJsonCodec.Encode writer payload
                                        let struct (fieldValue, rest) = payloadCodec.DecodeObj(ByteSource(writer.InternalData, 0))
                                        let rest = JsonBackend.Runtime.skipWhitespace rest

                                        if rest.Offset <> writer.InternalCount then
                                            failwithf
                                                "Union payload '%s' for case '%s' had trailing content"
                                                unionCodec.ValueName
                                                caseName

                                        struct (case.Construct(Some(box fieldValue)), next)
                                    finally
                                        writer.Release()
                            | None -> failwithf "Unknown union case '%s'" caseName
                        | _ -> failwith "Expected union object")
            }

        static member private CompileInlineUnion<'Union>(inlineUnionCodec: IInlineUnionCodec<'Union>) : Codec<'Union> =
            let specializer =
                { new ICodecSpecializer<obj> with
                    member _.Specialize<'T>(codec: CodecMapper.Codec<'T>) =
                        let compiled = JsonCompiler.Compile codec

                        box {
                            EncodeObj = (fun writer value -> compiled.Encode writer (unbox value))
                            DecodeObj =
                                (fun src ->
                                    let struct (value, next) = compiled.Decode src
                                    struct (box value, next))
                        } }

            let compiledCases =
                inlineUnionCodec.Cases
                |> List.map (fun case ->
                    let payloadCodec =
                        case.Specialize
                        |> Option.map (fun specialize ->
                            match case.Codec with
                            | Some originalCodecObj ->
                                if not (SchemaRuntime.supportsInlinePayloadShapeObj originalCodecObj) then
                                    failwithf "Inline union case '%s' payload schema must be object-shaped" case.Name
                            | None -> ()

                            specialize (box specializer) :?> RuntimeJsonCodec)

                    case, payloadCodec)

            let rawJsonCodec = JsonBackend.compile Schema.jsonValue

            let encodeInlinePayload (payloadCodec: RuntimeJsonCodec) (fieldValue: obj) =
                let writer = ResizableBuffer.Create(4096)

                try
                    payloadCodec.EncodeObj writer fieldValue
                    let struct (rawPayload, rest) = rawJsonCodec.Decode(ByteSource(writer.InternalData, 0))
                    let rest = JsonBackend.Runtime.skipWhitespace rest

                    if rest.Offset <> writer.InternalCount then
                        failwith "Inline union payload had trailing JSON content"

                    match rawPayload with
                    | JObject properties -> properties
                    | _ -> failwith "Inline union payload schema must encode as a JSON object"
                finally
                    writer.Release()

            let decodeInlinePayload (payloadCodec: RuntimeJsonCodec) (properties: (string * JsonValue) list) =
                let writer = ResizableBuffer.Create(4096)

                try
                    rawJsonCodec.Encode writer (JObject properties)
                    let struct (fieldValue, rest) = payloadCodec.DecodeObj(ByteSource(writer.InternalData, 0))
                    let rest = JsonBackend.Runtime.skipWhitespace rest

                    if rest.Offset <> writer.InternalCount then
                        failwith "Inline union payload had trailing JSON content"

                    fieldValue
                finally
                    writer.Release()

            {
                Encode =
                    (fun writer value ->
                        match
                            compiledCases
                            |> List.tryPick (fun (case, payloadCodec) ->
                                case.TryGetValue value |> Option.map (fun fieldValue -> case, payloadCodec, fieldValue))
                        with
                        | Some(case, payloadCodec, fieldValue) ->
                            let payloadProperties =
                                match payloadCodec with
                                | Some payloadCodec -> encodeInlinePayload payloadCodec fieldValue
                                | None -> []

                            writer.WriteByte(byte '{')
                            writeEscapedString writer inlineUnionCodec.DiscriminatorName
                            writer.WriteByte(byte ':')
                            writeEscapedString writer case.Name

                            for propertyName, propertyValue in payloadProperties do
                                if propertyName = inlineUnionCodec.DiscriminatorName then
                                    failwithf
                                        "Inline union case '%s' payload cannot reuse discriminator field '%s'"
                                        case.Name
                                        inlineUnionCodec.DiscriminatorName

                                writer.WriteByte(byte ',')
                                writeEscapedString writer propertyName
                                writer.WriteByte(byte ':')
                                rawJsonCodec.Encode writer propertyValue

                            writer.WriteByte(byte '}')
                        | None -> failwithf "No union case matched value for type %O" typeof<'Union>)
                Decode =
                    (fun src ->
                        let struct (rawValue, next) = JsonBackend.Runtime.jsonValueDecoder src

                        match rawValue with
                        | JObject properties ->
                            let tryFind name =
                                properties |> List.tryFind (fun (key, _) -> key = name) |> Option.map snd

                            let caseName =
                                match tryFind inlineUnionCodec.DiscriminatorName with
                                | Some(JString value) -> value
                                | Some _ ->
                                    failwithf
                                        "Union discriminator '%s' must be a string"
                                        inlineUnionCodec.DiscriminatorName
                                | None ->
                                    failwithf
                                        "Missing union discriminator '%s'"
                                        inlineUnionCodec.DiscriminatorName

                            let payloadProperties =
                                properties |> List.filter (fun (key, _) -> key <> inlineUnionCodec.DiscriminatorName)

                            match compiledCases |> List.tryFind (fun (case, _) -> case.Name = caseName) with
                            | Some(case, payloadCodec) ->
                                match payloadCodec with
                                | None ->
                                    if List.isEmpty payloadProperties then
                                        struct (case.Construct None, next)
                                    else
                                        failwithf
                                            "Union case '%s' does not accept payload fields alongside '%s'"
                                            caseName
                                            inlineUnionCodec.DiscriminatorName
                                | Some payloadCodec ->
                                    let fieldValue = decodeInlinePayload payloadCodec payloadProperties
                                    struct (case.Construct(Some fieldValue), next)
                            | None -> failwithf "Unknown union case '%s'" caseName
                        | _ -> failwith "Expected union object")
            }

    let compile (codec: CodecMapper.Codec<'T>) : Codec<'T> = JsonCompiler.Compile codec
    let compileSchema (codec: CodecMapper.Codec<'T>) : Codec<'T> = compile codec

    let buildAndCompile (builder: SchemaBuilder<'Record, 'Ctor, 'Record, 'Chain>) : Codec<'Record>
        when 'Chain :> IChainNode<'Record, 'Ctor, 'Record> =
        builder |> Schema.build |> compile

    let serialize (codec: Codec<'T>) (value: 'T) = JsonBackend.serialize codec value
    let deserialize (codec: Codec<'T>) (json: string) = JsonBackend.deserialize codec json
    let deserializeBytes (codec: Codec<'T>) (bytes: byte[]) = JsonBackend.deserializeBytes codec bytes
