namespace cmap

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

        new(data, offset) =
            {
                Data = data
                Offset = offset
            }

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
        {
            mutable InternalData: byte[]
            mutable InternalCount: int
        }

        static member Create(initialCapacity: int) =
            {
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
                let written = Encoding.UTF8.GetBytes(s, 0, s.Length, x.InternalData, x.InternalCount)
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
    {
        Name: string
        Type: System.Type
        GetValue: obj -> obj
        Schema: ISchema
    }

and ISchema =
    abstract member TargetType: System.Type
    abstract member Definition: SchemaDefinition

and SchemaDefinition =
    | Primitive of System.Type
    | Record of System.Type * SchemaField[] * (obj[] -> obj)
    | List of ISchema
    | Array of ISchema
    | Map of ISchema * (obj -> obj) * (obj -> obj)

type Schema<'T> = inherit ISchema

module Schema =
    let unwrap (s: ISchema) = s

    let inline create<'T> def =
        { new Schema<'T> with
            member _.TargetType = typeof<'T>
            member _.Definition = def
        }

    let int: Schema<int> = create (Primitive typeof<int>)
    let string: Schema<string> = create (Primitive typeof<string>)
    let bool: Schema<bool> = create (Primitive typeof<bool>)

    let inline map (wrap: 'U -> 'T) (unwrapFunc: 'T -> 'U) (inner: Schema<'U>) : Schema<'T> =
        create (Map(inner :> ISchema, (fun x -> box (wrap (unbox x))), (fun x -> box (unwrapFunc (unbox x)))))

    let inline list (inner: Schema<'T>) : Schema<'T list> = create (List(inner :> ISchema))

    let inline array (inner: Schema<'T>) : Schema<'T[]> = create (Array(inner :> ISchema))

    let rec resolveSchema (t: System.Type) : ISchema =
        if t = typeof<int> then
            int :> ISchema
        elif t = typeof<string> then
            string :> ISchema
        elif t = typeof<bool> then
            bool :> ISchema
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
        elif t.IsArray then
            let elementType = t.GetElementType()
            let elementSchema = resolveSchema elementType

            { new ISchema with
                member _.TargetType = t
                member _.Definition = Array(elementSchema)
            }
        else
            failwithf "Could not automatically resolve schema for type %O. Please provide it explicitly." t

type SchemaState<'T> =
    {
        Constructor: (obj[] -> 'T) option
        Fields: SchemaField list
    }

type SchemaBuilder<'T>() =
    member _.Yield(()) =
        {
            Constructor = None
            Fields = []
        }

    [<CustomOperation("construct")>]
    member inline _.Construct(state: SchemaState<'T>, ctor: obj[] -> 'T) = { state with Constructor = Some ctor }

    [<CustomOperation("construct")>]
    member inline _.Construct(state: SchemaState<'T>, ctor: 'a -> 'T) =
        { state with
            Constructor = Some(fun args -> ctor (unbox args.[0]))
        }

    [<CustomOperation("construct")>]
    member inline _.Construct(state: SchemaState<'T>, ctor: 'a -> 'b -> 'T) =
        { state with
            Constructor = Some(fun args -> ctor (unbox args.[0]) (unbox args.[1]))
        }

    [<CustomOperation("construct")>]
    member inline _.Construct(state: SchemaState<'T>, ctor: 'a -> 'b -> 'c -> 'T) =
        { state with
            Constructor = Some(fun args -> ctor (unbox args.[0]) (unbox args.[1]) (unbox args.[2]))
        }

    [<CustomOperation("construct")>]
    member inline _.Construct(state: SchemaState<'T>, ctor: 'a -> 'b -> 'c -> 'd -> 'T) =
        { state with
            Constructor = Some(fun args -> ctor (unbox args.[0]) (unbox args.[1]) (unbox args.[2]) (unbox args.[3]))
        }

    [<CustomOperation("construct")>]
    member inline _.Construct(state: SchemaState<'T>, ctor: 'a -> 'b -> 'c -> 'd -> 'e -> 'T) =
        { state with
            Constructor =
                Some(fun args -> ctor (unbox args.[0]) (unbox args.[1]) (unbox args.[2]) (unbox args.[3]) (unbox args.[4]))
        }

    [<CustomOperation("construct")>]
    member inline _.Construct(state: SchemaState<'T>, ctor: 'a -> 'b -> 'c -> 'd -> 'e -> 'f -> 'T) =
        { state with
            Constructor =
                Some(fun args ->
                    ctor
                        (unbox args.[0])
                        (unbox args.[1])
                        (unbox args.[2])
                        (unbox args.[3])
                        (unbox args.[4])
                        (unbox args.[5]))
        }

    [<CustomOperation("construct")>]
    member inline _.Construct(state: SchemaState<'T>, ctor: 'a -> 'b -> 'c -> 'd -> 'e -> 'f -> 'g -> 'T) =
        { state with
            Constructor =
                Some(fun args ->
                    ctor
                        (unbox args.[0])
                        (unbox args.[1])
                        (unbox args.[2])
                        (unbox args.[3])
                        (unbox args.[4])
                        (unbox args.[5])
                        (unbox args.[6]))
        }

    [<CustomOperation("construct")>]
    member inline _.Construct(state: SchemaState<'T>, ctor: 'a -> 'b -> 'c -> 'd -> 'e -> 'f -> 'g -> 'h -> 'T) =
        { state with
            Constructor =
                Some(fun args ->
                    ctor
                        (unbox args.[0])
                        (unbox args.[1])
                        (unbox args.[2])
                        (unbox args.[3])
                        (unbox args.[4])
                        (unbox args.[5])
                        (unbox args.[6])
                        (unbox args.[7]))
        }

    [<CustomOperation("construct")>]
    member inline _.Construct(state: SchemaState<'T>, ctor: 'a -> 'b -> 'c -> 'd -> 'e -> 'f -> 'g -> 'h -> 'i -> 'T) =
        { state with
            Constructor =
                Some(fun args ->
                    ctor
                        (unbox args.[0])
                        (unbox args.[1])
                        (unbox args.[2])
                        (unbox args.[3])
                        (unbox args.[4])
                        (unbox args.[5])
                        (unbox args.[6])
                        (unbox args.[7])
                        (unbox args.[8]))
        }

    [<CustomOperation("construct")>]
    member inline _.Construct
        (
            state: SchemaState<'T>,
            ctor: 'a -> 'b -> 'c -> 'd -> 'e -> 'f -> 'g -> 'h -> 'i -> 'j -> 'T
        ) =
        { state with
            Constructor =
                Some(fun args ->
                    ctor
                        (unbox args.[0])
                        (unbox args.[1])
                        (unbox args.[2])
                        (unbox args.[3])
                        (unbox args.[4])
                        (unbox args.[5])
                        (unbox args.[6])
                        (unbox args.[7])
                        (unbox args.[8])
                        (unbox args.[9]))
        }

    [<CustomOperation("construct")>]
    member inline _.Construct
        (
            state: SchemaState<'T>,
            ctor: 'a -> 'b -> 'c -> 'd -> 'e -> 'f -> 'g -> 'h -> 'i -> 'j -> 'k -> 'T
        ) =
        { state with
            Constructor =
                Some(fun args ->
                    ctor
                        (unbox args.[0])
                        (unbox args.[1])
                        (unbox args.[2])
                        (unbox args.[3])
                        (unbox args.[4])
                        (unbox args.[5])
                        (unbox args.[6])
                        (unbox args.[7])
                        (unbox args.[8])
                        (unbox args.[9])
                        (unbox args.[10]))
        }

    [<CustomOperation("construct")>]
    member inline _.Construct
        (
            state: SchemaState<'T>,
            ctor: 'a -> 'b -> 'c -> 'd -> 'e -> 'f -> 'g -> 'h -> 'i -> 'j -> 'k -> 'l -> 'T
        ) =
        { state with
            Constructor =
                Some(fun args ->
                    ctor
                        (unbox args.[0])
                        (unbox args.[1])
                        (unbox args.[2])
                        (unbox args.[3])
                        (unbox args.[4])
                        (unbox args.[5])
                        (unbox args.[6])
                        (unbox args.[7])
                        (unbox args.[8])
                        (unbox args.[9])
                        (unbox args.[10])
                        (unbox args.[11]))
        }

    [<CustomOperation("construct")>]
    member inline _.Construct
        (
            state: SchemaState<'T>,
            ctor: 'a -> 'b -> 'c -> 'd -> 'e -> 'f -> 'g -> 'h -> 'i -> 'j -> 'k -> 'l -> 'm -> 'T
        ) =
        { state with
            Constructor =
                Some(fun args ->
                    ctor
                        (unbox args.[0])
                        (unbox args.[1])
                        (unbox args.[2])
                        (unbox args.[3])
                        (unbox args.[4])
                        (unbox args.[5])
                        (unbox args.[6])
                        (unbox args.[7])
                        (unbox args.[8])
                        (unbox args.[9])
                        (unbox args.[10])
                        (unbox args.[11])
                        (unbox args.[12]))
        }

    [<CustomOperation("construct")>]
    member inline _.Construct
        (
            state: SchemaState<'T>,
            ctor: 'a -> 'b -> 'c -> 'd -> 'e -> 'f -> 'g -> 'h -> 'i -> 'j -> 'k -> 'l -> 'm -> 'n -> 'T
        ) =
        { state with
            Constructor =
                Some(fun args ->
                    ctor
                        (unbox args.[0])
                        (unbox args.[1])
                        (unbox args.[2])
                        (unbox args.[3])
                        (unbox args.[4])
                        (unbox args.[5])
                        (unbox args.[6])
                        (unbox args.[7])
                        (unbox args.[8])
                        (unbox args.[9])
                        (unbox args.[10])
                        (unbox args.[11])
                        (unbox args.[12])
                        (unbox args.[13]))
        }

    [<CustomOperation("construct")>]
    member inline _.Construct
        (
            state: SchemaState<'T>,
            ctor: 'a -> 'b -> 'c -> 'd -> 'e -> 'f -> 'g -> 'h -> 'i -> 'j -> 'k -> 'l -> 'm -> 'n -> 'o -> 'T
        ) =
        { state with
            Constructor =
                Some(fun args ->
                    ctor
                        (unbox args.[0])
                        (unbox args.[1])
                        (unbox args.[2])
                        (unbox args.[3])
                        (unbox args.[4])
                        (unbox args.[5])
                        (unbox args.[6])
                        (unbox args.[7])
                        (unbox args.[8])
                        (unbox args.[9])
                        (unbox args.[10])
                        (unbox args.[11])
                        (unbox args.[12])
                        (unbox args.[13])
                        (unbox args.[14]))
        }

    [<CustomOperation("construct")>]
    member inline _.Construct
        (
            state: SchemaState<'T>,
            ctor: 'a -> 'b -> 'c -> 'd -> 'e -> 'f -> 'g -> 'h -> 'i -> 'j -> 'k -> 'l -> 'm -> 'n -> 'o -> 'p -> 'T
        ) =
        { state with
            Constructor =
                Some(fun args ->
                    ctor
                        (unbox args.[0])
                        (unbox args.[1])
                        (unbox args.[2])
                        (unbox args.[3])
                        (unbox args.[4])
                        (unbox args.[5])
                        (unbox args.[6])
                        (unbox args.[7])
                        (unbox args.[8])
                        (unbox args.[9])
                        (unbox args.[10])
                        (unbox args.[11])
                        (unbox args.[12])
                        (unbox args.[13])
                        (unbox args.[14])
                        (unbox args.[15]))
        }

    [<CustomOperation("field")>]
    member inline _.Field(state: SchemaState<'T>, name: string, getter: 'T -> 'Field) =
        let s = Schema.resolveSchema typeof<'Field>

        let f =
            {
                Name = name
                Type = typeof<'Field>
                GetValue = (fun r -> box (getter (unbox r)))
                Schema = s
            }

        { state with
            Fields = f :: state.Fields
        }

    [<CustomOperation("field")>]
    member inline _.Field(state: SchemaState<'T>, name: string, getter: 'T -> 'Field, schema: Schema<'Field>) =
        let f =
            {
                Name = name
                Type = typeof<'Field>
                GetValue = (fun r -> box (getter (unbox r)))
                Schema = Schema.unwrap schema
            }

        { state with
            Fields = f :: state.Fields
        }

    member inline _.Run(state: SchemaState<'T>) : Schema<'T> =
        match state.Constructor with
        | None -> failwith "Schema must have a constructor. Use 'construct'."
        | Some ctor ->
            let targetType = typeof<'T>
            let fields = state.Fields |> List.rev |> List.toArray

            Schema.create<'T> (Record(targetType, fields, (fun args -> box (ctor args))))

[<AutoOpen>]
module SchemaExtensions =
    let schema<'T> = SchemaBuilder<'T>()

module Json =
    type JsonSource = ByteSource
    type JsonWriter = IByteWriter

    type Decoder<'T> = JsonSource -> struct ('T * JsonSource)
    type Encoder<'T> = JsonWriter -> 'T -> unit

    type Codec<'T> =
        {
            Encode: Encoder<'T>
            Decode: Decoder<'T>
        }

    module internal Runtime =
        let inline skipWhitespace (src: JsonSource) =
            let mutable i = src.Offset
            let data = src.Data

            while i < data.Length && (data.[i] = 32uy || data.[i] = 10uy || data.[i] = 13uy || data.[i] = 9uy) do
                i <- i + 1

            ByteSource(data, i)

        let intDecoder: Decoder<int> =
            fun src ->
                let src = skipWhitespace src

                if src.Offset >= src.Data.Length then
                    failwith "Unexpected end of input"

                let mutable i = src.Offset
                let mutable res = 0
                let mutable neg = false
                let data = src.Data

                if data.[i] = 45uy then
                    neg <- true
                    i <- i + 1

                let start = i

                while i < data.Length && data.[i] >= 48uy && data.[i] <= 57uy do
                    res <- res * 10 + (System.Convert.ToInt32(data.[i]) - 48)
                    i <- i + 1

                if i = start then
                    failwith "Expected digit"

                struct ((if neg then -res else res), ByteSource(data, i))

        let stringRaw (src: JsonSource) : struct (int * int * JsonSource) =
            let src = skipWhitespace src
            let data = src.Data

            if src.Offset >= data.Length || data.[src.Offset] <> 34uy then
                failwith "Expected \""

            let mutable i = src.Offset + 1

            while i < data.Length && not (data.[i] = 34uy && data.[i - 1] <> 92uy) do
                i <- i + 1

            if i >= data.Length then
                failwith "Unterminated string"

            struct (src.Offset + 1, i - (src.Offset + 1), ByteSource(data, i + 1))

        let stringDecoder: Decoder<string> =
            fun src ->
                let struct (offset, len, nextSrc) = stringRaw src
#if !FABLE_COMPILER
                struct (Encoding.UTF8.GetString(src.Data, offset, len), nextSrc)
#else
                struct (Encoding.UTF8.GetString(src.Data.[offset .. offset + len - 1]), nextSrc)
#endif

        let rec skipValue (src: JsonSource) : JsonSource =
            let src = skipWhitespace src

            if src.Offset >= src.Data.Length then
                src
            else
                let data = src.Data

                match data.[src.Offset] with
                | 123uy ->
                    let mutable depth = 1
                    let mutable i = src.Offset + 1

                    while depth > 0 && i < data.Length do
                        if data.[i] = 123uy then
                            depth <- depth + 1
                        elif data.[i] = 125uy then
                            depth <- depth - 1

                        i <- i + 1

                    ByteSource(data, i)
                | 91uy ->
                    let mutable depth = 1
                    let mutable i = src.Offset + 1

                    while depth > 0 && i < data.Length do
                        if data.[i] = 91uy then
                            depth <- depth + 1
                        elif data.[i] = 93uy then
                            depth <- depth - 1

                        i <- i + 1

                    ByteSource(data, i)
                | 34uy ->
                    let struct (_, _, nextSrc) = stringRaw src
                    nextSrc
                | _ ->
                    let mutable i = src.Offset

                    while i < data.Length && data.[i] <> 44uy && data.[i] <> 125uy && data.[i] <> 93uy do
                        i <- i + 1

                    ByteSource(data, i)

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

    type CompiledCodec =
        {
            Encode: JsonWriter -> obj -> unit
            Decode: JsonSource -> struct (obj * JsonSource)
        }

    let rec compileUntyped (schema: ISchema) : CompiledCodec =
        match schema.Definition with
        | Primitive t when t = typeof<int> ->
            {
                Encode = (fun w v -> w.WriteInt(unbox v))
                Decode = (fun src -> let struct (v, s) = Runtime.intDecoder src in struct (box v, s))
            }
        | Primitive t when t = typeof<string> ->
            {
                Encode = (fun w v -> w.WriteByte(34uy); w.WriteString(unbox v); w.WriteByte(34uy))
                Decode = (fun src -> let struct (v, s) = Runtime.stringDecoder src in struct (box v, s))
            }
        | Record(t, fields, ctor) ->
            let compiledFields =
                fields
                |> Array.mapi (fun i f ->
                    let codec = compileUntyped f.Schema
                    let keyBytes = Encoding.UTF8.GetBytes(f.Name)

                    {|
                        Name = f.Name
                        KeyBytes = keyBytes
                        Index = i
                        Codec = codec
                    |})

            let encoder (writer: JsonWriter) (vObj: obj) =
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
                    let struct (keyOffset, keyLen, afterKey) = Runtime.stringRaw current
                    let afterColon = Runtime.skipWhitespace afterKey

                    if afterColon.Offset >= data.Length || data.[afterColon.Offset] <> 58uy then
                        failwith "Expected :"

                    let valSrc = Runtime.skipWhitespace (afterColon.Advance(1))
                    let mutable found = false
                    let mutable i = 0

                    while i < compiledFields.Length && not found do
                        let f = compiledFields.[i]

                        if Runtime.bytesEqual f.KeyBytes data keyOffset keyLen then
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
                            failwithf "Missing required key: %s" f.Name

                        let struct (v, _) = f.Codec.Decode valSrc
                        v)

                struct (ctor args, current)

            {
                Encode = encoder
                Decode = decoder
            }
        | List innerSchema ->
            let innerCodec = compileUntyped innerSchema

            let encoder (writer: JsonWriter) (vObj: obj) =
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
            }
        | Map(inner, wrap, unwrapFunc) ->
            let innerCodec = compileUntyped inner

            {
                Encode = (fun w v -> innerCodec.Encode w (unwrapFunc v))
                Decode = (fun src -> let struct (v, s) = innerCodec.Decode src in struct (wrap v, s))
            }
        | _ -> failwithf "Unsupported schema type: %O" schema.Definition

    let compile (schema: Schema<'T>) : Codec<'T> =
        let compiled = compileUntyped (schema :> ISchema)

        {
            Encode = (fun w v -> compiled.Encode w (box v))
            Decode = (fun src -> let struct (v, s) = compiled.Decode src in struct (unbox v, s))
        }

    let serialize (codec: Codec<'T>) (value: 'T) =
        let writer = ResizableBuffer.Create(128)
        codec.Encode writer value
        Encoding.UTF8.GetString(writer.InternalData, 0, writer.InternalCount)

    let deserialize (codec: Codec<'T>) (json: string) =
        let bytes = Encoding.UTF8.GetBytes(json)
        let struct (v, _) = codec.Decode (ByteSource(bytes, 0))
        v

    let deserializeBytes (codec: Codec<'T>) (bytes: byte[]) =
        let struct (v, _) = codec.Decode (ByteSource(bytes, 0))
        v

module Xml =
    type XmlSource = ByteSource
    type XmlWriter = IByteWriter

    type Codec<'T> =
        {
            Encode: XmlWriter -> 'T -> unit
            Decode: XmlSource -> struct ('T * XmlSource)
        }

    type CompiledCodec =
        {
            Encode: XmlWriter -> string -> obj -> unit
            Decode: XmlSource -> string -> struct (obj * XmlSource)
        }

    let rec compileUntyped (schema: ISchema) : CompiledCodec =
        match schema.Definition with
        | Primitive t when t = typeof<int> ->
            {
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
                        let data = src.Data
                        let mutable i = src.Offset

                        if data.[i] <> 60uy then
                            failwith "Expected <"

                        i <- i + tag.Length + 2
                        let start = i

                        while data.[i] <> 60uy do
                            i <- i + 1

                        let valStr = Encoding.UTF8.GetString(data, start, i - start)
#if !FABLE_COMPILER
                        let v = System.Int32.Parse(valStr)
#else
                        let v = System.Int32.Parse(valStr)
#endif
                        i <- i + tag.Length + 3
                        struct (box v, ByteSource(data, i)))
            }
        | Primitive t when t = typeof<string> ->
            {
                Encode =
                    (fun w tag v ->
                        w.WriteByte(60uy)
                        w.WriteString(tag)
                        w.WriteByte(62uy)
                        w.WriteString(unbox v)
                        w.WriteByte(60uy)
                        w.WriteByte(47uy)
                        w.WriteString(tag)
                        w.WriteByte(62uy))
                Decode =
                    (fun src tag ->
                        let data = src.Data
                        let mutable i = src.Offset
                        i <- i + tag.Length + 2
                        let start = i

                        while data.[i] <> 60uy do
                            i <- i + 1

                        let v = Encoding.UTF8.GetString(data, start, i - start)
                        i <- i + tag.Length + 3
                        struct (box v, ByteSource(data, i)))
            }
        | Record(t, fields, ctor) ->
            let compiledFields =
                fields
                |> Array.map (fun f ->
                    {|
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
                        let mutable current = src

                        if current.Data.[current.Offset] <> 60uy then
                            failwith "Expected <"

                        current <- current.Advance(tag.Length + 2)

                        let args =
                            compiledFields
                            |> Array.map (fun f ->
                                let struct (v, next) = f.Codec.Decode current f.Name
                                current <- next
                                v)

                        current <- current.Advance(tag.Length + 3)
                        struct (ctor args, current))
            }
        | _ -> failwithf "Unsupported XML schema type"

    let compile (schema: Schema<'T>) : Codec<'T> =
        let compiled = compileUntyped (schema :> ISchema)
        let rootTag = schema.TargetType.Name.ToLower()

        {
            Encode = (fun w v -> compiled.Encode w rootTag (box v))
            Decode = (fun src -> let struct (v, s) = compiled.Decode src rootTag in struct (unbox v, s))
        }

    let serialize (codec: Codec<'T>) (value: 'T) =
        let writer = ResizableBuffer.Create(128)
        codec.Encode writer value
        Encoding.UTF8.GetString(writer.InternalData, 0, writer.InternalCount)
