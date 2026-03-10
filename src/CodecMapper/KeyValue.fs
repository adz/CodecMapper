namespace CodecMapper

open System.Text
open System.Collections.Generic
open System.Globalization
open Microsoft.FSharp.Reflection

/// Flat `string,string` contract projection for config-style schemas.
///
/// This backend is intentionally narrower than JSON or XML: it targets
/// flattened key/value surfaces such as app settings or environment variables.
module KeyValue =
    /// Options controlling how flattened keys are named.
    type Options = {
        Separator: string
        TransformKey: string -> string
    }

    module Options =
        /// Default flat key naming using dotted paths such as `parent.child`.
        let defaults = { Separator = "."; TransformKey = id }

        /// Environment-variable style naming using `__` separators and upper-case keys.
        let environment = {
            Separator = "__"
            TransformKey = fun key -> key.ToUpperInvariant()
        }

    /// A compiled flat key/value codec for a specific schema.
    type Codec<'T> = {
        Encode: 'T -> Map<string, string>
        Decode: Map<string, string> -> 'T
    }

    type CompiledCodec = {
        Encode: string list -> obj -> (string * string) list
        Decode: string list -> Map<string, string> -> obj option
        MissingValue: obj option
    }

    let private keyName (options: Options) (segments: string list) =
        match segments with
        | [] -> failwith "KeyValue paths must contain at least one segment"
        | _ -> segments |> String.concat options.Separator |> options.TransformKey

    let private tryFindValue (options: Options) (segments: string list) (values: Map<string, string>) =
        values |> Map.tryFind (keyName options segments)

    let private parsePrimitive (targetType: System.Type) (text: string) =
        if targetType = typeof<int> then
            box (Core.parseInt32Invariant "int" text)
        elif targetType = typeof<int64> then
            box (Core.parseInt64Invariant "int64" text)
        elif targetType = typeof<uint32> then
            box (Core.parseUInt32Invariant "uint32" text)
        elif targetType = typeof<uint64> then
            box (Core.parseUInt64Invariant "uint64" text)
        elif targetType = typeof<float> then
            box (Core.parseFloatInvariant "float" text)
        elif targetType = typeof<decimal> then
            box (Core.parseDecimalInvariant "decimal" text)
        elif targetType = typeof<string> then
            box text
        elif targetType = typeof<bool> then
            box (System.Boolean.Parse(text))
        elif targetType = typeof<int16> then
            box (Core.parseInt16Invariant "int16" text)
        elif targetType = typeof<byte> then
            box (Core.parseByteInvariant "byte" text)
        elif targetType = typeof<sbyte> then
            box (Core.parseSByteInvariant "sbyte" text)
        elif targetType = typeof<uint16> then
            box (Core.parseUInt16Invariant "uint16" text)
        else
            failwithf "KeyValue does not support primitive type %O" targetType

    let private formatPrimitive (targetType: System.Type) (value: obj) =
        if targetType = typeof<int> then
            string (unbox<int> value)
        elif targetType = typeof<int64> then
            (unbox<int64> value).ToString(CultureInfo.InvariantCulture)
        elif targetType = typeof<uint32> then
            (unbox<uint32> value).ToString(CultureInfo.InvariantCulture)
        elif targetType = typeof<uint64> then
            (unbox<uint64> value).ToString(CultureInfo.InvariantCulture)
        elif targetType = typeof<float> then
            Schema.formatFloat (unbox<float> value)
        elif targetType = typeof<decimal> then
            (unbox<decimal> value).ToString(CultureInfo.InvariantCulture)
        elif targetType = typeof<string> then
            unbox<string> value
        elif targetType = typeof<bool> then
            if unbox<bool> value then "true" else "false"
        elif targetType = typeof<int16> then
            (unbox<int16> value).ToString(CultureInfo.InvariantCulture)
        elif targetType = typeof<byte> then
            (unbox<byte> value).ToString(CultureInfo.InvariantCulture)
        elif targetType = typeof<sbyte> then
            (unbox<sbyte> value).ToString(CultureInfo.InvariantCulture)
        elif targetType = typeof<uint16> then
            (unbox<uint16> value).ToString(CultureInfo.InvariantCulture)
        else
            failwithf "KeyValue does not support primitive type %O" targetType

    let rec private compileUntyped (options: Options) (schema: ISchema) : CompiledCodec =
        match schema.Definition with
        | Primitive targetType -> {
            Encode = (fun path value -> [ keyName options path, formatPrimitive targetType value ])
            Decode = (fun path values -> tryFindValue options path values |> Option.map (parsePrimitive targetType))
            MissingValue = None
          }
        | Record(_, fields, ctor) ->
            let compiledFields =
                fields
                |> Array.mapi (fun index field -> {|
                    Index = index
                    Field = field
                    Codec = compileUntyped options field.Schema
                |})

            {
                Encode =
                    (fun path value ->
                        compiledFields
                        |> Array.toList
                        |> List.collect (fun field ->
                            field.Codec.Encode (path @ [ field.Field.Name ]) (field.Field.GetValue value)))
                Decode =
                    (fun path values ->
                        let decodedFields =
                            compiledFields
                            |> Array.map (fun field -> field, field.Codec.Decode (path @ [ field.Field.Name ]) values)

                        if decodedFields |> Array.forall (fun (_, decoded) -> decoded.IsNone) then
                            None
                        else
                            let args =
                                decodedFields
                                |> Array.map (fun (field, decoded) ->
                                    match decoded with
                                    | Some value -> value
                                    | None ->
                                        match field.Codec.MissingValue with
                                        | Some value -> value
                                        | None ->
                                            failwithf
                                                "Missing required key: %s"
                                                (keyName options (path @ [ field.Field.Name ])))

                            Some(ctor args))
                MissingValue = None
            }
        | Option innerSchema ->
            let innerCodec = compileUntyped options innerSchema
            let optionType = schema.TargetType

            {
                Encode =
                    (fun path value ->
                        if isNull value then
                            []
                        else
                            let _, fields = FSharpValue.GetUnionFields(value, optionType)
                            innerCodec.Encode path fields[0])
                Decode =
                    (fun path values ->
                        match innerCodec.Decode path values with
                        | Some value -> Some(Xml.Runtime.makeOptionSome optionType value)
                        | None -> Some(Xml.Runtime.makeOptionNone optionType))
                MissingValue = Some(Xml.Runtime.makeOptionNone optionType)
            }
        | MissingAsNone innerSchema ->
            let innerCodec = compileUntyped options innerSchema
            let optionType = schema.TargetType

            {
                Encode = innerCodec.Encode
                Decode = innerCodec.Decode
                MissingValue = Some(Xml.Runtime.makeOptionNone optionType)
            }
        | MissingAsValue(defaultValue, innerSchema) ->
            let innerCodec = compileUntyped options innerSchema

            {
                Encode = innerCodec.Encode
                Decode = innerCodec.Decode
                MissingValue = Some defaultValue
            }
        | NullAsValue(defaultValue, innerSchema) ->
            let innerCodec = compileUntyped options innerSchema

            {
                Encode = innerCodec.Encode
                Decode =
                    (fun path values ->
                        //
                        // KeyValue has no distinct null token, so the policy only
                        // applies when callers choose the literal "null" sentinel.
                        match tryFindValue options path values with
                        | Some "null" -> Some defaultValue
                        | Some _ -> innerCodec.Decode path values
                        | None -> innerCodec.Decode path values)
                MissingValue = innerCodec.MissingValue
            }
        | EmptyCollectionAsValue(_, innerSchema) ->
            //
            // Flat key/value payloads do not support collection shapes, so this
            // wrapper delegates to the underlying schema until that changes.
            compileUntyped
                options
                { new ISchema with
                    member _.TargetType = innerSchema.TargetType
                    member _.Definition = innerSchema.Definition
                }
        | EmptyStringAsNone innerSchema ->
            let innerCodec = compileUntyped options innerSchema
            let optionType = schema.TargetType
            let noneValue = Xml.Runtime.makeOptionNone optionType

            {
                Encode = innerCodec.Encode
                Decode =
                    (fun path values ->
                        match tryFindValue options path values with
                        | Some "" -> Some noneValue
                        | _ -> innerCodec.Decode path values)
                MissingValue = innerCodec.MissingValue
            }
        | Map(innerSchema, wrap, unwrap) ->
            let innerCodec = compileUntyped options innerSchema

            {
                Encode = (fun path value -> innerCodec.Encode path (unwrap value))
                Decode = (fun path values -> innerCodec.Decode path values |> Option.map wrap)
                MissingValue = innerCodec.MissingValue |> Option.map wrap
            }
        | List _
        | Array _
        | RawJsonValue ->
            failwithf "KeyValue only supports flattened record and scalar schemas, got %O" schema.Definition

    /// Compiles a schema into a reusable flat key/value codec using explicit options.
    let compileUsing (options: Options) (schema: Schema<'T>) : Codec<'T> =
        let compiled = compileUntyped options (schema :> ISchema)

        {
            Encode = (fun value -> compiled.Encode [] (box value) |> Map.ofList)
            Decode =
                (fun values ->
                    match compiled.Decode [] values with
                    | Some value -> unbox value
                    | None -> failwith "KeyValue payload did not contain any decodable fields")
        }

    /// Compiles a schema into a reusable flat key/value codec using dotted keys.
    let compile (schema: Schema<'T>) : Codec<'T> = compileUsing Options.defaults schema

    /// Serializes a value to a flat key/value map using a previously compiled codec.
    let serialize (codec: Codec<'T>) (value: 'T) = codec.Encode value

    /// Deserializes a flat key/value map using a previously compiled codec.
    let deserialize (codec: Codec<'T>) (values: Map<string, string>) = codec.Decode values

    /// Deserializes any sequence of key/value pairs by first normalizing it to a map.
    let deserializeSeq (codec: Codec<'T>) (values: seq<string * string>) = codec.Decode(Map.ofSeq values)
