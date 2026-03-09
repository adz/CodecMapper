namespace CodecMapper

open System.Text
open System.Collections.Generic
open Microsoft.FSharp.Reflection
open System.Diagnostics.CodeAnalysis

[<AutoOpen>]
module Core =
    /// A lightweight context for reading bytes.
    [<Struct>]
    type ByteSource =
        val Data: byte[]
        val Offset: int

        new(data, offset) = { Data = data; Offset = offset }

        member inline x.Advance(n: int) = ByteSource(x.Data, x.Offset + n)
        member inline x.SetOffset(n: int) = ByteSource(x.Data, n)

    module ByteSource =
        let inline advance (n: int) (src: ByteSource) = src.Advance(n)
        let inline setOffset (n: int) (src: ByteSource) = src.SetOffset(n)

    /// Abstraction for writing bytes, to be implemented per target platform.
    type IByteWriter =
        abstract member Ensure: int -> unit
        abstract member WriteByte: byte -> unit
        abstract member WriteString: string -> unit
        abstract member WriteInt: int -> unit
        abstract member Data: byte[]
        abstract member Count: int

    /// Optimized implementation of IByteWriter.
    type ResizableBuffer =
        { mutable InternalData: byte[]
          mutable InternalCount: int }

        static member Create(initialCapacity: int) =
            { InternalData = Array.zeroCreate initialCapacity
              InternalCount = 0 }

        interface IByteWriter with
            member x.Ensure(n: int) =
                let minCapacity = x.InternalCount + n

                if x.InternalData.Length < minCapacity then
                    let newCapacity = max (x.InternalData.Length * 2) minCapacity
                    let newData = Array.zeroCreate newCapacity
                    System.Array.Copy(x.InternalData, 0, newData, 0, x.InternalCount)
                    x.InternalData <- newData

            member x.WriteByte(b: byte) =
                (x :> IByteWriter).Ensure(1)
                x.InternalData.[x.InternalCount] <- b
                x.InternalCount <- x.InternalCount + 1

            member x.WriteString(s: string) =
#if !FABLE_COMPILER
                let maxBytes = Encoding.UTF8.GetMaxByteCount(s.Length)
                (x :> IByteWriter).Ensure(maxBytes)

                let written =
                    Encoding.UTF8.GetBytes(s, 0, s.Length, x.InternalData, x.InternalCount)

                x.InternalCount <- x.InternalCount + written
#else
                let bytes = Encoding.UTF8.GetBytes(s)
                (x :> IByteWriter).Ensure(bytes.Length)
                System.Array.Copy(bytes, 0, x.InternalData, x.InternalCount, bytes.Length)
                x.InternalCount <- x.InternalCount + bytes.Length
#endif

            member x.WriteInt(value: int) =
                if value = 0 then
                    (x :> IByteWriter).WriteByte(48uy)
                else
                    let mutable v = value

                    if v < 0 then
                        (x :> IByteWriter).WriteByte(45uy)
                        v <- -v

                    let digits = Array.zeroCreate 10
                    let mutable pos = 0

                    while v > 0 do
                        digits.[pos] <- byte (48 + (v % 10))
                        v <- v / 10
                        pos <- pos + 1

                    (x :> IByteWriter).Ensure(pos)

                    for i in 0 .. pos - 1 do
                        x.InternalData.[x.InternalCount + i] <- digits.[pos - 1 - i]

                    x.InternalCount <- x.InternalCount + pos

            member x.Data = x.InternalData
            member x.Count = x.InternalCount

/// Abstract blueprint for serialization.
type SchemaField =
    { Name: string
      Type: System.Type
      GetValue: obj -> obj
      Schema: ISchema }

and ISchema =
    abstract member TargetType: System.Type
    abstract member Definition: SchemaDefinition

and SchemaDefinition =
    | Primitive of System.Type
    | Record of System.Type * SchemaField[] * (obj[] -> obj)
    | List of ISchema
    | Array of ISchema
    | Option of ISchema
    | MissingAsNone of ISchema
    | EmptyStringAsNone of ISchema
    | Map of ISchema * (obj -> obj) * (obj -> obj)

type Schema<'T> =
    inherit ISchema

/// Builder state for the Pipeline DSL.
///
/// The builder keeps the constructor's remaining type in `'Ctor` so each
/// appended field proves, at compile time, that one more argument has been
/// supplied before `build` can close over the final record value.
type Builder<'T, 'Ctor> =
    {
        Fields: SchemaField list

        ///
        /// We evaluate the curried constructor from left to right against decoded
        /// field values stored in the compiler's `obj[]` buffer. This keeps the
        /// public DSL typed while still fitting the existing record compiler.
        App: obj[] -> int -> 'Ctor
    }

module Schema =
    let unwrap (s: ISchema) = s

    let inline create<'T> def =
        { new Schema<'T> with
            member _.TargetType = typeof<'T>
            member _.Definition = def }

    let int: Schema<int> = create (Primitive typeof<int>)
    let int64: Schema<int64> = create (Primitive typeof<int64>)
    let uint32: Schema<uint32> = create (Primitive typeof<uint32>)
    let uint64: Schema<uint64> = create (Primitive typeof<uint64>)
    let float: Schema<float> = create (Primitive typeof<float>)
    let decimal: Schema<decimal> = create (Primitive typeof<decimal>)
    let string: Schema<string> = create (Primitive typeof<string>)
    let bool: Schema<bool> = create (Primitive typeof<bool>)

    let inline map (wrap: 'U -> 'T) (unwrapFunc: 'T -> 'U) (inner: Schema<'U>) : Schema<'T> =
        create (Map(inner :> ISchema, (fun x -> box (wrap (unbox x))), (fun x -> box (unwrapFunc (unbox x)))))

    ///
    /// Smart constructors need a way to reject decoded values without forcing
    /// callers to smuggle exceptions through plain `map`.
    let inline tryMap (wrap: 'U -> Result<'T, string>) (unwrapFunc: 'T -> 'U) (inner: Schema<'U>) : Schema<'T> =
        create (
            Map(
                inner :> ISchema,
                (fun x ->
                    match wrap (unbox x) with
                    | Ok value -> box value
                    | Error message -> failwith message),
                (fun x -> box (unwrapFunc (unbox x)))
            )
        )

    ///
    /// Narrow numeric types can safely reuse the integer codec as long as the
    /// schema enforces range checks on decode.
    let private rangedInt<'T>
        (typeName: string)
        (minValue: int)
        (maxValue: int)
        (convert: int -> 'T)
        (toInt: 'T -> int)
        : Schema<'T> =
        int
        |> map
            (fun value ->
                if value < minValue || value > maxValue then
                    failwithf "%s value out of range: %d" typeName value

                convert value)
            toInt

    let int16: Schema<int16> =
        rangedInt
            "int16"
            (System.Convert.ToInt32(System.Int16.MinValue))
            (System.Convert.ToInt32(System.Int16.MaxValue))
            System.Convert.ToInt16
            System.Convert.ToInt32

    let byte: Schema<byte> =
        rangedInt "byte" 0 255 System.Convert.ToByte System.Convert.ToInt32

    let sbyte: Schema<sbyte> =
        rangedInt
            "sbyte"
            (System.Convert.ToInt32(System.SByte.MinValue))
            (System.Convert.ToInt32(System.SByte.MaxValue))
            System.Convert.ToSByte
            System.Convert.ToInt32

    let uint16: Schema<uint16> =
        rangedInt
            "uint16"
            0
            (System.Convert.ToInt32(System.UInt16.MaxValue))
            System.Convert.ToUInt16
            System.Convert.ToInt32

    ///
    /// Common domain identity and timestamp types ride on top of the string
    /// codec so JSON and XML stay symmetric without extra parser branches.
    let guid: Schema<System.Guid> =
        string |> map System.Guid.Parse (fun value -> value.ToString("D"))

    let char: Schema<char> =
        string
        |> map
            (fun value ->
                if value.Length <> 1 then
                    failwithf "char value must contain exactly one character, got %d" value.Length

                value.[0])
            (fun value -> value.ToString())

    let dateTime: Schema<System.DateTime> =
        string
        |> map
            (fun value ->
                System.DateTime.ParseExact(
                    value,
                    "O",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind
                ))
            (fun value -> value.ToString("O", System.Globalization.CultureInfo.InvariantCulture))

    let dateTimeOffset: Schema<System.DateTimeOffset> =
        string
        |> map
            (fun value ->
                System.DateTimeOffset.ParseExact(
                    value,
                    "O",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind
                ))
            (fun value -> value.ToString("O", System.Globalization.CultureInfo.InvariantCulture))

    let timeSpan: Schema<System.TimeSpan> =
        string
        |> map
            (fun value -> System.TimeSpan.ParseExact(value, "c", System.Globalization.CultureInfo.InvariantCulture))
            (fun value -> value.ToString("c", System.Globalization.CultureInfo.InvariantCulture))

    let inline list (inner: Schema<'T>) : Schema<'T list> = create (List(inner :> ISchema))

    let inline array (inner: Schema<'T>) : Schema<'T[]> = create (Array(inner :> ISchema))

    let inline option (inner: Schema<'T>) : Schema<'T option> = create (Option(inner :> ISchema))

    ///
    /// Config-style payloads sometimes treat absent fields as "no value"
    /// instead of as a contract violation. Keep that policy explicit.
    let inline missingAsNone (inner: Schema<'T option>) : Schema<'T option> =
        create (MissingAsNone(inner :> ISchema))

    ///
    /// Empty strings are often used as a legacy stand-in for "not provided".
    /// This wrapper keeps that conversion opt-in rather than weakening string
    /// option semantics globally.
    let emptyStringAsNone (inner: Schema<string option>) : Schema<string option> =
        create (EmptyStringAsNone(inner :> ISchema))

    let rec resolveSchema (t: System.Type) : ISchema =
        if t = typeof<int> then
            int :> ISchema
        elif t = typeof<int64> then
            int64 :> ISchema
        elif t = typeof<uint32> then
            uint32 :> ISchema
        elif t = typeof<uint64> then
            uint64 :> ISchema
        elif t = typeof<float> then
            float :> ISchema
        elif t = typeof<decimal> then
            decimal :> ISchema
        elif t = typeof<string> then
            string :> ISchema
        elif t = typeof<bool> then
            bool :> ISchema
        elif t = typeof<int16> then
            int16 :> ISchema
        elif t = typeof<byte> then
            byte :> ISchema
        elif t = typeof<sbyte> then
            sbyte :> ISchema
        elif t = typeof<uint16> then
            uint16 :> ISchema
        elif t = typeof<System.Guid> then
            guid :> ISchema
        elif t = typeof<char> then
            char :> ISchema
        elif t = typeof<System.DateTime> then
            dateTime :> ISchema
        elif t = typeof<System.DateTimeOffset> then
            dateTimeOffset :> ISchema
        elif t = typeof<System.TimeSpan> then
            timeSpan :> ISchema
        elif t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>> then
            let innerType = t.GetGenericArguments().[0]
            let innerSchema = resolveSchema innerType

            { new ISchema with
                member _.TargetType = t
                member _.Definition = Option(innerSchema) }
        elif
            t.IsGenericType
            && t.GetGenericTypeDefinition() = typeof<list<_>>.GetGenericTypeDefinition()
        then
            let innerType = t.GetGenericArguments().[0]
            let innerSchema = resolveSchema innerType

            { new ISchema with
                member _.TargetType = t
                member _.Definition = List(innerSchema) }
        elif t.IsArray then
            let elementType = t.GetElementType()
            let elementSchema = resolveSchema elementType

            { new ISchema with
                member _.TargetType = t
                member _.Definition = Array(elementSchema) }
        else
            failwithf "Could not automatically resolve schema for type %O. Please provide it explicitly." t

    // Pipeline DSL
    ///
    /// The pipeline starts by capturing the curried constructor up front so
    /// subsequent field steps only describe the wire layout.
    ///
    /// We still ask for `'T` explicitly because relying on field-label
    /// inference alone becomes brittle as soon as multiple record types share
    /// names like `Id` or `Name`.
    let inline define<'T> : Builder<'T, unit> = { Fields = []; App = (fun _ _ -> ()) }

    let inline construct (ctor: 'Ctor) (builder: Builder<'T, unit>) : Builder<'T, 'Ctor> =
        { Fields = builder.Fields
          App = (fun _ _ -> ctor) }

    let inline field
        (name: string)
        (getter: 'T -> 'Field)
        (builder: Builder<'T, 'Field -> 'Next>)
        : Builder<'T, 'Next> =
        let schema = resolveSchema typeof<'Field>

        let f =
            { Name = name
              Type = typeof<'Field>
              GetValue = (fun r -> box (getter (unbox r)))
              Schema = schema }

        let nextApp (args: obj[]) (idx: int) =
            let fCurried = builder.App args (idx - 1)
            let arg = unbox<'Field> args.[idx]
            fCurried arg

        { Fields = f :: builder.Fields
          App = nextApp }

    let inline fieldWith
        (name: string)
        (getter: 'T -> 'Field)
        (schema: Schema<'Field>)
        (builder: Builder<'T, 'Field -> 'Next>)
        : Builder<'T, 'Next> =
        let f =
            { Name = name
              Type = typeof<'Field>
              GetValue = (fun r -> box (getter (unbox r)))
              Schema = unwrap schema }

        let nextApp (args: obj[]) (idx: int) =
            let fCurried = builder.App args (idx - 1)
            let arg = unbox<'Field> args.[idx]
            fCurried arg

        { Fields = f :: builder.Fields
          App = nextApp }

    let build (builder: Builder<'T, 'T>) : Schema<'T> =
        let fields = builder.Fields |> List.rev |> List.toArray
        let targetType = typeof<'T>

        let buildFunc (args: obj[]) =
            box (builder.App args (args.Length - 1))

        create<'T> (Record(targetType, fields, buildFunc))

module Json =
    type JsonSource = ByteSource
    type JsonWriter = IByteWriter

    type Decoder<'T> = JsonSource -> struct ('T * JsonSource)

    type Codec<'T> =
        { Encode: IByteWriter -> 'T -> unit
          Decode: Decoder<'T> }

    module internal Runtime =
        let inline skipWhitespace (src: JsonSource) =
            let mutable i = src.Offset
            let data = src.Data

            while i < data.Length
                  && (data.[i] = 32uy || data.[i] = 10uy || data.[i] = 13uy || data.[i] = 9uy) do
                i <- i + 1

            ByteSource(data, i)

        let inline isDigit (b: byte) = b >= 48uy && b <= 57uy

        let numberToken (allowFractionAndExponent: bool) (src: JsonSource) =
            let src = skipWhitespace src

            if src.Offset >= src.Data.Length then
                failwith "Unexpected end of input"

            let data = src.Data
            let mutable i = src.Offset

            if data.[i] = 45uy then
                i <- i + 1

            if i >= data.Length then
                failwith "Expected digit"

            if data.[i] = 48uy then
                i <- i + 1

                if i < data.Length && isDigit data.[i] then
                    failwith "Leading zeroes are not allowed"
            elif isDigit data.[i] then
                while i < data.Length && isDigit data.[i] do
                    i <- i + 1
            else
                failwith "Expected digit"

            if allowFractionAndExponent && i < data.Length && data.[i] = 46uy then
                i <- i + 1

                if i >= data.Length || not (isDigit data.[i]) then
                    failwith "Expected digit"

                while i < data.Length && isDigit data.[i] do
                    i <- i + 1

            if
                allowFractionAndExponent
                && i < data.Length
                && (data.[i] = 101uy || data.[i] = 69uy)
            then
                i <- i + 1

                if i < data.Length && (data.[i] = 43uy || data.[i] = 45uy) then
                    i <- i + 1

                if i >= data.Length || not (isDigit data.[i]) then
                    failwith "Expected digit"

                while i < data.Length && isDigit data.[i] do
                    i <- i + 1

#if !FABLE_COMPILER
            let token = Encoding.UTF8.GetString(data, src.Offset, i - src.Offset)
#else
            let token = Encoding.UTF8.GetString(data.[src.Offset .. i - 1])
#endif

            struct (token, ByteSource(data, i))

        let intDecoder: Decoder<int> =
            fun src ->
                let struct (token, next) = numberToken false src

                try
                    struct (System.Int32.Parse(
                                token,
                                System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture
                            ),
                            next)
                with :? System.OverflowException ->
                    failwithf "int value out of range: %s" token

        let int64Decoder: Decoder<int64> =
            fun src ->
                let struct (token, next) = numberToken false src

                try
                    struct (System.Int64.Parse(
                                token,
                                System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture
                            ),
                            next)
                with :? System.OverflowException ->
                    failwithf "int64 value out of range: %s" token

        let uint32Decoder: Decoder<uint32> =
            fun src ->
                let struct (token, next) = numberToken false src

                try
                    struct (System.UInt32.Parse(
                                token,
                                System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture
                            ),
                            next)
                with :? System.OverflowException ->
                    failwithf "uint32 value out of range: %s" token

        let uint64Decoder: Decoder<uint64> =
            fun src ->
                let struct (token, next) = numberToken false src

                try
                    struct (System.UInt64.Parse(
                                token,
                                System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture
                            ),
                            next)
                with :? System.OverflowException ->
                    failwithf "uint64 value out of range: %s" token

        let floatDecoder: Decoder<float> =
            fun src ->
                let struct (token, next) = numberToken true src

                try
                    struct (System.Double.Parse(
                                token,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture
                            ),
                            next)
                with
                | :? System.FormatException -> failwithf "Invalid float value: %s" token
                | :? System.OverflowException -> failwithf "float value out of range: %s" token

        let decimalDecoder: Decoder<decimal> =
            fun src ->
                let struct (token, next) = numberToken true src

                try
                    struct (System.Decimal.Parse(
                                token,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture
                            ),
                            next)
                with
                | :? System.FormatException -> failwithf "Invalid decimal value: %s" token
                | :? System.OverflowException -> failwithf "decimal value out of range: %s" token

        let boolDecoder: Decoder<bool> =
            fun src ->
                let src = skipWhitespace src
                let data = src.Data

                if src.Offset >= data.Length then
                    failwith "Unexpected end of input"

                let remaining = data.Length - src.Offset

                if
                    remaining >= 4
                    && data.[src.Offset] = 116uy
                    && data.[src.Offset + 1] = 114uy
                    && data.[src.Offset + 2] = 117uy
                    && data.[src.Offset + 3] = 101uy
                then
                    struct (true, ByteSource(data, src.Offset + 4))
                elif
                    remaining >= 5
                    && data.[src.Offset] = 102uy
                    && data.[src.Offset + 1] = 97uy
                    && data.[src.Offset + 2] = 108uy
                    && data.[src.Offset + 3] = 115uy
                    && data.[src.Offset + 4] = 101uy
                then
                    struct (false, ByteSource(data, src.Offset + 5))
                else
                    failwith "Expected true or false"

        let nullDecoder (src: JsonSource) =
            let src = skipWhitespace src
            let data = src.Data

            if
                src.Offset + 3 < data.Length
                && data.[src.Offset] = 110uy
                && data.[src.Offset + 1] = 117uy
                && data.[src.Offset + 2] = 108uy
                && data.[src.Offset + 3] = 108uy
            then
                ByteSource(data, src.Offset + 4)
            else
                failwith "Expected null"

        let stringRaw (src: JsonSource) : struct (int * int * JsonSource) =
            let src = skipWhitespace src
            let data = src.Data

            if src.Offset >= data.Length || data.[src.Offset] <> 34uy then
                failwith "Expected \""

            ///
            /// Strings are also used while skipping unknown fields, so escaped
            /// quotes must not terminate the scan early.
            let isEscapedQuote index =
                let mutable slashCount = 0
                let mutable j = index - 1

                while j >= src.Offset && data.[j] = 92uy do
                    slashCount <- slashCount + 1
                    j <- j - 1

                slashCount % 2 = 1

            let mutable i = src.Offset + 1

            while i < data.Length && not (data.[i] = 34uy && not (isEscapedQuote i)) do
                i <- i + 1

            if i >= data.Length then
                failwith "Unterminated string"

            struct (src.Offset + 1, i - (src.Offset + 1), ByteSource(data, i + 1))

        let stringDecoder: Decoder<string> =
            fun src ->
                let src = skipWhitespace src
                let data = src.Data

                if src.Offset >= data.Length || data.[src.Offset] <> 34uy then
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
                    match data.[i] with
                    | 34uy -> finished <- true
                    | 92uy ->
                        appendSegment segmentStart i
                        i <- i + 1

                        if i >= data.Length then
                            failwith "Unterminated escape sequence"

                        if isNull builder then
                            builder <- StringBuilder()

                        match data.[i] with
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
                                ((hexValue data.[i + 1]) <<< 12)
                                ||| ((hexValue data.[i + 2]) <<< 8)
                                ||| ((hexValue data.[i + 3]) <<< 4)
                                ||| (hexValue data.[i + 4])

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

        let rec skipValueAt depth (src: JsonSource) : JsonSource =
            if depth > maxJsonDepth then
                failwith "Maximum JSON nesting depth exceeded"

            let src = skipWhitespace src

            if src.Offset >= src.Data.Length then
                src
            else
                let data = src.Data

                match data.[src.Offset] with
                | 123uy ->
                    let mutable current = skipWhitespace (src.Advance(1))
                    let mutable continueLoop = true

                    if current.Offset < data.Length && data.[current.Offset] = 125uy then
                        current <- current.Advance(1)
                        continueLoop <- false

                    while continueLoop do
                        let struct (_, _, afterKey) = stringRaw current
                        let afterColon = skipWhitespace afterKey

                        if afterColon.Offset >= data.Length || data.[afterColon.Offset] <> 58uy then
                            failwith "Expected :"

                        let afterValue = skipWhitespace (skipValueAt (depth + 1) (afterColon.Advance(1)))

                        if afterValue.Offset < data.Length && data.[afterValue.Offset] = 44uy then
                            current <- skipWhitespace (afterValue.Advance(1))
                        elif afterValue.Offset < data.Length && data.[afterValue.Offset] = 125uy then
                            current <- afterValue.Advance(1)
                            continueLoop <- false
                        else
                            failwith "Expected , or }"

                    current
                | 91uy ->
                    let mutable current = skipWhitespace (src.Advance(1))
                    let mutable continueLoop = true

                    if current.Offset < data.Length && data.[current.Offset] = 93uy then
                        current <- current.Advance(1)
                        continueLoop <- false

                    while continueLoop do
                        let afterItem = skipWhitespace (skipValueAt (depth + 1) current)

                        if afterItem.Offset < data.Length && data.[afterItem.Offset] = 44uy then
                            current <- skipWhitespace (afterItem.Advance(1))
                        elif afterItem.Offset < data.Length && data.[afterItem.Offset] = 93uy then
                            current <- afterItem.Advance(1)
                            continueLoop <- false
                        else
                            failwith "Expected , or ]"

                    current
                | 34uy ->
                    let struct (_, _, nextSrc) = stringRaw src
                    nextSrc
                | _ ->
                    let mutable i = src.Offset

                    while i < data.Length
                          && data.[i] <> 44uy
                          && data.[i] <> 125uy
                          && data.[i] <> 93uy
                          && data.[i] <> 32uy
                          && data.[i] <> 10uy
                          && data.[i] <> 13uy
                          && data.[i] <> 9uy do
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
                    if a.[i] <> b.[offset + i] then
                        equal <- false
                    else
                        i <- i + 1

                equal

        let makeList (elementType: System.Type) (elements: obj array) =
#if !FABLE_COMPILER
            let listType = typedefof<_ list>.MakeGenericType([| elementType |])
            let emptyList = listType.GetProperty("Empty").GetValue(null)
            let cons = listType.GetMethod("Cons")
            let mutable result = emptyList

            for i in elements.Length - 1 .. -1 .. 0 do
                result <- cons.Invoke(null, [| elements.[i]; result |])

            result
#else
            List.ofArray elements |> box
#endif

        let makeOptionNone (optionType: System.Type) =
            let noneCase =
                FSharpType.GetUnionCases(optionType) |> Array.find (fun c -> c.Name = "None")

            FSharpValue.MakeUnion(noneCase, [||])

    type CompiledCodec =
        { Encode: IByteWriter -> obj -> unit
          Decode: JsonSource -> struct (obj * JsonSource)
          MissingValue: obj option }

    let rec compileUntyped (schema: ISchema) : CompiledCodec =
        match schema.Definition with
        | Primitive t when t = typeof<int> ->
            { Encode = (fun w v -> w.WriteInt(unbox v))
              Decode = (fun src -> let struct (v, s) = Runtime.intDecoder src in struct (box v, s))
              MissingValue = None }
        | Primitive t when t = typeof<int64> ->
            { Encode =
                (fun w v ->
                    let value: int64 = unbox v
                    w.WriteString(value.ToString(System.Globalization.CultureInfo.InvariantCulture)))
              Decode = (fun src -> let struct (v, s) = Runtime.int64Decoder src in struct (box v, s))
              MissingValue = None }
        | Primitive t when t = typeof<uint32> ->
            { Encode =
                (fun w v ->
                    let value: uint32 = unbox v
                    w.WriteString(value.ToString(System.Globalization.CultureInfo.InvariantCulture)))
              Decode = (fun src -> let struct (v, s) = Runtime.uint32Decoder src in struct (box v, s))
              MissingValue = None }
        | Primitive t when t = typeof<uint64> ->
            { Encode =
                (fun w v ->
                    let value: uint64 = unbox v
                    w.WriteString(value.ToString(System.Globalization.CultureInfo.InvariantCulture)))
              Decode = (fun src -> let struct (v, s) = Runtime.uint64Decoder src in struct (box v, s))
              MissingValue = None }
        | Primitive t when t = typeof<float> ->
            { Encode =
                (fun w v ->
                    let value: float = unbox v
                    w.WriteString(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))
              Decode = (fun src -> let struct (v, s) = Runtime.floatDecoder src in struct (box v, s))
              MissingValue = None }
        | Primitive t when t = typeof<decimal> ->
            { Encode =
                (fun w v ->
                    let value: decimal = unbox v
                    w.WriteString(value.ToString(System.Globalization.CultureInfo.InvariantCulture)))
              Decode = (fun src -> let struct (v, s) = Runtime.decimalDecoder src in struct (box v, s))
              MissingValue = None }
        | Primitive t when t = typeof<string> ->
            { Encode =
                (fun w v ->
                    let value: string = unbox v
                    w.WriteByte(34uy)

                    ///
                    /// JSON strings must escape structural and control
                    /// characters or round-trip behavior becomes accidental.
                    let mutable segmentStart = 0

                    let flushSegment endIdx =
                        if endIdx > segmentStart then
                            w.WriteString(value.Substring(segmentStart, endIdx - segmentStart))

                    for i in 0 .. value.Length - 1 do
                        match value.[i] with
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
                            w.WriteString(sprintf "\\u%04x" (int c))
                            segmentStart <- i + 1
                        | _ -> ()

                    flushSegment value.Length

                    w.WriteByte(34uy))
              Decode = (fun src -> let struct (v, s) = Runtime.stringDecoder src in struct (box v, s))
              MissingValue = None }
        | Primitive t when t = typeof<bool> ->
            { Encode =
                (fun w v ->
                    if unbox<bool> v then
                        w.WriteString("true")
                    else
                        w.WriteString("false"))
              Decode = (fun src -> let struct (v, s) = Runtime.boolDecoder src in struct (box v, s))
              MissingValue = None }
        | Option innerSchema ->
            let innerCodec = compileUntyped innerSchema
            let optionType = schema.TargetType
            let cases = FSharpType.GetUnionCases(optionType)
            let noneCase = cases |> Array.find (fun c -> c.Name = "None")
            let someCase = cases |> Array.find (fun c -> c.Name = "Some")

            { Encode =
                (fun w v ->
                    if isNull v then
                        w.WriteString("null")
                    else
                        let _, fields = FSharpValue.GetUnionFields(v, optionType)
                        innerCodec.Encode w fields.[0])
              Decode =
                (fun src ->
                    let src = Runtime.skipWhitespace src
                    let data = src.Data

                    if src.Offset < data.Length && data.[src.Offset] = 110uy then
                        let next = Runtime.nullDecoder src
                        struct (FSharpValue.MakeUnion(noneCase, [||]), next)
                    else
                        let struct (value, next) = innerCodec.Decode src
                        struct (FSharpValue.MakeUnion(someCase, [| value |]), next))
              MissingValue = None }
        | MissingAsNone innerSchema ->
            let innerCodec = compileUntyped innerSchema
            let optionType = schema.TargetType

            { Encode = innerCodec.Encode
              Decode = innerCodec.Decode
              MissingValue = Some(Runtime.makeOptionNone optionType) }
        | EmptyStringAsNone innerSchema ->
            let innerCodec = compileUntyped innerSchema
            let optionType = schema.TargetType
            let noneValue = Runtime.makeOptionNone optionType

            { Encode = innerCodec.Encode
              Decode =
                (fun src ->
                    let src = Runtime.skipWhitespace src
                    let data = src.Data

                    if src.Offset < data.Length && data.[src.Offset] = 34uy then
                        let struct (text, next) = Runtime.stringDecoder src

                        if text = "" then
                            struct (noneValue, next)
                        else
                            innerCodec.Decode src
                    else
                        innerCodec.Decode src)
              MissingValue = innerCodec.MissingValue }
        | Record(t, fields, ctor) ->
            let compiledFields =
                fields
                |> Array.mapi (fun i f ->
                    let codec = compileUntyped f.Schema

                    {| Name = f.Name
                       Index = i
                       Codec = codec |})

            let encoder (writer: IByteWriter) (vObj: obj) =
                writer.WriteByte(123uy)
                let mutable first = true

                for f in compiledFields do
                    if not first then
                        writer.WriteByte(44uy)

                    writer.WriteByte(34uy)
                    writer.WriteString(f.Name)
                    writer.WriteByte(34uy)
                    writer.WriteByte(58uy)
                    f.Codec.Encode writer (fields.[f.Index].GetValue vObj)
                    first <- false

                writer.WriteByte(125uy)

            let decoder (src: JsonSource) =
                let src = Runtime.skipWhitespace src

                if src.Offset >= src.Data.Length || src.Data.[src.Offset] <> 123uy then
                    failwith "Expected {"

                let data = src.Data
                let mutable current = src.Advance(1)
                let fieldSources = Array.zeroCreate compiledFields.Length
                let mutable looping = true
                current <- Runtime.skipWhitespace current

                if current.Offset < data.Length && data.[current.Offset] = 125uy then
                    looping <- false
                    current <- current.Advance(1)

                while looping do
                    let struct (key, afterKey) = Runtime.stringDecoder current
                    let afterColon = Runtime.skipWhitespace afterKey

                    if afterColon.Offset >= data.Length || data.[afterColon.Offset] <> 58uy then
                        failwith "Expected :"

                    let valSrc = Runtime.skipWhitespace (afterColon.Advance(1))
                    let mutable found = false
                    let mutable i = 0

                    while i < compiledFields.Length && not found do
                        let f = compiledFields.[i]

                        if f.Name = key then
                            fieldSources.[f.Index] <- valSrc
                            found <- true
                        else
                            i <- i + 1

                    let afterVal = Runtime.skipWhitespace (Runtime.skipValue valSrc)

                    if afterVal.Offset < data.Length && data.[afterVal.Offset] = 44uy then
                        current <- afterVal.Advance(1)
                    elif afterVal.Offset < data.Length && data.[afterVal.Offset] = 125uy then
                        current <- afterVal.Advance(1)
                        looping <- false
                    else
                        failwith "Expected , or }"

                let args =
                    compiledFields
                    |> Array.map (fun f ->
                        let valSrc = fieldSources.[f.Index]

                        if valSrc.Data = null then
                            match f.Codec.MissingValue with
                            | Some value -> value
                            | None -> failwithf "Missing required key: %s" f.Name
                        else
                            let struct (v, _) = f.Codec.Decode valSrc
                            v)

                struct (ctor args, current)

            { Encode = encoder
              Decode = decoder
              MissingValue = None }
        | List innerSchema ->
            let innerCodec = compileUntyped innerSchema

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

                if src.Offset >= src.Data.Length || src.Data.[src.Offset] <> 91uy then
                    failwith "Expected ["

                src <- src.Advance(1)
                let mutable results = []
                let mutable continueLoop = true
                src <- Runtime.skipWhitespace src

                if src.Offset < src.Data.Length && src.Data.[src.Offset] = 93uy then
                    continueLoop <- false
                    src <- src.Advance(1)

                while continueLoop do
                    let struct (item, nextSrc) = innerCodec.Decode src
                    results <- item :: results
                    src <- Runtime.skipWhitespace nextSrc

                    if src.Offset < src.Data.Length && src.Data.[src.Offset] = 44uy then
                        src <- src.Advance(1)
                    elif src.Offset < src.Data.Length && src.Data.[src.Offset] = 93uy then
                        continueLoop <- false
                        src <- src.Advance(1)
                    else
                        failwith "Expected , or ]"

                struct (Runtime.makeList (innerSchema.TargetType) (List.rev results |> List.toArray), src)

            { Encode = encoder
              Decode = decoder
              MissingValue = None }
        | Array innerSchema ->
            let innerCodec = compileUntyped innerSchema

            let encoder (writer: IByteWriter) (vObj: obj) =
#if !FABLE_COMPILER
                let arr = vObj :?> System.Array
                writer.WriteByte(91uy)
                let mutable first = true

                for i in 0 .. arr.Length - 1 do
                    if not first then
                        writer.WriteByte(44uy)

                    innerCodec.Encode writer (arr.GetValue(i))
                    first <- false

                writer.WriteByte(93uy)
#else
                let arr = vObj :?> obj array
                writer.WriteByte(91uy)
                let mutable first = true

                for i in 0 .. arr.Length - 1 do
                    if not first then
                        writer.WriteByte(44uy)

                    innerCodec.Encode writer (arr.[i])
                    first <- false

                writer.WriteByte(93uy)
#endif

            let decoder (src: JsonSource) =
                let mutable src = Runtime.skipWhitespace src

                if src.Offset >= src.Data.Length || src.Data.[src.Offset] <> 91uy then
                    failwith "Expected ["

                src <- src.Advance(1)
                let results = ResizeArray<obj>()
                let mutable continueLoop = true
                src <- Runtime.skipWhitespace src

                if src.Offset < src.Data.Length && src.Data.[src.Offset] = 93uy then
                    continueLoop <- false
                    src <- src.Advance(1)

                while continueLoop do
                    let struct (item, nextSrc) = innerCodec.Decode src
                    results.Add(item)
                    src <- Runtime.skipWhitespace nextSrc

                    if src.Offset < src.Data.Length && src.Data.[src.Offset] = 44uy then
                        src <- src.Advance(1)
                    elif src.Offset < src.Data.Length && src.Data.[src.Offset] = 93uy then
                        continueLoop <- false
                        src <- src.Advance(1)
                    else
                        failwith "Expected , or ]"

#if !FABLE_COMPILER
                let targetArray = System.Array.CreateInstance(innerSchema.TargetType, results.Count)

                for i in 0 .. results.Count - 1 do
                    targetArray.SetValue(results.[i], i)

                struct (box targetArray, src)
#else
                struct (box (results.ToArray()), src)
#endif

            { Encode = encoder
              Decode = decoder
              MissingValue = None }
        | Map(inner, wrap, unwrapFunc) ->
            let innerCodec = compileUntyped inner

            { Encode = (fun w v -> innerCodec.Encode w (unwrapFunc v))
              Decode = (fun src -> let struct (v, s) = innerCodec.Decode src in struct (wrap v, s))
              MissingValue = innerCodec.MissingValue |> Option.map wrap }
        | _ -> failwithf "Unsupported schema type: %O" schema.Definition

    let compile (schema: Schema<'T>) : Codec<'T> =
        let compiled = compileUntyped (schema :> ISchema)

        { Encode = (fun w v -> compiled.Encode w (box v))
          Decode = (fun src -> let struct (v, s) = compiled.Decode src in struct (unbox v, s)) }

    let serialize (codec: Codec<'T>) (value: 'T) =
        let writer = ResizableBuffer.Create(128)
        codec.Encode writer value
        Encoding.UTF8.GetString(writer.InternalData, 0, writer.InternalCount)

    let deserialize (codec: Codec<'T>) (json: string) =
        let bytes = Encoding.UTF8.GetBytes(json)
        let struct (v, rest) = codec.Decode(ByteSource(bytes, 0))
        let rest = Runtime.skipWhitespace rest

        if rest.Offset <> bytes.Length then
            failwith "Trailing content after top-level JSON value"

        v

    let deserializeBytes (codec: Codec<'T>) (bytes: byte[]) =
        let struct (v, rest) = codec.Decode(ByteSource(bytes, 0))
        let rest = Runtime.skipWhitespace rest

        if rest.Offset <> bytes.Length then
            failwith "Trailing content after top-level JSON value"

        v

module Xml =
    type XmlSource = ByteSource
    type XmlWriter = IByteWriter

    type Codec<'T> =
        { Encode: XmlWriter -> 'T -> unit
          Decode: XmlSource -> struct ('T * XmlSource) }

    type CompiledCodec =
        { Encode: XmlWriter -> string -> obj -> unit
          Decode: XmlSource -> string -> struct (obj * XmlSource)
          MissingValue: obj option }

    module internal Runtime =
        let inline skipWhitespace (src: XmlSource) =
            let mutable i = src.Offset
            let data = src.Data

            while i < data.Length
                  && (data.[i] = 32uy || data.[i] = 10uy || data.[i] = 13uy || data.[i] = 9uy) do
                i <- i + 1

            ByteSource(data, i)

        ///
        /// The XML surface is intentionally small: element tags and escaped
        /// text nodes, with no attributes or mixed content.
        let expectOpenTag (tag: string) (src: XmlSource) =
            let src = skipWhitespace src
            let data = src.Data

            if src.Offset >= data.Length || data.[src.Offset] <> 60uy then
                failwith "Expected <"

            let mutable i = src.Offset + 1

            if i < data.Length && data.[i] = 47uy then
                failwithf "Expected <%s>" tag

            let start = i

            while i < data.Length && data.[i] <> 62uy do
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
                || data.[src.Offset] <> 60uy
                || data.[src.Offset + 1] <> 47uy
            then
                failwithf "Expected </%s>" tag

            let mutable i = src.Offset + 2
            let start = i

            while i < data.Length && data.[i] <> 62uy do
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
                || data.[src.Offset] <> 60uy
                || data.[src.Offset + 1] <> 47uy
            then
                None
            else
                let mutable i = src.Offset + 2
                let start = i

                while i < data.Length && data.[i] <> 62uy do
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
                match value.[i] with
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

            while i < data.Length && data.[i] <> 60uy do
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
        | Primitive t when t = typeof<int> ->
            { Encode =
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
                    let v = System.Int32.Parse(text.Trim())
                    struct (box v, current))
              MissingValue = None }
        | Primitive t when t = typeof<int64> ->
            { Encode =
                (fun w tag v ->
                    w.WriteByte(60uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy)
                    w.WriteString((unbox<int64> v).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    w.WriteByte(60uy)
                    w.WriteByte(47uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy))
              Decode =
                (fun src tag ->
                    let current = Runtime.expectOpenTag tag src
                    let struct (text, current) = Runtime.readTextNode current
                    let current = Runtime.expectCloseTag tag current

                    let value =
                        System.Int64.Parse(
                            text.Trim(),
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture
                        )

                    struct (box value, current))
              MissingValue = None }
        | Primitive t when t = typeof<uint32> ->
            { Encode =
                (fun w tag v ->
                    w.WriteByte(60uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy)
                    w.WriteString((unbox<uint32> v).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    w.WriteByte(60uy)
                    w.WriteByte(47uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy))
              Decode =
                (fun src tag ->
                    let current = Runtime.expectOpenTag tag src
                    let struct (text, current) = Runtime.readTextNode current
                    let current = Runtime.expectCloseTag tag current

                    let value =
                        System.UInt32.Parse(
                            text.Trim(),
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture
                        )

                    struct (box value, current))
              MissingValue = None }
        | Primitive t when t = typeof<uint64> ->
            { Encode =
                (fun w tag v ->
                    w.WriteByte(60uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy)
                    w.WriteString((unbox<uint64> v).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    w.WriteByte(60uy)
                    w.WriteByte(47uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy))
              Decode =
                (fun src tag ->
                    let current = Runtime.expectOpenTag tag src
                    let struct (text, current) = Runtime.readTextNode current
                    let current = Runtime.expectCloseTag tag current

                    let value =
                        System.UInt64.Parse(
                            text.Trim(),
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture
                        )

                    struct (box value, current))
              MissingValue = None }
        | Primitive t when t = typeof<float> ->
            { Encode =
                (fun w tag v ->
                    w.WriteByte(60uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy)
                    w.WriteString((unbox<float> v).ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                    w.WriteByte(60uy)
                    w.WriteByte(47uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy))
              Decode =
                (fun src tag ->
                    let current = Runtime.expectOpenTag tag src
                    let struct (text, current) = Runtime.readTextNode current
                    let current = Runtime.expectCloseTag tag current

                    let value =
                        System.Double.Parse(
                            text.Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture
                        )

                    struct (box value, current))
              MissingValue = None }
        | Primitive t when t = typeof<decimal> ->
            { Encode =
                (fun w tag v ->
                    w.WriteByte(60uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy)
                    w.WriteString((unbox<decimal> v).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    w.WriteByte(60uy)
                    w.WriteByte(47uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy))
              Decode =
                (fun src tag ->
                    let current = Runtime.expectOpenTag tag src
                    let struct (text, current) = Runtime.readTextNode current
                    let current = Runtime.expectCloseTag tag current

                    let value =
                        System.Decimal.Parse(
                            text.Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture
                        )

                    struct (box value, current))
              MissingValue = None }
        | Primitive t when t = typeof<string> ->
            { Encode =
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
              MissingValue = None }
        | Primitive t when t = typeof<bool> ->
            { Encode =
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
              MissingValue = None }
        | Option innerSchema ->
            let innerCodec = compileUntyped innerSchema
            let optionType = schema.TargetType

            { Encode =
                (fun w tag v ->
                    w.WriteByte(60uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy)

                    if not (isNull v) then
                        innerCodec.Encode w "some" (FSharpValue.GetUnionFields(v, optionType) |> snd |> Array.item 0)

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
              MissingValue = None }
        | MissingAsNone innerSchema ->
            let innerCodec = compileUntyped innerSchema
            let optionType = schema.TargetType

            { Encode = innerCodec.Encode
              Decode = innerCodec.Decode
              MissingValue = Some(Runtime.makeOptionNone optionType) }
        | EmptyStringAsNone innerSchema ->
            let innerCodec = compileUntyped innerSchema
            let optionType = schema.TargetType
            let noneValue = Runtime.makeOptionNone optionType

            { Encode = innerCodec.Encode
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
                            && fields.[0] :? string
                            && unbox<string> fields.[0] = ""
                        then
                            struct (noneValue, next)
                        else
                            struct (value, next))
              MissingValue = innerCodec.MissingValue }
        | Record(t, fields, ctor) ->
            let compiledFields =
                fields
                |> Array.map (fun f ->
                    {| Name = f.Name
                       Codec = compileUntyped f.Schema
                       GetValue = f.GetValue |})

            { Encode =
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
              MissingValue = None }
        | List innerSchema ->
            let innerCodec = compileUntyped innerSchema

            { Encode =
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
              MissingValue = None }
        | Array innerSchema ->
            let innerCodec = compileUntyped innerSchema

            { Encode =
                (fun w tag vObj ->
#if !FABLE_COMPILER
                    let arr = vObj :?> System.Array
#else
                    let arr = vObj :?> obj array
#endif
                    w.WriteByte(60uy)
                    w.WriteString(tag)
                    w.WriteByte(62uy)

#if !FABLE_COMPILER
                    for i in 0 .. arr.Length - 1 do
                        innerCodec.Encode w "item" (arr.GetValue(i))
#else
                    for i in 0 .. arr.Length - 1 do
                        innerCodec.Encode w "item" arr.[i]
#endif

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
                        targetArray.SetValue(results.[i], i)

                    struct (box targetArray, current)
#else
                    struct (box (results.ToArray()), current)
#endif
                )
              MissingValue = None }
        | Map(inner, wrap, unwrapFunc) ->
            let innerCodec = compileUntyped inner

            { Encode = (fun w tag v -> innerCodec.Encode w tag (unwrapFunc v))
              Decode = (fun src tag -> let struct (v, s) = innerCodec.Decode src tag in struct (wrap v, s))
              MissingValue = innerCodec.MissingValue |> Option.map wrap }
        | _ -> failwithf "Unsupported XML schema type"

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

        { Encode = (fun w v -> compiled.Encode w rootTag (box v))
          Decode = (fun src -> let struct (v, s) = compiled.Decode src rootTag in struct (unbox v, s)) }

    let serialize (codec: Codec<'T>) (value: 'T) =
        let writer = ResizableBuffer.Create(128)
        codec.Encode writer value
        Encoding.UTF8.GetString(writer.InternalData, 0, writer.InternalCount)

    let deserialize (codec: Codec<'T>) (xml: string) =
        let bytes = Encoding.UTF8.GetBytes(xml)
        let struct (v, rest) = codec.Decode(ByteSource(bytes, 0))
        let rest = Runtime.skipWhitespace rest

        if rest.Offset <> bytes.Length then
            failwith "Trailing content after top-level XML value"

        v

    let deserializeBytes (codec: Codec<'T>) (bytes: byte[]) =
        let struct (v, rest) = codec.Decode(ByteSource(bytes, 0))
        let rest = Runtime.skipWhitespace rest

        if rest.Offset <> bytes.Length then
            failwith "Trailing content after top-level XML value"

        v
