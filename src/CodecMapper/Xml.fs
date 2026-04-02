namespace CodecMapper

open System.Text
open System.Collections.Generic
open System.Globalization
open System.Runtime.CompilerServices
open Microsoft.FSharp.Reflection

/// XML codec compilation and runtime helpers.
///
/// The XML backend intentionally supports a smaller explicit subset than the
/// JSON backend: element content only, repeated `<item>` nodes for
/// collections, and ignorable inter-element whitespace.
module XmlBackend =
    /// The byte-level input state for XML decoding.
    type XmlSource = ByteSource

    /// The byte-level output abstraction used by XML encoders.
    type XmlWriter = IByteWriter

    /// A compiled XML codec for a specific schema.
    type Codec<'T> = {
        Encode: XmlWriter -> 'T -> unit
        Decode: XmlSource -> struct ('T * XmlSource)
    }

    type internal DecodePathSegment =
        | Element of string
        | Item of int

    type internal XmlDecodeException(path: DecodePathSegment list, detail: string, ?inner: exn) =
        inherit System.Exception(detail, defaultArg inner null)

        member _.Path = path
        member _.Detail = detail

        override _.Message =
            let renderPath segments =
                let builder = StringBuilder("$")

                for segment in segments do
                    match segment with
                    | Element name ->
                        builder.Append('/') |> ignore
                        builder.Append(name) |> ignore
                    | Item index ->
                        builder.Append("/item[") |> ignore
                        builder.Append(index) |> ignore
                        builder.Append(']') |> ignore

                builder.ToString()

            sprintf "XML decode error at %s: %s" (renderPath path) detail

    type CompiledCodec = {
        Encode: XmlWriter -> string -> obj -> unit
        Decode: XmlSource -> string -> struct (obj * XmlSource)
        MissingValue: obj option
    }

    module internal Runtime =
        let private asDecodeException detail path inner =
            XmlDecodeException(path, detail, inner) :> exn

        let decodeFailure detail =
            raise (asDecodeException detail [] null)

        let private prependPath segment (ex: exn) =
            match ex with
            | :? XmlDecodeException as decodeEx -> asDecodeException decodeEx.Detail (segment :: decodeEx.Path) ex
            | _ -> asDecodeException ex.Message [ segment ] ex

        let withPath segment f =
            try
                f ()
            with ex ->
                raise (prependPath segment ex)

        let withValidationContext f =
            try
                f ()
            with
            | :? XmlDecodeException -> reraise ()
            | ex -> raise (asDecodeException ("Validation failed: " + ex.Message) [] ex)

        let inline skipWhitespace (src: XmlSource) =
            let mutable i = src.Offset
            let data = src.Data

            while i < data.Length
                  && (data[i] = byte ' ' || data[i] = byte '\n' || data[i] = byte '\r' || data[i] = byte '\t') do
                i <- i + 1

            ByteSource(data, i)

        ///
        /// The XML surface is intentionally small: element tags and escaped
        /// text nodes, with no attributes or mixed content.
        let expectOpenTag (tag: string) (src: XmlSource) =
            let src = skipWhitespace src
            let data = src.Data

            if src.Offset >= data.Length || data[src.Offset] <> byte '<' then
                failwith "Expected <"

            let mutable i = src.Offset + 1

            if i < data.Length && data[i] = byte '/' then
                failwithf "Expected <%s>" tag

            let start = i

            while i < data.Length && data[i] <> byte '>' do
                i <- i + 1

            if i >= data.Length then
                failwith "Unterminated tag"

#if !FABLE_COMPILER
            let actual = Encoding.UTF8.GetString(data, start, i - start)
#else
            let actual = Encoding.UTF8.GetString(data.[start .. i - 1])
#endif

            if actual <> tag then
                failwithf "Expected <%s>" tag

            ByteSource(data, i + 1)

        let expectCloseTag (tag: string) (src: XmlSource) =
            let src = skipWhitespace src
            let data = src.Data

            if
                src.Offset + 2 >= data.Length
                || data[src.Offset] <> byte '<'
                || data[src.Offset + 1] <> byte '/'
            then
                failwithf "Expected </%s>" tag

            let mutable i = src.Offset + 2
            let start = i

            while i < data.Length && data[i] <> byte '>' do
                i <- i + 1

            if i >= data.Length then
                failwith "Unterminated tag"

#if !FABLE_COMPILER
            let actual = Encoding.UTF8.GetString(data, start, i - start)
#else
            let actual = Encoding.UTF8.GetString(data.[start .. i - 1])
#endif

            if actual <> tag then
                failwithf "Expected </%s>" tag

            ByteSource(data, i + 1)

        let tryReadCloseTag (tag: string) (src: XmlSource) =
            let src = skipWhitespace src
            let data = src.Data

            if
                src.Offset + tag.Length + 2 >= data.Length
                || data[src.Offset] <> byte '<'
                || data[src.Offset + 1] <> byte '/'
            then
                None
            else
                let mutable i = src.Offset + 2
                let start = i

                while i < data.Length && data[i] <> byte '>' do
                    i <- i + 1

                if i >= data.Length then
                    failwith "Unterminated tag"

#if !FABLE_COMPILER
                let actual = Encoding.UTF8.GetString(data, start, i - start)
#else
                let actual = Encoding.UTF8.GetString(data.[start .. i - 1])
#endif

                if actual = tag then Some(ByteSource(data, i + 1)) else None

        /// Captures all bytes up to the matching close tag for the current element content.
        ///
        /// This is used by inline tagged unions to wrap merged child elements
        /// back into one synthetic payload root for reuse of existing codecs.
        let sliceUntilCloseTag (tag: string) (src: XmlSource) =
            let current = skipWhitespace src
            let data = current.Data
            let start = current.Offset
            let mutable i = current.Offset
            let mutable depth = 0
            let mutable found = false
            let mutable closeStart = current.Offset
            let mutable nextOffset = current.Offset

            while i < data.Length && not found do
                if data[i] = byte '<' then
                    let mutable j = i + 1

                    if j < data.Length && data[j] = byte '/' then
                        j <- j + 1
                        let nameStart = j

                        while j < data.Length && data[j] <> byte '>' do
                            j <- j + 1

                        if j >= data.Length then
                            failwith "Unterminated tag"

#if !FABLE_COMPILER
                        let actual = Encoding.UTF8.GetString(data, nameStart, j - nameStart)
#else
                        let actual = Encoding.UTF8.GetString(data.[nameStart .. j - 1])
#endif

                        if depth = 0 && actual = tag then
                            found <- true
                            closeStart <- i
                            nextOffset <- j + 1
                        else
                            depth <- depth - 1
                            i <- j + 1
                    else
                        while j < data.Length && data[j] <> byte '>' do
                            j <- j + 1

                        if j >= data.Length then
                            failwith "Unterminated tag"

                        depth <- depth + 1
                        i <- j + 1
                else
                    i <- i + 1

            if not found then
                failwithf "Expected </%s>" tag

            let length = closeStart - start
            let slice = Array.zeroCreate<byte> length

            if length > 0 then
                System.Array.Copy(data, start, slice, 0, length)

            struct (slice, ByteSource(data, nextOffset))

        ///
        /// Text nodes must escape structural characters or the decoder cannot
        /// distinguish content from markup.
        let escapeText (value: string) =
            let builder = StringBuilder()

            for i in 0 .. value.Length - 1 do
                match value[i] with
                | '&' -> builder.Append("&amp;") |> ignore
                | '<' -> builder.Append("&lt;") |> ignore
                | '>' -> builder.Append("&gt;") |> ignore
                | '"' -> builder.Append("&quot;") |> ignore
                | '\'' -> builder.Append("&apos;") |> ignore
                | c -> builder.Append(c) |> ignore

            builder.ToString()

        let unescapeText (value: string) =
            value
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&quot;", "\"")
                .Replace("&apos;", "'")
                .Replace("&amp;", "&")

        let readTextNode (src: XmlSource) =
            let data = src.Data
            let mutable i = src.Offset

            while i < data.Length && data[i] <> byte '<' do
                i <- i + 1

#if !FABLE_COMPILER
            let raw = Encoding.UTF8.GetString(data, src.Offset, i - src.Offset)
#else
            let raw = Encoding.UTF8.GetString(data.[src.Offset .. i - 1])
#endif

            struct (unescapeText raw, ByteSource(data, i))

        let makeOptionNone (optionType: System.Type) =
            let noneCase =
                FSharpType.GetUnionCases(optionType) |> Array.find (fun c -> c.Name = "None")

            FSharpValue.MakeUnion(noneCase, [||])

        let makeOptionSome (optionType: System.Type) (value: obj) =
            let someCase =
                FSharpType.GetUnionCases(optionType) |> Array.find (fun c -> c.Name = "Some")

            FSharpValue.MakeUnion(someCase, [| value |])

    type private RuntimeSchemaRefComparer() =
        interface IEqualityComparer<RuntimeSchema> with
            member _.Equals(left, right) = obj.ReferenceEquals(left, right)
            member _.GetHashCode(value) = RuntimeHelpers.GetHashCode(value)

    let private compileUntyped (rootSchema: RuntimeSchema) : CompiledCodec =
        let cache = Dictionary<RuntimeSchema, CompiledCodec>(RuntimeSchemaRefComparer())

        let rec loop (schema: RuntimeSchema) : CompiledCodec =
            match cache.TryGetValue(schema) with
            | true, codec -> codec
            | false, _ ->
                let mutable encodeImpl = Unchecked.defaultof<IByteWriter -> string -> obj -> unit>

                let mutable decodeImpl =
                    Unchecked.defaultof<XmlSource -> string -> struct (obj * XmlSource)>

                let mutable missingValueImpl = None

                let placeholder = {
                    Encode = (fun writer tag value -> encodeImpl writer tag value)
                    Decode = (fun source tag -> decodeImpl source tag)
                    MissingValue = None
                }

                cache[schema] <- placeholder

                let compiled =
                    match schema.Definition with
                    | EPrimitive t when t = typeof<int> -> {
                        Encode =
                            (fun w tag v ->
                                w.WriteByte(byte '<')
                                w.WriteString(tag)
                                w.WriteByte(byte '>')
                                w.WriteInt(unbox v)
                                w.WriteByte(byte '<')
                                w.WriteByte(byte '/')
                                w.WriteString(tag)
                                w.WriteByte(byte '>'))
                        Decode =
                            (fun src tag ->
                                let current = Runtime.expectOpenTag tag src
                                let struct (text, current) = Runtime.readTextNode current
                                let current = Runtime.expectCloseTag tag current
                                let v = Core.parseInt32Invariant "int" (text.Trim())
                                struct (box v, current))
                        MissingValue = None
                      }
                    | EPrimitive t when t = typeof<int64> -> {
                        Encode =
                            (fun w tag v ->
                                w.WriteByte(byte '<')
                                w.WriteString(tag)
                                w.WriteByte(byte '>')
                                w.WriteString((unbox<int64> v).ToString(CultureInfo.InvariantCulture))
                                w.WriteByte(byte '<')
                                w.WriteByte(byte '/')
                                w.WriteString(tag)
                                w.WriteByte(byte '>'))
                        Decode =
                            (fun src tag ->
                                let current = Runtime.expectOpenTag tag src
                                let struct (text, current) = Runtime.readTextNode current
                                let current = Runtime.expectCloseTag tag current

                                let value = Core.parseInt64Invariant "int64" (text.Trim())

                                struct (box value, current))
                        MissingValue = None
                      }
                    | EPrimitive t when t = typeof<uint32> -> {
                        Encode =
                            (fun w tag v ->
                                w.WriteByte(byte '<')
                                w.WriteString(tag)
                                w.WriteByte(byte '>')
                                w.WriteString((unbox<uint32> v).ToString(CultureInfo.InvariantCulture))
                                w.WriteByte(byte '<')
                                w.WriteByte(byte '/')
                                w.WriteString(tag)
                                w.WriteByte(byte '>'))
                        Decode =
                            (fun src tag ->
                                let current = Runtime.expectOpenTag tag src
                                let struct (text, current) = Runtime.readTextNode current
                                let current = Runtime.expectCloseTag tag current

                                let value = Core.parseUInt32Invariant "uint32" (text.Trim())

                                struct (box value, current))
                        MissingValue = None
                      }
                    | EPrimitive t when t = typeof<uint64> -> {
                        Encode =
                            (fun w tag v ->
                                w.WriteByte(byte '<')
                                w.WriteString(tag)
                                w.WriteByte(byte '>')
                                w.WriteString((unbox<uint64> v).ToString(CultureInfo.InvariantCulture))
                                w.WriteByte(byte '<')
                                w.WriteByte(byte '/')
                                w.WriteString(tag)
                                w.WriteByte(byte '>'))
                        Decode =
                            (fun src tag ->
                                let current = Runtime.expectOpenTag tag src
                                let struct (text, current) = Runtime.readTextNode current
                                let current = Runtime.expectCloseTag tag current

                                let value = Core.parseUInt64Invariant "uint64" (text.Trim())

                                struct (box value, current))
                        MissingValue = None
                      }
                    | EPrimitive t when t = typeof<float> -> {
                        Encode =
                            (fun w tag v ->
                                w.WriteByte(byte '<')
                                w.WriteString(tag)
                                w.WriteByte(byte '>')
                                w.WriteString(Core.formatFloat (unbox<float> v))
                                w.WriteByte(byte '<')
                                w.WriteByte(byte '/')
                                w.WriteString(tag)
                                w.WriteByte(byte '>'))
                        Decode =
                            (fun src tag ->
                                let current = Runtime.expectOpenTag tag src
                                let struct (text, current) = Runtime.readTextNode current
                                let current = Runtime.expectCloseTag tag current

                                let value = Core.parseFloatInvariant "float" (text.Trim())

                                struct (box value, current))
                        MissingValue = None
                      }
                    | EPrimitive t when t = typeof<decimal> -> {
                        Encode =
                            (fun w tag v ->
                                w.WriteByte(byte '<')
                                w.WriteString(tag)
                                w.WriteByte(byte '>')
                                w.WriteString((unbox<decimal> v).ToString(CultureInfo.InvariantCulture))
                                w.WriteByte(byte '<')
                                w.WriteByte(byte '/')
                                w.WriteString(tag)
                                w.WriteByte(byte '>'))
                        Decode =
                            (fun src tag ->
                                let current = Runtime.expectOpenTag tag src
                                let struct (text, current) = Runtime.readTextNode current
                                let current = Runtime.expectCloseTag tag current

                                let value = Core.parseDecimalInvariant "decimal" (text.Trim())

                                struct (box value, current))
                        MissingValue = None
                      }
                    | EPrimitive t when t = typeof<string> -> {
                        Encode =
                            (fun w tag v ->
                                w.WriteByte(byte '<')
                                w.WriteString(tag)
                                w.WriteByte(byte '>')
                                w.WriteString(Runtime.escapeText (unbox v))
                                w.WriteByte(byte '<')
                                w.WriteByte(byte '/')
                                w.WriteString(tag)
                                w.WriteByte(byte '>'))
                        Decode =
                            (fun src tag ->
                                let current = Runtime.expectOpenTag tag src
                                let struct (v, current) = Runtime.readTextNode current
                                let current = Runtime.expectCloseTag tag current
                                struct (box v, current))
                        MissingValue = None
                      }
                    | EPrimitive t when t = typeof<bool> -> {
                        Encode =
                            (fun w tag v ->
                                w.WriteByte(byte '<')
                                w.WriteString(tag)
                                w.WriteByte(byte '>')
                                w.WriteString(if unbox<bool> v then "true" else "false")
                                w.WriteByte(byte '<')
                                w.WriteByte(byte '/')
                                w.WriteString(tag)
                                w.WriteByte(byte '>'))
                        Decode =
                            (fun src tag ->
                                let current = Runtime.expectOpenTag tag src
                                let struct (text, current) = Runtime.readTextNode current
                                let current = Runtime.expectCloseTag tag current

                                match text.Trim() with
                                | "true" -> struct (box true, current)
                                | "false" -> struct (box false, current)
                                | _ -> failwith "Expected true or false")
                        MissingValue = None
                      }
                    | EStringEnum(_, tryGetName, parseName) -> {
                        Encode =
                            (fun w tag v ->
                                match tryGetName v with
                                | Some name ->
                                    w.WriteByte(byte '<')
                                    w.WriteString(tag)
                                    w.WriteByte(byte '>')
                                    w.WriteString(Runtime.escapeText name)
                                    w.WriteByte(byte '<')
                                    w.WriteByte(byte '/')
                                    w.WriteString(tag)
                                    w.WriteByte(byte '>')
                                | None -> failwithf "No string enum name matched value for type %O" schema.TargetType)
                        Decode =
                            (fun src tag ->
                                let current = Runtime.expectOpenTag tag src
                                let struct (text, current) = Runtime.readTextNode current
                                let current = Runtime.expectCloseTag tag current
                                struct (parseName text, current))
                        MissingValue = None
                      }
                    | ERawJsonValue ->
                        let fail () =
                            failwith "Schema.jsonValue is JSON-only; XML has no symmetric raw JSON DOM representation"

                        {
                            Encode = (fun _ _ _ -> fail ())
                            Decode = (fun _ _ -> fail ())
                            MissingValue = None
                        }
                    | EOption innerSchema ->
                        let innerCodec = loop innerSchema
                        let optionType = schema.TargetType

                        {
                            Encode =
                                (fun w tag v ->
                                    w.WriteByte(byte '<')
                                    w.WriteString(tag)
                                    w.WriteByte(byte '>')

                                    if not (isNull v) then
                                        innerCodec.Encode
                                            w
                                            "some"
                                            (FSharpValue.GetUnionFields(v, optionType) |> snd |> Array.item 0)

                                    w.WriteByte(byte '<')
                                    w.WriteByte(byte '/')
                                    w.WriteString(tag)
                                    w.WriteByte(byte '>'))
                            Decode =
                                (fun src tag ->
                                    let current = Runtime.expectOpenTag tag src
                                    let current = Runtime.skipWhitespace current

                                    match Runtime.tryReadCloseTag tag current with
                                    | Some next -> struct (Runtime.makeOptionNone optionType, next)
                                    | None ->
                                        let struct (value, current) = innerCodec.Decode current "some"
                                        let current = Runtime.skipWhitespace current
                                        let current = Runtime.expectCloseTag tag current
                                        struct (Runtime.makeOptionSome optionType value, current))
                            MissingValue = None
                        }
                    | EMissingAsNone innerSchema ->
                        let innerCodec = loop innerSchema
                        let optionType = schema.TargetType

                        {
                            Encode = innerCodec.Encode
                            Decode = innerCodec.Decode
                            MissingValue = Some(Runtime.makeOptionNone optionType)
                        }
                    | EMissingAsValue(defaultValue, innerSchema) ->
                        let innerCodec = loop innerSchema

                        {
                            Encode = innerCodec.Encode
                            Decode = innerCodec.Decode
                            MissingValue = Some defaultValue
                        }
                    | ENullAsValue(defaultValue, innerSchema) ->
                        let innerCodec = loop innerSchema

                        {
                            Encode = innerCodec.Encode
                            Decode =
                                (fun src tag ->
                                    let current = Runtime.expectOpenTag tag src
                                    let current = Runtime.skipWhitespace current

                                    match Runtime.tryReadCloseTag tag current with
                                    | Some next -> struct (defaultValue, next)
                                    | None ->
                                        let struct (value, next) = innerCodec.Decode src tag
                                        struct (value, next))
                            MissingValue = innerCodec.MissingValue
                        }
                    | EEmptyCollectionAsValue(defaultValue, innerSchema) ->
                        let innerCodec = loop innerSchema

                        {
                            Encode = innerCodec.Encode
                            Decode =
                                (fun src tag ->
                                    let struct (value, next) = innerCodec.Decode src tag

                                    if Core.isEmptyCollectionValue value then
                                        struct (defaultValue, next)
                                    else
                                        struct (value, next))
                            MissingValue = innerCodec.MissingValue
                        }
                    | EEmptyStringAsNone innerSchema ->
                        let innerCodec = loop innerSchema
                        let optionType = schema.TargetType
                        let noneValue = Runtime.makeOptionNone optionType

                        {
                            Encode = innerCodec.Encode
                            Decode =
                                (fun src tag ->
                                    let struct (value, next) = innerCodec.Decode src tag

                                    if isNull value then
                                        struct (value, next)
                                    else
                                        let caseInfo, fields = FSharpValue.GetUnionFields(value, optionType)

                                        if
                                            caseInfo.Name = "Some"
                                            && fields.Length = 1
                                            && fields[0] :? string
                                            && unbox<string> fields[0] = ""
                                        then
                                            struct (noneValue, next)
                                        else
                                            struct (value, next))
                            MissingValue = innerCodec.MissingValue
                        }
                    | ERecord(t, recordRuntime) ->
                        let fields = recordRuntime.Fields

                        let compiledFields =
                            fields
                            |> List.toArray
                            |> Array.map (fun f -> {|
                                Name = f.Name
                                Codec = loop f.Codec
                                GetValue = f.GetObj
                            |})

                        {
                            Encode =
                                (fun w tag vObj ->
                                    w.WriteByte(byte '<')
                                    w.WriteString(tag)
                                    w.WriteByte(byte '>')

                                    for f in compiledFields do
                                        f.Codec.Encode w f.Name (f.GetValue vObj)

                                    w.WriteByte(byte '<')
                                    w.WriteByte(byte '/')
                                    w.WriteString(tag)
                                    w.WriteByte(byte '>'))
                            Decode =
                                (fun src tag ->
                                    let mutable current = Runtime.expectOpenTag tag src
                                    let recordState = recordRuntime.CreateState()

                                    try
                                        for index, f in compiledFields |> Array.indexed do
                                            current <- Runtime.skipWhitespace current

                                            let value =
                                                match Runtime.tryReadCloseTag tag current with
                                                | Some _ ->
                                                    match f.Codec.MissingValue with
                                                    | Some missingValue -> missingValue
                                                    | None ->
                                                        Runtime.withPath (Element f.Name) (fun () ->
                                                            Runtime.decodeFailure (sprintf "Expected <%s>" f.Name))
                                                | None ->
                                                    let struct (decoded, next) =
                                                        Runtime.withPath (Element f.Name) (fun () ->
                                                            f.Codec.Decode current f.Name)

                                                    current <- next
                                                    decoded

                                            recordRuntime.StoreField(recordState, index, value)

                                        current <- Runtime.expectCloseTag tag current
                                        struct (recordRuntime.Complete recordState, current)
                                    finally
                                        recordRuntime.Release recordState)
                            MissingValue = None
                        }
                    | EUnion(discriminatorName, valueName, cases) ->
                        let compiledCases =
                            cases
                            |> List.toArray
                            |> Array.map (fun case -> {|
                                Case = case
                                Codec = case.Codec |> Option.map loop
                            |})

                        let stringCodec = loop (RuntimeSchema.toRuntime Schema.string)

                        {
                            Encode =
                                (fun writer tag value ->
                                    match
                                        compiledCases
                                        |> Array.tryPick (fun compiled ->
                                            compiled.Case.TryGetValueObj value
                                            |> Option.map (fun fieldValue -> compiled, fieldValue))
                                    with
                                    | Some(compiled, fieldValue) ->
                                        writer.WriteByte(byte '<')
                                        writer.WriteString(tag)
                                        writer.WriteByte(byte '>')
                                        stringCodec.Encode writer discriminatorName (box compiled.Case.Name)

                                        match compiled.Codec with
                                        | Some codec -> codec.Encode writer valueName fieldValue
                                        | None -> ()

                                        writer.WriteByte(byte '<')
                                        writer.WriteByte(byte '/')
                                        writer.WriteString(tag)
                                        writer.WriteByte(byte '>')
                                    | None -> failwithf "No union case matched value for type %O" schema.TargetType)
                            Decode =
                                (fun src tag ->
                                    let mutable current = Runtime.expectOpenTag tag src
                                    current <- Runtime.skipWhitespace current

                                    let struct (rawCaseName, afterCase) =
                                        Runtime.withPath (Element discriminatorName) (fun () ->
                                            stringCodec.Decode current discriminatorName)

                                    let caseName = unbox<string> rawCaseName
                                    current <- Runtime.skipWhitespace afterCase

                                    match
                                        compiledCases |> Array.tryFind (fun compiled -> compiled.Case.Name = caseName)
                                    with
                                    | Some compiled ->
                                        let valueOpt, currentAfterValue =
                                            match compiled.Codec with
                                            | Some codec ->
                                                let struct (payload, next) =
                                                    Runtime.withPath (Element valueName) (fun () ->
                                                        codec.Decode current valueName)

                                                Some payload, Runtime.skipWhitespace next
                                            | None ->
                                                match Runtime.tryReadCloseTag tag current with
                                                | Some _ -> None, current
                                                | None ->
                                                    failwithf
                                                        "Union case '%s' does not accept a <%s> element"
                                                        caseName
                                                        valueName

                                        let current = Runtime.expectCloseTag tag currentAfterValue
                                        struct (compiled.Case.ConstructObj valueOpt, current)
                                    | None -> failwithf "Unknown union case '%s'" caseName)
                            MissingValue = None
                        }
                    | EInlineUnion(discriminatorName, cases) ->
                        let compiledCases =
                            cases
                            |> List.toArray
                            |> Array.map (fun case -> {|
                                Case = case
                                Codec =
                                    case.Codec
                                    |> Option.map (fun payloadSchema ->
                                        if not (RuntimeSchema.supportsInlinePayloadShape payloadSchema) then
                                            failwithf
                                                "Inline union case '%s' payload schema must be object-shaped"
                                                case.Name

                                        loop payloadSchema)
                            |})

                        let inlinePayloadTag = "payload"
                        let stringCodec = loop (RuntimeSchema.toRuntime Schema.string)

                        let encodeInlinePayload (codec: CompiledCodec) (fieldValue: obj) =
                            let writer = ResizableBuffer.Create(128)

                            try
                                codec.Encode writer inlinePayloadTag fieldValue

                                let afterOpen =
                                    Runtime.expectOpenTag inlinePayloadTag (ByteSource(writer.InternalData, 0))

                                let struct (innerBytes, _) = Runtime.sliceUntilCloseTag inlinePayloadTag afterOpen
                                Encoding.UTF8.GetString(innerBytes)
                            finally
                                writer.Release()

                        let decodeInlinePayload (codec: CompiledCodec) (contentBytes: byte[]) =
                            let openTag = "<" + inlinePayloadTag + ">"
                            let closeTag = "</" + inlinePayloadTag + ">"
                            let content = Encoding.UTF8.GetString(contentBytes)
                            let wrapped = openTag + content + closeTag
                            let wrappedBytes = Encoding.UTF8.GetBytes(wrapped)

                            let struct (fieldValue, rest) =
                                codec.Decode (ByteSource(wrappedBytes, 0)) inlinePayloadTag

                            let rest = Runtime.skipWhitespace rest

                            if rest.Offset <> wrappedBytes.Length then
                                failwith "Inline union payload had trailing XML content"

                            fieldValue

                        {
                            Encode =
                                (fun writer tag value ->
                                    match
                                        compiledCases
                                        |> Array.tryPick (fun compiled ->
                                            compiled.Case.TryGetValueObj value
                                            |> Option.map (fun fieldValue -> compiled, fieldValue))
                                    with
                                    | Some(compiled, fieldValue) ->
                                        writer.WriteByte(byte '<')
                                        writer.WriteString(tag)
                                        writer.WriteByte(byte '>')
                                        stringCodec.Encode writer discriminatorName (box compiled.Case.Name)

                                        match compiled.Codec with
                                        | Some codec ->
                                            let innerXml = encodeInlinePayload codec fieldValue
                                            writer.WriteString(innerXml)
                                        | None -> ()

                                        writer.WriteByte(byte '<')
                                        writer.WriteByte(byte '/')
                                        writer.WriteString(tag)
                                        writer.WriteByte(byte '>')
                                    | None -> failwithf "No union case matched value for type %O" schema.TargetType)
                            Decode =
                                (fun src tag ->
                                    let mutable current = Runtime.expectOpenTag tag src
                                    current <- Runtime.skipWhitespace current

                                    let struct (rawCaseName, afterCase) =
                                        Runtime.withPath (Element discriminatorName) (fun () ->
                                            stringCodec.Decode current discriminatorName)

                                    let caseName = unbox<string> rawCaseName
                                    current <- Runtime.skipWhitespace afterCase

                                    match
                                        compiledCases |> Array.tryFind (fun compiled -> compiled.Case.Name = caseName)
                                    with
                                    | Some compiled ->
                                        match compiled.Codec with
                                        | None ->
                                            match Runtime.tryReadCloseTag tag current with
                                            | Some next -> struct (compiled.Case.ConstructObj None, next)
                                            | None ->
                                                failwithf
                                                    "Union case '%s' does not accept payload elements alongside <%s>"
                                                    caseName
                                                    discriminatorName
                                        | Some codec ->
                                            let struct (payloadBytes, next) = Runtime.sliceUntilCloseTag tag current
                                            let fieldValue = decodeInlinePayload codec payloadBytes
                                            struct (compiled.Case.ConstructObj(Some fieldValue), next)
                                    | None -> failwithf "Unknown union case '%s'" caseName)
                            MissingValue = None
                        }
                    | EDelay factory -> loop (factory ())
                    | EList innerSchema ->
                        let innerCodec = loop innerSchema

                        {
                            Encode =
                                (fun w tag vObj ->
                                    let list = vObj :?> System.Collections.IEnumerable

                                    w.WriteByte(byte '<')
                                    w.WriteString(tag)
                                    w.WriteByte(byte '>')

                                    for item in list do
                                        innerCodec.Encode w "item" item

                                    w.WriteByte(byte '<')
                                    w.WriteByte(byte '/')
                                    w.WriteString(tag)
                                    w.WriteByte(byte '>'))
                            Decode =
                                (fun src tag ->
                                    let mutable current = Runtime.expectOpenTag tag src
                                    let results = ResizeArray<obj>()
                                    let mutable continueLoop = true
                                    let mutable index = 0

                                    while continueLoop do
                                        current <- Runtime.skipWhitespace current

                                        match Runtime.tryReadCloseTag tag current with
                                        | Some next ->
                                            current <- next
                                            continueLoop <- false
                                        | None ->
                                            let struct (item, next) =
                                                Runtime.withPath (Item index) (fun () ->
                                                    innerCodec.Decode current "item")

                                            results.Add(item)
                                            current <- next
                                            index <- index + 1

                                    struct (JsonBackend.Runtime.makeList innerSchema.TargetType (results.ToArray()), current))
                            MissingValue = None
                        }
                    | EArray innerSchema ->
                        let innerCodec = loop innerSchema

                        {
                            Encode =
                                (fun w tag vObj ->
                                    w.WriteByte(byte '<')
                                    w.WriteString(tag)
                                    w.WriteByte(byte '>')

                                    for item in (vObj :?> System.Collections.IEnumerable) do
                                        innerCodec.Encode w "item" item

                                    w.WriteByte(byte '<')
                                    w.WriteByte(byte '/')
                                    w.WriteString(tag)
                                    w.WriteByte(byte '>'))
                            Decode =
                                (fun src tag ->
                                    let mutable current = Runtime.expectOpenTag tag src
                                    let results = ResizeArray<obj>()
                                    let mutable continueLoop = true
                                    let mutable index = 0

                                    while continueLoop do
                                        current <- Runtime.skipWhitespace current

                                        match Runtime.tryReadCloseTag tag current with
                                        | Some next ->
                                            current <- next
                                            continueLoop <- false
                                        | None ->
                                            let struct (item, next) =
                                                Runtime.withPath (Item index) (fun () ->
                                                    innerCodec.Decode current "item")

                                            results.Add(item)
                                            current <- next
                                            index <- index + 1

#if !FABLE_COMPILER
                                    let targetArray =
                                        System.Array.CreateInstance(innerSchema.TargetType, results.Count)

                                    for i in 0 .. results.Count - 1 do
                                        targetArray.SetValue(results[i], i)

                                    struct (box targetArray, current)
#else
                                    struct (box (results.ToArray()), current)
#endif
                                )
                            MissingValue = None
                        }
                    | EMap(inner, wrap, unwrapFunc) ->
                        let innerCodec = loop inner

                        {
                            Encode = (fun w tag v -> innerCodec.Encode w tag (unwrapFunc v))
                            Decode =
                                (fun src tag ->
                                    let struct (v, s) = innerCodec.Decode src tag
                                    struct (Runtime.withValidationContext (fun () -> wrap v), s))
                            MissingValue = innerCodec.MissingValue |> Option.map wrap
                        }
                    | _ -> failwithf "Unsupported XML schema type"

                encodeImpl <- compiled.Encode
                decodeImpl <- compiled.Decode
                missingValueImpl <- compiled.MissingValue

                let finalized = {
                    Encode = (fun writer tag value -> encodeImpl writer tag value)
                    Decode = (fun source tag -> decodeImpl source tag)
                    MissingValue = missingValueImpl
                }

                cache[schema] <- finalized
                finalized

        loop rootSchema

    /// Compiles a contract into a reusable XML codec.
    let compile (schema: CodecMapper.Codec<'T>) : Codec<'T> =
        let compiled = compileUntyped (RuntimeSchema.toRuntime schema)

        let targetType = (schema :> CodecMapper.ICodecInfo).TargetType

        let rootTag =
            if targetType = typeof<int> then "int"
            elif targetType = typeof<int64> then "int64"
            elif targetType = typeof<uint32> then "uint32"
            elif targetType = typeof<uint64> then "uint64"
            elif targetType = typeof<float> then "float"
            elif targetType = typeof<decimal> then "decimal"
            elif targetType = typeof<string> then "string"
            elif targetType = typeof<bool> then "bool"
            else targetType.Name.ToLowerInvariant()

        {
            Encode = (fun w v -> compiled.Encode w rootTag (box v))
            Decode =
                (fun src ->
                    try
                        let struct (v, s) = compiled.Decode src rootTag
                        struct (unbox v, s)
                    with ex ->
                        match ex with
                        | :? XmlDecodeException -> raise ex
                        | _ -> Runtime.decodeFailure ex.Message)
        }

    ///
    /// Inline schema pipelines read more clearly when the final `build` and
    /// XML compile step collapse into one terminal pipeline stage.
    let inline buildAndCompile
        (builder: SchemaBuilder<'T, 'Ctor, 'T, 'Chain>)
        : Codec<'T>
        when 'Chain :> IChainNode<'T, 'Ctor, 'T> =
        builder |> Schema.build |> compile

    ///
    /// `codec` mirrors `Json.codec` for callers that still prefer the direct
    /// schema-to-codec alias over the longer `compile` name.
    let codec (schema: CodecMapper.Codec<'T>) : Codec<'T> = compile schema

    /// Serializes a value to XML using the schema-derived root element name.
    let serialize (codec: Codec<'T>) (value: 'T) =
        let writer = ResizableBuffer.Create(128)

        try
            codec.Encode writer value
            Encoding.UTF8.GetString(writer.InternalData, 0, writer.InternalCount)
        finally
            writer.Release()

    /// Deserializes an XML payload using the schema-derived root element name.
    let deserialize (codec: Codec<'T>) (xml: string) =
        let bytes = Encoding.UTF8.GetBytes(xml)
        let struct (v, rest) = codec.Decode(ByteSource(bytes, 0))
        let rest = Runtime.skipWhitespace rest

        if rest.Offset <> bytes.Length then
            Runtime.decodeFailure "Trailing content after top-level XML value"

        v

    /// Deserializes a UTF-8 byte payload using a previously compiled XML codec.
    let deserializeBytes (codec: Codec<'T>) (bytes: byte[]) =
        let struct (v, rest) = codec.Decode(ByteSource(bytes, 0))
        let rest = Runtime.skipWhitespace rest

        if rest.Offset <> bytes.Length then
            Runtime.decodeFailure "Trailing content after top-level XML value"

        v
