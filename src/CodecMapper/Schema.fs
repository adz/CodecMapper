namespace CodecMapper

open System.Text
open System.Collections.Generic
open System.Globalization
open Microsoft.FSharp.Reflection

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
    | MissingAsValue of obj * ISchema
    | NullAsValue of obj * ISchema
    | EmptyCollectionAsValue of obj * ISchema
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
    /// Empty strings are often accidental placeholders rather than meaningful
    /// values. This helper keeps that validation explicit and reusable.
    let nonEmptyString: Schema<string> =
        string
        |> tryMap
            (fun value ->
                if System.String.IsNullOrEmpty(value) then
                    Error "string must not be empty"
                else
                    Ok value)
            id

    ///
    /// Some contracts normalize surrounding whitespace at the boundary rather
    /// than making every caller remember to trim before encode and after decode.
    let trimmedString: Schema<string> =
        string |> map (fun value -> value.Trim()) (fun value -> value.Trim())

    ///
    /// Positive identifiers and counters are a common wire-level constraint,
    /// and `tryMap` keeps that rule opt-in rather than global.
    let positiveInt: Schema<int> =
        int
        |> tryMap (fun value -> if value > 0 then Ok value else Error "int must be positive") id

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

                value[0])
            (fun value -> value.ToString())

    /// A schema for `DateTime` using the round-trippable `"O"` string format.
    let dateTime: Schema<System.DateTime> =
        string
        |> map
            (fun value ->
                System.DateTime.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
            (fun value -> value.ToString("O", CultureInfo.InvariantCulture))

    /// A schema for `DateTimeOffset` using the round-trippable `"O"` string format.
    let dateTimeOffset: Schema<System.DateTimeOffset> =
        string
        |> map
            (fun value ->
                System.DateTimeOffset.ParseExact(
                    value,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind
                ))
            (fun value -> value.ToString("O", CultureInfo.InvariantCulture))

    /// A schema for `TimeSpan` using the invariant `"c"` format.
    let timeSpan: Schema<System.TimeSpan> =
        string
        |> map (fun value -> System.TimeSpan.ParseExact(value, "c", CultureInfo.InvariantCulture)) (fun value ->
            value.ToString("c", CultureInfo.InvariantCulture))

    ///
    /// Keep the .NET path using round-trip float formatting, but let Fable use
    /// the host number string form instead of rejecting the `"R"` specifier.
    let formatFloat (value: float) =
#if FABLE_COMPILER
        value.ToString()
#else
        value.ToString("R", CultureInfo.InvariantCulture)
#endif

    /// Builds a schema for an F# list.
    let inline list (inner: Schema<'T>) : Schema<'T list> = create (List(inner :> ISchema))

    ///
    /// Some wire contracts require at least one item, but keeping that rule in
    /// the schema is still clearer than scattering ad hoc list checks elsewhere.
    let inline nonEmptyList (inner: Schema<'T>) : Schema<'T list> =
        list inner
        |> tryMap
            (fun values ->
                match values with
                | [] -> Error "list must contain at least one item"
                | _ -> Ok values)
            id

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

    let private enumSchema (enumType: System.Type) (underlyingSchema: ISchema) : ISchema =
        { new ISchema with
            member _.TargetType = enumType

            member _.Definition =
                Map(
                    underlyingSchema,
                    (fun value ->
#if FABLE_COMPILER
                        value
#else
                        System.Enum.ToObject(enumType, value)
#endif
                    ),
                    (fun value ->
#if FABLE_COMPILER
                        value
#else
                        System.Convert.ChangeType(
                            value,
                            System.Enum.GetUnderlyingType(enumType),
                            CultureInfo.InvariantCulture
                        )
#endif
                    )
                )
        }

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
    /// Config-style payloads sometimes have explicit defaults that should be
    /// applied only when a field is absent, not when it is present-but-invalid.
    let inline missingAsValue (value: 'T) (inner: Schema<'T>) : Schema<'T> =
        create (MissingAsValue(box value, inner :> ISchema))

    ///
    /// Config-style payloads sometimes use explicit `null` as "use the
    /// default value" for non-option fields. Keep that policy local.
    let inline nullAsValue (value: 'T) (inner: Schema<'T>) : Schema<'T> =
        create (NullAsValue(box value, inner :> ISchema))

    ///
    /// Some config shapes use an explicit empty collection as "use the
    /// default collection" rather than as a meaningful distinct value.
    let inline emptyCollectionAsValue (value: 'T) (inner: Schema<'T>) : Schema<'T> =
        create (EmptyCollectionAsValue(box value, inner :> ISchema))

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
        elif t.IsEnum then
            let underlyingType = System.Enum.GetUnderlyingType(t)
            let underlyingSchema = resolveSchema underlyingType
            enumSchema t underlyingSchema
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
            let arg = unbox<'Field> args[idx]
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
            let arg = unbox<'Field> args[idx]
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
