namespace CodecMapper

open System
open System.Reflection
open System.Linq.Expressions

module internal JsonTypedRecordDecode =
    type Decoder<'T> = ByteSource -> struct ('T * ByteSource)

    type Runtime = {
        SkipWhitespace: ByteSource -> ByteSource
        StringRaw: ByteSource -> struct (int * int * bool * ByteSource)
        StringDecoder: ByteSource -> struct (string * ByteSource)
        SkipValue: ByteSource -> ByteSource
        WrapFieldError: string -> exn -> exn
    }

    type FieldBox = {
        FieldType: Type
        RuntimeField: obj
    }

    type private CompiledField<'T> = {
        Name: string
        PropertyText: string
        PropertyUtf8: byte[]
        Decode: Decoder<'T>
    }

    type private IRecordDecoder =
        abstract member Decode: ByteSource -> struct (obj * ByteSource)

    let createField<'T> (name: string) (decode: Decoder<'T>) : FieldBox = {
        FieldType = typeof<'T>
        RuntimeField = box {
            Name = name
            PropertyText = name
            PropertyUtf8 = System.Text.Encoding.UTF8.GetBytes(name)
            Decode = decode
        }
    }

    let private bytesEqual (expected: byte[]) (data: byte[]) (offset: int) (length: int) =
        if expected.Length <> length then
            false
        else
            let mutable index = 0
            let mutable equal = true

            while index < length && equal do
                if expected[index] <> data[offset + index] then
                    equal <- false
                else
                    index <- index + 1

            equal

    let private expectByte (runtime: Runtime) (expected: byte) (label: string) (src: ByteSource) =
        let src = runtime.SkipWhitespace src

        if src.Offset >= src.Data.Length || src.Data[src.Offset] <> expected then
            failwithf "Expected %s" label

        runtime.SkipWhitespace (src.Advance(1))

    let listDecoder<'T> (runtime: Runtime) (decodeItem: Decoder<'T>) : Decoder<'T list> =
        fun src ->
            let mutable current = expectByte runtime (byte '[') "[" src
            let data = current.Data
            let mutable items = []
            let mutable continueLoop = true

            if current.Offset < data.Length && data[current.Offset] = byte ']' then
                current <- runtime.SkipWhitespace (current.Advance(1))
                continueLoop <- false

            while continueLoop do
                let struct (item, afterItem) = decodeItem current
                items <- item :: items
                let afterItem = runtime.SkipWhitespace afterItem

                if afterItem.Offset < data.Length && data[afterItem.Offset] = byte ',' then
                    current <- runtime.SkipWhitespace (afterItem.Advance(1))
                elif afterItem.Offset < data.Length && data[afterItem.Offset] = byte ']' then
                    current <- runtime.SkipWhitespace (afterItem.Advance(1))
                    continueLoop <- false
                else
                    failwith "Expected , or ]"

            struct (List.rev items, current)

    let arrayDecoder<'T> (runtime: Runtime) (decodeItem: Decoder<'T>) : Decoder<'T array> =
        fun src ->
            let struct (items, next) = listDecoder runtime decodeItem src
            struct (List.toArray items, next)

    let private propertyEquals
        (runtime: Runtime)
        (fieldText: string)
        (fieldUtf8: byte[])
        (current: ByteSource)
        (start: int)
        (length: int)
        (hadEscapes: bool)
        =
        if hadEscapes then
            let struct (name, _) = runtime.StringDecoder current
            name = fieldText
        else
            bytesEqual fieldUtf8 current.Data start length

    let private decodeFieldValue (runtime: Runtime) (field: CompiledField<'T>) (afterColon: ByteSource) =
        try
            field.Decode afterColon
        with ex ->
            raise (runtime.WrapFieldError field.Name ex)

    let private readObject
        (runtime: Runtime)
        (src: ByteSource)
        (decodeField: ByteSource -> int -> int -> bool -> ByteSource -> ByteSource)
        =
        let mutable current = expectByte runtime (byte '{') "{" src
        let data = current.Data
        let mutable continueLoop = true

        if current.Offset < data.Length && data[current.Offset] = byte '}' then
            current <- runtime.SkipWhitespace (current.Advance(1))
            continueLoop <- false

        while continueLoop do
            let struct (keyStart, keyLength, keyHasEscapes, afterRawKey) = runtime.StringRaw current
            let afterColon = expectByte runtime (byte ':') ":" afterRawKey
            let afterValue = decodeField current keyStart keyLength keyHasEscapes afterColon

            if afterValue.Offset < data.Length && data[afterValue.Offset] = byte ',' then
                current <- runtime.SkipWhitespace (afterValue.Advance(1))
            elif afterValue.Offset < data.Length && data[afterValue.Offset] = byte '}' then
                current <- runtime.SkipWhitespace (afterValue.Advance(1))
                continueLoop <- false
            else
                failwith "Expected , or }"

        current

    let private requireField (runtime: Runtime) fieldName seen =
        if not seen then
            raise (runtime.WrapFieldError fieldName (Exception(sprintf "Missing required key '%s'" fieldName)))

    type private RecordDecoder1<'T, 'A>
        (runtime: Runtime, field1: CompiledField<'A>, ctor: Func<'A, 'T>) =
        interface IRecordDecoder with
            member _.Decode(src: ByteSource) =
                let mutable value1 = Unchecked.defaultof<'A>
                let mutable saw1 = false

                let current =
                    readObject runtime src (fun current keyStart keyLength keyHasEscapes afterColon ->
                        if propertyEquals runtime field1.PropertyText field1.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field1 afterColon
                            value1 <- value
                            saw1 <- true
                            runtime.SkipWhitespace next
                        else
                            runtime.SkipWhitespace (runtime.SkipValue afterColon))

                requireField runtime field1.Name saw1
                struct (box (ctor.Invoke(value1)), current)

    type private RecordDecoder2<'T, 'A, 'B>
        (runtime: Runtime, field1: CompiledField<'A>, field2: CompiledField<'B>, ctor: Func<'A, 'B, 'T>) =
        interface IRecordDecoder with
            member _.Decode(src: ByteSource) =
                let mutable value1 = Unchecked.defaultof<'A>
                let mutable value2 = Unchecked.defaultof<'B>
                let mutable saw1 = false
                let mutable saw2 = false

                let current =
                    readObject runtime src (fun current keyStart keyLength keyHasEscapes afterColon ->
                        if propertyEquals runtime field1.PropertyText field1.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field1 afterColon
                            value1 <- value
                            saw1 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field2.PropertyText field2.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field2 afterColon
                            value2 <- value
                            saw2 <- true
                            runtime.SkipWhitespace next
                        else
                            runtime.SkipWhitespace (runtime.SkipValue afterColon))

                requireField runtime field1.Name saw1
                requireField runtime field2.Name saw2
                struct (box (ctor.Invoke(value1, value2)), current)

    type private RecordDecoder3<'T, 'A, 'B, 'C>
        (
            runtime: Runtime,
            field1: CompiledField<'A>,
            field2: CompiledField<'B>,
            field3: CompiledField<'C>,
            ctor: Func<'A, 'B, 'C, 'T>
        ) =
        interface IRecordDecoder with
            member _.Decode(src: ByteSource) =
                let mutable value1 = Unchecked.defaultof<'A>
                let mutable value2 = Unchecked.defaultof<'B>
                let mutable value3 = Unchecked.defaultof<'C>
                let mutable saw1 = false
                let mutable saw2 = false
                let mutable saw3 = false

                let current =
                    readObject runtime src (fun current keyStart keyLength keyHasEscapes afterColon ->
                        if propertyEquals runtime field1.PropertyText field1.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field1 afterColon
                            value1 <- value
                            saw1 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field2.PropertyText field2.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field2 afterColon
                            value2 <- value
                            saw2 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field3.PropertyText field3.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field3 afterColon
                            value3 <- value
                            saw3 <- true
                            runtime.SkipWhitespace next
                        else
                            runtime.SkipWhitespace (runtime.SkipValue afterColon))

                requireField runtime field1.Name saw1
                requireField runtime field2.Name saw2
                requireField runtime field3.Name saw3
                struct (box (ctor.Invoke(value1, value2, value3)), current)

    type private RecordDecoder4<'T, 'A, 'B, 'C, 'D>
        (
            runtime: Runtime,
            field1: CompiledField<'A>,
            field2: CompiledField<'B>,
            field3: CompiledField<'C>,
            field4: CompiledField<'D>,
            ctor: Func<'A, 'B, 'C, 'D, 'T>
        ) =
        interface IRecordDecoder with
            member _.Decode(src: ByteSource) =
                let mutable value1 = Unchecked.defaultof<'A>
                let mutable value2 = Unchecked.defaultof<'B>
                let mutable value3 = Unchecked.defaultof<'C>
                let mutable value4 = Unchecked.defaultof<'D>
                let mutable saw1 = false
                let mutable saw2 = false
                let mutable saw3 = false
                let mutable saw4 = false

                let current =
                    readObject runtime src (fun current keyStart keyLength keyHasEscapes afterColon ->
                        if propertyEquals runtime field1.PropertyText field1.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field1 afterColon
                            value1 <- value
                            saw1 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field2.PropertyText field2.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field2 afterColon
                            value2 <- value
                            saw2 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field3.PropertyText field3.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field3 afterColon
                            value3 <- value
                            saw3 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field4.PropertyText field4.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field4 afterColon
                            value4 <- value
                            saw4 <- true
                            runtime.SkipWhitespace next
                        else
                            runtime.SkipWhitespace (runtime.SkipValue afterColon))

                requireField runtime field1.Name saw1
                requireField runtime field2.Name saw2
                requireField runtime field3.Name saw3
                requireField runtime field4.Name saw4
                struct (box (ctor.Invoke(value1, value2, value3, value4)), current)

    type private RecordDecoder5<'T, 'A, 'B, 'C, 'D, 'E>
        (
            runtime: Runtime,
            field1: CompiledField<'A>,
            field2: CompiledField<'B>,
            field3: CompiledField<'C>,
            field4: CompiledField<'D>,
            field5: CompiledField<'E>,
            ctor: Func<'A, 'B, 'C, 'D, 'E, 'T>
        ) =
        interface IRecordDecoder with
            member _.Decode(src: ByteSource) =
                let mutable value1 = Unchecked.defaultof<'A>
                let mutable value2 = Unchecked.defaultof<'B>
                let mutable value3 = Unchecked.defaultof<'C>
                let mutable value4 = Unchecked.defaultof<'D>
                let mutable value5 = Unchecked.defaultof<'E>
                let mutable saw1 = false
                let mutable saw2 = false
                let mutable saw3 = false
                let mutable saw4 = false
                let mutable saw5 = false

                let current =
                    readObject runtime src (fun current keyStart keyLength keyHasEscapes afterColon ->
                        if propertyEquals runtime field1.PropertyText field1.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field1 afterColon
                            value1 <- value
                            saw1 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field2.PropertyText field2.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field2 afterColon
                            value2 <- value
                            saw2 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field3.PropertyText field3.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field3 afterColon
                            value3 <- value
                            saw3 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field4.PropertyText field4.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field4 afterColon
                            value4 <- value
                            saw4 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field5.PropertyText field5.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field5 afterColon
                            value5 <- value
                            saw5 <- true
                            runtime.SkipWhitespace next
                        else
                            runtime.SkipWhitespace (runtime.SkipValue afterColon))

                requireField runtime field1.Name saw1
                requireField runtime field2.Name saw2
                requireField runtime field3.Name saw3
                requireField runtime field4.Name saw4
                requireField runtime field5.Name saw5
                struct (box (ctor.Invoke(value1, value2, value3, value4, value5)), current)

    type private RecordDecoder6<'T, 'A, 'B, 'C, 'D, 'E, 'F>
        (
            runtime: Runtime,
            field1: CompiledField<'A>,
            field2: CompiledField<'B>,
            field3: CompiledField<'C>,
            field4: CompiledField<'D>,
            field5: CompiledField<'E>,
            field6: CompiledField<'F>,
            ctor: Func<'A, 'B, 'C, 'D, 'E, 'F, 'T>
        ) =
        interface IRecordDecoder with
            member _.Decode(src: ByteSource) =
                let mutable value1 = Unchecked.defaultof<'A>
                let mutable value2 = Unchecked.defaultof<'B>
                let mutable value3 = Unchecked.defaultof<'C>
                let mutable value4 = Unchecked.defaultof<'D>
                let mutable value5 = Unchecked.defaultof<'E>
                let mutable value6 = Unchecked.defaultof<'F>
                let mutable saw1 = false
                let mutable saw2 = false
                let mutable saw3 = false
                let mutable saw4 = false
                let mutable saw5 = false
                let mutable saw6 = false

                let current =
                    readObject runtime src (fun current keyStart keyLength keyHasEscapes afterColon ->
                        if propertyEquals runtime field1.PropertyText field1.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field1 afterColon
                            value1 <- value
                            saw1 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field2.PropertyText field2.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field2 afterColon
                            value2 <- value
                            saw2 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field3.PropertyText field3.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field3 afterColon
                            value3 <- value
                            saw3 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field4.PropertyText field4.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field4 afterColon
                            value4 <- value
                            saw4 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field5.PropertyText field5.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field5 afterColon
                            value5 <- value
                            saw5 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field6.PropertyText field6.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field6 afterColon
                            value6 <- value
                            saw6 <- true
                            runtime.SkipWhitespace next
                        else
                            runtime.SkipWhitespace (runtime.SkipValue afterColon))

                requireField runtime field1.Name saw1
                requireField runtime field2.Name saw2
                requireField runtime field3.Name saw3
                requireField runtime field4.Name saw4
                requireField runtime field5.Name saw5
                requireField runtime field6.Name saw6
                struct (box (ctor.Invoke(value1, value2, value3, value4, value5, value6)), current)

    type private RecordDecoder7<'T, 'A, 'B, 'C, 'D, 'E, 'F, 'G>
        (
            runtime: Runtime,
            field1: CompiledField<'A>,
            field2: CompiledField<'B>,
            field3: CompiledField<'C>,
            field4: CompiledField<'D>,
            field5: CompiledField<'E>,
            field6: CompiledField<'F>,
            field7: CompiledField<'G>,
            ctor: Func<'A, 'B, 'C, 'D, 'E, 'F, 'G, 'T>
        ) =
        interface IRecordDecoder with
            member _.Decode(src: ByteSource) =
                let mutable value1 = Unchecked.defaultof<'A>
                let mutable value2 = Unchecked.defaultof<'B>
                let mutable value3 = Unchecked.defaultof<'C>
                let mutable value4 = Unchecked.defaultof<'D>
                let mutable value5 = Unchecked.defaultof<'E>
                let mutable value6 = Unchecked.defaultof<'F>
                let mutable value7 = Unchecked.defaultof<'G>
                let mutable saw1 = false
                let mutable saw2 = false
                let mutable saw3 = false
                let mutable saw4 = false
                let mutable saw5 = false
                let mutable saw6 = false
                let mutable saw7 = false

                let current =
                    readObject runtime src (fun current keyStart keyLength keyHasEscapes afterColon ->
                        if propertyEquals runtime field1.PropertyText field1.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field1 afterColon
                            value1 <- value
                            saw1 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field2.PropertyText field2.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field2 afterColon
                            value2 <- value
                            saw2 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field3.PropertyText field3.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field3 afterColon
                            value3 <- value
                            saw3 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field4.PropertyText field4.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field4 afterColon
                            value4 <- value
                            saw4 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field5.PropertyText field5.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field5 afterColon
                            value5 <- value
                            saw5 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field6.PropertyText field6.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field6 afterColon
                            value6 <- value
                            saw6 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field7.PropertyText field7.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field7 afterColon
                            value7 <- value
                            saw7 <- true
                            runtime.SkipWhitespace next
                        else
                            runtime.SkipWhitespace (runtime.SkipValue afterColon))

                requireField runtime field1.Name saw1
                requireField runtime field2.Name saw2
                requireField runtime field3.Name saw3
                requireField runtime field4.Name saw4
                requireField runtime field5.Name saw5
                requireField runtime field6.Name saw6
                requireField runtime field7.Name saw7
                struct (box (ctor.Invoke(value1, value2, value3, value4, value5, value6, value7)), current)

    type private RecordDecoder8<'T, 'A, 'B, 'C, 'D, 'E, 'F, 'G, 'H>
        (
            runtime: Runtime,
            field1: CompiledField<'A>,
            field2: CompiledField<'B>,
            field3: CompiledField<'C>,
            field4: CompiledField<'D>,
            field5: CompiledField<'E>,
            field6: CompiledField<'F>,
            field7: CompiledField<'G>,
            field8: CompiledField<'H>,
            ctor: Func<'A, 'B, 'C, 'D, 'E, 'F, 'G, 'H, 'T>
        ) =
        interface IRecordDecoder with
            member _.Decode(src: ByteSource) =
                let mutable value1 = Unchecked.defaultof<'A>
                let mutable value2 = Unchecked.defaultof<'B>
                let mutable value3 = Unchecked.defaultof<'C>
                let mutable value4 = Unchecked.defaultof<'D>
                let mutable value5 = Unchecked.defaultof<'E>
                let mutable value6 = Unchecked.defaultof<'F>
                let mutable value7 = Unchecked.defaultof<'G>
                let mutable value8 = Unchecked.defaultof<'H>
                let mutable saw1 = false
                let mutable saw2 = false
                let mutable saw3 = false
                let mutable saw4 = false
                let mutable saw5 = false
                let mutable saw6 = false
                let mutable saw7 = false
                let mutable saw8 = false

                let current =
                    readObject runtime src (fun current keyStart keyLength keyHasEscapes afterColon ->
                        if propertyEquals runtime field1.PropertyText field1.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field1 afterColon
                            value1 <- value
                            saw1 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field2.PropertyText field2.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field2 afterColon
                            value2 <- value
                            saw2 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field3.PropertyText field3.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field3 afterColon
                            value3 <- value
                            saw3 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field4.PropertyText field4.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field4 afterColon
                            value4 <- value
                            saw4 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field5.PropertyText field5.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field5 afterColon
                            value5 <- value
                            saw5 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field6.PropertyText field6.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field6 afterColon
                            value6 <- value
                            saw6 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field7.PropertyText field7.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field7 afterColon
                            value7 <- value
                            saw7 <- true
                            runtime.SkipWhitespace next
                        elif propertyEquals runtime field8.PropertyText field8.PropertyUtf8 current keyStart keyLength keyHasEscapes then
                            let struct (value, next) = decodeFieldValue runtime field8 afterColon
                            value8 <- value
                            saw8 <- true
                            runtime.SkipWhitespace next
                        else
                            runtime.SkipWhitespace (runtime.SkipValue afterColon))

                requireField runtime field1.Name saw1
                requireField runtime field2.Name saw2
                requireField runtime field3.Name saw3
                requireField runtime field4.Name saw4
                requireField runtime field5.Name saw5
                requireField runtime field6.Name saw6
                requireField runtime field7.Name saw7
                requireField runtime field8.Name saw8
                struct (box (ctor.Invoke(value1, value2, value3, value4, value5, value6, value7, value8)), current)

    let private tryFindConstructor (targetType: Type) (fieldTypes: Type array) =
        targetType.GetConstructors(BindingFlags.Instance ||| BindingFlags.Public ||| BindingFlags.NonPublic)
        |> Array.tryFind (fun ctor ->
            let parameters = ctor.GetParameters()

            parameters.Length = fieldTypes.Length
            && Array.forall2 (fun (parameter: ParameterInfo) fieldType -> parameter.ParameterType = fieldType) parameters fieldTypes)

    let private buildCtorDelegateType (fieldTypes: Type array) (targetType: Type) =
        match fieldTypes.Length with
        | 1 -> typedefof<Func<_, _>>.MakeGenericType([| fieldTypes[0]; targetType |])
        | 2 -> typedefof<Func<_, _, _>>.MakeGenericType([| fieldTypes[0]; fieldTypes[1]; targetType |])
        | 3 ->
            typedefof<Func<_, _, _, _>>.MakeGenericType([| fieldTypes[0]; fieldTypes[1]; fieldTypes[2]; targetType |])
        | 4 ->
            typedefof<Func<_, _, _, _, _>>.MakeGenericType(
                [| fieldTypes[0]; fieldTypes[1]; fieldTypes[2]; fieldTypes[3]; targetType |]
            )
        | 5 ->
            typedefof<Func<_, _, _, _, _, _>>.MakeGenericType(
                [| fieldTypes[0]; fieldTypes[1]; fieldTypes[2]; fieldTypes[3]; fieldTypes[4]; targetType |]
            )
        | 6 ->
            typedefof<Func<_, _, _, _, _, _, _>>.MakeGenericType(
                [|
                    fieldTypes[0]
                    fieldTypes[1]
                    fieldTypes[2]
                    fieldTypes[3]
                    fieldTypes[4]
                    fieldTypes[5]
                    targetType
                |]
            )
        | 7 ->
            typedefof<Func<_, _, _, _, _, _, _, _>>.MakeGenericType(
                [|
                    fieldTypes[0]
                    fieldTypes[1]
                    fieldTypes[2]
                    fieldTypes[3]
                    fieldTypes[4]
                    fieldTypes[5]
                    fieldTypes[6]
                    targetType
                |]
            )
        | 8 ->
            typedefof<Func<_, _, _, _, _, _, _, _, _>>.MakeGenericType(
                [|
                    fieldTypes[0]
                    fieldTypes[1]
                    fieldTypes[2]
                    fieldTypes[3]
                    fieldTypes[4]
                    fieldTypes[5]
                    fieldTypes[6]
                    fieldTypes[7]
                    targetType
                |]
            )
        | _ -> invalidArg (nameof fieldTypes) "Typed record decode only supports arities 1 through 8."

    let private buildCtorDelegate (targetType: Type) (fieldTypes: Type array) =
        match tryFindConstructor targetType fieldTypes with
        | None -> None
        | Some ctor ->
            let parameters = fieldTypes |> Array.mapi (fun index fieldType -> Expression.Parameter(fieldType, "arg" + string index))
            let body = Expression.New(ctor, parameters |> Array.map (fun parameter -> parameter :> Expression))
            let delegateType = buildCtorDelegateType fieldTypes targetType
            Some(Expression.Lambda(delegateType, body, parameters).Compile())

    let private wrapRuntimeDecoder<'T> (runtimeDecoder: ByteSource -> struct (obj * ByteSource)) : Decoder<'T> =
        fun src ->
            let struct (value, next) = runtimeDecoder src
            struct (unbox<'T> value, next)

    let private boxTypedDecoder<'T> (decoder: Decoder<'T>) : ByteSource -> struct (obj * ByteSource) =
        fun src ->
            let struct (value, next) = decoder src
            struct (box value, next)

    type private ReflectionHelpers =
        static member CreateField<'T>(name: string, decode: Decoder<'T>) = createField name decode
        static member CreateFieldFromRuntime<'T>(name: string, decode: ByteSource -> struct (obj * ByteSource)) =
            createField name (wrapRuntimeDecoder decode)
        static member ListDecoder<'T>(runtime: Runtime, decodeItem: Decoder<'T>) = listDecoder runtime decodeItem
        static member ArrayDecoder<'T>(runtime: Runtime, decodeItem: Decoder<'T>) = arrayDecoder runtime decodeItem
        static member WrapRuntimeDecoder<'T>(runtimeDecoder: ByteSource -> struct (obj * ByteSource)) = wrapRuntimeDecoder runtimeDecoder
        static member BoxTypedDecoder<'T>(decoder: Decoder<'T>) = boxTypedDecoder decoder

    let private createFieldMethod =
        typeof<ReflectionHelpers>.GetMethod("CreateField", BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)

    let private createFieldFromRuntimeMethod =
        typeof<ReflectionHelpers>.GetMethod("CreateFieldFromRuntime", BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)

    let private listDecoderMethod =
        typeof<ReflectionHelpers>.GetMethod("ListDecoder", BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)

    let private arrayDecoderMethod =
        typeof<ReflectionHelpers>.GetMethod("ArrayDecoder", BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)

    let private wrapRuntimeDecoderMethod =
        typeof<ReflectionHelpers>.GetMethod("WrapRuntimeDecoder", BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)

    let private boxTypedDecoderMethod =
        typeof<ReflectionHelpers>.GetMethod("BoxTypedDecoder", BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)

    let createFieldDynamic (fieldType: Type) (name: string) (runtimeDecoder: obj) : FieldBox =
        createFieldMethod.MakeGenericMethod([| fieldType |]).Invoke(null, [| box name; runtimeDecoder |]) :?> FieldBox

    let createFieldFromRuntimeDynamic
        (fieldType: Type)
        (name: string)
        (runtimeDecoder: ByteSource -> struct (obj * ByteSource))
        : FieldBox =
        createFieldFromRuntimeMethod.MakeGenericMethod([| fieldType |]).Invoke(null, [| box name; box runtimeDecoder |]) :?> FieldBox

    let makeListDecoderDynamic (runtime: Runtime) (elementType: Type) (runtimeDecoder: obj) =
        listDecoderMethod.MakeGenericMethod([| elementType |]).Invoke(null, [| box runtime; runtimeDecoder |])

    let makeArrayDecoderDynamic (runtime: Runtime) (elementType: Type) (runtimeDecoder: obj) =
        arrayDecoderMethod.MakeGenericMethod([| elementType |]).Invoke(null, [| box runtime; runtimeDecoder |])

    let boxDecoderDynamic (targetType: Type) (typedDecoder: obj) : (ByteSource -> struct (obj * ByteSource)) =
        boxTypedDecoderMethod.MakeGenericMethod([| targetType |]).Invoke(null, [| typedDecoder |]) :?> _

    let private wrapRuntimeDecoderDynamic (targetType: Type) (runtimeDecoder: ByteSource -> struct (obj * ByteSource)) =
        wrapRuntimeDecoderMethod.MakeGenericMethod([| targetType |]).Invoke(null, [| box runtimeDecoder |])

    let private tryCreateRecordDecoder (runtime: Runtime) (targetType: Type) (fields: FieldBox array) =
        if fields.Length = 0 || fields.Length > 8 then
            None
        else
            let fieldTypes = fields |> Array.map _.FieldType

            match buildCtorDelegate targetType fieldTypes with
            | None -> None
            | Some ctorDelegate ->
                let decoderType =
                    match fields.Length with
                    | 1 -> typedefof<RecordDecoder1<_, _>>
                    | 2 -> typedefof<RecordDecoder2<_, _, _>>
                    | 3 -> typedefof<RecordDecoder3<_, _, _, _>>
                    | 4 -> typedefof<RecordDecoder4<_, _, _, _, _>>
                    | 5 -> typedefof<RecordDecoder5<_, _, _, _, _, _>>
                    | 6 -> typedefof<RecordDecoder6<_, _, _, _, _, _, _>>
                    | 7 -> typedefof<RecordDecoder7<_, _, _, _, _, _, _, _>>
                    | 8 -> typedefof<RecordDecoder8<_, _, _, _, _, _, _, _, _>>
                    | _ -> invalidArg (nameof fields) "Typed record decode only supports arities 1 through 8."

                let genericArgs = Array.append [| targetType |] fieldTypes
                let closedType = decoderType.MakeGenericType(genericArgs)
                let args = Array.append [| box runtime |] (fields |> Array.map _.RuntimeField)
                let args = Array.append args [| ctorDelegate |]
                Some(Activator.CreateInstance(closedType, args) :?> IRecordDecoder)

    let tryCompileRecordDecoder (runtime: Runtime) (targetType: Type) (fields: FieldBox array) =
        tryCreateRecordDecoder runtime targetType fields
        |> Option.map (fun decoder -> wrapRuntimeDecoderDynamic targetType decoder.Decode)

    let tryCompileRecordDecoderRuntime (runtime: Runtime) (targetType: Type) (fields: FieldBox array) =
        tryCreateRecordDecoder runtime targetType fields
        |> Option.map _.Decode
