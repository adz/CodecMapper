// `CodecMapper` is a schema-first codec library for explicit wire contracts.
//
// Start in `Schema` to describe the wire shape, then compile that schema in
// `Json` or `Xml` depending on the format boundary you need to talk to.
namespace CodecMapper

open System.Text
open System.Collections.Generic
open System.Globalization
open Microsoft.FSharp.Reflection

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

    ///
    /// Field-policy wrappers sometimes need to distinguish explicit empty
    /// collections from non-empty ones without caring about the concrete type.
    let isEmptyCollectionValue (value: obj) =
        if isNull value || value :? string then
            false
        elif value :? System.Collections.IEnumerable then
            let enumerator = (value :?> System.Collections.IEnumerable).GetEnumerator()
            not (enumerator.MoveNext())
        else
            false

    let private isDigitsOnly (text: string) =
        let mutable valid = text.Length > 0
        let mutable index = 0

        while valid && index < text.Length do
            let ch = text[index]

            if ch < '0' || ch > '9' then
                valid <- false
            else
                index <- index + 1

        valid

    type private NumericTokenState =
        | InvalidToken
        | OutOfRangeToken

    ///
    /// Integer parsing only needs ASCII digit validation, so we can classify
    /// invalid text versus range overflow without relying on exception types.
    let private classifyIntegerToken (allowMinus: bool) (text: string) =
        if text = "" then
            InvalidToken
        elif allowMinus && text[0] = '-' then
            if text.Length = 1 then InvalidToken
            elif isDigitsOnly (text.Substring(1)) then OutOfRangeToken
            else InvalidToken
        elif isDigitsOnly text then
            OutOfRangeToken
        else
            InvalidToken

    let tryParseInt32Invariant (text: string) =
        match System.Int32.TryParse(text) with
        | true, value -> Some value
        | false, _ -> None

    let tryParseInt64Invariant (text: string) =
        match System.Int64.TryParse(text) with
        | true, value -> Some value
        | false, _ -> None

    let tryParseUInt32Invariant (text: string) =
        match System.UInt32.TryParse(text) with
        | true, value -> Some value
        | false, _ -> None

    let tryParseUInt64Invariant (text: string) =
        match System.UInt64.TryParse(text) with
        | true, value -> Some value
        | false, _ -> None

    let tryParseInt16Invariant (text: string) =
        match System.Int16.TryParse(text) with
        | true, value -> Some value
        | false, _ -> None

    let tryParseByteInvariant (text: string) =
        match System.Byte.TryParse(text) with
        | true, value -> Some value
        | false, _ -> None

    let tryParseSByteInvariant (text: string) =
        match System.SByte.TryParse(text) with
        | true, value -> Some value
        | false, _ -> None

    let tryParseUInt16Invariant (text: string) =
        match System.UInt16.TryParse(text) with
        | true, value -> Some value
        | false, _ -> None

    let private tryParseFloatPlatformInvariant (text: string) =
#if FABLE_COMPILER
        match System.Double.TryParse(text) with
        | true, value when not (System.Double.IsInfinity(value) || System.Double.IsNaN(value)) -> Some value
        | _ -> None
#else
        match System.Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true, value when not (System.Double.IsInfinity(value) || System.Double.IsNaN(value)) -> Some value
        | _ -> None
#endif

    let private tryParseDecimalPlatformInvariant (text: string) =
#if FABLE_COMPILER
        match System.Decimal.TryParse(text) with
        | true, value -> Some value
        | false, _ -> None
#else
        match System.Decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true, value -> Some value
        | false, _ -> None
#endif

    let tryParseFloatInvariant = tryParseFloatPlatformInvariant
    let tryParseDecimalInvariant = tryParseDecimalPlatformInvariant

    let private failIntegerToken typeName allowMinus token =
        match classifyIntegerToken allowMinus token with
        | OutOfRangeToken -> failwithf "%s value out of range: %s" typeName token
        | InvalidToken -> failwithf "Invalid %s value: %s" typeName token

    let parseInt32Invariant typeName token =
        match tryParseInt32Invariant token with
        | Some value -> value
        | None -> failIntegerToken typeName true token

    let parseInt64Invariant typeName token =
        match tryParseInt64Invariant token with
        | Some value -> value
        | None -> failIntegerToken typeName true token

    let parseUInt32Invariant typeName token =
        match tryParseUInt32Invariant token with
        | Some value -> value
        | None -> failIntegerToken typeName false token

    let parseUInt64Invariant typeName token =
        match tryParseUInt64Invariant token with
        | Some value -> value
        | None -> failIntegerToken typeName false token

    let parseInt16Invariant typeName token =
        match tryParseInt16Invariant token with
        | Some value -> value
        | None -> failIntegerToken typeName true token

    let parseByteInvariant typeName token =
        match tryParseByteInvariant token with
        | Some value -> value
        | None -> failIntegerToken typeName false token

    let parseSByteInvariant typeName token =
        match tryParseSByteInvariant token with
        | Some value -> value
        | None -> failIntegerToken typeName true token

    let parseUInt16Invariant typeName token =
        match tryParseUInt16Invariant token with
        | Some value -> value
        | None -> failIntegerToken typeName false token

    let parseFloatInvariant typeName token =
        match tryParseFloatInvariant token with
        | Some value -> value
        | None -> failwithf "Invalid %s value: %s" typeName token

    let parseDecimalInvariant typeName token =
        match tryParseDecimalInvariant token with
        | Some value -> value
        | None -> failwithf "Invalid %s value: %s" typeName token

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
                x.InternalData[x.InternalCount] <- b
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
                        digits[pos] <- byte (48 + (v % 10))
                        v <- v / 10
                        pos <- pos + 1

                    (x :> IByteWriter).Ensure(pos)

                    for i in 0 .. pos - 1 do
                        x.InternalData[x.InternalCount + i] <- digits[pos - 1 - i]

                    x.InternalCount <- x.InternalCount + pos

            member x.Data = x.InternalData
            member x.Count = x.InternalCount
