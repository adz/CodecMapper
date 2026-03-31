/// Importers for existing .NET contract metadata into `CodecMapper` schemas.
///
/// The bridge is intentionally .NET-only and aimed at migration scenarios:
/// start from C# classes that already carry serializer attributes, import a
/// `Schema<'T>`, then compile and use that schema like any handwritten one.
namespace CodecMapper.Bridge

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Globalization
open System.Reflection
open System.Runtime.Serialization
open System.Text.Json.Serialization
open Newtonsoft.Json
open CodecMapper

/// Controls how CLR member names map to wire names when no explicit serializer
/// attribute overrides the property name.
type NamingPolicy =
    | Exact
    | CamelCase
    | SnakeCaseLower
    | SnakeCaseUpper
    | KebabCaseLower
    | KebabCaseUpper

/// Options for schema import from annotated CLR models.
type BridgeOptions = {
    DefaultNaming: NamingPolicy
    IncludeFields: bool
    RespectNullableAnnotations: bool
}

/// Common bridge option presets.
module BridgeOptions =
    /// Conservative defaults for contract import.
    ///
    /// These defaults prefer explicit attributes over convention-heavy import.
    let defaults = {
        DefaultNaming = Exact
        IncludeFields = false
        RespectNullableAnnotations = false
    }

type private Flavor =
    | SystemTextJson
    | NewtonsoftJson
    | DataContract

type private MemberBinding = {
    ClrName: string
    WireName: string
    MemberType: Type
    Getter: obj -> obj
    Setter: (obj -> obj -> unit) option
    Required: bool
}

type private ConstructionPlan =
    | Constructor of ConstructorInfo * MemberBinding array
    | Setters of ConstructorInfo * MemberBinding array

type private ImportedSchema<'T>(fields: FieldRuntime list, createObj: obj[] -> obj) =
    inherit Schema<'T>()

    interface IMappingDefinitionRuntime with
        member _.FieldsRuntime = fields
        member _.CreateObj(values) = createObj values

type private SchemaFactory =
    static member CreateImported<'T>(fields: FieldRuntime list, createFunc: Func<obj[], obj>) : Schema<'T> =
        ImportedSchema<'T>(fields, fun values -> createFunc.Invoke(values)) :> Schema<'T>

    static member BuildListSchema<'T>(inner: Schema<'T>) : Schema<List<'T>> =
        let typedInner = inner

        Schema.array typedInner
        |> Schema.map (fun (items: 'T[]) -> new List<'T>(items)) (fun (items: List<'T>) -> items.ToArray())
        :> Schema<List<'T>>

    static member BuildReadOnlyListSchema<'T>(inner: Schema<'T>) : Schema<IReadOnlyList<'T>> =
        Schema.readOnlyList inner

    static member BuildCollectionSchema<'T>(inner: Schema<'T>) : Schema<ICollection<'T>> =
        Schema.collection inner

    static member BuildArraySchema<'T>(inner: Schema<'T>) : Schema<'T array> =
        Schema.array inner

    static member BuildNullableSchema<'T when 'T: struct and 'T :> ValueType and 'T: (new: unit -> 'T)>
        (inner: Schema<'T>)
        : Schema<Nullable<'T>> =
        let typedInner = inner

        Schema.option typedInner
        |> Schema.map
            (fun value ->
                match value with
                | Some innerValue -> Nullable innerValue
                | None -> Nullable())
            (fun (value: Nullable<'T>) -> if value.HasValue then Some value.Value else None)
        :> Schema<Nullable<'T>>

    static member BuildEnumSchema<'TEnum, 'TUnderlying when 'TUnderlying: struct and 'TUnderlying :> ValueType and 'TUnderlying: (new: unit -> 'TUnderlying)>
        (inner: Schema<'TUnderlying>)
        : Schema<'TEnum> =
        Schema.map
            (fun value -> Enum.ToObject(typeof<'TEnum>, box value) :?> 'TEnum)
            (fun (value: 'TEnum) ->
                unbox<'TUnderlying>(Convert.ChangeType(value, typeof<'TUnderlying>, CultureInfo.InvariantCulture)))
            inner

module private Runtime =
    let private findGenericMethod name =
        typeof<SchemaFactory>.GetMethods(BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)
        |> Array.find (fun methodInfo -> methodInfo.Name = name && methodInfo.IsGenericMethodDefinition)

    let private createImportedMethod = findGenericMethod (nameof SchemaFactory.CreateImported)

    let private buildListSchemaMethod =
        findGenericMethod (nameof SchemaFactory.BuildListSchema)

    let private buildReadOnlyListSchemaMethod =
        findGenericMethod (nameof SchemaFactory.BuildReadOnlyListSchema)

    let private buildCollectionSchemaMethod =
        findGenericMethod (nameof SchemaFactory.BuildCollectionSchema)

    let private buildArraySchemaMethod =
        findGenericMethod (nameof SchemaFactory.BuildArraySchema)

    let private buildNullableSchemaMethod =
        findGenericMethod (nameof SchemaFactory.BuildNullableSchema)

    let private buildEnumSchemaMethod =
        findGenericMethod (nameof SchemaFactory.BuildEnumSchema)

    let private cache = ConcurrentDictionary<Flavor * Type, obj>()

    let private publicInstance = BindingFlags.Instance ||| BindingFlags.Public

    let createImported (targetType: Type) (fields: FieldRuntime list) (createFunc: obj[] -> obj) =
        createImportedMethod
            .MakeGenericMethod([| targetType |])
            .Invoke(null, [| box fields; Func<obj[], obj>(createFunc) |])

    let private convertName namingPolicy (name: string) =
        let splitWords (value: string) =
            let words = ResizeArray<string>()
            let mutable start = 0

            for i in 1 .. value.Length - 1 do
                if Char.IsUpper(value[i]) && (not (Char.IsUpper(value[i - 1]))) then
                    words.Add(value.Substring(start, i - start))
                    start <- i

            words.Add(value.Substring(start))
            words |> Seq.toArray

        let words = splitWords name

        match namingPolicy with
        | Exact -> name
        | CamelCase ->
            if String.IsNullOrEmpty(name) then
                name
            else
                Char.ToLowerInvariant(name[0]).ToString() + name.Substring(1)
        | SnakeCaseLower -> words |> Array.map _.ToLowerInvariant() |> String.concat "_"
        | SnakeCaseUpper -> words |> Array.map _.ToUpperInvariant() |> String.concat "_"
        | KebabCaseLower -> words |> Array.map _.ToLowerInvariant() |> String.concat "-"
        | KebabCaseUpper -> words |> Array.map _.ToUpperInvariant() |> String.concat "-"

    let rec tryResolveBuiltin (targetType: Type) : obj option =
        let buildInner (methodInfo: MethodInfo) innerType innerSchema =
            methodInfo.MakeGenericMethod([| innerType |]).Invoke(null, [| innerSchema |])

        if targetType = typeof<int> then
            Some(box Schema.int)
        elif targetType = typeof<int64> then
            Some(box Schema.int64)
        elif targetType = typeof<uint32> then
            Some(box Schema.uint32)
        elif targetType = typeof<uint64> then
            Some(box Schema.uint64)
        elif targetType = typeof<float> then
            Some(box Schema.float)
        elif targetType = typeof<decimal> then
            Some(box Schema.decimal)
        elif targetType = typeof<string> then
            Some(box Schema.string)
        elif targetType = typeof<bool> then
            Some(box Schema.bool)
        elif targetType = typeof<int16> then
            Some(box Schema.int16)
        elif targetType = typeof<byte> then
            Some(box Schema.byte)
        elif targetType = typeof<sbyte> then
            Some(box Schema.sbyte)
        elif targetType = typeof<uint16> then
            Some(box Schema.uint16)
        elif targetType = typeof<Guid> then
            Some(box Schema.guid)
        elif targetType = typeof<char> then
            Some(box Schema.char)
        elif targetType = typeof<DateTime> then
            Some(box Schema.dateTime)
        elif targetType = typeof<DateTimeOffset> then
            Some(box Schema.dateTimeOffset)
        elif targetType = typeof<TimeSpan> then
            Some(box Schema.timeSpan)
        elif targetType = typeof<JsonValue> then
            Some(box Schema.jsonValue)
        elif targetType.IsEnum then
            let underlyingType = Enum.GetUnderlyingType(targetType)

            tryResolveBuiltin underlyingType
            |> Option.map (fun innerSchema ->
                buildEnumSchemaMethod
                    .MakeGenericMethod([| targetType; underlyingType |])
                    .Invoke(null, [| innerSchema |]))
        elif targetType.IsGenericType && targetType.GetGenericTypeDefinition() = typedefof<Nullable<_>> then
            let innerType = targetType.GetGenericArguments().[0]
            tryResolveBuiltin innerType |> Option.map (buildInner buildNullableSchemaMethod innerType)
        elif targetType.IsGenericType && targetType.GetGenericTypeDefinition() = typedefof<List<_>> then
            let innerType = targetType.GetGenericArguments().[0]
            tryResolveBuiltin innerType |> Option.map (buildInner buildListSchemaMethod innerType)
        elif targetType.IsGenericType && targetType.GetGenericTypeDefinition() = typedefof<IReadOnlyList<_>> then
            let innerType = targetType.GetGenericArguments().[0]
            tryResolveBuiltin innerType |> Option.map (buildInner buildReadOnlyListSchemaMethod innerType)
        elif targetType.IsGenericType && targetType.GetGenericTypeDefinition() = typedefof<ICollection<_>> then
            let innerType = targetType.GetGenericArguments().[0]
            tryResolveBuiltin innerType |> Option.map (buildInner buildCollectionSchemaMethod innerType)
        elif targetType.IsArray then
            let innerType = targetType.GetElementType()
            tryResolveBuiltin innerType |> Option.map (buildInner buildArraySchemaMethod innerType)
        else
            None

    let private hasUnsupportedTypeAttributes flavor (targetType: Type) =
        match flavor with
        | SystemTextJson ->
            if
                targetType.IsDefined(typeof<System.Text.Json.Serialization.JsonPolymorphicAttribute>, true)
                || targetType.IsDefined(typeof<System.Text.Json.Serialization.JsonDerivedTypeAttribute>, true)
            then
                failwithf
                    "Polymorphic System.Text.Json contracts on %s are not supported by CodecMapper.Bridge."
                    targetType.FullName
        | NewtonsoftJson -> ()
        | DataContract ->
            if targetType.IsDefined(typeof<KnownTypeAttribute>, true) then
                failwithf "KnownType polymorphism on %s is not supported by CodecMapper.Bridge." targetType.FullName

    let private getJsonIgnoreCondition (propertyInfo: PropertyInfo) =
        match propertyInfo.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() with
        | null -> None
        | attribute ->
            let conditionProperty =
                attribute.GetType().GetProperty("Condition", BindingFlags.Instance ||| BindingFlags.Public)

            if isNull conditionProperty then
                Some JsonIgnoreCondition.Always
            else
                Some(conditionProperty.GetValue(attribute) :?> JsonIgnoreCondition)

    let private isIgnored flavor (propertyInfo: PropertyInfo) =
        match flavor with
        | SystemTextJson ->
            match getJsonIgnoreCondition propertyInfo with
            | Some JsonIgnoreCondition.Always -> true
            | Some JsonIgnoreCondition.Never
            | None -> false
            | Some other ->
                failwithf
                    "System.Text.Json ignore condition '%O' on %s.%s is not supported by CodecMapper.Bridge."
                    other
                    propertyInfo.DeclaringType.FullName
                    propertyInfo.Name
        | NewtonsoftJson -> propertyInfo.IsDefined(typeof<Newtonsoft.Json.JsonIgnoreAttribute>, true)
        | DataContract -> false

    let private hasUnsupportedMemberAttributes flavor (propertyInfo: PropertyInfo) =
        match flavor with
        | SystemTextJson ->
            if propertyInfo.IsDefined(typeof<System.Text.Json.Serialization.JsonConverterAttribute>, true) then
                failwithf
                    "JsonConverter on %s.%s is not supported by CodecMapper.Bridge."
                    propertyInfo.DeclaringType.FullName
                    propertyInfo.Name

            if propertyInfo.IsDefined(typeof<System.Text.Json.Serialization.JsonExtensionDataAttribute>, true) then
                failwithf
                    "JsonExtensionData on %s.%s is not supported by CodecMapper.Bridge."
                    propertyInfo.DeclaringType.FullName
                    propertyInfo.Name
        | NewtonsoftJson ->
            if propertyInfo.IsDefined(typeof<Newtonsoft.Json.JsonConverterAttribute>, true) then
                failwithf
                    "JsonConverter on %s.%s is not supported by CodecMapper.Bridge."
                    propertyInfo.DeclaringType.FullName
                    propertyInfo.Name

            if propertyInfo.IsDefined(typeof<Newtonsoft.Json.JsonExtensionDataAttribute>, true) then
                failwithf
                    "JsonExtensionData on %s.%s is not supported by CodecMapper.Bridge."
                    propertyInfo.DeclaringType.FullName
                    propertyInfo.Name
        | DataContract -> ()

    let private isRequired flavor (propertyInfo: PropertyInfo) =
        match flavor with
        | SystemTextJson -> propertyInfo.IsDefined(typeof<System.Text.Json.Serialization.JsonRequiredAttribute>, true)
        | NewtonsoftJson ->
            propertyInfo.IsDefined(typeof<Newtonsoft.Json.JsonRequiredAttribute>, true)
            || match propertyInfo.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>() with
               | null -> false
               | attribute -> attribute.Required <> Newtonsoft.Json.Required.Default
        | DataContract ->
            match propertyInfo.GetCustomAttribute<DataMemberAttribute>() with
            | null -> false
            | attribute -> attribute.IsRequired

    let private resolveWireName flavor (options: BridgeOptions) (propertyInfo: PropertyInfo) =
        match flavor with
        | SystemTextJson ->
            match propertyInfo.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>() with
            | null -> convertName options.DefaultNaming propertyInfo.Name
            | attribute -> attribute.Name
        | NewtonsoftJson ->
            match propertyInfo.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>() with
            | null -> convertName options.DefaultNaming propertyInfo.Name
            | attribute when String.IsNullOrWhiteSpace(attribute.PropertyName) ->
                convertName options.DefaultNaming propertyInfo.Name
            | attribute -> attribute.PropertyName
        | DataContract ->
            match propertyInfo.GetCustomAttribute<DataMemberAttribute>() with
            | null -> convertName options.DefaultNaming propertyInfo.Name
            | attribute when String.IsNullOrWhiteSpace(attribute.Name) ->
                convertName options.DefaultNaming propertyInfo.Name
            | attribute -> attribute.Name

    let private getConstructorAttribute flavor =
        match flavor with
        | SystemTextJson -> Some typeof<System.Text.Json.Serialization.JsonConstructorAttribute>
        | NewtonsoftJson -> Some typeof<Newtonsoft.Json.JsonConstructorAttribute>
        | DataContract -> None

    let private getProperties flavor options (targetType: Type) =
        let properties =
            targetType.GetProperties(publicInstance)
            |> Array.filter (fun propertyInfo -> propertyInfo.GetIndexParameters().Length = 0 && propertyInfo.CanRead)

        let includedProperties =
            match flavor with
            | DataContract ->
                if not (targetType.IsDefined(typeof<DataContractAttribute>, true)) then
                    failwithf "Type %s is missing [DataContract]." targetType.FullName

                properties
                |> Array.filter (fun propertyInfo -> propertyInfo.IsDefined(typeof<DataMemberAttribute>, true))
            | _ -> properties |> Array.filter (isIgnored flavor >> not)

        includedProperties
        |> Array.map (fun propertyInfo ->
            hasUnsupportedMemberAttributes flavor propertyInfo

            {
                ClrName = propertyInfo.Name
                WireName = resolveWireName flavor options propertyInfo
                MemberType = propertyInfo.PropertyType
                Getter = fun instance -> propertyInfo.GetValue(instance)
                Setter =
                    if
                        propertyInfo.CanWrite
                        && not (isNull propertyInfo.SetMethod)
                        && propertyInfo.SetMethod.IsPublic
                    then
                        Some(fun instance value -> propertyInfo.SetValue(instance, value))
                    else
                        None
                Required = isRequired flavor propertyInfo
            })

    let private getConstructionPlan flavor (targetType: Type) (members: MemberBinding array) =
        let constructorAttribute = getConstructorAttribute flavor

        let constructors = targetType.GetConstructors(publicInstance)

        let attributedConstructors =
            match constructorAttribute with
            | Some attribute -> constructors |> Array.filter (fun ctor -> ctor.IsDefined(attribute, true))
            | None -> [||]

        let ctor =
            match attributedConstructors with
            | [| single |] -> single
            | [||] ->
                match constructors with
                | [| single |] -> single
                | _ ->
                    failwithf
                        "Could not choose a constructor for %s. Add an explicit serializer constructor attribute or reduce the public constructors."
                        targetType.FullName
            | _ -> failwithf "Multiple serializer constructors are annotated on %s." targetType.FullName

        let parameters = ctor.GetParameters()

        if parameters.Length = 0 then
            let nonSettable =
                members |> Array.filter (fun memberInfo -> memberInfo.Setter.IsNone)

            if nonSettable.Length > 0 then
                let missing = nonSettable |> Array.map _.ClrName |> String.concat ", "

                failwithf
                    "Type %s uses a parameterless constructor, but these members are not publicly settable: %s."
                    targetType.FullName
                    missing

            let orderedMembers = members |> Array.sortBy _.WireName
            Setters(ctor, orderedMembers)
        else
            let lookup =
                members
                |> Array.groupBy (fun memberInfo -> memberInfo.ClrName.ToLowerInvariant())
                |> dict

            let orderedMembers =
                parameters
                |> Array.map (fun parameter ->
                    let key = parameter.Name.ToLowerInvariant()

                    match lookup.TryGetValue key with
                    | true, [| memberInfo |] when memberInfo.MemberType = parameter.ParameterType -> memberInfo
                    | true, [| memberInfo |] ->
                        failwithf
                            "Constructor parameter %s on %s does not match property type %O."
                            parameter.Name
                            targetType.FullName
                            parameter.ParameterType
                    | true, duplicates ->
                        let names = duplicates |> Array.map _.ClrName |> String.concat ", "

                        failwithf
                            "Constructor parameter %s on %s matches multiple members: %s."
                            parameter.Name
                            targetType.FullName
                            names
                    | _ ->
                        failwithf
                            "Constructor parameter %s on %s could not be matched to a readable public property."
                            parameter.Name
                            targetType.FullName)

            let unmatched =
                members
                |> Array.filter (fun memberInfo ->
                    orderedMembers
                    |> Array.exists (fun matched -> matched.ClrName = memberInfo.ClrName)
                    |> not)

            if unmatched.Length > 0 then
                let names = unmatched |> Array.map _.ClrName |> String.concat ", "

                failwithf
                    "Type %s mixes constructor-bound and setter-bound members, which CodecMapper.Bridge does not support yet: %s."
                    targetType.FullName
                    names

            Constructor(ctor, orderedMembers)

    let rec private importType flavor (options: BridgeOptions) (path: Type list) (targetType: Type) : obj =
        match tryResolveBuiltin targetType with
        | Some schema -> schema
        | None ->
            if path |> List.exists (fun seen -> seen = targetType) then
                failwithf "Recursive type graphs are not supported by CodecMapper.Bridge yet: %s." targetType.FullName

            cache.GetOrAdd(
                (flavor, targetType),
                fun _ ->
                    let nextPath = targetType :: path

                    hasUnsupportedTypeAttributes flavor targetType

                    let members = getProperties flavor options targetType

                    if members.Length = 0 then
                        failwithf
                            "Could not import %s because it exposes no readable public properties."
                            targetType.FullName

                    let duplicateNames =
                        members
                        |> Array.countBy _.WireName
                        |> Array.filter (fun (_, count) -> count > 1)

                    if duplicateNames.Length > 0 then
                        let names = duplicateNames |> Array.map fst |> String.concat ", "

                        failwithf
                            "Type %s maps multiple members to the same wire name: %s."
                            targetType.FullName
                            names

                    let memberSchemas =
                        members
                        |> Array.map (fun memberInfo ->
                            memberInfo.ClrName, importType flavor options nextPath memberInfo.MemberType)
                        |> dict

                    let makeField (memberInfo: MemberBinding) = {
                        Name = memberInfo.WireName
                        Codec = memberSchemas[memberInfo.ClrName]
                        GetObj = memberInfo.Getter
                    }

                    let plan = getConstructionPlan flavor targetType members

                    let fields, buildFunc =
                        match plan with
                        | Constructor(ctor, orderedMembers) ->
                            let fields = orderedMembers |> Array.map makeField |> Array.toList
                            let buildFunc (args: obj[]) : obj = ctor.Invoke(args)
                            fields, buildFunc
                        | Setters(ctor, orderedMembers) ->
                            let fields = orderedMembers |> Array.map makeField |> Array.toList

                            let buildFunc (args: obj[]) : obj =
                                let instance = ctor.Invoke(Array.empty)

                                for i = 0 to orderedMembers.Length - 1 do
                                    match orderedMembers[i].Setter with
                                    | Some setter -> setter instance args[i]
                                    | None -> invalidOp "Setter plan contained a non-settable member."

                                instance

                            fields, buildFunc

                    createImported targetType fields buildFunc
            )

    let import<'T> flavor options : Schema<'T> =
        importType flavor options [] typeof<'T> :?> Schema<'T>

/// Imports schemas from `System.Text.Json`-annotated CLR types.
///
/// Supported metadata today is intentionally narrow: rename, ignore,
/// required, and constructor binding. Unsupported serializer-specific
/// features fail explicitly during import.
module SystemTextJson =
    /// Imports a `Schema<'T>` from a `System.Text.Json`-annotated CLR type.
    let import<'T> options =
        Runtime.import<'T> Flavor.SystemTextJson options

/// Imports schemas from `Newtonsoft.Json`-annotated CLR types.
module NewtonsoftJson =
    /// Imports a `Schema<'T>` from a `Newtonsoft.Json`-annotated CLR type.
    let import<'T> options =
        Runtime.import<'T> Flavor.NewtonsoftJson options

/// Imports schemas from `[DataContract]` / `[DataMember]` CLR types.
///
/// This is the strictest import path: when `[DataContract]` is present, only
/// `[DataMember]` properties are considered part of the wire contract.
module DataContracts =
    /// Imports a `Schema<'T>` from a `[DataContract]` CLR type.
    let import<'T> options =
        Runtime.import<'T> Flavor.DataContract options

type private SetterFieldBinding<'T> = {
    Name: string
    FieldType: Type
    GetValue: obj -> obj
    Setter: 'T -> obj -> unit
    Schema: obj
}

/// Mutable fluent builder for authoring setter-bound schemas from C#.
///
/// This is a object bridge over the normal `Schema` runtime for parameterless
/// C# classes with settable properties.
[<Sealed>]
type SetterRecordBuilder<'T when 'T: not struct>(factory: Func<'T>) as this =
    let fields = ResizeArray<SetterFieldBinding<'T>>()

    do
        if isNull factory then
            nullArg (nameof factory)

    member private _.AddField<'Field>
        (name: string, getter: Func<'T, 'Field>, setter: Action<'T, 'Field>, schema: Schema<'Field>)
        =
        if String.IsNullOrWhiteSpace(name) then
            invalidArg (nameof name) "Field name must not be empty."

        if isNull getter then
            nullArg (nameof getter)

        if isNull setter then
            nullArg (nameof setter)

        fields.Add(
            {
                Name = name
                FieldType = typeof<'Field>
                GetValue = (fun value -> box (getter.Invoke(unbox<'T> value)))
                Setter = (fun target value -> setter.Invoke(target, unbox<'Field> value))
                Schema = schema
            }
        )

        this

    /// Adds a field that can be resolved automatically from its CLR type.
    member this.Field<'Field>(name: string, getter: Func<'T, 'Field>, setter: Action<'T, 'Field>) =
        match Runtime.tryResolveBuiltin typeof<'Field> with
        | Some schema -> this.AddField(name, getter, setter, unbox<Schema<'Field>> schema)
        | None -> failwithf "Could not automatically resolve schema for type %O." typeof<'Field>

    /// Adds a field with an explicit child schema.
    member this.FieldWith<'Field>
        (name: string, getter: Func<'T, 'Field>, setter: Action<'T, 'Field>, schema: Schema<'Field>)
        =
        if isNull (box schema) then
            nullArg (nameof schema)

        this.AddField(name, getter, setter, schema)

    /// Closes the fluent builder and returns a normal `Schema<'T>`.
    member _.Build() : Schema<'T> =
        let schemaFields =
            fields
            |> Seq.map (fun field -> {
                Name = field.Name
                Codec = field.Schema
                GetObj = field.GetValue
            })
            |> Seq.toList

        let buildFunc (args: obj[]) : obj =
            let instance = factory.Invoke()

            for i = 0 to fields.Count - 1 do
                fields[i].Setter instance args[i]

            box instance

        Runtime.createImported typeof<'T> schemaFields buildFunc :?> Schema<'T>

/// C#-friendly entry points for schema authoring and codec compilation.
///
/// The canonical authoring style remains the F# `Schema` DSL. This wrapper is
/// for the cases where writing that schema directly from C# is preferable to
/// bridge import or future code generation.
[<AbstractClass; Sealed>]
type CSharpSchema =
    /// Starts a fluent builder for a setter-bound C# class.
    static member Record<'T when 'T: not struct>(factory: Func<'T>) = SetterRecordBuilder<'T>(factory)

    /// Compiles a schema into a JSON codec.
    static member Json<'T>(schema: Schema<'T>) = Json.compileSchema schema

    /// Compiles a schema into an XML codec.
    static member Xml<'T>(schema: Schema<'T>) = Xml.compileSchema schema

    /// Compiles a schema into a flat key/value codec.
    static member KeyValue<'T>(schema: Schema<'T>) = KeyValue.compileSchema schema

    /// Compiles a schema into a YAML codec.
    static member Yaml<'T>(schema: Schema<'T>) = Yaml.compileSchema schema
