// `CodecMapper` is a schema-first codec library for explicit wire contracts.
//
// Start in `Schema` to describe the wire shape, then compile that schema in
// `Json` or `Xml` depending on the format boundary you need to talk to.
namespace CodecMapper

open System.Text
open System.Collections.Generic
open System.Globalization
open System.Buffers
#if !FABLE_COMPILER
open System.Buffers.Text
#endif
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

#if !FABLE_COMPILER
    ///
    /// JSON integer tokens are ASCII-only, so the .NET fast path can parse
    /// directly from source bytes and keep string allocation on the error path.
    let private parseSignedIntegerBytes typeName limit allowMinus (data: byte[]) (offset: int) (length: int) =
        let inline tokenText () =
            Encoding.UTF8.GetString(data, offset, length)

        if length = 0 then
            failwithf "Invalid %s value: %s" typeName (tokenText ())

        let mutable index = offset
        let endExclusive = offset + length
        let mutable negative = false

        if allowMinus && data[index] = 45uy then
            negative <- true
            index <- index + 1

        if index >= endExclusive then
            failwithf "Invalid %s value: %s" typeName (tokenText ())

        let maxMagnitude = if negative then limit + 1UL else limit
        let mutable magnitude = 0UL

        while index < endExclusive do
            let digit = int data[index] - int 48uy

            if digit < 0 || digit > 9 then
                failwithf "Invalid %s value: %s" typeName (tokenText ())

            let digitMagnitude = uint64 digit

            if magnitude > (maxMagnitude - digitMagnitude) / 10UL then
                failwithf "%s value out of range: %s" typeName (tokenText ())

            magnitude <- magnitude * 10UL + digitMagnitude
            index <- index + 1

        struct (negative, magnitude)

    ///
    /// Unsigned integer decoding follows the same byte-level path but keeps
    /// syntax and range failures aligned with the string-based helpers.
    let private parseUnsignedIntegerBytes typeName limit (data: byte[]) (offset: int) (length: int) =
        let inline tokenText () =
            Encoding.UTF8.GetString(data, offset, length)

        if length = 0 then
            failwithf "Invalid %s value: %s" typeName (tokenText ())

        let mutable index = offset
        let endExclusive = offset + length
        let mutable magnitude = 0UL

        while index < endExclusive do
            let digit = int data[index] - int 48uy

            if digit < 0 || digit > 9 then
                failwithf "Invalid %s value: %s" typeName (tokenText ())

            let digitMagnitude = uint64 digit

            if magnitude > (limit - digitMagnitude) / 10UL then
                failwithf "%s value out of range: %s" typeName (tokenText ())

            magnitude <- magnitude * 10UL + digitMagnitude
            index <- index + 1

        magnitude

    let parseInt32InvariantBytes typeName (data: byte[]) (offset: int) (length: int) =
        let struct (negative, magnitude) =
            parseSignedIntegerBytes typeName 2147483647UL true data offset length

        if negative then
            if magnitude = 2147483648UL then
                System.Int32.MinValue
            else
                -int magnitude
        else
            int magnitude

    let parseInt64InvariantBytes typeName (data: byte[]) (offset: int) (length: int) =
        let struct (negative, magnitude) =
            parseSignedIntegerBytes typeName 9223372036854775807UL true data offset length

        if negative then
            if magnitude = 9223372036854775808UL then
                System.Int64.MinValue
            else
                -int64 magnitude
        else
            int64 magnitude

    let parseUInt32InvariantBytes typeName (data: byte[]) (offset: int) (length: int) =
        parseUnsignedIntegerBytes typeName 4294967295UL data offset length |> uint32

    let parseUInt64InvariantBytes typeName (data: byte[]) (offset: int) (length: int) =
        parseUnsignedIntegerBytes typeName System.UInt64.MaxValue data offset length
#endif

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

#if !FABLE_COMPILER
    ///
    /// The handwritten JSON parser already identifies numeric token bounds in
    /// the UTF-8 source, so the .NET fast path can parse directly from bytes
    /// and avoid allocating transient strings for every float and decimal.
    let parseFloatInvariantBytes typeName (data: byte[]) (offset: int) (length: int) =
        let token = System.ReadOnlySpan<byte>(data, offset, length)
        let mutable value = 0.0
        let mutable consumed = 0

        if
            Utf8Parser.TryParse(token, &value, &consumed, 'G')
            && consumed = length
            && not (System.Double.IsInfinity(value) || System.Double.IsNaN(value))
        then
            value
        else
            failwithf "Invalid %s value: %s" typeName (Encoding.UTF8.GetString(data, offset, length))

    ///
    /// Decimal parsing follows the same byte-level path so JSON numeric decode
    /// stays symmetric with the existing integer fast paths on .NET.
    let parseDecimalInvariantBytes typeName (data: byte[]) (offset: int) (length: int) =
        let token = System.ReadOnlySpan<byte>(data, offset, length)
        let mutable value = 0M
        let mutable consumed = 0

        if Utf8Parser.TryParse(token, &value, &consumed, 'G') && consumed = length then
            value
        else
            failwithf "Invalid %s value: %s" typeName (Encoding.UTF8.GetString(data, offset, length))
#endif

    /// Abstraction for writing bytes, to be implemented per target platform.
    type IByteWriter =
        /// Ensures that at least `n` more bytes can be written without reallocating.
        abstract member Ensure: int -> unit

        /// Writes a single byte.
        abstract member WriteByte: byte -> unit

        /// Writes a UTF-8 string payload.
        abstract member WriteString: string -> unit

        ///
        /// JSON escaping often needs to flush a slice of an existing string,
        /// and writing that slice directly avoids transient substring objects
        /// on the hot serialization path.
        abstract member WriteStringSlice: string * int * int -> unit

        /// Writes an integer value.
        abstract member WriteInt: int -> unit

        /// Writes an `int64` value.
        abstract member WriteInt64: int64 -> unit

        /// Writes a `uint32` value.
        abstract member WriteUInt32: uint32 -> unit

        /// Writes a `uint64` value.
        abstract member WriteUInt64: uint64 -> unit

        /// Writes a `float` value.
        abstract member WriteFloat: float -> unit

        /// Writes a `decimal` value.
        abstract member WriteDecimal: decimal -> unit

        /// Exposes the current backing storage.
        abstract member Data: byte[]

        /// Exposes the number of written bytes.
        abstract member Count: int

    /// Growable in-memory byte buffer used by the built-in codecs.
    type ResizableBuffer = {
        mutable InternalData: byte[]
        mutable InternalCount: int
        mutable ReturnToPool: bool
    } with

        ///
        /// The codec writers overwrite every byte they publish, so on .NET we
        /// can skip zero-initializing fresh buffers and avoid the memset-heavy
        /// growth cost that shows up in profiling traces.
        static member private Allocate(capacity: int) =
#if !FABLE_COMPILER
            System.GC.AllocateUninitializedArray<byte>(capacity)
#else
            Array.zeroCreate capacity
#endif

        ///
        /// Serialization buffers are short-lived scratch space, so renting from
        /// the shared pool removes most of the transient array allocation cost
        /// without changing the public string-returning API.
        static member private Rent(capacity: int) =
#if !FABLE_COMPILER
            ArrayPool<byte>.Shared.Rent(capacity)
#else
            Array.zeroCreate capacity
#endif

        /// Creates a new buffer with the requested initial capacity.
        static member Create(initialCapacity: int) = {
            InternalData = ResizableBuffer.Rent(initialCapacity)
            InternalCount = 0
            ReturnToPool = true
        }

        ///
        /// The .NET runtime rents arrays from the shared pool, while Fable
        /// keeps the simpler zero-allocation array semantics.
        static member private Return(buffer: byte[]) =
#if !FABLE_COMPILER
            ArrayPool<byte>.Shared.Return(buffer, false)
#else
            ignore buffer
#endif

        ///
        /// Benchmarks and serializers should always return pooled storage after
        /// materializing the final string so hot-path allocation stays low.
        member x.Release() =
            if x.ReturnToPool then
                ResizableBuffer.Return(x.InternalData)
                x.InternalData <- [||]
                x.InternalCount <- 0
                x.ReturnToPool <- false

        ///
        /// Integer encoders share the same digit-emission loop, so keeping the
        /// helper on the concrete buffer avoids duplicating bounds checks and
        /// temporary allocations across the wider numeric writer surface.
        member private x.WriteUInt64Digits(value: uint64) =
            if value = 0UL then
                (x :> IByteWriter).WriteByte(48uy)
            else
                let mutable v = value
                let digits = Array.zeroCreate 20
                let mutable pos = 0

                while v > 0UL do
                    digits[pos] <- byte (48 + int (v % 10UL))
                    v <- v / 10UL
                    pos <- pos + 1

                (x :> IByteWriter).Ensure(pos)

                for i in 0 .. pos - 1 do
                    x.InternalData[x.InternalCount + i] <- digits[pos - 1 - i]

                x.InternalCount <- x.InternalCount + pos

        interface IByteWriter with
            member x.Ensure(n: int) =
                let minCapacity = x.InternalCount + n

                if x.InternalData.Length < minCapacity then
                    let newCapacity = max (x.InternalData.Length * 2) minCapacity

                    let newData =
                        if x.ReturnToPool then
                            ResizableBuffer.Rent(newCapacity)
                        else
                            ResizableBuffer.Allocate(newCapacity)

                    System.Array.Copy(x.InternalData, 0, newData, 0, x.InternalCount)

#if !FABLE_COMPILER
                    if x.ReturnToPool then
                        ResizableBuffer.Return(x.InternalData)
#endif

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

            member x.WriteStringSlice(s: string, startIndex: int, length: int) =
#if !FABLE_COMPILER
                let maxBytes = Encoding.UTF8.GetMaxByteCount(length)
                (x :> IByteWriter).Ensure(maxBytes)

                let written =
                    Encoding.UTF8.GetBytes(s, startIndex, length, x.InternalData, x.InternalCount)

                x.InternalCount <- x.InternalCount + written
#else
                let slice = s.Substring(startIndex, length)
                let bytes = Encoding.UTF8.GetBytes(slice)
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

            member x.WriteInt64(value: int64) =
                if value = 0L then
                    (x :> IByteWriter).WriteByte(48uy)
                elif value = System.Int64.MinValue then
                    (x :> IByteWriter).WriteString("-9223372036854775808")
                else if value < 0L then
                    (x :> IByteWriter).WriteByte(45uy)
                    x.WriteUInt64Digits(uint64 (-value))
                else
                    x.WriteUInt64Digits(uint64 value)

            member x.WriteUInt32(value: uint32) = x.WriteUInt64Digits(uint64 value)

            member x.WriteUInt64(value: uint64) = x.WriteUInt64Digits(value)

            member x.WriteFloat(value: float) =
#if !FABLE_COMPILER
                let mutable written = 0
                (x :> IByteWriter).Ensure(32)

                let destination =
                    System.Span<byte>(x.InternalData, x.InternalCount, x.InternalData.Length - x.InternalCount)

                if Utf8Formatter.TryFormat(value, destination, &written) then
                    x.InternalCount <- x.InternalCount + written
                else
                    (x :> IByteWriter).WriteString(value.ToString("R", CultureInfo.InvariantCulture))
#else
                (x :> IByteWriter).WriteString(Schema.formatFloat value)
#endif

            member x.WriteDecimal(value: decimal) =
#if !FABLE_COMPILER
                let mutable written = 0
                (x :> IByteWriter).Ensure(40)

                let destination =
                    System.Span<byte>(x.InternalData, x.InternalCount, x.InternalData.Length - x.InternalCount)

                if Utf8Formatter.TryFormat(value, destination, &written) then
                    x.InternalCount <- x.InternalCount + written
                else
                    (x :> IByteWriter).WriteString(value.ToString(CultureInfo.InvariantCulture))
#else
                (x :> IByteWriter).WriteString(value.ToString(CultureInfo.InvariantCulture))
#endif

            member x.Data = x.InternalData
            member x.Count = x.InternalCount
