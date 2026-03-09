namespace CodecMapper.Bridge

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Reflection
open System.Text.Json.Serialization
open Newtonsoft.Json
open CodecMapper

type NamingPolicy =
    | Exact
    | CamelCase
    | SnakeCaseLower
    | SnakeCaseUpper
    | KebabCaseLower
    | KebabCaseUpper

type BridgeOptions =
    {
        DefaultNaming: NamingPolicy
        IncludeFields: bool
        RespectNullableAnnotations: bool
    }

module BridgeOptions =
    let defaults =
        {
            DefaultNaming = Exact
            IncludeFields = false
            RespectNullableAnnotations = false
        }

type private Flavor =
    | SystemTextJson
    | NewtonsoftJson

type private MemberBinding =
    {
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

type private SchemaFactory =
    static member Create<'T>(definition: SchemaDefinition) : ISchema = Schema.create<'T> definition :> ISchema

    static member BuildListSchema<'T>(inner: ISchema) : ISchema =
        let typedInner = inner :?> Schema<'T>

        Schema.array typedInner
        |> Schema.map (fun (items: 'T[]) -> new List<'T>(items)) (fun (items: List<'T>) -> items.ToArray())
        :> ISchema

    static member BuildNullableSchema<'T when 'T : struct and 'T :> ValueType and 'T: (new: unit -> 'T)>(inner: ISchema) : ISchema =
        let typedInner = inner :?> Schema<'T>

        Schema.option typedInner
        |> Schema.map
            (fun value ->
                match value with
                | Some innerValue -> Nullable innerValue
                | None -> Nullable())
            (fun (value: Nullable<'T>) ->
                if value.HasValue then
                    Some value.Value
                else
                    None)
        :> ISchema

module private Runtime =
    let private findGenericMethod name =
        typeof<SchemaFactory>.GetMethods(BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)
        |> Array.find (fun methodInfo -> methodInfo.Name = name && methodInfo.IsGenericMethodDefinition)

    let private createSchemaMethod =
        findGenericMethod (nameof SchemaFactory.Create)

    let private buildListSchemaMethod =
        findGenericMethod (nameof SchemaFactory.BuildListSchema)

    let private buildNullableSchemaMethod =
        findGenericMethod (nameof SchemaFactory.BuildNullableSchema)

    let private cache = ConcurrentDictionary<Flavor * Type, ISchema>()

    let private publicInstance = BindingFlags.Instance ||| BindingFlags.Public

    let private createErased (targetType: Type) (definition: SchemaDefinition) =
        createSchemaMethod.MakeGenericMethod([| targetType |]).Invoke(null, [| box definition |]) :?> ISchema

    let private convertName namingPolicy (name: string) =
        let splitWords (value: string) =
            let words = ResizeArray<string>()
            let mutable start = 0

            for i in 1 .. value.Length - 1 do
                if Char.IsUpper(value.[i]) && (not (Char.IsUpper(value.[i - 1]))) then
                    words.Add(value.Substring(start, i - start))
                    start <- i

            words.Add(value.Substring(start))
            words |> Seq.toArray

        let words = splitWords name

        match namingPolicy with
        | Exact -> name
        | CamelCase ->
            if String.IsNullOrEmpty(name) then name else Char.ToLowerInvariant(name.[0]).ToString() + name.Substring(1)
        | SnakeCaseLower -> words |> Array.map _.ToLowerInvariant() |> String.concat "_"
        | SnakeCaseUpper -> words |> Array.map _.ToUpperInvariant() |> String.concat "_"
        | KebabCaseLower -> words |> Array.map _.ToLowerInvariant() |> String.concat "-"
        | KebabCaseUpper -> words |> Array.map _.ToUpperInvariant() |> String.concat "-"

    let private tryResolveBuiltin (targetType: Type) =
        try
            Some (Schema.resolveSchema targetType)
        with _ ->
            None

    let private getJsonIgnoreCondition (propertyInfo: PropertyInfo) =
        match propertyInfo.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() with
        | null -> None
        | attribute ->
            let conditionProperty =
                attribute.GetType().GetProperty("Condition", BindingFlags.Instance ||| BindingFlags.Public)

            if isNull conditionProperty then
                Some JsonIgnoreCondition.Always
            else
                Some (conditionProperty.GetValue(attribute) :?> JsonIgnoreCondition)

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

    let private hasUnsupportedMemberAttributes flavor (propertyInfo: PropertyInfo) =
        match flavor with
        | SystemTextJson ->
            if propertyInfo.IsDefined(typeof<System.Text.Json.Serialization.JsonConverterAttribute>, true) then
                failwithf
                    "JsonConverter on %s.%s is not supported by CodecMapper.Bridge."
                    propertyInfo.DeclaringType.FullName
                    propertyInfo.Name
        | NewtonsoftJson ->
            if propertyInfo.IsDefined(typeof<Newtonsoft.Json.JsonConverterAttribute>, true) then
                failwithf
                    "JsonConverter on %s.%s is not supported by CodecMapper.Bridge."
                    propertyInfo.DeclaringType.FullName
                    propertyInfo.Name

    let private isRequired flavor (propertyInfo: PropertyInfo) =
        match flavor with
        | SystemTextJson -> propertyInfo.IsDefined(typeof<System.Text.Json.Serialization.JsonRequiredAttribute>, true)
        | NewtonsoftJson ->
            propertyInfo.IsDefined(typeof<Newtonsoft.Json.JsonRequiredAttribute>, true)
            ||
            match propertyInfo.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>() with
            | null -> false
            | attribute -> attribute.Required <> Newtonsoft.Json.Required.Default

    let private resolveWireName flavor (options: BridgeOptions) (propertyInfo: PropertyInfo) =
        match flavor with
        | SystemTextJson ->
            match propertyInfo.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>() with
            | null -> convertName options.DefaultNaming propertyInfo.Name
            | attribute -> attribute.Name
        | NewtonsoftJson ->
            match propertyInfo.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>() with
            | null -> convertName options.DefaultNaming propertyInfo.Name
            | attribute when String.IsNullOrWhiteSpace(attribute.PropertyName) -> convertName options.DefaultNaming propertyInfo.Name
            | attribute -> attribute.PropertyName

    let private getConstructorAttribute flavor =
        match flavor with
        | SystemTextJson -> typeof<System.Text.Json.Serialization.JsonConstructorAttribute>
        | NewtonsoftJson -> typeof<Newtonsoft.Json.JsonConstructorAttribute>

    let private getProperties flavor options (targetType: Type) =
        targetType.GetProperties(publicInstance)
        |> Array.filter (fun propertyInfo -> propertyInfo.GetIndexParameters().Length = 0 && propertyInfo.CanRead)
        |> Array.filter (isIgnored flavor >> not)
        |> Array.map (fun propertyInfo ->
            hasUnsupportedMemberAttributes flavor propertyInfo

            {
                ClrName = propertyInfo.Name
                WireName = resolveWireName flavor options propertyInfo
                MemberType = propertyInfo.PropertyType
                Getter = fun instance -> propertyInfo.GetValue(instance)
                Setter =
                    if propertyInfo.CanWrite && not (isNull propertyInfo.SetMethod) && propertyInfo.SetMethod.IsPublic then
                        Some (fun instance value -> propertyInfo.SetValue(instance, value))
                    else
                        None
                Required = isRequired flavor propertyInfo
            })

    let private getConstructionPlan flavor (targetType: Type) (members: MemberBinding array) =
        let constructorAttribute = getConstructorAttribute flavor

        let constructors = targetType.GetConstructors(publicInstance)
        let attributedConstructors =
            constructors |> Array.filter (fun ctor -> ctor.IsDefined(constructorAttribute, true))

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
            | _ ->
                failwithf "Multiple serializer constructors are annotated on %s." targetType.FullName

        let parameters = ctor.GetParameters()

        if parameters.Length = 0 then
            let nonSettable =
                members
                |> Array.filter (fun memberInfo -> memberInfo.Setter.IsNone)

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
                        failwithf "Constructor parameter %s on %s matches multiple members: %s." parameter.Name targetType.FullName names
                    | _ ->
                        failwithf
                            "Constructor parameter %s on %s could not be matched to a readable public property."
                            parameter.Name
                            targetType.FullName)

            let unmatched =
                members
                |> Array.filter (fun memberInfo -> orderedMembers |> Array.exists (fun matched -> matched.ClrName = memberInfo.ClrName) |> not)

            if unmatched.Length > 0 then
                let names = unmatched |> Array.map _.ClrName |> String.concat ", "
                failwithf
                    "Type %s mixes constructor-bound and setter-bound members, which CodecMapper.Bridge does not support yet: %s."
                    targetType.FullName
                    names

            Constructor(ctor, orderedMembers)

    let rec private importType flavor (options: BridgeOptions) (path: Type list) (targetType: Type) : ISchema =
        match tryResolveBuiltin targetType with
        | Some schema -> schema
        | None ->
            if path |> List.exists (fun seen -> seen = targetType) then
                failwithf "Recursive type graphs are not supported by CodecMapper.Bridge yet: %s." targetType.FullName

            cache.GetOrAdd(
                (flavor, targetType),
                fun _ ->
                    let nextPath = targetType :: path

                    match Nullable.GetUnderlyingType(targetType) with
                    | null ->
                        if targetType.IsGenericType && targetType.GetGenericTypeDefinition() = typedefof<List<_>> then
                            let elementType = targetType.GetGenericArguments().[0]
                            let innerSchema = importType flavor options nextPath elementType

                            buildListSchemaMethod.MakeGenericMethod([| elementType |]).Invoke(null, [| box innerSchema |]) :?> ISchema
                        else
                            let members = getProperties flavor options targetType

                            if members.Length = 0 then
                                failwithf "Could not import %s because it exposes no readable public properties." targetType.FullName

                            let duplicateNames =
                                members
                                |> Array.countBy _.WireName
                                |> Array.filter (fun (_, count) -> count > 1)

                            if duplicateNames.Length > 0 then
                                let names = duplicateNames |> Array.map fst |> String.concat ", "
                                failwithf "Type %s maps multiple members to the same wire name: %s." targetType.FullName names

                            let memberSchemas =
                                members
                                |> Array.map (fun memberInfo -> memberInfo.ClrName, importType flavor options nextPath memberInfo.MemberType)
                                |> dict

                            let makeField (memberInfo: MemberBinding) =
                                {
                                    Name = memberInfo.WireName
                                    Type = memberInfo.MemberType
                                    GetValue = memberInfo.Getter
                                    Schema = memberSchemas.[memberInfo.ClrName]
                                }

                            let plan = getConstructionPlan flavor targetType members

                            let fields, buildFunc =
                                match plan with
                                | Constructor(ctor, orderedMembers) ->
                                    let fields = orderedMembers |> Array.map makeField
                                    let buildFunc (args: obj[]) : obj = ctor.Invoke(args)
                                    fields, buildFunc
                                | Setters(ctor, orderedMembers) ->
                                    let fields = orderedMembers |> Array.map makeField

                                    let buildFunc (args: obj[]) : obj =
                                        let instance = ctor.Invoke(Array.empty)

                                        for i = 0 to orderedMembers.Length - 1 do
                                            match orderedMembers.[i].Setter with
                                            | Some setter -> setter instance args.[i]
                                            | None -> invalidOp "Setter plan contained a non-settable member."

                                        instance

                                    fields, buildFunc

                            createErased targetType (Record(targetType, fields, buildFunc))
                    | underlyingType ->
                        let innerSchema = importType flavor options nextPath underlyingType
                        buildNullableSchemaMethod.MakeGenericMethod([| underlyingType |]).Invoke(null, [| box innerSchema |]) :?> ISchema
            )

    let import<'T> flavor options : Schema<'T> =
        importType flavor options [] typeof<'T> :?> Schema<'T>

module SystemTextJson =
    let import<'T> options = Runtime.import<'T> Flavor.SystemTextJson options

module NewtonsoftJson =
    let import<'T> options = Runtime.import<'T> Flavor.NewtonsoftJson options
