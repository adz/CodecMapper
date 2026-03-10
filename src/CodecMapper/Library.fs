// `CodecMapper` is a schema-first codec library for explicit wire contracts.
//
// Start in `Schema` to describe the wire shape, then compile that schema in
// `Json` or `Xml` depending on the format boundary you need to talk to.
namespace CodecMapper

open System.Text
open System.Collections.Generic
open Microsoft.FSharp.Reflection
open System.Diagnostics.CodeAnalysis

/// Low-level byte reading and writing primitives shared by the JSON and XML runtimes.
[<AutoOpen>]
module Core =
    /// A lightweight context for reading bytes.
    [<Struct>]
    type ByteSource =
        val Data: byte[]
        val Offset: int

        new(data, offset) = { Data = data; Offset = offset }

        /// Advances the source by `n` bytes.
        member inline x.Advance(n: int) = ByteSource(x.Data, x.Offset + n)

        /// Returns a copy of the source with an explicit absolute offset.
        member inline x.SetOffset(n: int) = ByteSource(x.Data, n)

    /// Functional helpers over `ByteSource`.
    module ByteSource =
        /// Advances the source by `n` bytes.
        let inline advance (n: int) (src: ByteSource) = src.Advance(n)

        /// Returns a copy of the source with an explicit absolute offset.
        let inline setOffset (n: int) (src: ByteSource) = src.SetOffset(n)

    /// Abstraction for writing bytes, to be implemented per target platform.
    type IByteWriter =
        /// Ensures that at least `n` more bytes can be written without reallocating.
        abstract member Ensure: int -> unit

        /// Writes a single byte.
        abstract member WriteByte: byte -> unit

        /// Writes a UTF-8 string payload.
        abstract member WriteString: string -> unit

        /// Writes an integer value.
        abstract member WriteInt: int -> unit

        /// Exposes the current backing storage.
        abstract member Data: byte[]

        /// Exposes the number of written bytes.
        abstract member Count: int

    /// Growable in-memory byte buffer used by the built-in codecs.
    type ResizableBuffer = {
        mutable InternalData: byte[]
        mutable InternalCount: int
    } with

        /// Creates a new buffer with the requested initial capacity.
        static member Create(initialCapacity: int) = {
            InternalData = Array.zeroCreate initialCapacity
            InternalCount = 0
        }

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

/// Represents arbitrary JSON when a contract cannot be lowered into a more precise schema.
///
/// This is the escape hatch for dynamic-key objects, heterogeneous arrays, and
/// other JSON Schema shapes that do not fit the normal record/list/primitive
/// model without losing parseability.
type JsonValue =
    | JNull
    | JBool of bool
    | JNumber of string
    | JString of string
    | JArray of JsonValue list
    | JObject of (string * JsonValue) list

/// Captures one named field inside a record schema.
///
/// A `SchemaField` binds the wire name, CLR type, getter, and nested schema
/// for a single record member.
type SchemaField = {
    Name: string
    Type: System.Type
    GetValue: obj -> obj
    Schema: ISchema
}

/// Erased schema abstraction used internally and by advanced integrations.
///
/// Most callers should stay on `Schema<'T>`, but codecs and bridges compile
/// against this untyped representation.
and ISchema =
    abstract member TargetType: System.Type
    abstract member Definition: SchemaDefinition

/// The structural schema shapes understood by the compiler backends.
and SchemaDefinition =
    | Primitive of System.Type
    | Record of System.Type * SchemaField[] * (obj[] -> obj)
    | List of ISchema
    | Array of ISchema
    | Option of ISchema
    | MissingAsNone of ISchema
    | EmptyStringAsNone of ISchema
    | Map of ISchema * (obj -> obj) * (obj -> obj)
    | RawJsonValue

/// A typed schema for values of `'T`.
///
/// Schemas are pure descriptions. Compile them with `Json.compile` or
/// `Xml.compile` to get executable codecs.
type Schema<'T> =
    inherit ISchema

/// Builder state for the Pipeline DSL.
///
/// The builder keeps the constructor's remaining type in `'Ctor` so each
/// appended field proves, at compile time, that one more argument has been
/// supplied before `build` can close over the final record value.
type Builder<'T, 'Ctor> = {
    Fields: SchemaField list

    ///
    /// We evaluate the curried constructor from left to right against decoded
    /// field values stored in the compiler's `obj[]` buffer. This keeps the
    /// public DSL typed while still fitting the existing record compiler.
    App: obj[] -> int -> 'Ctor
}

/// Helpers for building explicit schemas and reusing common built-in shapes.
///
/// This is the main authoring surface for `CodecMapper`. Build a `Schema<'T>`
/// once, then compile it into JSON or XML codecs.
module Schema =
    /// Exposes the erased schema view for advanced integrations.
    let unwrap (s: ISchema) = s

    /// Creates a schema from a raw structural definition.
    ///
    /// This is public for advanced integrations, but most callers should
    /// prefer the higher-level helpers in this module.
    let inline create<'T> def =
        { new Schema<'T> with
            member _.TargetType = typeof<'T>
            member _.Definition = def
        }

    /// A schema for `int`.
    let int: Schema<int> = create (Primitive typeof<int>)

    /// A schema for `int64`.
    let int64: Schema<int64> = create (Primitive typeof<int64>)

    /// A schema for `uint32`.
    let uint32: Schema<uint32> = create (Primitive typeof<uint32>)

    /// A schema for `uint64`.
    let uint64: Schema<uint64> = create (Primitive typeof<uint64>)

    /// A schema for `float`.
    let float: Schema<float> = create (Primitive typeof<float>)

    /// A schema for `decimal`.
    let decimal: Schema<decimal> = create (Primitive typeof<decimal>)

    /// A schema for `string`.
    let string: Schema<string> = create (Primitive typeof<string>)

    /// A schema for `bool`.
    let bool: Schema<bool> = create (Primitive typeof<bool>)

    /// Projects an existing schema through total wrap and unwrap functions.
    ///
    /// Use this when the wire shape is unchanged but the in-memory model uses
    /// a wrapper type.
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
    let inline private rangedInt<'T>
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

    /// A schema for `int16`.
    let int16: Schema<int16> =
        rangedInt
            "int16"
            (System.Convert.ToInt32(System.Int16.MinValue))
            (System.Convert.ToInt32(System.Int16.MaxValue))
            System.Convert.ToInt16
            System.Convert.ToInt32

    /// A schema for `byte`.
    let byte: Schema<byte> =
        rangedInt "byte" 0 255 System.Convert.ToByte System.Convert.ToInt32

    /// A schema for `sbyte`.
    let sbyte: Schema<sbyte> =
        rangedInt
            "sbyte"
            (System.Convert.ToInt32(System.SByte.MinValue))
            (System.Convert.ToInt32(System.SByte.MaxValue))
            System.Convert.ToSByte
            System.Convert.ToInt32

    /// A schema for `uint16`.
    let uint16: Schema<uint16> =
        rangedInt
            "uint16"
            0
            (System.Convert.ToInt32(System.UInt16.MaxValue))
            System.Convert.ToUInt16
            System.Convert.ToInt32

    /// A schema for `Guid` using the round-trippable `"D"` string format.
    ///
    /// Common domain identity and timestamp types ride on top of the string
    /// codec so JSON and XML stay symmetric without extra parser branches.
    let guid: Schema<System.Guid> =
        string |> map System.Guid.Parse (fun value -> value.ToString("D"))

    /// A schema for `char` backed by a single-character string.
    let char: Schema<char> =
        string
        |> map
            (fun value ->
                if value.Length <> 1 then
                    failwithf "char value must contain exactly one character, got %d" value.Length

                value.[0])
            (fun value -> value.ToString())

    /// A schema for `DateTime` using the round-trippable `"O"` string format.
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

    /// A schema for `DateTimeOffset` using the round-trippable `"O"` string format.
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

    /// A schema for `TimeSpan` using the invariant `"c"` format.
    let timeSpan: Schema<System.TimeSpan> =
        string
        |> map
            (fun value -> System.TimeSpan.ParseExact(value, "c", System.Globalization.CultureInfo.InvariantCulture))
            (fun value -> value.ToString("c", System.Globalization.CultureInfo.InvariantCulture))

    /// Builds a schema for an F# list.
    let inline list (inner: Schema<'T>) : Schema<'T list> = create (List(inner :> ISchema))

    /// Builds a schema for an array.
    let inline array (inner: Schema<'T>) : Schema<'T[]> = create (Array(inner :> ISchema))

    /// Builds a schema for `ResizeArray<'T>` / `List<T>`.
    ///
    /// .NET interop often surfaces mutable list shapes even when the wire
    /// contract is just a homogeneous JSON or XML array.
    let inline resizeArray (inner: Schema<'T>) : Schema<ResizeArray<'T>> =
        array inner |> map ResizeArray (fun (items: ResizeArray<'T>) -> items.ToArray())

    /// Builds a schema for `IReadOnlyList<'T>`.
    ///
    /// Keep the wire form identical to arrays while still allowing .NET-facing
    /// APIs to expose read-only collection interfaces.
    let inline readOnlyList (inner: Schema<'T>) : Schema<IReadOnlyList<'T>> = create (Array(inner :> ISchema))

    /// Builds a schema for `ICollection<'T>`.
    ///
    /// This preserves the normal array wire shape while interoperating with
    /// common mutable collection interfaces from .NET APIs.
    let inline collection (inner: Schema<'T>) : Schema<ICollection<'T>> = create (Array(inner :> ISchema))

    /// Builds a schema for an option value.
    ///
    /// The default semantics are strict: `None` is explicit on the wire, and
    /// missing fields still fail unless you add a field-policy wrapper.
    let inline option (inner: Schema<'T>) : Schema<'T option> = create (Option(inner :> ISchema))

    /// Builds a schema for arbitrary JSON values.
    ///
    /// This is the explicit fallback for imported contracts that cannot be
    /// represented as a deterministic record/array/primitive schema without a
    /// separate normalization or validation step.
    let jsonValue: Schema<JsonValue> = create RawJsonValue

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

    /// Resolves a built-in schema for a CLR type.
    ///
    /// This powers automatic field resolution for primitives, F# collections,
    /// selected .NET collection interfaces, options, and the common built-in
    /// wrapper types.
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
        elif t = typeof<JsonValue> then
            jsonValue :> ISchema
        elif t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>> then
            let innerType = t.GetGenericArguments().[0]
            let innerSchema = resolveSchema innerType

            { new ISchema with
                member _.TargetType = t
                member _.Definition = Option(innerSchema)
            }
        elif
            t.IsGenericType
            && t.GetGenericTypeDefinition() = typeof<list<_>>.GetGenericTypeDefinition()
        then
            let innerType = t.GetGenericArguments().[0]
            let innerSchema = resolveSchema innerType

            { new ISchema with
                member _.TargetType = t
                member _.Definition = List(innerSchema)
            }
        elif t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<IReadOnlyList<_>> then
            let innerType = t.GetGenericArguments().[0]
            let innerSchema = resolveSchema innerType

            { new ISchema with
                member _.TargetType = t
                member _.Definition = Array(innerSchema)
            }
        elif t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<ICollection<_>> then
            let innerType = t.GetGenericArguments().[0]
            let innerSchema = resolveSchema innerType

            { new ISchema with
                member _.TargetType = t
                member _.Definition = Array(innerSchema)
            }
        elif t.IsArray then
            let elementType = t.GetElementType()
            let elementSchema = resolveSchema elementType

            { new ISchema with
                member _.TargetType = t
                member _.Definition = Array(elementSchema)
            }
        else
            failwithf "Could not automatically resolve schema for type %O. Please provide it explicitly." t

    /// Pipeline DSL helpers.
    ///
    /// The pipeline starts by capturing the curried constructor up front so
    /// subsequent field steps only describe the wire layout.
    ///
    /// We still ask for `'T` explicitly because relying on field-label
    /// inference alone becomes brittle as soon as multiple record types share
    /// names like `Id` or `Name`.
    ///
    /// Starts a pipeline schema definition for `'T`.
    let inline define<'T> : Builder<'T, unit> = { Fields = []; App = (fun _ _ -> ()) }

    /// Captures the constructor used to rebuild `'T` during decoding.
    let inline construct (ctor: 'Ctor) (builder: Builder<'T, unit>) : Builder<'T, 'Ctor> = {
        Fields = builder.Fields
        App = (fun _ _ -> ctor)
    }

    /// Adds a field that can be resolved automatically from its type.
    let inline field
        (name: string)
        (getter: 'T -> 'Field)
        (builder: Builder<'T, 'Field -> 'Next>)
        : Builder<'T, 'Next> =
        let schema = resolveSchema typeof<'Field>

        let f = {
            Name = name
            Type = typeof<'Field>
            GetValue = (fun r -> box (getter (unbox r)))
            Schema = schema
        }

        let nextApp (args: obj[]) (idx: int) =
            let fCurried = builder.App args (idx - 1)
            let arg = unbox<'Field> args.[idx]
            fCurried arg

        {
            Fields = f :: builder.Fields
            App = nextApp
        }

    /// Adds a field with an explicit nested schema.
    ///
    /// Use this when the field type needs a custom schema or should not rely
    /// on automatic resolution.
    let inline fieldWith
        (name: string)
        (getter: 'T -> 'Field)
        (schema: Schema<'Field>)
        (builder: Builder<'T, 'Field -> 'Next>)
        : Builder<'T, 'Next> =
        let f = {
            Name = name
            Type = typeof<'Field>
            GetValue = (fun r -> box (getter (unbox r)))
            Schema = unwrap schema
        }

        let nextApp (args: obj[]) (idx: int) =
            let fCurried = builder.App args (idx - 1)
            let arg = unbox<'Field> args.[idx]
            fCurried arg

        {
            Fields = f :: builder.Fields
            App = nextApp
        }

    /// Closes a fully-applied pipeline and returns the schema for `'T`.
    let inline build (builder: Builder<'T, 'T>) : Schema<'T> =
        let fields = builder.Fields |> List.rev |> List.toArray
        let targetType = typeof<'T>

        let buildFunc (args: obj[]) =
            box (builder.App args (args.Length - 1))

        create<'T> (Record(targetType, fields, buildFunc))

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

        let rec jsonValueDecoderAt depth (src: JsonSource) : struct (JsonValue * JsonSource) =
            if depth > maxJsonDepth then
                failwith "Maximum JSON nesting depth exceeded"

            let src = skipWhitespace src

            if src.Offset >= src.Data.Length then
                failwith "Unexpected end of input"

            let data = src.Data

            match data.[src.Offset] with
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

                if current.Offset < data.Length && data.[current.Offset] = 93uy then
                    current <- current.Advance(1)
                    looping <- false

                while looping do
                    let struct (item, next) = jsonValueDecoderAt (depth + 1) current
                    items.Add(item)

                    let afterItem = skipWhitespace next

                    if afterItem.Offset < data.Length && data.[afterItem.Offset] = 44uy then
                        current <- skipWhitespace (afterItem.Advance(1))
                    elif afterItem.Offset < data.Length && data.[afterItem.Offset] = 93uy then
                        current <- afterItem.Advance(1)
                        looping <- false
                    else
                        failwith "Expected , or ]"

                struct (JArray(List.ofSeq items), current)
            | 123uy ->
                let mutable current = skipWhitespace (src.Advance(1))
                let fields = ResizeArray<string * JsonValue>()
                let mutable looping = true

                if current.Offset < data.Length && data.[current.Offset] = 125uy then
                    current <- current.Advance(1)
                    looping <- false

                while looping do
                    let struct (key, afterKey) = stringDecoder current
                    let afterColon = skipWhitespace afterKey

                    if afterColon.Offset >= data.Length || data.[afterColon.Offset] <> 58uy then
                        failwith "Expected :"

                    let struct (value, next) = jsonValueDecoderAt (depth + 1) (afterColon.Advance(1))
                    fields.Add(key, value)

                    let afterValue = skipWhitespace next

                    if afterValue.Offset < data.Length && data.[afterValue.Offset] = 44uy then
                        current <- skipWhitespace (afterValue.Advance(1))
                    elif afterValue.Offset < data.Length && data.[afterValue.Offset] = 125uy then
                        current <- afterValue.Advance(1)
                        looping <- false
                    else
                        failwith "Expected , or }"

                struct (JObject(List.ofSeq fields), current)
            | _ ->
                let struct (token, next) = numberToken true src
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
                    w.WriteString(value.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            Decode = (fun src -> let struct (v, s) = Runtime.int64Decoder src in struct (box v, s))
            MissingValue = None
          }
        | Primitive t when t = typeof<uint32> -> {
            Encode =
                (fun w v ->
                    let value: uint32 = unbox v
                    w.WriteString(value.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            Decode = (fun src -> let struct (v, s) = Runtime.uint32Decoder src in struct (box v, s))
            MissingValue = None
          }
        | Primitive t when t = typeof<uint64> -> {
            Encode =
                (fun w v ->
                    let value: uint64 = unbox v
                    w.WriteString(value.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            Decode = (fun src -> let struct (v, s) = Runtime.uint64Decoder src in struct (box v, s))
            MissingValue = None
          }
        | Primitive t when t = typeof<float> -> {
            Encode =
                (fun w v ->
                    let value: float = unbox v
                    w.WriteString(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))
            Decode = (fun src -> let struct (v, s) = Runtime.floatDecoder src in struct (box v, s))
            MissingValue = None
          }
        | Primitive t when t = typeof<decimal> -> {
            Encode =
                (fun w v ->
                    let value: decimal = unbox v
                    w.WriteString(value.ToString(System.Globalization.CultureInfo.InvariantCulture)))
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
                        writer.WriteString(value.Substring(segmentStart, endIdx - segmentStart))

                for i in 0 .. value.Length - 1 do
                    match value.[i] with
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
                        writer.WriteString(sprintf "\\u%04x" (int c))
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

                        if src.Offset < data.Length && data.[src.Offset] = 34uy then
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

                    {|
                        Name = f.Name
                        Index = i
                        Codec = codec
                    |})

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

            {
                Encode = encoder
                Decode = decoder
                MissingValue = None
            }
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

            {
                Encode = encoder
                Decode = decoder
                MissingValue = None
            }
        | Map(inner, wrap, unwrapFunc) ->
            let innerCodec = compileUntyped inner

            {
                Encode = (fun w v -> innerCodec.Encode w (unwrapFunc v))
                Decode = (fun src -> let struct (v, s) = innerCodec.Decode src in struct (wrap v, s))
                MissingValue = innerCodec.MissingValue |> Option.map wrap
            }
        | _ -> failwithf "Unsupported schema type: %O" schema.Definition

    /// Compiles a schema into a reusable JSON codec.
    let compile (schema: Schema<'T>) : Codec<'T> =
        let compiled = compileUntyped (schema :> ISchema)

        {
            Encode = (fun w v -> compiled.Encode w (box v))
            Decode = (fun src -> let struct (v, s) = compiled.Decode src in struct (unbox v, s))
        }

    /// Serializes a value to JSON using a previously compiled codec.
    let serialize (codec: Codec<'T>) (value: 'T) =
        let writer = ResizableBuffer.Create(128)
        codec.Encode writer value
        Encoding.UTF8.GetString(writer.InternalData, 0, writer.InternalCount)

    /// Deserializes a JSON payload using a previously compiled codec.
    ///
    /// The entire payload must be consumed. Trailing content is treated as an
    /// error rather than ignored.
    let deserialize (codec: Codec<'T>) (json: string) =
        let bytes = Encoding.UTF8.GetBytes(json)
        let struct (v, rest) = codec.Decode(ByteSource(bytes, 0))
        let rest = Runtime.skipWhitespace rest

        if rest.Offset <> bytes.Length then
            failwith "Trailing content after top-level JSON value"

        v

    /// Deserializes a UTF-8 byte payload using a previously compiled codec.
    let deserializeBytes (codec: Codec<'T>) (bytes: byte[]) =
        let struct (v, rest) = codec.Decode(ByteSource(bytes, 0))
        let rest = Runtime.skipWhitespace rest

        if rest.Offset <> bytes.Length then
            failwith "Trailing content after top-level JSON value"

        v

/// JSON Schema export for the JSON wire shape described by `Schema<'T>`.
///
/// This exports the structural JSON contract implied by the schema. Mapping
/// wrappers such as `Schema.map` and `Schema.tryMap` contribute the underlying
/// wire shape, while field-policy wrappers affect whether object properties are
/// listed as required.
module JsonSchema =
    /// Validates a JSON Schema `format` string against domain-specific rules.
    type FormatValidator = string -> Result<unit, string>

    /// Options for JSON Schema import.
    ///
    /// Import stays structural by default, but callers can opt into extra
    /// string-format checks by registering validators for specific `format`
    /// names.
    type ImportOptions = {
        FormatValidators: (string * FormatValidator) list
    }

    /// Helpers for building JSON Schema import options.
    module ImportOptions =
        let private withOrReplace
            (name: string)
            (validator: FormatValidator)
            (validators: (string * FormatValidator) list)
            =
            (name, validator)
            :: (validators |> List.filter (fun (existing, _) -> existing <> name))

        /// No custom format validators.
        let empty = { FormatValidators = [] }

        /// Adds or replaces one `format` validator.
        let withFormat (name: string) (validator: FormatValidator) (options: ImportOptions) = {
            options with
                FormatValidators = withOrReplace name validator options.FormatValidators
        }

        /// Built-in validators for the most common round-trippable string formats.
        let defaults =
            empty
            |> withFormat "uuid" (fun value ->
                match System.Guid.TryParse(value) with
                | true, _ -> Ok()
                | false, _ -> Error "String did not match the uuid format")
            |> withFormat "date-time" (fun value ->
                match
                    System.DateTimeOffset.TryParse(
                        value,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind
                    )
                with
                | true, _ -> Ok()
                | false, _ -> Error "String did not match the date-time format")

    /// Describes the outcome of importing a JSON Schema into `Schema<JsonValue>`.
    ///
    /// The imported schema always decodes through the raw JSON DOM fallback.
    /// These diagnostics explain which keywords were actively enforced and
    /// which keywords caused the importer to stay on the raw-fallback path.
    type ImportReport = {
        Schema: Schema<JsonValue>
        EnforcedKeywords: string list
        NormalizedKeywords: string list
        FallbackKeywords: string list
        Warnings: string list
    }

    type private Node =
        | Any
        | Null
        | Boolean
        | Integer
        | Number
        | String
        | ArrayNode of Node
        | ObjectNode of title: string option * properties: (string * Node) array * required: string array
        | AnyOfNode of Node array

    let private escapeJsonString (value: string) =
        let builder = System.Text.StringBuilder(value.Length + 8)

        for ch in value do
            match ch with
            | '"' -> builder.Append("\\\"") |> ignore
            | '\\' -> builder.Append("\\\\") |> ignore
            | '\b' -> builder.Append("\\b") |> ignore
            | '\f' -> builder.Append("\\f") |> ignore
            | '\n' -> builder.Append("\\n") |> ignore
            | '\r' -> builder.Append("\\r") |> ignore
            | '\t' -> builder.Append("\\t") |> ignore
            | c when int c < 32 ->
                builder.Append("\\u") |> ignore
                builder.Append((int c).ToString("x4")) |> ignore
            | c -> builder.Append(c) |> ignore

        builder.ToString()

    let private appendQuoted (builder: System.Text.StringBuilder) (value: string) =
        builder.Append('"') |> ignore
        builder.Append(escapeJsonString value) |> ignore
        builder.Append('"') |> ignore

    let rec private appendNode (builder: System.Text.StringBuilder) (node: Node) =
        let appendTypeObject typeName =
            builder.Append("{\"type\":") |> ignore
            appendQuoted builder typeName
            builder.Append('}') |> ignore

        match node with
        | Any -> builder.Append("{}") |> ignore
        | Null -> appendTypeObject "null"
        | Boolean -> appendTypeObject "boolean"
        | Integer -> appendTypeObject "integer"
        | Number -> appendTypeObject "number"
        | String -> appendTypeObject "string"
        | ArrayNode items ->
            builder.Append("{\"type\":\"array\",\"items\":") |> ignore
            appendNode builder items
            builder.Append('}') |> ignore
        | ObjectNode(title, properties, required) ->
            builder.Append("{\"type\":\"object\"") |> ignore

            match title with
            | Some value ->
                builder.Append(",\"title\":") |> ignore
                appendQuoted builder value
            | None -> ()

            builder.Append(",\"properties\":{") |> ignore

            for i in 0 .. properties.Length - 1 do
                if i > 0 then
                    builder.Append(',') |> ignore

                let name, propertyNode = properties.[i]
                appendQuoted builder name
                builder.Append(':') |> ignore
                appendNode builder propertyNode

            builder.Append('}') |> ignore

            if required.Length > 0 then
                builder.Append(",\"required\":[") |> ignore

                for i in 0 .. required.Length - 1 do
                    if i > 0 then
                        builder.Append(',') |> ignore

                    appendQuoted builder required.[i]

                builder.Append(']') |> ignore

            builder.Append('}') |> ignore
        | AnyOfNode nodes ->
            builder.Append("{\"anyOf\":[") |> ignore

            for i in 0 .. nodes.Length - 1 do
                if i > 0 then
                    builder.Append(',') |> ignore

                appendNode builder nodes.[i]

            builder.Append("]}") |> ignore

    let rec private exportNode (schema: ISchema) =
        match schema.Definition with
        | RawJsonValue -> Any
        | Primitive targetType when targetType = typeof<bool> -> Boolean
        | Primitive targetType when targetType = typeof<float> || targetType = typeof<decimal> -> Number
        | Primitive targetType when targetType = typeof<string> -> String
        | Primitive targetType when targetType = typeof<char> -> String
        | Primitive targetType when targetType = typeof<System.Guid> -> String
        | Primitive targetType when targetType = typeof<System.DateTime> -> String
        | Primitive targetType when targetType = typeof<System.DateTimeOffset> -> String
        | Primitive targetType when targetType = typeof<System.TimeSpan> -> String
        | Primitive _ -> Integer
        | List innerSchema
        | Array innerSchema -> ArrayNode(exportNode innerSchema)
        | Option innerSchema -> AnyOfNode [| exportNode innerSchema; Null |]
        | MissingAsNone innerSchema -> exportNode innerSchema
        | EmptyStringAsNone innerSchema -> exportNode innerSchema
        | Map(innerSchema, _, _) -> exportNode innerSchema
        | Record(targetType, fields, _) ->
            let properties =
                fields |> Array.map (fun field -> field.Name, exportNode field.Schema)

            let required =
                fields
                |> Array.choose (fun field ->
                    match field.Schema.Definition with
                    | MissingAsNone _ -> None
                    | _ -> Some field.Name)

            ObjectNode(Some targetType.Name, properties, required)

    let private appendRootNode (builder: System.Text.StringBuilder) (node: Node) =
        match node with
        | Any -> ()
        | Null -> builder.Append(",\"type\":\"null\"") |> ignore
        | Boolean -> builder.Append(",\"type\":\"boolean\"") |> ignore
        | Integer -> builder.Append(",\"type\":\"integer\"") |> ignore
        | Number -> builder.Append(",\"type\":\"number\"") |> ignore
        | String -> builder.Append(",\"type\":\"string\"") |> ignore
        | ArrayNode items ->
            builder.Append(",\"type\":\"array\",\"items\":") |> ignore
            appendNode builder items
        | ObjectNode(_, properties, required) ->
            builder.Append(",\"type\":\"object\",\"properties\":{") |> ignore

            for i in 0 .. properties.Length - 1 do
                if i > 0 then
                    builder.Append(',') |> ignore

                let name, propertyNode = properties.[i]
                appendQuoted builder name
                builder.Append(':') |> ignore
                appendNode builder propertyNode

            builder.Append('}') |> ignore

            if required.Length > 0 then
                builder.Append(",\"required\":[") |> ignore

                for i in 0 .. required.Length - 1 do
                    if i > 0 then
                        builder.Append(',') |> ignore

                    appendQuoted builder required.[i]

                builder.Append(']') |> ignore
        | AnyOfNode nodes ->
            builder.Append(",\"anyOf\":[") |> ignore

            for i in 0 .. nodes.Length - 1 do
                if i > 0 then
                    builder.Append(',') |> ignore

                appendNode builder nodes.[i]

            builder.Append(']') |> ignore

    let private tryFindProperty (name: string) (properties: (string * JsonValue) list) =
        properties |> List.tryFind (fun (key, _) -> key = name) |> Option.map snd

    let private tryGetStringProperty (name: string) (properties: (string * JsonValue) list) =
        match tryFindProperty name properties with
        | Some(JString value) -> Some value
        | _ -> None

    let private tryGetBoolProperty (name: string) (properties: (string * JsonValue) list) =
        match tryFindProperty name properties with
        | Some(JBool value) -> Some value
        | _ -> None

    let private tryGetIntProperty (name: string) (properties: (string * JsonValue) list) =
        match tryFindProperty name properties with
        | Some(JNumber token) ->
            match
                System.Int32.TryParse(
                    token,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            with
            | true, value -> Some value
            | false, _ -> None
        | _ -> None

    let private tryGetDecimalProperty (name: string) (properties: (string * JsonValue) list) =
        match tryFindProperty name properties with
        | Some(JNumber token) ->
            Some(
                System.Decimal.Parse(
                    token,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            )
        | _ -> None

    let private tryGetArrayProperty (name: string) (properties: (string * JsonValue) list) =
        match tryFindProperty name properties with
        | Some(JArray values) -> Some values
        | _ -> None

    let private tryGetObjectProperty (name: string) (properties: (string * JsonValue) list) =
        match tryFindProperty name properties with
        | Some(JObject values) -> Some values
        | _ -> None

    let private unsupportedImportKeywords = set [ "dependentSchemas"; "not" ]

    let private enforcedImportKeywords =
        set [
            "$defs"
            "$ref"
            "allOf"
            "anyOf"
            "oneOf"
            "if"
            "then"
            "else"
            "type"
            "properties"
            "required"
            "items"
            "additionalProperties"
            "patternProperties"
            "propertyNames"
            "prefixItems"
            "contains"
            "enum"
            "const"
            "minLength"
            "maxLength"
            "minimum"
            "maximum"
            "exclusiveMinimum"
            "exclusiveMaximum"
            "multipleOf"
            "minItems"
            "maxItems"
            "minProperties"
            "maxProperties"
            "pattern"
            "format"
        ]

    let private isIntegerToken (token: string) =
        if System.String.IsNullOrEmpty(token) then
            false
        else
            let mutable index = 0

            if token.[0] = '-' then
                index <- 1

            if index >= token.Length then
                false
            else
                let mutable valid = true

                while index < token.Length && valid do
                    let ch = token.[index]

                    if ch < '0' || ch > '9' then
                        valid <- false
                    else
                        index <- index + 1

                valid

    let private equalJsonValue left right = left = right

    let private tryParseDecimalToken (token: string) =
        match
            System.Decimal.TryParse(
                token,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture
            )
        with
        | true, value -> Some value
        | false, _ -> None

    let private combineImportRules (rules: (JsonValue -> Result<JsonValue, string>) list) =
        fun value ->
            let mutable current = Ok value

            for rule in rules do
                match current with
                | Ok validated -> current <- rule validated
                | Error _ -> ()

            current

    let private ruleMatches (rule: JsonValue -> Result<JsonValue, string>) (value: JsonValue) =
        match rule value with
        | Ok _ -> true
        | Error _ -> false

    let private tryFindFormatValidator (options: ImportOptions) (formatName: string) =
        options.FormatValidators
        |> List.tryFind (fun (name, _) -> name = formatName)
        |> Option.map snd

    let private addDistinct
        (values: ResizeArray<string>)
        (seen: System.Collections.Generic.HashSet<string>)
        (value: string)
        =
        if seen.Add(value) then
            values.Add(value)

    let rec private collectSchemaKeywordsInto
        (value: JsonValue)
        (keywords: ResizeArray<string>)
        (seen: System.Collections.Generic.HashSet<string>)
        =
        match value with
        | JObject properties ->
            for name, propertyValue in properties do
                addDistinct keywords seen name
                collectSchemaKeywordsInto propertyValue keywords seen
        | JArray values ->
            for item in values do
                collectSchemaKeywordsInto item keywords seen
        | _ -> ()

    let private collectSchemaKeywords (value: JsonValue) =
        let keywords = ResizeArray<string>()
        let seen = System.Collections.Generic.HashSet<string>()
        collectSchemaKeywordsInto value keywords seen
        List.ofSeq keywords

    let private decodePointerToken (token: string) =
        token.Replace("~1", "/").Replace("~0", "~")

    let private tryResolveJsonPointer (root: JsonValue) (pointer: string) =
        let rec loop current segments =
            match segments, current with
            | [], _ -> Some current
            | segment :: rest, JObject properties ->
                properties
                |> List.tryFind (fun (name, _) -> name = segment)
                |> Option.bind (fun (_, next) -> loop next rest)
            | segment :: rest, JArray items ->
                match System.Int32.TryParse(segment, System.Globalization.CultureInfo.InvariantCulture) with
                | true, index when index >= 0 && index < items.Length -> loop items.[index] rest
                | _ -> None
            | _ -> None

        if pointer = "#" then
            Some root
        elif pointer.StartsWith("#/") then
            pointer.Substring(2).Split('/')
            |> Array.toList
            |> List.map decodePointerToken
            |> loop root
        else
            None

    let private mergeObjectProperties
        (baseProperties: (string * JsonValue) list)
        (overrideProperties: (string * JsonValue) list)
        =
        let mutable merged = baseProperties

        for name, value in overrideProperties do
            merged <- (name, value) :: (merged |> List.filter (fun (existing, _) -> existing <> name))

        List.rev merged

    let private distinctStrings values = values |> List.distinct

    let private mergeRequiredArrays left right =
        (left @ right)
        |> List.choose (function
            | JString value -> Some(JString value)
            | _ -> None)
        |> distinctStrings

    let rec private mergeSchemaObjects
        (warnings: ResizeArray<string>)
        (mergeNested: JsonValue -> JsonValue -> JsonValue)
        (leftProperties: (string * JsonValue) list)
        (rightProperties: (string * JsonValue) list)
        =
        let leftMap = Map.ofList leftProperties
        let rightMap = Map.ofList rightProperties

        let keys =
            Set.union
                (leftMap |> Seq.map (fun pair -> pair.Key) |> Set.ofSeq)
                (rightMap |> Seq.map (fun pair -> pair.Key) |> Set.ofSeq)

        keys
        |> Seq.map (fun key ->
            let value =
                match Map.tryFind key leftMap, Map.tryFind key rightMap with
                | Some(JObject leftObj), Some(JObject rightObj) when key = "properties" ->
                    JObject(mergeSchemaObjects warnings mergeNested leftObj rightObj)
                | Some(JArray leftArr), Some(JArray rightArr) when key = "required" ->
                    JArray(mergeRequiredArrays leftArr rightArr)
                | Some leftValue, Some rightValue when key = "items" -> mergeNested leftValue rightValue
                | Some(JString leftType), Some(JString rightType) when key = "type" && leftType = rightType ->
                    JString leftType
                | Some leftValue, Some rightValue when leftValue = rightValue -> leftValue
                | Some leftValue, Some rightValue ->
                    warnings.Add(sprintf "Merged conflicting allOf keyword '%s' by applying both branches" key)
                    mergeNested leftValue rightValue
                | Some leftValue, None -> leftValue
                | None, Some rightValue -> rightValue
                | None, None -> JNull

            key, value)
        |> Seq.toList

    let private normalizeSchemaRefs (schemaNode: JsonValue) =
        let warnings = ResizeArray<string>()
        let normalizedKeywords = ResizeArray<string>()
        let normalizedKeywordSet = System.Collections.Generic.HashSet<string>()

        let addNormalized keyword =
            if normalizedKeywordSet.Add(keyword) then
                normalizedKeywords.Add(keyword)

        let rec normalize stack current =
            let rec mergeAllOfSchemas left right =
                match left, right with
                | JObject leftProperties, JObject rightProperties ->
                    JObject(mergeSchemaObjects warnings mergeAllOfSchemas leftProperties rightProperties)
                | _, rightValue -> rightValue

            match current with
            | JObject properties ->
                let normalizedProperties =
                    properties
                    |> List.filter (fun (name, _) -> name <> "$defs" && name <> "$ref" && name <> "allOf")
                    |> List.map (fun (name, value) -> name, normalize stack value)

                let withRefResolved =
                    match tryGetStringProperty "$ref" properties with
                    | Some pointer ->
                        addNormalized "$ref"

                        if stack |> List.contains pointer then
                            warnings.Add(sprintf "Cyclic local $ref detected and left unresolved: %s" pointer)
                            JObject normalizedProperties
                        else
                            match tryResolveJsonPointer schemaNode pointer with
                            | Some referenced ->
                                let normalizedReferenced = normalize (pointer :: stack) referenced

                                match normalizedReferenced, normalizedProperties with
                                | JObject referencedProperties, overrideProperties ->
                                    JObject(
                                        mergeSchemaObjects
                                            warnings
                                            mergeAllOfSchemas
                                            referencedProperties
                                            overrideProperties
                                    )
                                | _, [] -> normalizedReferenced
                                | _, _ ->
                                    warnings.Add(
                                        sprintf "Ignored sibling keywords next to non-object $ref target: %s" pointer
                                    )

                                    normalizedReferenced
                            | None ->
                                warnings.Add(
                                    sprintf "Unsupported or unresolved $ref left on raw fallback path: %s" pointer
                                )

                                JObject normalizedProperties
                    | None -> JObject normalizedProperties

                match tryGetArrayProperty "allOf" properties with
                | Some branches ->
                    addNormalized "allOf"

                    let normalizedBranches = branches |> List.map (normalize stack)

                    let mergedAllOf =
                        normalizedBranches
                        |> List.fold
                            (fun state branch ->
                                match state, branch with
                                | JObject leftProperties, JObject rightProperties ->
                                    JObject(
                                        mergeSchemaObjects warnings mergeAllOfSchemas leftProperties rightProperties
                                    )
                                | _, JObject rightProperties ->
                                    JObject(mergeSchemaObjects warnings mergeAllOfSchemas [] rightProperties)
                                | JObject leftProperties, _ ->
                                    warnings.Add("Ignored non-object allOf branch during normalization")
                                    JObject leftProperties
                                | _, _ ->
                                    warnings.Add("Ignored non-object allOf branch during normalization")
                                    state)
                            (JObject [])

                    match withRefResolved, mergedAllOf with
                    | JObject currentProperties, JObject mergedProperties ->
                        JObject(mergeSchemaObjects warnings mergeAllOfSchemas mergedProperties currentProperties)
                    | _ -> withRefResolved
                | None -> withRefResolved
            | JArray values -> JArray(values |> List.map (normalize stack))
            | _ -> current

        let normalizedRoot = normalize [] schemaNode

        normalizedRoot, (List.ofSeq normalizedKeywords), (List.ofSeq warnings)

    let private collectImportWarnings (options: ImportOptions) (schemaNode: JsonValue) =
        let warnings = ResizeArray<string>()

        let rec loop current =
            match current with
            | JObject properties ->
                match tryGetStringProperty "pattern" properties with
                | Some pattern ->
                    try
                        System.Text.RegularExpressions.Regex(pattern) |> ignore
                    with ex ->
                        warnings.Add(sprintf "Invalid regex pattern ignored during import: %s (%s)" pattern ex.Message)
                | None -> ()

                match tryGetObjectProperty "patternProperties" properties with
                | Some patternSchemas ->
                    for pattern, _ in patternSchemas do
                        try
                            System.Text.RegularExpressions.Regex(pattern) |> ignore
                        with ex ->
                            warnings.Add(
                                sprintf
                                    "Invalid patternProperties regex ignored during import: %s (%s)"
                                    pattern
                                    ex.Message
                            )
                | None -> ()

                match tryGetStringProperty "format" properties with
                | Some formatName when tryFindFormatValidator options formatName |> Option.isNone ->
                    warnings.Add(sprintf "No validator configured for JSON Schema format: %s" formatName)
                | _ -> ()

                for _, value in properties do
                    loop value
            | JArray values ->
                for value in values do
                    loop value
            | _ -> ()

        loop schemaNode
        List.ofSeq warnings

    let rec private importRule
        (options: ImportOptions)
        (schemaNode: JsonValue)
        : JsonValue -> Result<JsonValue, string> =
        let accept value = Ok value

        let importTypeRule (typeName: string) =
            match typeName with
            | "null" ->
                (fun value ->
                    match value with
                    | JNull -> Ok value
                    | _ -> Error "Expected null")
            | "boolean" ->
                (fun value ->
                    match value with
                    | JBool _ -> Ok value
                    | _ -> Error "Expected boolean")
            | "number" ->
                (fun value ->
                    match value with
                    | JNumber _ -> Ok value
                    | _ -> Error "Expected number")
            | "integer" ->
                (fun value ->
                    match value with
                    | JNumber token when isIntegerToken token -> Ok value
                    | JNumber _ -> Error "Expected integer"
                    | _ -> Error "Expected integer")
            | "string" ->
                (fun value ->
                    match value with
                    | JString _ -> Ok value
                    | _ -> Error "Expected string")
            | "array" ->
                (fun value ->
                    match value with
                    | JArray items ->
                        match schemaNode with
                        | JObject properties ->
                            let prefixValidators =
                                match tryGetArrayProperty "prefixItems" properties with
                                | Some schemas -> schemas |> List.map (importRule options)
                                | None -> []

                            let containsValidator =
                                match tryFindProperty "contains" properties with
                                | Some containsSchema -> Some(importRule options containsSchema)
                                | None -> None

                            let trailingItemsRule =
                                match tryFindProperty "items" properties with
                                | Some(JBool false) -> Some None
                                | Some(JBool true) -> Some(Some(fun item -> Ok item))
                                | Some itemSchema -> Some(Some(importRule options itemSchema))
                                | None -> None

                            let mutable error = None

                            let setError message =
                                match error with
                                | Some _ -> ()
                                | None -> error <- Some message

                            let validated =
                                items
                                |> List.mapi (fun index item ->
                                    let validator =
                                        if index < prefixValidators.Length then
                                            Some prefixValidators.[index]
                                        else
                                            match trailingItemsRule with
                                            | Some(Some rule) -> Some rule
                                            | Some None ->
                                                setError "Array contains items beyond prefixItems"
                                                None
                                            | None ->
                                                match tryFindProperty "items" properties with
                                                | Some itemSchema -> Some(importRule options itemSchema)
                                                | None -> None

                                    match validator with
                                    | Some validateItem ->
                                        match validateItem item with
                                        | Ok next -> next
                                        | Error message ->
                                            setError (sprintf "Array item %d: %s" index message)
                                            item
                                    | None -> item)

                            match error with
                            | Some message -> Error message
                            | None ->
                                match containsValidator with
                                | Some containsRule when not (validated |> List.exists (ruleMatches containsRule)) ->
                                    Error "Array did not satisfy contains"
                                | _ -> Ok(JArray validated)
                        | _ -> Ok value
                    | _ -> Error "Expected array")
            | "object" ->
                (fun value ->
                    match value with
                    | JObject fields ->
                        match schemaNode with
                        | JObject properties ->
                            let propertyRules =
                                match tryGetObjectProperty "properties" properties with
                                | Some schemaProperties ->
                                    schemaProperties
                                    |> List.map (fun (name, propertySchema) -> name, importRule options propertySchema)
                                | None -> []

                            let patternRules =
                                match tryGetObjectProperty "patternProperties" properties with
                                | Some patternSchemas ->
                                    patternSchemas
                                    |> List.choose (fun (pattern, propertySchema) ->
                                        try
                                            Some(
                                                System.Text.RegularExpressions.Regex(pattern),
                                                importRule options propertySchema
                                            )
                                        with _ ->
                                            None)
                                | None -> []

                            let propertyNameRule =
                                match tryFindProperty "propertyNames" properties with
                                | Some propertyNameSchema -> Some(importRule options propertyNameSchema)
                                | None -> None

                            let additionalPropertiesRule =
                                match tryFindProperty "additionalProperties" properties with
                                | Some(JBool false) -> Some None
                                | Some(JBool true) -> Some(Some(fun item -> Ok item))
                                | Some schema -> Some(Some(importRule options schema))
                                | None -> None

                            let required =
                                match tryGetArrayProperty "required" properties with
                                | Some names ->
                                    names
                                    |> List.choose (function
                                        | JString name -> Some name
                                        | _ -> None)
                                    |> Set.ofList
                                | None -> Set.empty

                            let fieldMap = Map.ofList fields

                            let missingRequired =
                                required |> Seq.tryFind (fun name -> not (fieldMap.ContainsKey name))

                            match missingRequired with
                            | Some name -> Error(sprintf "Missing required property: %s" name)
                            | None ->
                                let mutable error = None

                                let setError message =
                                    match error with
                                    | Some _ -> ()
                                    | None -> error <- Some message

                                let validatedFields =
                                    fields
                                    |> List.map (fun (name, fieldValue) ->
                                        match propertyNameRule with
                                        | Some validateName ->
                                            match validateName (JString name) with
                                            | Ok _ -> ()
                                            | Error message -> setError (sprintf "Property name %s: %s" name message)
                                        | None -> ()

                                        let directRule =
                                            propertyRules
                                            |> List.tryFind (fun (fieldName, _) -> fieldName = name)
                                            |> Option.map snd

                                        let matchingPatternRules =
                                            patternRules
                                            |> List.choose (fun (regex, rule) ->
                                                if regex.IsMatch name then Some rule else None)

                                        let validators =
                                            match directRule, matchingPatternRules with
                                            | Some rule, patternMatches -> rule :: patternMatches
                                            | None, patternMatches -> patternMatches

                                        let validateUnknownProperty () =
                                            match additionalPropertiesRule with
                                            | Some(Some validateProperty) ->
                                                match validateProperty fieldValue with
                                                | Ok validated -> name, validated
                                                | Error message ->
                                                    setError (sprintf "Property %s: %s" name message)
                                                    name, fieldValue
                                            | Some None ->
                                                setError (sprintf "Unexpected property: %s" name)
                                                name, fieldValue
                                            | None -> name, fieldValue

                                        match validators with
                                        | [] -> validateUnknownProperty ()
                                        | _ ->
                                            let mutable currentValue = fieldValue

                                            for validateField in validators do
                                                match validateField currentValue with
                                                | Ok validated -> currentValue <- validated
                                                | Error message -> setError (sprintf "Property %s: %s" name message)

                                            name, currentValue)

                                match error with
                                | Some message -> Error message
                                | None -> Ok(JObject validatedFields)
                        | _ -> Ok value
                    | _ -> Error "Expected object")
            | _ -> accept

        match schemaNode with
        | JBool true -> accept
        | JBool false -> (fun _ -> Error "JSON Schema 'false' does not allow any values")
        | JObject properties ->
            let importedRules =
                let typeRules =
                    match tryFindProperty "type" properties with
                    | Some(JString typeName) -> [ importTypeRule typeName ]
                    | Some(JArray typeNames) ->
                        let branches =
                            typeNames
                            |> List.choose (function
                                | JString typeName -> Some(importTypeRule typeName)
                                | _ -> None)

                        match branches with
                        | [] -> []
                        | _ -> [
                            fun value ->
                                branches
                                |> List.tryPick (fun branch ->
                                    match branch value with
                                    | Ok validated -> Some(Ok validated)
                                    | Error _ -> None)
                                |> Option.defaultValue (Error "Value did not match any allowed JSON Schema type")
                          ]
                    | _ ->
                        match tryFindProperty "properties" properties, tryFindProperty "items" properties with
                        | Some _, _ -> [ importTypeRule "object" ]
                        | _, Some _ -> [ importTypeRule "array" ]
                        | _ -> []

                let enumRule =
                    match tryGetArrayProperty "enum" properties with
                    | Some cases ->
                        Some(fun value ->
                            if cases |> List.exists (equalJsonValue value) then
                                Ok value
                            else
                                Error "Value did not match the JSON Schema enum")
                    | None -> None

                let constRule =
                    match tryFindProperty "const" properties with
                    | Some expected ->
                        Some(fun value ->
                            if equalJsonValue value expected then
                                Ok value
                            else
                                Error "Value did not match the JSON Schema const")
                    | None -> None

                let stringLengthRules = [
                    match tryGetIntProperty "minLength" properties with
                    | Some minLength ->
                        yield
                            (fun value ->
                                match value with
                                | JString text when text.Length < minLength ->
                                    Error(sprintf "String length must be at least %d" minLength)
                                | _ -> Ok value)
                    | None -> ()
                    match tryGetIntProperty "maxLength" properties with
                    | Some maxLength ->
                        yield
                            (fun value ->
                                match value with
                                | JString text when text.Length > maxLength ->
                                    Error(sprintf "String length must be at most %d" maxLength)
                                | _ -> Ok value)
                    | None -> ()
                ]

                let patternRules = [
                    match tryGetStringProperty "pattern" properties with
                    | Some pattern ->
                        try
                            let regex = System.Text.RegularExpressions.Regex(pattern)

                            yield
                                (fun value ->
                                    match value with
                                    | JString text when not (regex.IsMatch text) ->
                                        Error(sprintf "String did not match pattern %s" pattern)
                                    | _ -> Ok value)
                        with _ ->
                            ()
                    | None -> ()
                    match tryGetStringProperty "format" properties with
                    | Some formatName ->
                        match tryFindFormatValidator options formatName with
                        | Some validator ->
                            yield
                                (fun value ->
                                    match value with
                                    | JString text ->
                                        match validator text with
                                        | Ok() -> Ok value
                                        | Error message -> Error message
                                    | _ -> Ok value)
                        | None -> ()
                    | None -> ()
                ]

                let branchRules = [
                    match tryGetArrayProperty "oneOf" properties with
                    | Some branches ->
                        let validators = branches |> List.map (importRule options)

                        yield
                            (fun value ->
                                let successes = validators |> List.filter (fun rule -> ruleMatches rule value)

                                match successes with
                                | [ validate ] -> validate value
                                | [] -> Error "Value did not match any oneOf branch"
                                | _ -> Error "Value matched more than one oneOf branch")
                    | None -> ()
                    match tryGetArrayProperty "anyOf" properties with
                    | Some branches ->
                        let validators = branches |> List.map (importRule options)

                        yield
                            (fun value ->
                                match validators |> List.tryFind (fun rule -> ruleMatches rule value) with
                                | Some validate -> validate value
                                | None -> Error "Value did not match any anyOf branch")
                    | None -> ()
                    match tryFindProperty "if" properties with
                    | Some ifSchema ->
                        let ifRule = importRule options ifSchema
                        let thenRule = tryFindProperty "then" properties |> Option.map (importRule options)
                        let elseRule = tryFindProperty "else" properties |> Option.map (importRule options)

                        yield
                            (fun value ->
                                if ruleMatches ifRule value then
                                    match thenRule with
                                    | Some validateThen -> validateThen value
                                    | None -> Ok value
                                else
                                    match elseRule with
                                    | Some validateElse -> validateElse value
                                    | None -> Ok value)
                    | None -> ()
                ]

                let numberRules = [
                    match tryGetDecimalProperty "minimum" properties with
                    | Some minimum ->
                        yield
                            (fun value ->
                                match value with
                                | JNumber token ->
                                    match tryParseDecimalToken token with
                                    | Some number when number < minimum ->
                                        Error(sprintf "Number must be at least %M" minimum)
                                    | Some _ -> Ok value
                                    | None -> Error "Expected number"
                                | _ -> Ok value)
                    | None -> ()
                    match tryGetDecimalProperty "maximum" properties with
                    | Some maximum ->
                        yield
                            (fun value ->
                                match value with
                                | JNumber token ->
                                    match tryParseDecimalToken token with
                                    | Some number when number > maximum ->
                                        Error(sprintf "Number must be at most %M" maximum)
                                    | Some _ -> Ok value
                                    | None -> Error "Expected number"
                                | _ -> Ok value)
                    | None -> ()
                    match tryGetDecimalProperty "exclusiveMinimum" properties with
                    | Some minimum ->
                        yield
                            (fun value ->
                                match value with
                                | JNumber token ->
                                    match tryParseDecimalToken token with
                                    | Some number when number <= minimum ->
                                        Error(sprintf "Number must be greater than %M" minimum)
                                    | Some _ -> Ok value
                                    | None -> Error "Expected number"
                                | _ -> Ok value)
                    | None -> ()
                    match tryGetDecimalProperty "exclusiveMaximum" properties with
                    | Some maximum ->
                        yield
                            (fun value ->
                                match value with
                                | JNumber token ->
                                    match tryParseDecimalToken token with
                                    | Some number when number >= maximum ->
                                        Error(sprintf "Number must be less than %M" maximum)
                                    | Some _ -> Ok value
                                    | None -> Error "Expected number"
                                | _ -> Ok value)
                    | None -> ()
                    match tryGetDecimalProperty "multipleOf" properties with
                    | Some factor ->
                        yield
                            (fun value ->
                                match value with
                                | JNumber token ->
                                    match tryParseDecimalToken token with
                                    | Some _ when factor = 0M -> Error "multipleOf must not be zero"
                                    | Some number when number % factor <> 0M ->
                                        Error(sprintf "Number must be a multiple of %M" factor)
                                    | Some _ -> Ok value
                                    | None -> Error "Expected number"
                                | _ -> Ok value)
                    | None -> ()
                ]

                let collectionSizeRules = [
                    match tryGetIntProperty "minItems" properties with
                    | Some minItems ->
                        yield
                            (fun value ->
                                match value with
                                | JArray items when items.Length < minItems ->
                                    Error(sprintf "Array length must be at least %d" minItems)
                                | _ -> Ok value)
                    | None -> ()
                    match tryGetIntProperty "maxItems" properties with
                    | Some maxItems ->
                        yield
                            (fun value ->
                                match value with
                                | JArray items when items.Length > maxItems ->
                                    Error(sprintf "Array length must be at most %d" maxItems)
                                | _ -> Ok value)
                    | None -> ()
                    match tryGetIntProperty "minProperties" properties with
                    | Some minProperties ->
                        yield
                            (fun value ->
                                match value with
                                | JObject fields when fields.Length < minProperties ->
                                    Error(sprintf "Object must contain at least %d properties" minProperties)
                                | _ -> Ok value)
                    | None -> ()
                    match tryGetIntProperty "maxProperties" properties with
                    | Some maxProperties ->
                        yield
                            (fun value ->
                                match value with
                                | JObject fields when fields.Length > maxProperties ->
                                    Error(sprintf "Object must contain at most %d properties" maxProperties)
                                | _ -> Ok value)
                    | None -> ()
                ]

                [
                    yield! typeRules
                    yield! stringLengthRules
                    yield! patternRules
                    yield! numberRules
                    yield! collectionSizeRules
                    yield! branchRules
                    match enumRule with
                    | Some rule -> yield rule
                    | None -> ()
                    match constRule with
                    | Some rule -> yield rule
                    | None -> ()
                ]

            match importedRules with
            | [] -> accept
            | _ -> combineImportRules importedRules
        | _ -> accept

    /// Generates a compact JSON Schema document for the JSON wire shape of a schema.
    ///
    /// The exported schema describes the JSON contract only. XML-specific shape
    /// details and business-rule semantics inside mapping functions are not
    /// representable here and therefore export as the underlying wire form.
    let generate (schema: Schema<'T>) =
        let rootNode = exportNode schema
        let builder = System.Text.StringBuilder()
        let title = schema.TargetType.Name

        builder.Append("{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\"")
        |> ignore

        builder.Append(",\"title\":") |> ignore
        //
        // The schema already captures the concrete target type when it is built.
        // Reusing that value avoids a runtime generic type lookup that Fable
        // cannot preserve after erasure.
        appendQuoted builder title
        appendRootNode builder rootNode

        builder.Append('}') |> ignore
        builder.ToString()

    /// Imports the deterministic JSON Schema subset into a JSON-only `Schema<JsonValue>`.
    ///
    /// The imported schema keeps the original JSON shape by decoding through
    /// `Schema.jsonValue` first, then applying the supported JSON Schema rules
    /// as structural refinement over the raw DOM. Unsupported branch-shaping
    /// features currently fall back to the unconstrained raw JSON schema.
    let importWithReportUsing (options: ImportOptions) (jsonSchemaText: string) : ImportReport =
        let parsedSchema = Json.deserialize (Json.compile Schema.jsonValue) jsonSchemaText

        let normalizedSchema, normalizedKeywords, normalizationWarnings =
            normalizeSchemaRefs parsedSchema

        let validate = importRule options normalizedSchema
        let schema = Schema.jsonValue |> Schema.tryMap validate id
        let allKeywords = collectSchemaKeywords parsedSchema
        let importWarnings = collectImportWarnings options normalizedSchema

        {
            Schema = schema
            EnforcedKeywords = allKeywords |> List.filter enforcedImportKeywords.Contains
            NormalizedKeywords = normalizedKeywords
            FallbackKeywords = allKeywords |> List.filter unsupportedImportKeywords.Contains
            Warnings = normalizationWarnings @ importWarnings
        }

    /// Imports JSON Schema into `Schema<JsonValue>` and returns diagnostics.
    ///
    /// This uses the built-in format validator set. Use `importWithReportUsing`
    /// when you need project-specific `format` rules.
    let importWithReport (jsonSchemaText: string) : ImportReport =
        importWithReportUsing ImportOptions.defaults jsonSchemaText

    /// Imports the deterministic JSON Schema subset into a JSON-only `Schema<JsonValue>`.
    ///
    /// This is the convenience entrypoint when you only need the imported
    /// schema itself. Use `importWithReport` when you also need diagnostics
    /// about which keywords were enforced or left on the raw-fallback path.
    let importUsing (options: ImportOptions) (jsonSchemaText: string) : Schema<JsonValue> =
        (importWithReportUsing options jsonSchemaText).Schema

    /// Imports the deterministic JSON Schema subset into a JSON-only `Schema<JsonValue>`.
    ///
    /// This uses the built-in format validator set. Use `importUsing` when you
    /// need project-specific `format` validation behavior.
    let import (jsonSchemaText: string) : Schema<JsonValue> =
        (importWithReport jsonSchemaText).Schema

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
                    let v = System.Int32.Parse(text.Trim())
                    struct (box v, current))
            MissingValue = None
          }
        | Primitive t when t = typeof<int64> -> {
            Encode =
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
            MissingValue = None
          }
        | Primitive t when t = typeof<uint32> -> {
            Encode =
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
            MissingValue = None
          }
        | Primitive t when t = typeof<uint64> -> {
            Encode =
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
            MissingValue = None
          }
        | Primitive t when t = typeof<float> -> {
            Encode =
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
            MissingValue = None
          }
        | Primitive t when t = typeof<decimal> -> {
            Encode =
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
                                && fields.[0] :? string
                                && unbox<string> fields.[0] = ""
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
                            targetArray.SetValue(results.[i], i)

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
