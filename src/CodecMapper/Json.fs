namespace CodecMapper

open System.Text
open System.Collections.Generic
open System.Globalization
#if !FABLE_COMPILER
open System.Collections.Concurrent
#endif
open Microsoft.FSharp.Reflection

/// JSON codec compilation and runtime helpers.
///
/// Compile a schema once, then reuse the resulting codec for repeated JSON
/// serialization and deserialization.
module Json =
    /// The byte-level input state for JSON decoding.
    type JsonSource = ByteSource

    /// The byte-level output abstraction used by JSON encoders.
    type JsonWriter = IByteWriter

    /// Decoder shape used by the compiled JSON runtime.
    type Decoder<'T> = JsonSource -> struct ('T * JsonSource)

    /// A compiled JSON codec for a specific schema.
    type Codec<'T> = {
        Encode: IByteWriter -> 'T -> unit
        Decode: Decoder<'T>
    }

    ///
    /// Tiny payloads fit comfortably in the old `128`-byte buffer, but the
    /// benchmark runner now measures batches of `100` records where repeated
    /// growth and copy steps dominate allocation churn. Starting larger keeps
    /// the hot JSON path closer to realistic message sizes.
    let private defaultSerializeBufferCapacity = 4096

    type internal DecodePathSegment =
        | Property of string
        | Index of int

    type internal JsonDecodeException(path: DecodePathSegment list, detail: string, ?inner: exn) =
        inherit System.Exception(detail, defaultArg inner null)

        member _.Path = path
        member _.Detail = detail

        override _.Message =
            let renderPath segments =
                let builder = StringBuilder("$")

                for segment in segments do
                    match segment with
                    | Property name ->
                        builder.Append('.') |> ignore
                        builder.Append(name) |> ignore
                    | Index index ->
                        builder.Append('[') |> ignore
                        builder.Append(index) |> ignore
                        builder.Append(']') |> ignore

                builder.ToString()

            sprintf "JSON decode error at %s: %s" (renderPath path) detail

    module internal Runtime =
#if !FABLE_COMPILER
        let private objectArrayPools = ConcurrentDictionary<int, ConcurrentBag<obj array>>()
#else
        let private objectArrayPools = Dictionary<int, ResizeArray<obj array>>()
#endif

        let private asDecodeException detail path inner =
            JsonDecodeException(path, detail, inner) :> exn

        let decodeFailure detail =
            raise (asDecodeException detail [] null)

        let private prependPath segment (ex: exn) =
            match ex with
            | :? JsonDecodeException as decodeEx -> asDecodeException decodeEx.Detail (segment :: decodeEx.Path) ex
            | _ -> asDecodeException ex.Message [ segment ] ex

        let withPath segment f =
            try
                f ()
            with ex ->
                raise (prependPath segment ex)

        ///
        /// The hot decode path already knows the field or item segment up
        /// front, so accepting the decoder directly avoids closure allocation
        /// around every successful nested decode.
        let inline decodeAtPath segment (decoder: JsonSource -> struct ('T * JsonSource)) (src: JsonSource) =
            try
                decoder src
            with ex ->
                raise (prependPath segment ex)

        let withValidationContext f =
            try
                f ()
            with
            | :? JsonDecodeException -> reraise ()
            | ex -> raise (asDecodeException ("Validation failed: " + ex.Message) [] ex)

        let inline skipWhitespace (src: JsonSource) =
            let mutable i = src.Offset
            let data = src.Data

            while i < data.Length
                  && (data[i] = 32uy || data[i] = 10uy || data[i] = 13uy || data[i] = 9uy) do
                i <- i + 1

            ByteSource(data, i)

        let inline isDigit (b: byte) = b >= 48uy && b <= 57uy

        let numberToken (allowFractionAndExponent: bool) (src: JsonSource) =
            let src = skipWhitespace src

            if src.Offset >= src.Data.Length then
                failwith "Unexpected end of input"

            let data = src.Data
            let mutable i = src.Offset

            if data[i] = 45uy then
                i <- i + 1

            if i >= data.Length then
                failwith "Expected digit"

            if data[i] = 48uy then
                i <- i + 1

                if i < data.Length && isDigit data[i] then
                    failwith "Leading zeroes are not allowed"
            elif isDigit data[i] then
                while i < data.Length && isDigit data[i] do
                    i <- i + 1
            else
                failwith "Expected digit"

            if allowFractionAndExponent && i < data.Length && data[i] = 46uy then
                i <- i + 1

                if i >= data.Length || not (isDigit data[i]) then
                    failwith "Expected digit"

                while i < data.Length && isDigit data[i] do
                    i <- i + 1

            if
                allowFractionAndExponent
                && i < data.Length
                && (data[i] = 101uy || data[i] = 69uy)
            then
                i <- i + 1

                if i < data.Length && (data[i] = 43uy || data[i] = 45uy) then
                    i <- i + 1

                if i >= data.Length || not (isDigit data[i]) then
                    failwith "Expected digit"

                while i < data.Length && isDigit data[i] do
                    i <- i + 1

            struct (src.Offset, i - src.Offset, ByteSource(data, i))

        let intDecoder: Decoder<int> =
            fun src ->
                let struct (start, length, next) = numberToken false src
#if !FABLE_COMPILER
                struct (Core.parseInt32InvariantBytes "int" src.Data start length, next)
#else
                let token = Encoding.UTF8.GetString(src.Data.[start .. start + length - 1])
                struct (Core.parseInt32Invariant "int" token, next)
#endif

        let int64Decoder: Decoder<int64> =
            fun src ->
                let struct (start, length, next) = numberToken false src
#if !FABLE_COMPILER
                struct (Core.parseInt64InvariantBytes "int64" src.Data start length, next)
#else
                let token = Encoding.UTF8.GetString(src.Data.[start .. start + length - 1])
                struct (Core.parseInt64Invariant "int64" token, next)
#endif

        let uint32Decoder: Decoder<uint32> =
            fun src ->
                let struct (start, length, next) = numberToken false src
#if !FABLE_COMPILER
                struct (Core.parseUInt32InvariantBytes "uint32" src.Data start length, next)
#else
                let token = Encoding.UTF8.GetString(src.Data.[start .. start + length - 1])
                struct (Core.parseUInt32Invariant "uint32" token, next)
#endif

        let uint64Decoder: Decoder<uint64> =
            fun src ->
                let struct (start, length, next) = numberToken false src
#if !FABLE_COMPILER
                struct (Core.parseUInt64InvariantBytes "uint64" src.Data start length, next)
#else
                let token = Encoding.UTF8.GetString(src.Data.[start .. start + length - 1])
                struct (Core.parseUInt64Invariant "uint64" token, next)
#endif

        let floatDecoder: Decoder<float> =
            fun src ->
                let struct (start, length, next) = numberToken true src
#if !FABLE_COMPILER
                struct (Core.parseFloatInvariantBytes "float" src.Data start length, next)
#else
                let token = Encoding.UTF8.GetString(src.Data.[start .. start + length - 1])
                struct (Core.parseFloatInvariant "float" token, next)
#endif

        let decimalDecoder: Decoder<decimal> =
            fun src ->
                let struct (start, length, next) = numberToken true src
#if !FABLE_COMPILER
                struct (Core.parseDecimalInvariantBytes "decimal" src.Data start length, next)
#else
                let token = Encoding.UTF8.GetString(src.Data.[start .. start + length - 1])
                struct (Core.parseDecimalInvariant "decimal" token, next)
#endif

        let boolDecoder: Decoder<bool> =
            fun src ->
                let src = skipWhitespace src
                let data = src.Data

                if src.Offset >= data.Length then
                    failwith "Unexpected end of input"

                let remaining = data.Length - src.Offset

                if
                    remaining >= 4
                    && data[src.Offset] = 116uy
                    && data[src.Offset + 1] = 114uy
                    && data[src.Offset + 2] = 117uy
                    && data[src.Offset + 3] = 101uy
                then
                    struct (true, ByteSource(data, src.Offset + 4))
                elif
                    remaining >= 5
                    && data[src.Offset] = 102uy
                    && data[src.Offset + 1] = 97uy
                    && data[src.Offset + 2] = 108uy
                    && data[src.Offset + 3] = 115uy
                    && data[src.Offset + 4] = 101uy
                then
                    struct (false, ByteSource(data, src.Offset + 5))
                else
                    failwith "Expected true or false"

        let nullDecoder (src: JsonSource) =
            let src = skipWhitespace src
            let data = src.Data

            if
                src.Offset + 3 < data.Length
                && data[src.Offset] = 110uy
                && data[src.Offset + 1] = 117uy
                && data[src.Offset + 2] = 108uy
                && data[src.Offset + 3] = 108uy
            then
                ByteSource(data, src.Offset + 4)
            else
                failwith "Expected null"

        let stringRaw (src: JsonSource) : struct (int * int * bool * JsonSource) =
            let src = skipWhitespace src
            let data = src.Data

            if src.Offset >= data.Length || data[src.Offset] <> 34uy then
                failwith "Expected \""

            let mutable i = src.Offset + 1
            let mutable finished = false
            let mutable hadEscapes = false

            //
            // Unknown-field skipping should stay linear even for escaped text,
            // so scan forward once instead of recounting backslashes at every
            // candidate quote.
            while i < data.Length && not finished do
                match data[i] with
                | 34uy -> finished <- true
                | 92uy ->
                    hadEscapes <- true
                    i <- i + 1

                    if i >= data.Length then
                        failwith "Unterminated escape sequence"

                    if data[i] = 117uy then
                        if i + 4 >= data.Length then
                            failwith "Unterminated unicode escape"

                        i <- i + 4
                | _ -> ()

                if not finished then
                    i <- i + 1

            if not finished then
                failwith "Unterminated string"

            struct (src.Offset + 1, i - (src.Offset + 1), hadEscapes, ByteSource(data, i + 1))

        let stringDecoder: Decoder<string> =
            fun src ->
                let src = skipWhitespace src
                let data = src.Data

                if src.Offset >= data.Length || data[src.Offset] <> 34uy then
                    failwith "Expected \""

                let mutable i = src.Offset + 1
                let mutable segmentStart = i
                let mutable builder = null

                let appendSegment startIdx endIdx =
                    if endIdx > startIdx then
#if !FABLE_COMPILER
                        let segment = Encoding.UTF8.GetString(data, startIdx, endIdx - startIdx)
#else
                        let segment = Encoding.UTF8.GetString(data.[startIdx .. endIdx - 1])
#endif

                        if isNull builder then
                            builder <- StringBuilder()

                        builder.Append(segment) |> ignore

                let hexValue (b: byte) =
                    if b >= 48uy && b <= 57uy then int b - int 48uy
                    elif b >= 65uy && b <= 70uy then int b - int 65uy + 10
                    elif b >= 97uy && b <= 102uy then int b - int 97uy + 10
                    else failwith "Invalid unicode escape"

                let mutable finished = false

                while i < data.Length && not finished do
                    match data[i] with
                    | 34uy -> finished <- true
                    | 92uy ->
                        appendSegment segmentStart i
                        i <- i + 1

                        if i >= data.Length then
                            failwith "Unterminated escape sequence"

                        if isNull builder then
                            builder <- StringBuilder()

                        match data[i] with
                        | 34uy -> builder.Append('"') |> ignore
                        | 92uy -> builder.Append('\\') |> ignore
                        | 47uy -> builder.Append('/') |> ignore
                        | 98uy -> builder.Append('\b') |> ignore
                        | 102uy -> builder.Append('\f') |> ignore
                        | 110uy -> builder.Append('\n') |> ignore
                        | 114uy -> builder.Append('\r') |> ignore
                        | 116uy -> builder.Append('\t') |> ignore
                        | 117uy ->
                            if i + 4 >= data.Length then
                                failwith "Unterminated unicode escape"

                            let codePoint =
                                ((hexValue data[i + 1]) <<< 12)
                                ||| ((hexValue data[i + 2]) <<< 8)
                                ||| ((hexValue data[i + 3]) <<< 4)
                                ||| (hexValue data[i + 4])

                            builder.Append(char codePoint) |> ignore
                            i <- i + 4
                        | _ -> failwith "Invalid escape sequence"

                        i <- i + 1
                        segmentStart <- i
                    | _ -> i <- i + 1

                if not finished then
                    failwith "Unterminated string"

                let value =
                    if isNull builder then
#if !FABLE_COMPILER
                        Encoding.UTF8.GetString(data, segmentStart, i - segmentStart)
#else
                        Encoding.UTF8.GetString(data.[segmentStart .. i - 1])
#endif
                    else
                        appendSegment segmentStart i
                        builder.ToString()

                struct (value, ByteSource(data, i + 1))

        let maxJsonDepth = 256

        let rec jsonValueDecoderAt depth (src: JsonSource) : struct (JsonValue * JsonSource) =
            if depth > maxJsonDepth then
                failwith "Maximum JSON nesting depth exceeded"

            let src = skipWhitespace src

            if src.Offset >= src.Data.Length then
                failwith "Unexpected end of input"

            let data = src.Data

            match data[src.Offset] with
            | 110uy ->
                let next = nullDecoder src
                struct (JNull, next)
            | 116uy
            | 102uy ->
                let struct (value, next) = boolDecoder src
                struct (JBool value, next)
            | 34uy ->
                let struct (value, next) = stringDecoder src
                struct (JString value, next)
            | 91uy ->
                let mutable current = skipWhitespace (src.Advance(1))
                let items = ResizeArray<JsonValue>()
                let mutable looping = true

                if current.Offset < data.Length && data[current.Offset] = 93uy then
                    current <- current.Advance(1)
                    looping <- false

                while looping do
                    let struct (item, next) = jsonValueDecoderAt (depth + 1) current
                    items.Add(item)

                    let afterItem = skipWhitespace next

                    if afterItem.Offset < data.Length && data[afterItem.Offset] = 44uy then
                        current <- skipWhitespace (afterItem.Advance(1))
                    elif afterItem.Offset < data.Length && data[afterItem.Offset] = 93uy then
                        current <- afterItem.Advance(1)
                        looping <- false
                    else
                        failwith "Expected , or ]"

                struct (JArray(List.ofSeq items), current)
            | 123uy ->
                let mutable current = skipWhitespace (src.Advance(1))
                let fields = ResizeArray<string * JsonValue>()
                let mutable looping = true

                if current.Offset < data.Length && data[current.Offset] = 125uy then
                    current <- current.Advance(1)
                    looping <- false

                while looping do
                    let struct (key, afterKey) = stringDecoder current
                    let afterColon = skipWhitespace afterKey

                    if afterColon.Offset >= data.Length || data[afterColon.Offset] <> 58uy then
                        failwith "Expected :"

                    let struct (value, next) = jsonValueDecoderAt (depth + 1) (afterColon.Advance(1))
                    fields.Add(key, value)

                    let afterValue = skipWhitespace next

                    if afterValue.Offset < data.Length && data[afterValue.Offset] = 44uy then
                        current <- skipWhitespace (afterValue.Advance(1))
                    elif afterValue.Offset < data.Length && data[afterValue.Offset] = 125uy then
                        current <- afterValue.Advance(1)
                        looping <- false
                    else
                        failwith "Expected , or }"

                struct (JObject(List.ofSeq fields), current)
            | _ ->
                let struct (start, length, next) = numberToken true src
#if !FABLE_COMPILER
                let token = Encoding.UTF8.GetString(src.Data, start, length)
#else
                let token = Encoding.UTF8.GetString(src.Data.[start .. start + length - 1])
#endif
                struct (JNumber token, next)

        let jsonValueDecoder (src: JsonSource) = jsonValueDecoderAt 0 src

        let rec skipValueAt depth (src: JsonSource) : JsonSource =
            if depth > maxJsonDepth then
                failwith "Maximum JSON nesting depth exceeded"

            let src = skipWhitespace src

            if src.Offset >= src.Data.Length then
                src
            else
                let data = src.Data

                match data[src.Offset] with
                | 123uy ->
                    let mutable current = skipWhitespace (src.Advance(1))
                    let mutable continueLoop = true

                    if current.Offset < data.Length && data[current.Offset] = 125uy then
                        current <- current.Advance(1)
                        continueLoop <- false

                    while continueLoop do
                        let struct (_, _, _, afterKey) = stringRaw current
                        let afterColon = skipWhitespace afterKey

                        if afterColon.Offset >= data.Length || data[afterColon.Offset] <> 58uy then
                            failwith "Expected :"

                        let afterValue = skipWhitespace (skipValueAt (depth + 1) (afterColon.Advance(1)))

                        if afterValue.Offset < data.Length && data[afterValue.Offset] = 44uy then
                            current <- skipWhitespace (afterValue.Advance(1))
                        elif afterValue.Offset < data.Length && data[afterValue.Offset] = 125uy then
                            current <- afterValue.Advance(1)
                            continueLoop <- false
                        else
                            failwith "Expected , or }"

                    current
                | 91uy ->
                    let mutable current = skipWhitespace (src.Advance(1))
                    let mutable continueLoop = true

                    if current.Offset < data.Length && data[current.Offset] = 93uy then
                        current <- current.Advance(1)
                        continueLoop <- false

                    while continueLoop do
                        let afterItem = skipWhitespace (skipValueAt (depth + 1) current)

                        if afterItem.Offset < data.Length && data[afterItem.Offset] = 44uy then
                            current <- skipWhitespace (afterItem.Advance(1))
                        elif afterItem.Offset < data.Length && data[afterItem.Offset] = 93uy then
                            current <- afterItem.Advance(1)
                            continueLoop <- false
                        else
                            failwith "Expected , or ]"

                    current
                | 34uy ->
                    let struct (_, _, _, nextSrc) = stringRaw src
                    nextSrc
                | _ ->
                    let mutable i = src.Offset

                    while i < data.Length
                          && data[i] <> 44uy
                          && data[i] <> 125uy
                          && data[i] <> 93uy
                          && data[i] <> 32uy
                          && data[i] <> 10uy
                          && data[i] <> 13uy
                          && data[i] <> 9uy do
                        i <- i + 1

                    ByteSource(data, i)

        let skipValue (src: JsonSource) : JsonSource = skipValueAt 0 src

        let inline bytesEqual (a: byte[]) (b: byte[]) (offset: int) (len: int) =
            if a.Length <> len then
                false
            else
                let mutable i = 0
                let mutable equal = true

                while i < len && equal do
                    if a[i] <> b[offset + i] then equal <- false else i <- i + 1

                equal

#if !FABLE_COMPILER
        let private listBuilders = ConcurrentDictionary<System.Type, obj array -> obj>()
#else
        let private listBuilders = Dictionary<System.Type, obj array -> obj>()
#endif

        let makeListBuilder (elementType: System.Type) =
#if !FABLE_COMPILER
            listBuilders.GetOrAdd(
                elementType,
                System.Func<_, _>(fun elementType ->
                    let listType = typedefof<_ list>.MakeGenericType([| elementType |])
                    let emptyList = listType.GetProperty("Empty").GetValue(null)
                    let cons = listType.GetMethod("Cons")

                    fun (elements: obj array) ->
                        let mutable result = emptyList

                        for i in elements.Length - 1 .. -1 .. 0 do
                            result <- cons.Invoke(null, [| elements[i]; result |])

                        result)
            )
#else
            match listBuilders.TryGetValue(elementType) with
            | true, builder -> builder
            | false, _ ->
                let builder = fun (elements: obj array) -> List.ofArray elements |> box
                listBuilders[elementType] <- builder
                builder
#endif

        ///
        /// XML shares the same erased list-construction helper, so keep the
        /// old entrypoint as a thin wrapper over the cached builder.
        let makeList (elementType: System.Type) (elements: obj array) = makeListBuilder elementType elements

        let makeOptionNone (optionType: System.Type) =
            let noneCase =
                FSharpType.GetUnionCases(optionType) |> Array.find (fun c -> c.Name = "None")

            FSharpValue.MakeUnion(noneCase, [||])

        ///
        /// Record decode still needs temporary erased storage today, but
        /// pooling the `obj[]` buffers removes one of the largest remaining
        /// allocation sources on nested decode workloads.
        let rentObjectArray length =
#if !FABLE_COMPILER
            let pool = objectArrayPools.GetOrAdd(length, fun _ -> ConcurrentBag<obj array>())
            let mutable rented = Unchecked.defaultof<obj array>

            if pool.TryTake(&rented) then
                rented
            else
                Array.zeroCreate length
#else
            match objectArrayPools.TryGetValue(length) with
            | true, pool when pool.Count > 0 ->
                let lastIndex = pool.Count - 1
                let rented = pool[lastIndex]
                pool.RemoveAt(lastIndex)
                rented
            | _ -> Array.zeroCreate length
#endif

        ///
        /// Record field buffers may hold arbitrary user objects, so return
        /// them cleared to avoid keeping payload graphs alive across runs.
        let returnObjectArray (buffer: obj array) (usedLength: int) =
            for i in 0 .. usedLength - 1 do
                buffer[i] <- null

#if !FABLE_COMPILER
            objectArrayPools.GetOrAdd(usedLength, fun _ -> ConcurrentBag<obj array>()).Add(buffer)
#else
            let pool =
                match objectArrayPools.TryGetValue(usedLength) with
                | true, existing -> existing
                | false, _ ->
                    let created = ResizeArray<obj array>()
                    objectArrayPools[usedLength] <- created
                    created

            pool.Add(buffer)
#endif

    type CompiledCodec = {
        Encode: IByteWriter -> obj -> unit
        Decode: JsonSource -> struct (obj * JsonSource)
        MissingValue: obj option
    }

    let rec compileUntyped (schema: ISchema) : CompiledCodec =
        match schema.Definition with
        | Primitive t when t = typeof<int> -> {
            Encode = (fun w v -> w.WriteInt(unbox v))
            Decode = (fun src -> let struct (v, s) = Runtime.intDecoder src in struct (box v, s))
            MissingValue = None
          }
        | Primitive t when t = typeof<int64> -> {
            Encode =
                (fun w v ->
                    let value: int64 = unbox v
                    w.WriteInt64(value))
            Decode = (fun src -> let struct (v, s) = Runtime.int64Decoder src in struct (box v, s))
            MissingValue = None
          }
        | Primitive t when t = typeof<uint32> -> {
            Encode =
                (fun w v ->
                    let value: uint32 = unbox v
                    w.WriteUInt32(value))
            Decode = (fun src -> let struct (v, s) = Runtime.uint32Decoder src in struct (box v, s))
            MissingValue = None
          }
        | Primitive t when t = typeof<uint64> -> {
            Encode =
                (fun w v ->
                    let value: uint64 = unbox v
                    w.WriteUInt64(value))
            Decode = (fun src -> let struct (v, s) = Runtime.uint64Decoder src in struct (box v, s))
            MissingValue = None
          }
        | Primitive t when t = typeof<float> -> {
            Encode =
                (fun w v ->
                    let value: float = unbox v
                    w.WriteFloat(value))
            Decode = (fun src -> let struct (v, s) = Runtime.floatDecoder src in struct (box v, s))
            MissingValue = None
          }
        | Primitive t when t = typeof<decimal> -> {
            Encode =
                (fun w v ->
                    let value: decimal = unbox v
                    w.WriteDecimal(value))
            Decode = (fun src -> let struct (v, s) = Runtime.decimalDecoder src in struct (box v, s))
            MissingValue = None
          }
        | Primitive t when t = typeof<string> -> {
            Encode =
                (fun w v ->
                    let value: string = unbox v
                    w.WriteByte(34uy)

                    ///
                    /// JSON strings must escape structural and control
                    /// characters or round-trip behavior becomes accidental.
                    let mutable segmentStart = 0

                    let flushSegment endIdx =
                        if endIdx > segmentStart then
                            w.WriteStringSlice(value, segmentStart, endIdx - segmentStart)

                    let writeUnicodeEscape (writer: IByteWriter) (c: char) =
                        let hexDigit value =
                            if value < 10 then
                                byte (int '0' + value)
                            else
                                byte (int 'a' + value - 10)

                        let code = int c
                        writer.WriteByte(92uy)
                        writer.WriteByte(117uy)
                        writer.WriteByte(hexDigit ((code >>> 12) &&& 0xF))
                        writer.WriteByte(hexDigit ((code >>> 8) &&& 0xF))
                        writer.WriteByte(hexDigit ((code >>> 4) &&& 0xF))
                        writer.WriteByte(hexDigit (code &&& 0xF))

                    for i in 0 .. value.Length - 1 do
                        match value[i] with
                        | '"' ->
                            flushSegment i
                            w.WriteString("\\\"")
                            segmentStart <- i + 1
                        | '\\' ->
                            flushSegment i
                            w.WriteString("\\\\")
                            segmentStart <- i + 1
                        | '\b' ->
                            flushSegment i
                            w.WriteString("\\b")
                            segmentStart <- i + 1
                        | '\f' ->
                            flushSegment i
                            w.WriteString("\\f")
                            segmentStart <- i + 1
                        | '\n' ->
                            flushSegment i
                            w.WriteString("\\n")
                            segmentStart <- i + 1
                        | '\r' ->
                            flushSegment i
                            w.WriteString("\\r")
                            segmentStart <- i + 1
                        | '\t' ->
                            flushSegment i
                            w.WriteString("\\t")
                            segmentStart <- i + 1
                        | c when int c < 32 ->
                            flushSegment i
                            writeUnicodeEscape w c
                            segmentStart <- i + 1
                        | _ -> ()

                    flushSegment value.Length

                    w.WriteByte(34uy))
            Decode = (fun src -> let struct (v, s) = Runtime.stringDecoder src in struct (box v, s))
            MissingValue = None
          }
        | Primitive t when t = typeof<bool> -> {
            Encode =
                (fun w v ->
                    if unbox<bool> v then
                        w.WriteString("true")
                    else
                        w.WriteString("false"))
            Decode = (fun src -> let struct (v, s) = Runtime.boolDecoder src in struct (box v, s))
            MissingValue = None
          }
        | RawJsonValue ->
            let writeEscapedString (writer: IByteWriter) (value: string) =
                writer.WriteByte(34uy)
                let mutable segmentStart = 0

                let flushSegment endIdx =
                    if endIdx > segmentStart then
                        writer.WriteStringSlice(value, segmentStart, endIdx - segmentStart)

                let writeUnicodeEscape (writer: IByteWriter) (c: char) =
                    let hexDigit value =
                        if value < 10 then
                            byte (int '0' + value)
                        else
                            byte (int 'a' + value - 10)

                    let code = int c
                    writer.WriteByte(92uy)
                    writer.WriteByte(117uy)
                    writer.WriteByte(hexDigit ((code >>> 12) &&& 0xF))
                    writer.WriteByte(hexDigit ((code >>> 8) &&& 0xF))
                    writer.WriteByte(hexDigit ((code >>> 4) &&& 0xF))
                    writer.WriteByte(hexDigit (code &&& 0xF))

                for i in 0 .. value.Length - 1 do
                    match value[i] with
                    | '"' ->
                        flushSegment i
                        writer.WriteString("\\\"")
                        segmentStart <- i + 1
                    | '\\' ->
                        flushSegment i
                        writer.WriteString("\\\\")
                        segmentStart <- i + 1
                    | '\b' ->
                        flushSegment i
                        writer.WriteString("\\b")
                        segmentStart <- i + 1
                    | '\f' ->
                        flushSegment i
                        writer.WriteString("\\f")
                        segmentStart <- i + 1
                    | '\n' ->
                        flushSegment i
                        writer.WriteString("\\n")
                        segmentStart <- i + 1
                    | '\r' ->
                        flushSegment i
                        writer.WriteString("\\r")
                        segmentStart <- i + 1
                    | '\t' ->
                        flushSegment i
                        writer.WriteString("\\t")
                        segmentStart <- i + 1
                    | c when int c < 32 ->
                        flushSegment i
                        writeUnicodeEscape writer c
                        segmentStart <- i + 1
                    | _ -> ()

                flushSegment value.Length
                writer.WriteByte(34uy)

            let rec encodeJsonValue (writer: IByteWriter) (value: JsonValue) =
                match value with
                | JNull -> writer.WriteString("null")
                | JBool flag -> writer.WriteString(if flag then "true" else "false")
                | JNumber token -> writer.WriteString(token)
                | JString text -> writeEscapedString writer text
                | JArray items ->
                    writer.WriteByte(91uy)
                    let mutable first = true

                    for item in items do
                        if not first then
                            writer.WriteByte(44uy)

                        encodeJsonValue writer item
                        first <- false

                    writer.WriteByte(93uy)
                | JObject properties ->
                    writer.WriteByte(123uy)
                    let mutable first = true

                    for key, item in properties do
                        if not first then
                            writer.WriteByte(44uy)

                        writeEscapedString writer key
                        writer.WriteByte(58uy)
                        encodeJsonValue writer item
                        first <- false

                    writer.WriteByte(125uy)

            {
                Encode = (fun writer value -> encodeJsonValue writer (unbox<JsonValue> value))
                Decode =
                    (fun src ->
                        let struct (value, next) = Runtime.jsonValueDecoder src
                        struct (box value, next))
                MissingValue = None
            }
        | Option innerSchema ->
            let innerCodec = compileUntyped innerSchema
            let optionType = schema.TargetType
            let cases = FSharpType.GetUnionCases(optionType)
            let noneCase = cases |> Array.find (fun c -> c.Name = "None")
            let someCase = cases |> Array.find (fun c -> c.Name = "Some")

            {
                Encode =
                    (fun w v ->
                        if isNull v then
                            w.WriteString("null")
                        else
                            let _, fields = FSharpValue.GetUnionFields(v, optionType)
                            innerCodec.Encode w fields[0])
                Decode =
                    (fun src ->
                        let src = Runtime.skipWhitespace src
                        let data = src.Data

                        if src.Offset < data.Length && data[src.Offset] = 110uy then
                            let next = Runtime.nullDecoder src
                            struct (FSharpValue.MakeUnion(noneCase, [||]), next)
                        else
                            let struct (value, next) = innerCodec.Decode src
                            struct (FSharpValue.MakeUnion(someCase, [| value |]), next))
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
                    (fun src ->
                        let current = Runtime.skipWhitespace src
                        let data = current.Data

                        if current.Offset < data.Length && data[current.Offset] = 110uy then
                            let next = Runtime.nullDecoder current
                            struct (defaultValue, next)
                        else
                            innerCodec.Decode src)
                MissingValue = innerCodec.MissingValue
            }
        | EmptyCollectionAsValue(defaultValue, innerSchema) ->
            let innerCodec = compileUntyped innerSchema

            {
                Encode = innerCodec.Encode
                Decode =
                    (fun src ->
                        let struct (value, next) = innerCodec.Decode src

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
                    (fun src ->
                        let src = Runtime.skipWhitespace src
                        let data = src.Data

                        if src.Offset < data.Length && data[src.Offset] = 34uy then
                            let struct (text, next) = Runtime.stringDecoder src

                            if text = "" then
                                struct (noneValue, next)
                            else
                                innerCodec.Decode src
                        else
                            innerCodec.Decode src)
                MissingValue = innerCodec.MissingValue
            }
        | Record(t, fields, ctor) ->
            let compiledFields =
                fields
                |> Array.mapi (fun i f ->
                    let codec = compileUntyped f.Schema
                    let encodedName = "\"" + f.Name + "\":"
                    let rawName = Encoding.UTF8.GetBytes(f.Name)

                    {|
                        Name = f.Name
                        EncodedName = encodedName
                        RawName = rawName
                        Index = i
                        Codec = codec
                    |})

            let inline hashRawBytes (data: byte[]) (start: int) (length: int) =
                let mutable hash = 14695981039346656037UL
                let mutable i = 0

                while i < length do
                    hash <- (hash ^^^ uint64 data[start + i]) * 1099511628211UL
                    i <- i + 1

                hash

            let rawFieldIndices =
                Dictionary<struct (uint64 * int), int array>(compiledFields.Length)

            do
                let buckets =
                    Dictionary<struct (uint64 * int), ResizeArray<int>>(compiledFields.Length)

                for field in compiledFields do
                    let key =
                        struct (hashRawBytes field.RawName 0 field.RawName.Length, field.RawName.Length)

                    match buckets.TryGetValue(key) with
                    | true, bucket -> bucket.Add(field.Index)
                    | false, _ ->
                        let bucket = ResizeArray()
                        bucket.Add(field.Index)
                        buckets[key] <- bucket

                for KeyValue(key, bucket) in buckets do
                    rawFieldIndices[key] <- bucket.ToArray()

            ///
            /// Object decode used to linearly scan every field name for every
            /// property in the payload. A fixed lookup table keeps the compiled
            /// cost up front and removes repeated per-property scans.
            let fieldIndices = Dictionary<string, int>(compiledFields.Length)

            do
                for field in compiledFields do
                    fieldIndices[field.Name] <- field.Index

            let encoder (writer: IByteWriter) (vObj: obj) =
                writer.WriteByte(123uy)
                let mutable first = true

                for f in compiledFields do
                    if not first then
                        writer.WriteByte(44uy)

                    writer.WriteString(f.EncodedName)
                    f.Codec.Encode writer (fields[f.Index].GetValue vObj)
                    first <- false

                writer.WriteByte(125uy)

            let decoder (src: JsonSource) =
                let src = Runtime.skipWhitespace src

                if src.Offset >= src.Data.Length || src.Data[src.Offset] <> 123uy then
                    failwith "Expected {"

                ///
                /// Most schema field names are simple ASCII without escapes, so
                /// compare the raw UTF-8 bytes first and only allocate a key
                /// string when the payload uses escapes in the property name.
                let tryFindFieldIndexByRawKey (start: int) (length: int) (data: byte[]) : int option =
                    let mutable i = 0
                    let mutable hasEscapes = false

                    while i < length && not hasEscapes do
                        if data[start + i] = 92uy then
                            hasEscapes <- true

                        i <- i + 1

                    if hasEscapes then
                        None
                    else
                        let key = struct (hashRawBytes data start length, length)

                        match rawFieldIndices.TryGetValue(key) with
                        | false, _ -> None
                        | true, candidates ->
                            let mutable candidateIndex = 0
                            let mutable matched: int option = None

                            while candidateIndex < candidates.Length && matched.IsNone do
                                let candidate = compiledFields[candidates[candidateIndex]].RawName

                                if Runtime.bytesEqual candidate data start length then
                                    matched <- Some candidates[candidateIndex]

                                candidateIndex <- candidateIndex + 1

                            matched

                let data = src.Data
                let mutable current = src.Advance(1)
                let fieldValues = Runtime.rentObjectArray compiledFields.Length
                let useSeenMask = compiledFields.Length <= 64
                let mutable fieldSeenMask = 0UL

                let fieldSeen =
                    if useSeenMask then
                        [||]
                    else
                        Array.zeroCreate compiledFields.Length

                let mutable looping = true
                current <- Runtime.skipWhitespace current

                if current.Offset < data.Length && data[current.Offset] = 125uy then
                    looping <- false
                    current <- current.Advance(1)

                while looping do
                    let struct (keyStart, keyLength, keyHasEscapes, afterRawKey) =
                        Runtime.stringRaw current

                    let mutable fieldIndex =
                        if keyHasEscapes then
                            None
                        else
                            tryFindFieldIndexByRawKey keyStart keyLength data

                    if fieldIndex.IsNone && keyHasEscapes then
                        let struct (key, _) = Runtime.stringDecoder current

                        match fieldIndices.TryGetValue(key) with
                        | true, index -> fieldIndex <- Some index
                        | false, _ -> ()

                    let afterColon = Runtime.skipWhitespace afterRawKey

                    if afterColon.Offset >= data.Length || data[afterColon.Offset] <> 58uy then
                        failwith "Expected :"

                    let valSrc = Runtime.skipWhitespace (afterColon.Advance(1))

                    let afterVal =
                        match fieldIndex with
                        | Some index ->
                            let field = compiledFields[index]

                            let struct (value, nextSrc) =
                                Runtime.decodeAtPath (Property field.Name) field.Codec.Decode valSrc

                            fieldValues[index] <- value

                            if useSeenMask then
                                fieldSeenMask <- fieldSeenMask ||| (1UL <<< index)
                            else
                                fieldSeen[index] <- true

                            Runtime.skipWhitespace nextSrc
                        | None -> Runtime.skipWhitespace (Runtime.skipValue valSrc)

                    if afterVal.Offset < data.Length && data[afterVal.Offset] = 44uy then
                        current <- afterVal.Advance(1)
                    elif afterVal.Offset < data.Length && data[afterVal.Offset] = 125uy then
                        current <- afterVal.Advance(1)
                        looping <- false
                    else
                        failwith "Expected , or }"

                try
                    for f in compiledFields do
                        let seen =
                            if useSeenMask then
                                (fieldSeenMask &&& (1UL <<< f.Index)) <> 0UL
                            else
                                fieldSeen[f.Index]

                        if not seen then
                            match f.Codec.MissingValue with
                            | Some value -> fieldValues[f.Index] <- value
                            | None ->
                                Runtime.withPath (Property f.Name) (fun () ->
                                    Runtime.decodeFailure (sprintf "Missing required key '%s'" f.Name))

                    try
                        struct (ctor fieldValues, current)
                    with ex ->
                        match ex with
                        | :? JsonDecodeException -> raise ex
                        | _ -> raise (JsonDecodeException([], ex.Message, ex))
                finally
                    Runtime.returnObjectArray fieldValues compiledFields.Length

            {
                Encode = encoder
                Decode = decoder
                MissingValue = None
            }
        | List innerSchema ->
            let innerCodec = compileUntyped innerSchema
            let buildList = Runtime.makeListBuilder innerSchema.TargetType

            let encoder (writer: IByteWriter) (vObj: obj) =
                let list = vObj :?> System.Collections.IEnumerable
                writer.WriteByte(91uy)
                let mutable first = true

                for item in list do
                    if not first then
                        writer.WriteByte(44uy)

                    innerCodec.Encode writer item
                    first <- false

                writer.WriteByte(93uy)

            let decoder (src: JsonSource) =
                let mutable src = Runtime.skipWhitespace src

                if src.Offset >= src.Data.Length || src.Data[src.Offset] <> 91uy then
                    failwith "Expected ["

                src <- src.Advance(1)
                let results = ResizeArray<obj>()
                let mutable continueLoop = true
                src <- Runtime.skipWhitespace src

                if src.Offset < src.Data.Length && src.Data[src.Offset] = 93uy then
                    continueLoop <- false
                    src <- src.Advance(1)

                let mutable index = 0

                while continueLoop do
                    let struct (item, nextSrc) =
                        Runtime.decodeAtPath (Index index) innerCodec.Decode src

                    results.Add(item)
                    src <- Runtime.skipWhitespace nextSrc
                    index <- index + 1

                    if src.Offset < src.Data.Length && src.Data[src.Offset] = 44uy then
                        src <- src.Advance(1)
                    elif src.Offset < src.Data.Length && src.Data[src.Offset] = 93uy then
                        continueLoop <- false
                        src <- src.Advance(1)
                    else
                        failwith "Expected , or ]"

                struct (buildList (results.ToArray()), src)

            {
                Encode = encoder
                Decode = decoder
                MissingValue = None
            }
        | Array innerSchema ->
            let innerCodec = compileUntyped innerSchema

            let encoder (writer: IByteWriter) (vObj: obj) =
                writer.WriteByte(91uy)
                let mutable first = true

                for item in (vObj :?> System.Collections.IEnumerable) do
                    if not first then
                        writer.WriteByte(44uy)

                    innerCodec.Encode writer item
                    first <- false

                writer.WriteByte(93uy)

            let decoder (src: JsonSource) =
                let mutable src = Runtime.skipWhitespace src

                if src.Offset >= src.Data.Length || src.Data[src.Offset] <> 91uy then
                    failwith "Expected ["

                src <- src.Advance(1)
                let results = ResizeArray<obj>()
                let mutable continueLoop = true
                src <- Runtime.skipWhitespace src

                if src.Offset < src.Data.Length && src.Data[src.Offset] = 93uy then
                    continueLoop <- false
                    src <- src.Advance(1)

                let mutable index = 0

                while continueLoop do
                    let struct (item, nextSrc) =
                        Runtime.decodeAtPath (Index index) innerCodec.Decode src

                    results.Add(item)
                    src <- Runtime.skipWhitespace nextSrc
                    index <- index + 1

                    if src.Offset < src.Data.Length && src.Data[src.Offset] = 44uy then
                        src <- src.Advance(1)
                    elif src.Offset < src.Data.Length && src.Data[src.Offset] = 93uy then
                        continueLoop <- false
                        src <- src.Advance(1)
                    else
                        failwith "Expected , or ]"

#if !FABLE_COMPILER
                let targetArray = System.Array.CreateInstance(innerSchema.TargetType, results.Count)

                for i in 0 .. results.Count - 1 do
                    targetArray.SetValue(results[i], i)

                struct (box targetArray, src)
#else
                struct (box (results.ToArray()), src)
#endif

            {
                Encode = encoder
                Decode = decoder
                MissingValue = None
            }
        | Map(inner, wrap, unwrapFunc) ->
            let innerCodec = compileUntyped inner

            {
                Encode = (fun w v -> innerCodec.Encode w (unwrapFunc v))
                Decode =
                    (fun src ->
                        let struct (v, s) = innerCodec.Decode src

                        try
                            struct (Runtime.withValidationContext (fun () -> wrap v), s)
                        with ex ->
                            match ex with
                            | :? JsonDecodeException -> raise ex
                            | _ -> raise (JsonDecodeException([], ex.Message, ex)))
                MissingValue = innerCodec.MissingValue |> Option.map wrap
            }
        | _ -> failwithf "Unsupported schema type: %O" schema.Definition

    /// Compiles a schema into a reusable JSON codec.
    let compile (schema: Schema<'T>) : Codec<'T> =
        let compiled = compileUntyped (schema :> ISchema)

        {
            Encode = (fun w v -> compiled.Encode w (box v))
            Decode =
                (fun src ->
                    try
                        let struct (v, s) = compiled.Decode src
                        struct (unbox v, s)
                    with ex ->
                        match ex with
                        | :? JsonDecodeException -> raise ex
                        | _ -> Runtime.decodeFailure ex.Message)
        }

    ///
    /// `codec` is the shorter authoring alias for `compile`, so examples can
    /// stay explicit about the compile step without repeating the longer name.
    let codec (schema: Schema<'T>) : Codec<'T> = compile schema

    /// Serializes a value to JSON using a previously compiled codec.
    let serialize (codec: Codec<'T>) (value: 'T) =
        let writer = ResizableBuffer.Create(defaultSerializeBufferCapacity)

        try
            codec.Encode writer value
            Encoding.UTF8.GetString(writer.InternalData, 0, writer.InternalCount)
        finally
            writer.Release()

    /// Deserializes a JSON payload using a previously compiled codec.
    ///
    /// The entire payload must be consumed. Trailing content is treated as an
    /// error rather than ignored.
    let deserialize (codec: Codec<'T>) (json: string) =
        let bytes = Encoding.UTF8.GetBytes(json)
        let struct (v, rest) = codec.Decode(ByteSource(bytes, 0))
        let rest = Runtime.skipWhitespace rest

        if rest.Offset <> bytes.Length then
            Runtime.decodeFailure "Trailing content after top-level JSON value"

        v

    /// Deserializes a UTF-8 byte payload using a previously compiled codec.
    let deserializeBytes (codec: Codec<'T>) (bytes: byte[]) =
        let struct (v, rest) = codec.Decode(ByteSource(bytes, 0))
        let rest = Runtime.skipWhitespace rest

        if rest.Offset <> bytes.Length then
            Runtime.decodeFailure "Trailing content after top-level JSON value"

        v
