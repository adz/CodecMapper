namespace CodecMapper

open System.Text
open System.Collections.Generic
open System.Globalization
open Microsoft.FSharp.Reflection

/// XML codec compilation and runtime helpers.
///
/// The XML backend intentionally supports a smaller explicit subset than the
/// JSON backend: element content only, repeated `<item>` nodes for
/// collections, and ignorable inter-element whitespace.
module Xml =
    /// The byte-level input state for XML decoding.
    type XmlSource = ByteSource

    /// The byte-level output abstraction used by XML encoders.
    type XmlWriter = IByteWriter

    /// A compiled XML codec for a specific schema.
    type Codec<'T> = {
        Encode: XmlWriter -> 'T -> unit
        Decode: XmlSource -> struct ('T * XmlSource)
    }

    type CompiledCodec = {
        Encode: XmlWriter -> string -> obj -> unit
        Decode: XmlSource -> string -> struct (obj * XmlSource)
        MissingValue: obj option
    }

    module internal Runtime =
        let inline skipWhitespace (src: XmlSource) =
            let mutable i = src.Offset
            let data = src.Data

            while i < data.Length
                  && (data[i] = 32uy || data[i] = 10uy || data[i] = 13uy || data[i] = 9uy) do
                i <- i + 1

            ByteSource(data, i)

        ///
        /// The XML surface is intentionally small: element tags and escaped
        /// text nodes, with no attributes or mixed content.
        let expectOpenTag (tag: string) (src: XmlSource) =
            let src = skipWhitespace src
            let data = src.Data

            if src.Offset >= data.Length || data[src.Offset] <> 60uy then
                failwith "Expected <"

            let mutable i = src.Offset + 1

            if i < data.Length && data[i] = 47uy then
                failwithf "Expected <%s>" tag

            let start = i

            while i < data.Length && data[i] <> 62uy do
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
                || data[src.Offset] <> 60uy
                || data[src.Offset + 1] <> 47uy
            then
                failwithf "Expected </%s>" tag

            let mutable i = src.Offset + 2
            let start = i

            while i < data.Length && data[i] <> 62uy do
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
                || data[src.Offset] <> 60uy
                || data[src.Offset + 1] <> 47uy
            then
                None
            else
                let mutable i = src.Offset + 2
                let start = i

                while i < data.Length && data[i] <> 62uy do
                    i <- i + 1

                if i >= data.Length then
                    failwith "Unterminated tag"

#if !FABLE_COMPILER
                let actual = Encoding.UTF8.GetString(data, start, i - start)
#else
                let actual = Encoding.UTF8.GetString(data.[start .. i - 1])
#endif

                if actual = tag then Some(ByteSource(data, i + 1)) else None

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

            while i < data.Length && data[i] <> 60uy do
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

    let rec compileUntyped (schema: ISchema) : CompiledCodec =
        match schema.Definition with
        | Primitive t when t = typeof<int> -> {
            Encode =
                (fun w tag v ->
                    w.WriteByte(60uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy)
                    w.WriteInt(unbox v)
                    w.WriteByte(60uy)
                    w.WriteByte(47uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy))
            Decode =
                (fun src tag ->
                    let current = Runtime.expectOpenTag tag src
                    let struct (text, current) = Runtime.readTextNode current
                    let current = Runtime.expectCloseTag tag current
                    let v = Core.parseInt32Invariant "int" (text.Trim())
                    struct (box v, current))
            MissingValue = None
          }
        | Primitive t when t = typeof<int64> -> {
            Encode =
                (fun w tag v ->
                    w.WriteByte(60uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy)
                    w.WriteString((unbox<int64> v).ToString(CultureInfo.InvariantCulture))
                    w.WriteByte(60uy)
                    w.WriteByte(47uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy))
            Decode =
                (fun src tag ->
                    let current = Runtime.expectOpenTag tag src
                    let struct (text, current) = Runtime.readTextNode current
                    let current = Runtime.expectCloseTag tag current

                    let value = Core.parseInt64Invariant "int64" (text.Trim())

                    struct (box value, current))
            MissingValue = None
          }
        | Primitive t when t = typeof<uint32> -> {
            Encode =
                (fun w tag v ->
                    w.WriteByte(60uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy)
                    w.WriteString((unbox<uint32> v).ToString(CultureInfo.InvariantCulture))
                    w.WriteByte(60uy)
                    w.WriteByte(47uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy))
            Decode =
                (fun src tag ->
                    let current = Runtime.expectOpenTag tag src
                    let struct (text, current) = Runtime.readTextNode current
                    let current = Runtime.expectCloseTag tag current

                    let value = Core.parseUInt32Invariant "uint32" (text.Trim())

                    struct (box value, current))
            MissingValue = None
          }
        | Primitive t when t = typeof<uint64> -> {
            Encode =
                (fun w tag v ->
                    w.WriteByte(60uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy)
                    w.WriteString((unbox<uint64> v).ToString(CultureInfo.InvariantCulture))
                    w.WriteByte(60uy)
                    w.WriteByte(47uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy))
            Decode =
                (fun src tag ->
                    let current = Runtime.expectOpenTag tag src
                    let struct (text, current) = Runtime.readTextNode current
                    let current = Runtime.expectCloseTag tag current

                    let value = Core.parseUInt64Invariant "uint64" (text.Trim())

                    struct (box value, current))
            MissingValue = None
          }
        | Primitive t when t = typeof<float> -> {
            Encode =
                (fun w tag v ->
                    w.WriteByte(60uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy)
                    w.WriteString(Schema.formatFloat (unbox<float> v))
                    w.WriteByte(60uy)
                    w.WriteByte(47uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy))
            Decode =
                (fun src tag ->
                    let current = Runtime.expectOpenTag tag src
                    let struct (text, current) = Runtime.readTextNode current
                    let current = Runtime.expectCloseTag tag current

                    let value = Core.parseFloatInvariant "float" (text.Trim())

                    struct (box value, current))
            MissingValue = None
          }
        | Primitive t when t = typeof<decimal> -> {
            Encode =
                (fun w tag v ->
                    w.WriteByte(60uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy)
                    w.WriteString((unbox<decimal> v).ToString(CultureInfo.InvariantCulture))
                    w.WriteByte(60uy)
                    w.WriteByte(47uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy))
            Decode =
                (fun src tag ->
                    let current = Runtime.expectOpenTag tag src
                    let struct (text, current) = Runtime.readTextNode current
                    let current = Runtime.expectCloseTag tag current

                    let value = Core.parseDecimalInvariant "decimal" (text.Trim())

                    struct (box value, current))
            MissingValue = None
          }
        | Primitive t when t = typeof<string> -> {
            Encode =
                (fun w tag v ->
                    w.WriteByte(60uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy)
                    w.WriteString(Runtime.escapeText (unbox v))
                    w.WriteByte(60uy)
                    w.WriteByte(47uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy))
            Decode =
                (fun src tag ->
                    let current = Runtime.expectOpenTag tag src
                    let struct (v, current) = Runtime.readTextNode current
                    let current = Runtime.expectCloseTag tag current
                    struct (box v, current))
            MissingValue = None
          }
        | Primitive t when t = typeof<bool> -> {
            Encode =
                (fun w tag v ->
                    w.WriteByte(60uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy)
                    w.WriteString(if unbox<bool> v then "true" else "false")
                    w.WriteByte(60uy)
                    w.WriteByte(47uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy))
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
        | RawJsonValue ->
            let fail () =
                failwith "Schema.jsonValue is JSON-only; XML has no symmetric raw JSON DOM representation"

            {
                Encode = (fun _ _ _ -> fail ())
                Decode = (fun _ _ -> fail ())
                MissingValue = None
            }
        | Option innerSchema ->
            let innerCodec = compileUntyped innerSchema
            let optionType = schema.TargetType

            {
                Encode =
                    (fun w tag v ->
                        w.WriteByte(60uy)
                        w.WriteString(tag)
                        w.WriteByte(62uy)

                        if not (isNull v) then
                            innerCodec.Encode
                                w
                                "some"
                                (FSharpValue.GetUnionFields(v, optionType) |> snd |> Array.item 0)

                        w.WriteByte(60uy)
                        w.WriteByte(47uy)
                        w.WriteString(tag)
                        w.WriteByte(62uy))
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
        | MissingAsNone innerSchema ->
            let innerCodec = compileUntyped innerSchema
            let optionType = schema.TargetType

            {
                Encode = innerCodec.Encode
                Decode = innerCodec.Decode
                MissingValue = Some(Runtime.makeOptionNone optionType)
            }
        | MissingAsValue(defaultValue, innerSchema) ->
            let innerCodec = compileUntyped innerSchema

            {
                Encode = innerCodec.Encode
                Decode = innerCodec.Decode
                MissingValue = Some defaultValue
            }
        | NullAsValue(defaultValue, innerSchema) ->
            let innerCodec = compileUntyped innerSchema

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
        | EmptyCollectionAsValue(defaultValue, innerSchema) ->
            let innerCodec = compileUntyped innerSchema

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
        | EmptyStringAsNone innerSchema ->
            let innerCodec = compileUntyped innerSchema
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
        | Record(t, fields, ctor) ->
            let compiledFields =
                fields
                |> Array.map (fun f -> {|
                    Name = f.Name
                    Codec = compileUntyped f.Schema
                    GetValue = f.GetValue
                |})

            {
                Encode =
                    (fun w tag vObj ->
                        w.WriteByte(60uy)
                        w.WriteString(tag)
                        w.WriteByte(62uy)

                        for f in compiledFields do
                            f.Codec.Encode w f.Name (f.GetValue vObj)

                        w.WriteByte(60uy)
                        w.WriteByte(47uy)
                        w.WriteString(tag)
                        w.WriteByte(62uy))
                Decode =
                    (fun src tag ->
                        let mutable current = Runtime.expectOpenTag tag src

                        let args =
                            compiledFields
                            |> Array.map (fun f ->
                                current <- Runtime.skipWhitespace current

                                match Runtime.tryReadCloseTag tag current with
                                | Some _ ->
                                    match f.Codec.MissingValue with
                                    | Some value -> value
                                    | None -> failwithf "Expected <%s>" f.Name
                                | None ->
                                    let struct (v, next) = f.Codec.Decode current f.Name
                                    current <- next
                                    v)

                        current <- Runtime.expectCloseTag tag current
                        struct (ctor args, current))
                MissingValue = None
            }
        | List innerSchema ->
            let innerCodec = compileUntyped innerSchema

            {
                Encode =
                    (fun w tag vObj ->
                        let list = vObj :?> System.Collections.IEnumerable

                        w.WriteByte(60uy)
                        w.WriteString(tag)
                        w.WriteByte(62uy)

                        for item in list do
                            innerCodec.Encode w "item" item

                        w.WriteByte(60uy)
                        w.WriteByte(47uy)
                        w.WriteString(tag)
                        w.WriteByte(62uy))
                Decode =
                    (fun src tag ->
                        let mutable current = Runtime.expectOpenTag tag src
                        let results = ResizeArray<obj>()
                        let mutable continueLoop = true

                        while continueLoop do
                            current <- Runtime.skipWhitespace current

                            match Runtime.tryReadCloseTag tag current with
                            | Some next ->
                                current <- next
                                continueLoop <- false
                            | None ->
                                let struct (item, next) = innerCodec.Decode current "item"
                                results.Add(item)
                                current <- next

                        struct (Json.Runtime.makeList innerSchema.TargetType (results.ToArray()), current))
                MissingValue = None
            }
        | Array innerSchema ->
            let innerCodec = compileUntyped innerSchema

            {
                Encode =
                    (fun w tag vObj ->
                        w.WriteByte(60uy)
                        w.WriteString(tag)
                        w.WriteByte(62uy)

                        for item in (vObj :?> System.Collections.IEnumerable) do
                            innerCodec.Encode w "item" item

                        w.WriteByte(60uy)
                        w.WriteByte(47uy)
                        w.WriteString(tag)
                        w.WriteByte(62uy))
                Decode =
                    (fun src tag ->
                        let mutable current = Runtime.expectOpenTag tag src
                        let results = ResizeArray<obj>()
                        let mutable continueLoop = true

                        while continueLoop do
                            current <- Runtime.skipWhitespace current

                            match Runtime.tryReadCloseTag tag current with
                            | Some next ->
                                current <- next
                                continueLoop <- false
                            | None ->
                                let struct (item, next) = innerCodec.Decode current "item"
                                results.Add(item)
                                current <- next

#if !FABLE_COMPILER
                        let targetArray = System.Array.CreateInstance(innerSchema.TargetType, results.Count)

                        for i in 0 .. results.Count - 1 do
                            targetArray.SetValue(results[i], i)

                        struct (box targetArray, current)
#else
                        struct (box (results.ToArray()), current)
#endif
                    )
                MissingValue = None
            }
        | Map(inner, wrap, unwrapFunc) ->
            let innerCodec = compileUntyped inner

            {
                Encode = (fun w tag v -> innerCodec.Encode w tag (unwrapFunc v))
                Decode = (fun src tag -> let struct (v, s) = innerCodec.Decode src tag in struct (wrap v, s))
                MissingValue = innerCodec.MissingValue |> Option.map wrap
            }
        | _ -> failwithf "Unsupported XML schema type"

    /// Compiles a schema into a reusable XML codec.
    let compile (schema: Schema<'T>) : Codec<'T> =
        let compiled = compileUntyped (schema :> ISchema)

        let rootTag =
            if schema.TargetType = typeof<int> then "int"
            elif schema.TargetType = typeof<int64> then "int64"
            elif schema.TargetType = typeof<uint32> then "uint32"
            elif schema.TargetType = typeof<uint64> then "uint64"
            elif schema.TargetType = typeof<float> then "float"
            elif schema.TargetType = typeof<decimal> then "decimal"
            elif schema.TargetType = typeof<string> then "string"
            elif schema.TargetType = typeof<bool> then "bool"
            else schema.TargetType.Name.ToLowerInvariant()

        {
            Encode = (fun w v -> compiled.Encode w rootTag (box v))
            Decode = (fun src -> let struct (v, s) = compiled.Decode src rootTag in struct (unbox v, s))
        }

    /// Serializes a value to XML using the schema-derived root element name.
    let serialize (codec: Codec<'T>) (value: 'T) =
        let writer = ResizableBuffer.Create(128)
        codec.Encode writer value
        Encoding.UTF8.GetString(writer.InternalData, 0, writer.InternalCount)

    /// Deserializes an XML payload using the schema-derived root element name.
    let deserialize (codec: Codec<'T>) (xml: string) =
        let bytes = Encoding.UTF8.GetBytes(xml)
        let struct (v, rest) = codec.Decode(ByteSource(bytes, 0))
        let rest = Runtime.skipWhitespace rest

        if rest.Offset <> bytes.Length then
            failwith "Trailing content after top-level XML value"

        v

    /// Deserializes a UTF-8 byte payload using a previously compiled XML codec.
    let deserializeBytes (codec: Codec<'T>) (bytes: byte[]) =
        let struct (v, rest) = codec.Decode(ByteSource(bytes, 0))
        let rest = Runtime.skipWhitespace rest

        if rest.Offset <> bytes.Length then
            failwith "Trailing content after top-level XML value"

        v
