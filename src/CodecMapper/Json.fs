namespace CodecMapper

open System.Text
open System.Collections.Generic
open System.Globalization
open System.Runtime.CompilerServices
#if !FABLE_COMPILER
open System.Collections.Concurrent
#endif
open Microsoft.FSharp.Reflection

/// JSON codec compilation and runtime helpers.
///
/// Compile a schema once, then reuse the resulting codec for repeated JSON
/// serialization and deserialization.
module JsonBackend =
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

        let inline isWhitespaceByte (b: byte) =
            b = byte ' ' || b = byte '\n' || b = byte '\r' || b = byte '\t'

        let inline skipWhitespace (src: JsonSource) =
            let data = src.Data
            let offset = src.Offset

            if offset >= data.Length || not (isWhitespaceByte data[offset]) then
                src
            else
                let mutable i = offset + 1

                while i < data.Length && isWhitespaceByte data[i] do
                    i <- i + 1

                ByteSource(data, i)

        ///
        /// Benchmark-only typed experiments live in a separate friend assembly,
        /// so they need a non-inline entrypoint to reuse the handwritten
        /// parser without tripping cross-assembly inline restrictions.
        let skipWhitespaceShared (src: JsonSource) = skipWhitespace src

        let inline isDigit (b: byte) = b >= byte '0' && b <= byte '9'

        ///
        /// Object and array parsing repeatedly need the same "skip trailing
        /// whitespace, then inspect comma or closing delimiter" logic.
        /// Centralizing that keeps the hot loops shorter and removes duplicate
        /// whitespace scans at each call site.
        let inline readSeparatorOrClose (closeByte: byte) (current: JsonSource) (errorMessage: string) =
            let current = skipWhitespace current
            let data = current.Data

            if current.Offset >= data.Length then
                failwith errorMessage

            if data[current.Offset] = byte ',' then
                struct (skipWhitespace (current.Advance(1)), true)
            elif data[current.Offset] = closeByte then
                struct (current.Advance(1), false)
            else
                failwith errorMessage

        ///
        /// Most authored JSON payloads place the colon immediately after the
        /// property name, so check that byte first and only fall back to the
        /// whitespace-tolerant path when needed.
        let inline advancePastColon (current: JsonSource) =
            let data = current.Data
            let offset = current.Offset

            if offset < data.Length && data[offset] = byte ':' then
                current.Advance(1)
            else
                let current = skipWhitespace current

                if current.Offset >= data.Length || data[current.Offset] <> byte ':' then
                    failwith "Expected :"

                current.Advance(1)

        let inline private needsStringEscape (c: char) =
            c = '"' || c = '\\' || int c < 32

        let private writeUnicodeEscape (writer: IByteWriter) (c: char) =
            let hexDigit value =
                if value < 10 then
                    byte (int '0' + value)
                else
                    byte (int 'a' + value - 10)

            let code = int c
            writer.WriteByte(byte '\\')
            writer.WriteByte(byte 'u')
            writer.WriteByte(hexDigit ((code >>> 12) &&& 0xF))
            writer.WriteByte(hexDigit ((code >>> 8) &&& 0xF))
            writer.WriteByte(hexDigit ((code >>> 4) &&& 0xF))
            writer.WriteByte(hexDigit (code &&& 0xF))

        let writeEscapedString (writer: IByteWriter) (value: string) =
            let mutable index = 0
            let mutable doneFastScan = false

            while not doneFastScan && index < value.Length do
                if needsStringEscape value[index] then
                    doneFastScan <- true
                else
                    index <- index + 1

            writer.WriteByte(byte '"')

            if index = value.Length then
                writer.WriteString(value)
            else
                if index > 0 then
                    writer.WriteStringSlice(value, 0, index)

                let mutable segmentStart = index

                while index < value.Length do
                    match value[index] with
                    | '"' ->
                        if index > segmentStart then
                            writer.WriteStringSlice(value, segmentStart, index - segmentStart)

                        writer.WriteString("\\\"")
                        segmentStart <- index + 1
                    | '\\' ->
                        if index > segmentStart then
                            writer.WriteStringSlice(value, segmentStart, index - segmentStart)

                        writer.WriteString("\\\\")
                        segmentStart <- index + 1
                    | '\b' ->
                        if index > segmentStart then
                            writer.WriteStringSlice(value, segmentStart, index - segmentStart)

                        writer.WriteString("\\b")
                        segmentStart <- index + 1
                    | '\f' ->
                        if index > segmentStart then
                            writer.WriteStringSlice(value, segmentStart, index - segmentStart)

                        writer.WriteString("\\f")
                        segmentStart <- index + 1
                    | '\n' ->
                        if index > segmentStart then
                            writer.WriteStringSlice(value, segmentStart, index - segmentStart)

                        writer.WriteString("\\n")
                        segmentStart <- index + 1
                    | '\r' ->
                        if index > segmentStart then
                            writer.WriteStringSlice(value, segmentStart, index - segmentStart)

                        writer.WriteString("\\r")
                        segmentStart <- index + 1
                    | '\t' ->
                        if index > segmentStart then
                            writer.WriteStringSlice(value, segmentStart, index - segmentStart)

                        writer.WriteString("\\t")
                        segmentStart <- index + 1
                    | c when int c < 32 ->
                        if index > segmentStart then
                            writer.WriteStringSlice(value, segmentStart, index - segmentStart)

                        writeUnicodeEscape writer c
                        segmentStart <- index + 1
                    | _ -> ()

                    index <- index + 1

                if index > segmentStart then
                    writer.WriteStringSlice(value, segmentStart, index - segmentStart)

            writer.WriteByte(byte '"')

        let numberToken (allowFractionAndExponent: bool) (src: JsonSource) =
            let src = skipWhitespace src

            if src.Offset >= src.Data.Length then
                failwith "Unexpected end of input"

            let data = src.Data
            let mutable i = src.Offset

            if data[i] = byte '-' then
                i <- i + 1

            if i >= data.Length then
                failwith "Expected digit"

            if data[i] = byte '0' then
                i <- i + 1

                if i < data.Length && isDigit data[i] then
                    failwith "Leading zeroes are not allowed"
            elif isDigit data[i] then
                while i < data.Length && isDigit data[i] do
                    i <- i + 1
            else
                failwith "Expected digit"

            if allowFractionAndExponent && i < data.Length && data[i] = byte '.' then
                i <- i + 1

                if i >= data.Length || not (isDigit data[i]) then
                    failwith "Expected digit"

                while i < data.Length && isDigit data[i] do
                    i <- i + 1

            if
                allowFractionAndExponent
                && i < data.Length
                && (data[i] = byte 'e' || data[i] = byte 'E')
            then
                i <- i + 1

                if i < data.Length && (data[i] = byte '+' || data[i] = byte '-') then
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
                    && data[src.Offset] = byte 't'
                    && data[src.Offset + 1] = byte 'r'
                    && data[src.Offset + 2] = byte 'u'
                    && data[src.Offset + 3] = byte 'e'
                then
                    struct (true, ByteSource(data, src.Offset + 4))
                elif
                    remaining >= 5
                    && data[src.Offset] = byte 'f'
                    && data[src.Offset + 1] = byte 'a'
                    && data[src.Offset + 2] = byte 'l'
                    && data[src.Offset + 3] = byte 's'
                    && data[src.Offset + 4] = byte 'e'
                then
                    struct (false, ByteSource(data, src.Offset + 5))
                else
                    failwith "Expected true or false"

        let nullDecoder (src: JsonSource) =
            let src = skipWhitespace src
            let data = src.Data

            if
                src.Offset + 3 < data.Length
                && data[src.Offset] = byte 'n'
                && data[src.Offset + 1] = byte 'u'
                && data[src.Offset + 2] = byte 'l'
                && data[src.Offset + 3] = byte 'l'
            then
                ByteSource(data, src.Offset + 4)
            else
                failwith "Expected null"

        let stringRaw (src: JsonSource) : struct (int * int * bool * JsonSource) =
            let src = skipWhitespace src
            let data = src.Data
            let dataLength = data.Length

            if src.Offset >= dataLength || data[src.Offset] <> byte '"' then
                failwith "Expected \""

            let mutable i = src.Offset + 1
            let mutable finished = false
            let mutable hadEscapes = false

            //
            // Unknown-field skipping should stay linear even for escaped text,
            // so scan forward once instead of recounting backslashes at every
            // candidate quote.
#if !FABLE_COMPILER
            while i < dataLength && not finished do
                while i < dataLength && data[i] <> byte '"' && data[i] <> byte '\\' do
                    i <- i + 1

                if i < dataLength then
                    if data[i] = byte '"' then
                        finished <- true
                    else
                        hadEscapes <- true
                        i <- i + 1

                        if i >= dataLength then
                            failwith "Unterminated escape sequence"

                        if data[i] = byte 'u' then
                            if i + 4 >= dataLength then
                                failwith "Unterminated unicode escape"

                            i <- i + 4

                        i <- i + 1
#else
            while i < dataLength && not finished do
                while i < dataLength && data[i] <> byte '"' && data[i] <> byte '\\' do
                    i <- i + 1

                if i < dataLength then
                    if data[i] = byte '"' then
                        finished <- true
                    else
                        hadEscapes <- true
                        i <- i + 1

                        if i >= dataLength then
                            failwith "Unterminated escape sequence"

                        if data[i] = byte 'u' then
                            if i + 4 >= dataLength then
                                failwith "Unterminated unicode escape"

                            i <- i + 4

                        i <- i + 1
#endif

            if not finished then
                failwith "Unterminated string"

            struct (src.Offset + 1, i - (src.Offset + 1), hadEscapes, ByteSource(data, i + 1))

        let stringDecoder: Decoder<string> =
            fun src ->
                let src = skipWhitespace src
                let data = src.Data

                if src.Offset >= data.Length || data[src.Offset] <> byte '"' then
                    failwith "Expected \""

                let mutable i = src.Offset + 1
                let mutable segmentStart = i
                let mutable builder = null

                let appendSegment startIdx endIdx =
                    if endIdx > startIdx then
                        if isNull builder then
                            builder <- StringBuilder()

                        let mutable scan = startIdx
                        let mutable asciiOnly = true

                        while scan < endIdx && asciiOnly do
                            if data[scan] >= 128uy then
                                asciiOnly <- false
                            else
                                scan <- scan + 1

                        if asciiOnly then
                            let mutable appendIndex = startIdx

                            while appendIndex < endIdx do
                                builder.Append(char data[appendIndex]) |> ignore
                                appendIndex <- appendIndex + 1
                        else
#if !FABLE_COMPILER
                            builder.Append(Encoding.UTF8.GetString(data, startIdx, endIdx - startIdx)) |> ignore
#else
                            builder.Append(Encoding.UTF8.GetString(data.[startIdx .. endIdx - 1])) |> ignore
#endif

                let hexValue (b: byte) =
                    if b >= byte '0' && b <= byte '9' then int b - int (byte '0')
                    elif b >= byte 'A' && b <= byte 'F' then int b - int (byte 'A') + 10
                    elif b >= byte 'a' && b <= byte 'f' then int b - int (byte 'a') + 10
                    else failwith "Invalid unicode escape"

                let mutable finished = false

                while i < data.Length && not finished do
                    match data[i] with
                    | b when b = byte '"' -> finished <- true
                    | b when b = byte '\\' ->
                        appendSegment segmentStart i
                        i <- i + 1

                        if i >= data.Length then
                            failwith "Unterminated escape sequence"

                        if isNull builder then
                            builder <- StringBuilder()

                        match data[i] with
                        | b when b = byte '"' -> builder.Append('"') |> ignore
                        | b when b = byte '\\' -> builder.Append('\\') |> ignore
                        | b when b = byte '/' -> builder.Append('/') |> ignore
                        | b when b = byte 'b' -> builder.Append('\b') |> ignore
                        | b when b = byte 'f' -> builder.Append('\f') |> ignore
                        | b when b = byte 'n' -> builder.Append('\n') |> ignore
                        | b when b = byte 'r' -> builder.Append('\r') |> ignore
                        | b when b = byte 't' -> builder.Append('\t') |> ignore
                        | b when b = byte 'u' ->
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
            | b when b = byte 'n' ->
                let next = nullDecoder src
                struct (JNull, next)
            | b when b = byte 't' || b = byte 'f' ->
                let struct (value, next) = boolDecoder src
                struct (JBool value, next)
            | b when b = byte '"' ->
                let struct (value, next) = stringDecoder src
                struct (JString value, next)
            | b when b = byte '[' ->
                let mutable current = skipWhitespace (src.Advance(1))
                let items = ResizeArray<JsonValue>()
                let mutable looping = true

                if current.Offset < data.Length && data[current.Offset] = byte ']' then
                    current <- current.Advance(1)
                    looping <- false

                while looping do
                    let struct (item, next) = jsonValueDecoderAt (depth + 1) current
                    items.Add(item)

                    let struct (nextCurrent, continueLoop) =
                        readSeparatorOrClose (byte ']') next "Expected , or ]"

                    current <- nextCurrent
                    looping <- continueLoop

                struct (JArray(List.ofSeq items), current)
            | b when b = byte '{' ->
                let mutable current = skipWhitespace (src.Advance(1))
                let fields = ResizeArray<string * JsonValue>()
                let mutable looping = true

                if current.Offset < data.Length && data[current.Offset] = byte '}' then
                    current <- current.Advance(1)
                    looping <- false

                while looping do
                    let struct (key, afterKey) = stringDecoder current
                    let afterColon = advancePastColon afterKey

                    let struct (value, next) = jsonValueDecoderAt (depth + 1) afterColon
                    fields.Add(key, value)

                    let struct (nextCurrent, continueLoop) =
                        readSeparatorOrClose (byte '}') next "Expected , or }"

                    current <- nextCurrent
                    looping <- continueLoop

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
                | b when b = byte '{' ->
                    let mutable current = skipWhitespace (src.Advance(1))
                    let mutable continueLoop = true

                    if current.Offset < data.Length && data[current.Offset] = byte '}' then
                        current <- current.Advance(1)
                        continueLoop <- false

                    while continueLoop do
                        let struct (_, _, _, afterKey) = stringRaw current
                        let afterColon = advancePastColon afterKey

                        let struct (nextCurrent, keepLooping) =
                            readSeparatorOrClose (byte '}') (skipValueAt (depth + 1) afterColon) "Expected , or }"

                        current <- nextCurrent
                        continueLoop <- keepLooping

                    current
                | b when b = byte '[' ->
                    let mutable current = skipWhitespace (src.Advance(1))
                    let mutable continueLoop = true

                    if current.Offset < data.Length && data[current.Offset] = byte ']' then
                        current <- current.Advance(1)
                        continueLoop <- false

                    while continueLoop do
                        let struct (nextCurrent, keepLooping) =
                            readSeparatorOrClose (byte ']') (skipValueAt (depth + 1) current) "Expected , or ]"

                        current <- nextCurrent
                        continueLoop <- keepLooping

                    current
                | b when b = byte '"' ->
                    let struct (_, _, _, nextSrc) = stringRaw src
                    nextSrc
                | _ ->
                    let mutable i = src.Offset

                    while i < data.Length
                          && data[i] <> byte ','
                          && data[i] <> byte '}'
                          && data[i] <> byte ']'
                          && data[i] <> byte ' '
                          && data[i] <> byte '\n'
                          && data[i] <> byte '\r'
                          && data[i] <> byte '\t' do
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

        ///
        /// Keep the benchmark experiment comparing the same raw-key matching
        /// logic without forcing the friend assembly through inline expansion.
        let bytesEqualShared (a: byte[]) (b: byte[]) (offset: int) (len: int) = bytesEqual a b offset len

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
        /// XML shares the same shared list-construction helper, so keep the
        /// old entrypoint as a thin wrapper over the cached builder.
        let makeList (elementType: System.Type) (elements: obj array) = makeListBuilder elementType elements

        let makeOptionNone (optionType: System.Type) =
            let noneCase =
                FSharpType.GetUnionCases(optionType) |> Array.find (fun c -> c.Name = "None")

            FSharpValue.MakeUnion(noneCase, [||])

        ///
        /// Record decode still needs temporary object storage today, but
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

    type private RuntimeSchemaRefComparer() =
        interface IEqualityComparer<RuntimeSchema> with
            member _.Equals(left, right) = obj.ReferenceEquals(left, right)
            member _.GetHashCode(value) = RuntimeHelpers.GetHashCode(value)

    let private typedRuntime: JsonTypedRecordDecode.Runtime = {
        SkipWhitespace = Runtime.skipWhitespace
        StringRaw = Runtime.stringRaw
        StringDecoder = Runtime.stringDecoder
        SkipValue = Runtime.skipValue
        WrapFieldError =
            (fun fieldName ex ->
                match ex with
                | :? JsonDecodeException as decodeEx ->
                    JsonDecodeException(Property fieldName :: decodeEx.Path, decodeEx.Detail, ex) :> exn
                | _ -> JsonDecodeException([ Property fieldName ], ex.Message, ex) :> exn)
    }

    let rec private tryCompileTypedRecordRuntimeDecoder (schema: RuntimeSchema) : (ByteSource -> struct (obj * ByteSource)) option =
        match schema.Definition with
        | ERecord(targetType, recordRuntime) ->
            let fields = recordRuntime.Fields

            let typedFields =
                fields
                |> List.map (fun field ->
                    match field.Codec.Definition with
                    | EPrimitive t when t = typeof<int> ->
                        Some(JsonTypedRecordDecode.createField field.Name Runtime.intDecoder)
                    | EPrimitive t when t = typeof<int64> ->
                        Some(JsonTypedRecordDecode.createField field.Name Runtime.int64Decoder)
                    | EPrimitive t when t = typeof<uint32> ->
                        Some(JsonTypedRecordDecode.createField field.Name Runtime.uint32Decoder)
                    | EPrimitive t when t = typeof<uint64> ->
                        Some(JsonTypedRecordDecode.createField field.Name Runtime.uint64Decoder)
                    | EPrimitive t when t = typeof<float> ->
                        Some(JsonTypedRecordDecode.createField field.Name Runtime.floatDecoder)
                    | EPrimitive t when t = typeof<decimal> ->
                        Some(JsonTypedRecordDecode.createField field.Name Runtime.decimalDecoder)
                    | EPrimitive t when t = typeof<string> ->
                        Some(JsonTypedRecordDecode.createField field.Name Runtime.stringDecoder)
                    | EPrimitive t when t = typeof<bool> ->
                        Some(JsonTypedRecordDecode.createField field.Name Runtime.boolDecoder)
                    | ERecord _ when field.TargetType = field.Codec.TargetType ->
                        tryCompileTypedRecordRuntimeDecoder field.Codec
                        |> Option.map (fun runtimeDecoder ->
                            JsonTypedRecordDecode.createFieldFromRuntimeDynamic field.TargetType field.Name runtimeDecoder)
                    | _ -> None)

            if typedFields |> List.exists Option.isNone then
                None
            else
                JsonTypedRecordDecode.tryCompileRecordDecoderRuntime
                    typedRuntime
                    targetType
                    (typedFields |> List.choose id |> List.toArray)
        | _ -> None

    let private tryCompileTypedRecordDecoder (targetType: System.Type) (fields: RuntimeField list) =
        tryCompileTypedRecordRuntimeDecoder {
            TargetType = targetType
            Definition =
                ERecord(
                    targetType,
                    {
                        Fields = fields
                        CreateState = (fun () -> failwith "unreachable placeholder state")
                        StoreField = (fun _ -> failwith "unreachable placeholder store")
                        Complete = (fun _ -> failwith "unreachable placeholder complete")
                        Release = (fun _ -> ())
                    }
                )
        }

    let private compileUntyped (rootSchema: RuntimeSchema) : CompiledCodec =
        let cache = Dictionary<RuntimeSchema, CompiledCodec>(RuntimeSchemaRefComparer())

        let rec loop (schema: RuntimeSchema) : CompiledCodec =
            match cache.TryGetValue(schema) with
            | true, codec -> codec
            | false, _ ->
                let mutable encodeImpl = Unchecked.defaultof<IByteWriter -> obj -> unit>

                let mutable decodeImpl =
                    Unchecked.defaultof<JsonSource -> struct (obj * JsonSource)>

                let mutable missingValueImpl = None

                let placeholder = {
                    Encode = (fun writer value -> encodeImpl writer value)
                    Decode = (fun source -> decodeImpl source)
                    MissingValue = None
                }

                cache[schema] <- placeholder

                let compiled =
                    match schema.Definition with
                    | EPrimitive t when t = typeof<int> -> {
                        Encode = (fun w v -> w.WriteInt(unbox v))
                        Decode = (fun src -> let struct (v, s) = Runtime.intDecoder src in struct (box v, s))
                        MissingValue = None
                      }
                    | EPrimitive t when t = typeof<int64> -> {
                        Encode =
                            (fun w v ->
                                let value: int64 = unbox v
                                w.WriteInt64(value))
                        Decode = (fun src -> let struct (v, s) = Runtime.int64Decoder src in struct (box v, s))
                        MissingValue = None
                      }
                    | EPrimitive t when t = typeof<uint32> -> {
                        Encode =
                            (fun w v ->
                                let value: uint32 = unbox v
                                w.WriteUInt32(value))
                        Decode = (fun src -> let struct (v, s) = Runtime.uint32Decoder src in struct (box v, s))
                        MissingValue = None
                      }
                    | EPrimitive t when t = typeof<uint64> -> {
                        Encode =
                            (fun w v ->
                                let value: uint64 = unbox v
                                w.WriteUInt64(value))
                        Decode = (fun src -> let struct (v, s) = Runtime.uint64Decoder src in struct (box v, s))
                        MissingValue = None
                      }
                    | EPrimitive t when t = typeof<float> -> {
                        Encode =
                            (fun w v ->
                                let value: float = unbox v
                                w.WriteFloat(value))
                        Decode = (fun src -> let struct (v, s) = Runtime.floatDecoder src in struct (box v, s))
                        MissingValue = None
                      }
                    | EPrimitive t when t = typeof<decimal> -> {
                        Encode =
                            (fun w v ->
                                let value: decimal = unbox v
                                w.WriteDecimal(value))
                        Decode = (fun src -> let struct (v, s) = Runtime.decimalDecoder src in struct (box v, s))
                        MissingValue = None
                      }
                    | EPrimitive t when t = typeof<string> -> {
                        Encode =
                            (fun w v ->
                                let value: string = unbox v
                                Runtime.writeEscapedString w value)
                        Decode = (fun src -> let struct (v, s) = Runtime.stringDecoder src in struct (box v, s))
                        MissingValue = None
                      }
                    | EPrimitive t when t = typeof<bool> -> {
                        Encode =
                            (fun w v ->
                                if unbox<bool> v then
                                    w.WriteString("true")
                                else
                                    w.WriteString("false"))
                        Decode = (fun src -> let struct (v, s) = Runtime.boolDecoder src in struct (box v, s))
                        MissingValue = None
                      }
                    | EStringEnum(_, tryGetName, parseName) -> {
                        Encode =
                            (fun w v ->
                                match tryGetName v with
                                | Some name ->
                                    Runtime.writeEscapedString w name
                                | None -> failwithf "No string enum name matched value for type %O" schema.TargetType)
                        Decode =
                            (fun src ->
                                let struct (name, next) = Runtime.stringDecoder src
                                struct (parseName name, next))
                        MissingValue = None
                      }
                    | ERawJsonValue ->
                        let rec encodeJsonValue (writer: IByteWriter) (value: JsonValue) =
                            match value with
                            | JNull -> writer.WriteString("null")
                            | JBool flag -> writer.WriteString(if flag then "true" else "false")
                            | JNumber token -> writer.WriteString(token)
                            | JString text -> Runtime.writeEscapedString writer text
                            | JArray items ->
                                writer.WriteByte(byte '[')
                                let mutable first = true

                                for item in items do
                                    if not first then
                                        writer.WriteByte(byte ',')

                                    encodeJsonValue writer item
                                    first <- false

                                writer.WriteByte(byte ']')
                            | JObject properties ->
                                writer.WriteByte(byte '{')
                                let mutable first = true

                                for key, item in properties do
                                    if not first then
                                        writer.WriteByte(byte ',')

                                    Runtime.writeEscapedString writer key
                                    writer.WriteByte(byte ':')
                                    encodeJsonValue writer item
                                    first <- false

                                writer.WriteByte(byte '}')

                        {
                            Encode = (fun writer value -> encodeJsonValue writer (unbox<JsonValue> value))
                            Decode =
                                (fun src ->
                                    let struct (value, next) = Runtime.jsonValueDecoder src
                                    struct (box value, next))
                            MissingValue = None
                        }
                    | EOption innerSchema ->
                        let innerCodec = loop innerSchema
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

                                    if src.Offset < data.Length && data[src.Offset] = byte 'n' then
                                        let next = Runtime.nullDecoder src
                                        struct (FSharpValue.MakeUnion(noneCase, [||]), next)
                                    else
                                        let struct (value, next) = innerCodec.Decode src
                                        struct (FSharpValue.MakeUnion(someCase, [| value |]), next))
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
                                (fun src ->
                                    let current = Runtime.skipWhitespace src
                                    let data = current.Data

                                    if current.Offset < data.Length && data[current.Offset] = byte 'n' then
                                        let next = Runtime.nullDecoder current
                                        struct (defaultValue, next)
                                    else
                                        innerCodec.Decode src)
                            MissingValue = innerCodec.MissingValue
                        }
                    | EEmptyCollectionAsValue(defaultValue, innerSchema) ->
                        let innerCodec = loop innerSchema

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
                    | EEmptyStringAsNone innerSchema ->
                        let innerCodec = loop innerSchema
                        let optionType = schema.TargetType
                        let noneValue = Runtime.makeOptionNone optionType

                        {
                            Encode = innerCodec.Encode
                            Decode =
                                (fun src ->
                                    let src = Runtime.skipWhitespace src
                                    let data = src.Data

                                    if src.Offset < data.Length && data[src.Offset] = byte '"' then
                                        let struct (text, next) = Runtime.stringDecoder src

                                        if text = "" then
                                            struct (noneValue, next)
                                        else
                                            innerCodec.Decode src
                                    else
                                        innerCodec.Decode src)
                            MissingValue = innerCodec.MissingValue
                        }
                    | ERecord(t, recordRuntime) ->
                        let fields = recordRuntime.Fields

                        let compiledFields =
                            fields
                            |> List.toArray
                            |> Array.mapi (fun i f ->
                                let codec = loop f.Codec
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

                        let useDirectRawLookup = compiledFields.Length <= 8

                        let rawFieldIndices =
                            if useDirectRawLookup then
                                Unchecked.defaultof<Dictionary<struct (uint64 * int), int>>
                            else
                                Dictionary<struct (uint64 * int), int>(compiledFields.Length)

                        let rawFieldCollisions =
                            if useDirectRawLookup then
                                Unchecked.defaultof<Dictionary<struct (uint64 * int), int array>>
                            else
                                Dictionary<struct (uint64 * int), int array>()

                        do
                            if not useDirectRawLookup then
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
                                    if bucket.Count = 1 then
                                        rawFieldIndices[key] <- bucket[0]
                                    else
                                        rawFieldCollisions[key] <- bucket.ToArray()

                        ///
                        /// Object decode used to linearly scan every field name for every
                        /// property in the payload. A fixed lookup table keeps the compiled
                        /// cost up front and removes repeated per-property scans.
                        let fieldIndices = Dictionary<string, int>(compiledFields.Length)

                        do
                            for field in compiledFields do
                                fieldIndices[field.Name] <- field.Index

                        let encoder (writer: IByteWriter) (vObj: obj) =
                            writer.WriteByte(byte '{')
                            let mutable first = true

                            for f in compiledFields do
                                if not first then
                                    writer.WriteByte(byte ',')

                                writer.WriteString(f.EncodedName)
                                f.Codec.Encode writer (fields[f.Index].GetObj vObj)
                                first <- false

                            writer.WriteByte(byte '}')

                        let fallbackDecoder (src: JsonSource) =
                            let src = Runtime.skipWhitespace src

                            if src.Offset >= src.Data.Length || src.Data[src.Offset] <> byte '{' then
                                failwith "Expected {"

                            ///
                            /// Most schema field names are simple ASCII without escapes, so
                            /// compare the raw UTF-8 bytes first and only allocate a key
                            /// string when the payload uses escapes in the property name.
                            let tryFindFieldIndexByRawKey (start: int) (length: int) (data: byte[]) : int option =
                                if useDirectRawLookup then
                                    let mutable fieldIndex = 0
                                    let mutable matched: int option = None

                                    while fieldIndex < compiledFields.Length && matched.IsNone do
                                        let candidate = compiledFields[fieldIndex].RawName

                                        if
                                            candidate.Length = length
                                            && candidate[0] = data[start]
                                            && Runtime.bytesEqual candidate data start length
                                        then
                                            matched <- Some compiledFields[fieldIndex].Index

                                        fieldIndex <- fieldIndex + 1

                                    matched
                                else
                                    let key = struct (hashRawBytes data start length, length)

                                    match rawFieldIndices.TryGetValue(key) with
                                    | true, index -> Some index
                                    | false, _ ->
                                        match rawFieldCollisions.TryGetValue(key) with
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
                            let recordState = recordRuntime.CreateState()
                            let useSeenMask = compiledFields.Length <= 64
                            let mutable fieldSeenMask = 0UL

                            let fieldSeen =
                                if useSeenMask then
                                    [||]
                                else
                                    Array.zeroCreate compiledFields.Length

                            let mutable looping = true
                            current <- Runtime.skipWhitespace current

                            if current.Offset < data.Length && data[current.Offset] = byte '}' then
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

                                let valSrc = Runtime.skipWhitespace (Runtime.advancePastColon afterRawKey)

                                let afterVal =
                                    match fieldIndex with
                                    | Some index ->
                                        let field = compiledFields[index]

                                        let struct (value, nextSrc) =
                                            Runtime.decodeAtPath (Property field.Name) field.Codec.Decode valSrc

                                        recordRuntime.StoreField(recordState, index, value)

                                        if useSeenMask then
                                            fieldSeenMask <- fieldSeenMask ||| (1UL <<< index)
                                        else
                                            fieldSeen[index] <- true

                                        Runtime.skipWhitespace nextSrc
                                    | None -> Runtime.skipWhitespace (Runtime.skipValue valSrc)

                                if afterVal.Offset < data.Length && data[afterVal.Offset] = byte ',' then
                                    current <- afterVal.Advance(1)
                                elif afterVal.Offset < data.Length && data[afterVal.Offset] = byte '}' then
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
                                        | Some value -> recordRuntime.StoreField(recordState, f.Index, value)
                                        | None ->
                                            Runtime.withPath (Property f.Name) (fun () ->
                                                Runtime.decodeFailure (sprintf "Missing required key '%s'" f.Name))

                                try
                                    struct (recordRuntime.Complete recordState, current)
                                with ex ->
                                    match ex with
                                    | :? JsonDecodeException -> raise ex
                                    | _ -> raise (JsonDecodeException([], ex.Message, ex))
                            finally
                                recordRuntime.Release recordState

                        let decoder =
                            match tryCompileTypedRecordDecoder t fields with
                            | Some typedDecoder -> typedDecoder
                            | None -> fallbackDecoder

                        {
                            Encode = encoder
                            Decode = decoder
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

                        let rawJsonCodec = loop (RuntimeSchema.toRuntime Schema.jsonValue)

                        let encodeCaseName (writer: IByteWriter) (name: string) =
                            Runtime.writeEscapedString writer name

                        {
                            Encode =
                                (fun writer value ->
                                    match
                                        compiledCases
                                        |> Array.tryPick (fun compiled ->
                                            compiled.Case.TryGetValueObj value
                                            |> Option.map (fun fieldValue -> compiled, fieldValue))
                                    with
                                    | Some(compiled, fieldValue) ->
                                        writer.WriteByte(byte '{')
                                        encodeCaseName writer discriminatorName
                                        writer.WriteByte(byte ':')
                                        encodeCaseName writer compiled.Case.Name

                                        match compiled.Codec with
                                        | Some codec ->
                                            writer.WriteByte(byte ',')
                                            encodeCaseName writer valueName
                                            writer.WriteByte(byte ':')
                                            codec.Encode writer fieldValue
                                        | None -> ()

                                        writer.WriteByte(byte '}')
                                    | None -> failwithf "No union case matched value for type %O" schema.TargetType)
                            Decode =
                                (fun src ->
                                    let struct (rawValue, next) = Runtime.jsonValueDecoder src

                                    match rawValue with
                                    | JObject properties ->
                                        let tryFind name =
                                            properties |> List.tryFind (fun (key, _) -> key = name) |> Option.map snd

                                        let caseName =
                                            match tryFind discriminatorName with
                                            | Some(JString value) -> value
                                            | Some _ ->
                                                failwithf "Union discriminator '%s' must be a string" discriminatorName
                                            | None -> failwithf "Missing union discriminator '%s'" discriminatorName

                                        match
                                            compiledCases
                                            |> Array.tryFind (fun compiled -> compiled.Case.Name = caseName)
                                        with
                                        | Some compiled ->
                                            match compiled.Codec with
                                            | None ->
                                                match tryFind valueName with
                                                | Some _ ->
                                                    failwithf
                                                        "Union case '%s' does not accept payload '%s'"
                                                        caseName
                                                        valueName
                                                | None -> struct (compiled.Case.ConstructObj None, next)
                                            | Some codec ->
                                                let payload =
                                                    match tryFind valueName with
                                                    | Some value -> value
                                                    | None ->
                                                        failwithf
                                                            "Missing union payload '%s' for case '%s'"
                                                            valueName
                                                            caseName

                                                let writer = ResizableBuffer.Create(defaultSerializeBufferCapacity)

                                                try
                                                    rawJsonCodec.Encode writer (box payload)

                                                    let struct (fieldValue, rest) =
                                                        codec.Decode(ByteSource(writer.InternalData, 0))

                                                    let rest = Runtime.skipWhitespace rest

                                                    if rest.Offset <> writer.InternalCount then
                                                        failwithf
                                                            "Union payload '%s' for case '%s' had trailing content"
                                                            valueName
                                                            caseName

                                                    struct (compiled.Case.ConstructObj(Some fieldValue), next)
                                                finally
                                                    writer.Release()
                                        | None -> failwithf "Unknown union case '%s'" caseName
                                    | _ -> failwith "Expected union object")
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

                        let rawJsonCodec = loop (RuntimeSchema.toRuntime Schema.jsonValue)

                        let encodeCaseName (writer: IByteWriter) (name: string) =
                            Runtime.writeEscapedString writer name

                        let encodeInlinePayload (codec: CompiledCodec) (fieldValue: obj) =
                            let writer = ResizableBuffer.Create(defaultSerializeBufferCapacity)

                            try
                                codec.Encode writer fieldValue

                                let struct (rawPayload, rest) =
                                    rawJsonCodec.Decode(ByteSource(writer.InternalData, 0))

                                let rest = Runtime.skipWhitespace rest

                                if rest.Offset <> writer.InternalCount then
                                    failwith "Inline union payload had trailing JSON content"

                                match unbox<JsonValue> rawPayload with
                                | JObject properties -> properties
                                | _ -> failwith "Inline union payload schema must encode as a JSON object"
                            finally
                                writer.Release()

                        let decodeInlinePayload (codec: CompiledCodec) (properties: (string * JsonValue) list) =
                            let payloadObject = JObject properties
                            let writer = ResizableBuffer.Create(defaultSerializeBufferCapacity)

                            try
                                rawJsonCodec.Encode writer (box payloadObject)
                                let struct (fieldValue, rest) = codec.Decode(ByteSource(writer.InternalData, 0))
                                let rest = Runtime.skipWhitespace rest

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
                                        |> Array.tryPick (fun compiled ->
                                            compiled.Case.TryGetValueObj value
                                            |> Option.map (fun fieldValue -> compiled, fieldValue))
                                    with
                                    | Some(compiled, fieldValue) ->
                                        let payloadProperties =
                                            match compiled.Codec with
                                            | Some codec -> encodeInlinePayload codec fieldValue
                                            | None -> []

                                        writer.WriteByte(byte '{')
                                        encodeCaseName writer discriminatorName
                                        writer.WriteByte(byte ':')
                                        encodeCaseName writer compiled.Case.Name

                                        for propertyName, propertyValue in payloadProperties do
                                            if propertyName = discriminatorName then
                                                failwithf
                                                    "Inline union case '%s' payload cannot reuse discriminator field '%s'"
                                                    compiled.Case.Name
                                                    discriminatorName

                                            writer.WriteByte(byte ',')
                                            encodeCaseName writer propertyName
                                            writer.WriteByte(byte ':')
                                            rawJsonCodec.Encode writer (box propertyValue)

                                        writer.WriteByte(byte '}')
                                    | None -> failwithf "No union case matched value for type %O" schema.TargetType)
                            Decode =
                                (fun src ->
                                    let struct (rawValue, next) = Runtime.jsonValueDecoder src

                                    match rawValue with
                                    | JObject properties ->
                                        let tryFind name =
                                            properties |> List.tryFind (fun (key, _) -> key = name) |> Option.map snd

                                        let caseName =
                                            match tryFind discriminatorName with
                                            | Some(JString value) -> value
                                            | Some _ ->
                                                failwithf "Union discriminator '%s' must be a string" discriminatorName
                                            | None -> failwithf "Missing union discriminator '%s'" discriminatorName

                                        let payloadProperties =
                                            properties |> List.filter (fun (key, _) -> key <> discriminatorName)

                                        match
                                            compiledCases
                                            |> Array.tryFind (fun compiled -> compiled.Case.Name = caseName)
                                        with
                                        | Some compiled ->
                                            match compiled.Codec with
                                            | None ->
                                                if List.isEmpty payloadProperties then
                                                    struct (compiled.Case.ConstructObj None, next)
                                                else
                                                    failwithf
                                                        "Union case '%s' does not accept payload fields alongside '%s'"
                                                        caseName
                                                        discriminatorName
                                            | Some codec ->
                                                let fieldValue = decodeInlinePayload codec payloadProperties
                                                struct (compiled.Case.ConstructObj(Some fieldValue), next)
                                        | None -> failwithf "Unknown union case '%s'" caseName
                                    | _ -> failwith "Expected union object")
                            MissingValue = None
                        }
                    | EDelay factory -> loop (factory ())
                    | EList innerSchema ->
                        let innerCodec = loop innerSchema
                        let buildList = Runtime.makeListBuilder innerSchema.TargetType

                        let encoder (writer: IByteWriter) (vObj: obj) =
                            let list = vObj :?> System.Collections.IEnumerable
                            writer.WriteByte(byte '[')
                            let mutable first = true

                            for item in list do
                                if not first then
                                    writer.WriteByte(byte ',')

                                innerCodec.Encode writer item
                                first <- false

                            writer.WriteByte(byte ']')

                        let decoder (src: JsonSource) =
                            let mutable src = Runtime.skipWhitespace src

                            if src.Offset >= src.Data.Length || src.Data[src.Offset] <> byte '[' then
                                failwith "Expected ["

                            src <- src.Advance(1)
                            let results = ResizeArray<obj>()
                            let mutable continueLoop = true
                            src <- Runtime.skipWhitespace src

                            if src.Offset < src.Data.Length && src.Data[src.Offset] = byte ']' then
                                continueLoop <- false
                                src <- src.Advance(1)

                            let mutable index = 0

                            while continueLoop do
                                let struct (item, nextSrc) =
                                    Runtime.decodeAtPath (Index index) innerCodec.Decode src

                                results.Add(item)
                                src <- Runtime.skipWhitespace nextSrc
                                index <- index + 1

                                if src.Offset < src.Data.Length && src.Data[src.Offset] = byte ',' then
                                    src <- src.Advance(1)
                                elif src.Offset < src.Data.Length && src.Data[src.Offset] = byte ']' then
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
                    | EArray innerSchema ->
                        let innerCodec = loop innerSchema

                        let encoder (writer: IByteWriter) (vObj: obj) =
                            writer.WriteByte(byte '[')
                            let mutable first = true

                            for item in (vObj :?> System.Collections.IEnumerable) do
                                if not first then
                                    writer.WriteByte(byte ',')

                                innerCodec.Encode writer item
                                first <- false

                            writer.WriteByte(byte ']')

                        let decoder (src: JsonSource) =
                            let mutable src = Runtime.skipWhitespace src

                            if src.Offset >= src.Data.Length || src.Data[src.Offset] <> byte '[' then
                                failwith "Expected ["

                            src <- src.Advance(1)
                            let results = ResizeArray<obj>()
                            let mutable continueLoop = true
                            src <- Runtime.skipWhitespace src

                            if src.Offset < src.Data.Length && src.Data[src.Offset] = byte ']' then
                                continueLoop <- false
                                src <- src.Advance(1)

                            let mutable index = 0

                            while continueLoop do
                                let struct (item, nextSrc) =
                                    Runtime.decodeAtPath (Index index) innerCodec.Decode src

                                results.Add(item)
                                src <- Runtime.skipWhitespace nextSrc
                                index <- index + 1

                                if src.Offset < src.Data.Length && src.Data[src.Offset] = byte ',' then
                                    src <- src.Advance(1)
                                elif src.Offset < src.Data.Length && src.Data[src.Offset] = byte ']' then
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
                    | EMap(inner, wrap, unwrapFunc) ->
                        let innerCodec = loop inner

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

                encodeImpl <- compiled.Encode
                decodeImpl <- compiled.Decode
                missingValueImpl <- compiled.MissingValue

                let finalized = {
                    Encode = (fun writer value -> encodeImpl writer value)
                    Decode = (fun source -> decodeImpl source)
                    MissingValue = missingValueImpl
                }

                cache[schema] <- finalized
                finalized

        loop rootSchema

    /// Compiles a contract into a reusable JSON codec.
    let compile (schema: CodecMapper.Codec<'T>) : Codec<'T> =
        let compiled = compileUntyped (RuntimeSchema.toRuntime schema)

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
    /// Inline schema pipelines read more clearly when the final `build` and
    /// JSON compile step collapse into one terminal pipeline stage.
    let inline buildAndCompile
        (builder: SchemaBuilder<'T, 'Ctor, 'T, 'Chain>)
        : Codec<'T>
        when 'Chain :> IChainNode<'T, 'Ctor, 'T> =
        builder |> Schema.build |> compile

    ///
    /// `codec` remains as the shorter schema-to-codec alias for callers that
    /// prefer the direct `compile schema` shape without the longer name.
    let codec (schema: CodecMapper.Codec<'T>) : Codec<'T> = compile schema

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
