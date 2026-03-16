namespace CodecMapper

open System.Text
open System.Collections.Generic
open System.Globalization
open System.Runtime.CompilerServices
open Microsoft.FSharp.Reflection

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
                    System.DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
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

    type private SchemaRefComparer() =
        interface IEqualityComparer<ISchema> with
            member _.Equals(left, right) = obj.ReferenceEquals(left, right)
            member _.GetHashCode(value) = RuntimeHelpers.GetHashCode(value)

    type private ExportContext = {
        DelayNames: Dictionary<ISchema, string>
        DelayBodies: Dictionary<ISchema, JsonValue>
        DelayOrder: ResizeArray<ISchema>
        DelayInProgress: HashSet<ISchema>
        UsedNames: Dictionary<string, int>
    }

    let private rawJsonCodec = Json.compile Schema.jsonValue

    let private schemaObject (properties: (string * JsonValue) list) = JObject properties
    let private stringArray values = values |> List.map JString |> JArray

    let private schemaRef name =
        schemaObject [ "$ref", JString("#/$defs/" + name) ]

    let private allocateDefinitionName (context: ExportContext) (targetType: System.Type) =
        let baseName = targetType.Name

        match context.UsedNames.TryGetValue(baseName) with
        | true, count ->
            let next = count + 1
            context.UsedNames[baseName] <- next
            baseName + string next
        | false, _ ->
            context.UsedNames[baseName] <- 0
            baseName

    let private isMissingAllowed (schema: ISchema) =
        match schema.Definition with
        | MissingAsNone _
        | MissingAsValue _ -> true
        | _ -> false

    let private objectNode titleOpt properties required =
        let allProperties =
            match titleOpt with
            | Some title -> [
                "type", JString "object"
                "title", JString title
                "properties", schemaObject properties
              ]
            | None -> [ "type", JString "object"; "properties", schemaObject properties ]

        if List.isEmpty required then
            schemaObject allProperties
        else
            schemaObject (allProperties @ [ "required", stringArray required ])

    let private expectObjectContract context (node: JsonValue) =
        match node with
        | JObject properties -> properties
        | _ -> failwithf "Inline union payload schema for %O must export as an object contract" context

    let private primitiveNode typeName =
        schemaObject [ "type", JString typeName ]

    let rec private exportSchema (context: ExportContext) (isRoot: bool) (schema: ISchema) : JsonValue =
        match schema.Definition with
        | RawJsonValue -> schemaObject []
        | Primitive targetType when targetType = typeof<bool> -> primitiveNode "boolean"
        | Primitive targetType when targetType = typeof<float> || targetType = typeof<decimal> -> primitiveNode "number"
        | Primitive targetType when targetType = typeof<string> -> primitiveNode "string"
        | Primitive targetType when targetType = typeof<char> -> primitiveNode "string"
        | Primitive targetType when targetType = typeof<System.Guid> -> primitiveNode "string"
        | Primitive targetType when targetType = typeof<System.DateTime> -> primitiveNode "string"
        | Primitive targetType when targetType = typeof<System.DateTimeOffset> -> primitiveNode "string"
        | Primitive targetType when targetType = typeof<System.TimeSpan> -> primitiveNode "string"
        | Primitive _ -> primitiveNode "integer"
        | StringEnum(names, _, _) -> schemaObject [ "type", JString "string"; "enum", stringArray (Array.toList names) ]
        | List innerSchema
        | Array innerSchema -> schemaObject [ "type", JString "array"; "items", exportSchema context false innerSchema ]
        | Option innerSchema ->
            schemaObject [
                "anyOf", JArray [ exportSchema context false innerSchema; primitiveNode "null" ]
            ]
        | MissingAsNone innerSchema -> exportSchema context isRoot innerSchema
        | MissingAsValue(_, innerSchema) -> exportSchema context isRoot innerSchema
        | NullAsValue(_, innerSchema) -> exportSchema context isRoot innerSchema
        | EmptyCollectionAsValue(_, innerSchema) -> exportSchema context isRoot innerSchema
        | EmptyStringAsNone innerSchema -> exportSchema context isRoot innerSchema
        | Map(innerSchema, _, _) -> exportSchema context isRoot innerSchema
        | Delay factory ->
            let definitionName =
                match context.DelayNames.TryGetValue(schema) with
                | true, existing -> existing
                | false, _ ->
                    let created = allocateDefinitionName context schema.TargetType
                    context.DelayNames[schema] <- created
                    context.DelayOrder.Add(schema)
                    created

            if
                not (context.DelayBodies.ContainsKey(schema))
                && not (context.DelayInProgress.Contains(schema))
            then
                context.DelayInProgress.Add(schema) |> ignore
                let body = exportSchema context false (factory ())
                context.DelayInProgress.Remove(schema) |> ignore
                context.DelayBodies[schema] <- body

            if isRoot then
                context.DelayBodies[schema]
            else
                schemaRef definitionName
        | Record(targetType, fields, _) ->
            let properties =
                fields
                |> Array.map (fun field -> field.Name, exportSchema context false field.Schema)
                |> Array.toList

            let required =
                fields
                |> Array.choose (fun field ->
                    if isMissingAllowed field.Schema then
                        None
                    else
                        Some field.Name)
                |> Array.toList

            objectNode (Some targetType.Name) properties required
        | Union(discriminatorName, valueName, cases) ->
            let caseNodes =
                cases
                |> Array.map (fun case ->
                    let baseProperties = [ discriminatorName, schemaObject [ "const", JString case.Name ] ]
                    let baseRequired = [ discriminatorName ]

                    let properties, required =
                        match case.Schema with
                        | Some payloadSchema ->
                            baseProperties @ [ valueName, exportSchema context false payloadSchema ],
                            baseRequired @ [ valueName ]
                        | None -> baseProperties, baseRequired

                    objectNode None properties required)
                |> Array.toList

            schemaObject [ "oneOf", JArray caseNodes ]
        | InlineUnion(discriminatorName, cases) ->
            let caseNodes =
                cases
                |> Array.map (fun case ->
                    let baseProperties = [ discriminatorName, schemaObject [ "const", JString case.Name ] ]
                    let baseRequired = [ discriminatorName ]

                    let properties, required =
                        match case.Schema with
                        | Some payloadSchema ->
                            if not (Schema.supportsInlinePayloadShape payloadSchema) then
                                failwithf "Inline union case '%s' payload schema must be object-shaped" case.Name

                            let payloadContract =
                                expectObjectContract
                                    payloadSchema.TargetType
                                    (exportSchema context false payloadSchema)

                            let mergedProperties =
                                match payloadContract |> List.tryFind (fun (name, _) -> name = "properties") with
                                | Some(_, JObject properties) ->
                                    properties
                                    |> List.filter (fun (name, _) ->
                                        if name = discriminatorName then
                                            failwithf
                                                "Inline union case '%s' payload cannot reuse discriminator field '%s'"
                                                case.Name
                                                discriminatorName

                                        true)
                                | _ ->
                                    failwithf
                                        "Inline union payload schema for %O must export named object properties"
                                        payloadSchema.TargetType

                            let mergedRequired =
                                match payloadContract |> List.tryFind (fun (name, _) -> name = "required") with
                                | Some(_, JArray values) ->
                                    values
                                    |> List.choose (function
                                        | JString value -> Some value
                                        | _ -> None)
                                | _ -> []

                            baseProperties @ mergedProperties, baseRequired @ mergedRequired
                        | None -> baseProperties, baseRequired

                    objectNode None properties required)
                |> Array.toList

            schemaObject [ "oneOf", JArray caseNodes ]

    let private buildRootDocument (schema: ISchema) =
        let context = {
            DelayNames = Dictionary<ISchema, string>(SchemaRefComparer())
            DelayBodies = Dictionary<ISchema, JsonValue>(SchemaRefComparer())
            DelayOrder = ResizeArray()
            DelayInProgress = HashSet<ISchema>(SchemaRefComparer())
            UsedNames = Dictionary<string, int>()
        }

        let body = exportSchema context true schema

        let definitions =
            context.DelayOrder
            |> Seq.choose (fun delayedSchema ->
                match
                    context.DelayNames.TryGetValue(delayedSchema), context.DelayBodies.TryGetValue(delayedSchema)
                with
                | (true, name), (true, definition) -> Some(name, definition)
                | _ -> None)
            |> Seq.toList

        let rootProperties =
            match body with
            | JObject properties -> properties |> List.filter (fun (name, _) -> name <> "title")
            | _ -> failwithf "Expected exported schema object for %O" schema.TargetType

        let baseDocument = [
            "$schema", JString "https://json-schema.org/draft/2020-12/schema"
            "title", JString schema.TargetType.Name
        ]

        let withBody = baseDocument @ rootProperties

        if List.isEmpty definitions then
            schemaObject withBody
        else
            schemaObject (withBody @ [ "$defs", schemaObject definitions ])

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
        | Some(JNumber token) -> Core.tryParseInt32Invariant token
        | _ -> None

    let private tryGetDecimalProperty (name: string) (properties: (string * JsonValue) list) =
        match tryFindProperty name properties with
        | Some(JNumber token) -> Core.tryParseDecimalInvariant token
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

            if token[0] = '-' then
                index <- 1

            if index >= token.Length then
                false
            else
                let mutable valid = true

                while index < token.Length && valid do
                    let ch = token[index]

                    if ch < '0' || ch > '9' then
                        valid <- false
                    else
                        index <- index + 1

                valid

    let private equalJsonValue left right = left = right

    let private tryParseDecimalToken (token: string) = Core.tryParseDecimalInvariant token

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

    let private addDistinct (values: ResizeArray<string>) (seen: HashSet<string>) (value: string) =
        if seen.Add(value) then
            values.Add(value)

    let rec private collectSchemaKeywordsInto
        (value: JsonValue)
        (keywords: ResizeArray<string>)
        (seen: HashSet<string>)
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
        let seen = HashSet<string>()
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
                match Core.tryParseInt32Invariant segment with
                | Some index when index >= 0 && index < items.Length -> loop items[index] rest
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
        let normalizedKeywordSet = HashSet<string>()

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
                                            Some prefixValidators[index]
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
        buildRootDocument (schema :> ISchema) |> Json.serialize rawJsonCodec

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
