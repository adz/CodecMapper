namespace CodecMapper

open System.Text
open System.Collections.Generic
open System.Globalization
open Microsoft.FSharp.Reflection

/// YAML projection layered on top of the portable `JsonValue` model.
///
/// This intentionally supports a small YAML subset suitable for config-style
/// contracts: mappings, sequences, scalars, `null`, and quoted/plain strings.
module Yaml =
    type Codec<'T> = {
        Encode: 'T -> string
        Decode: string -> 'T
    }

    type internal YamlDecodeException(path: string, detail: string, ?inner: exn) =
        inherit System.Exception(detail, defaultArg inner null)

        member _.Path = path
        member _.Detail = detail

        override _.Message = sprintf "YAML decode error at %s: %s" path detail

    type private Line = { Indent: int; Content: string }

    let private rawJsonCodec = Json.compile Schema.jsonValue

    let private raiseDecodeFailure path detail inner =
        raise (YamlDecodeException(path, detail, inner))

    let private wrapYamlFailure path detail f =
        try
            f ()
        with
        | :? YamlDecodeException -> reraise ()
        | ex -> raiseDecodeFailure path detail ex

    let private renderJsonPath (segments: Json.DecodePathSegment list) =
        let builder = StringBuilder("$")

        for segment in segments do
            match segment with
            | Json.Property name ->
                builder.Append('.') |> ignore
                builder.Append(name) |> ignore
            | Json.Index index ->
                builder.Append('[') |> ignore
                builder.Append(index) |> ignore
                builder.Append(']') |> ignore

        builder.ToString()

    let private normalizeNewlines (text: string) =
        text.Replace("\r\n", "\n").Replace('\r', '\n')

    let private parseJsonValueText (json: string) =
        let bytes = Encoding.UTF8.GetBytes(json)
        let struct (value, rest) = Json.Runtime.jsonValueDecoder (ByteSource(bytes, 0))
        let rest = Json.Runtime.skipWhitespace rest

        if rest.Offset <> bytes.Length then
            raiseDecodeFailure "$" "Trailing content after top-level JSON value" null

        value

    let private renderJsonValueText (value: JsonValue) = Json.serialize rawJsonCodec value

    let private yamlNumberPattern =
        System.Text.RegularExpressions.Regex("^-?(0|[1-9][0-9]*)(\\.[0-9]+)?([eE][+-]?[0-9]+)?$")

    let private safePlainStringPattern =
        System.Text.RegularExpressions.Regex("^[A-Za-z0-9_./-]+$")

    let private isYamlNumberToken (text: string) = yamlNumberPattern.IsMatch(text)

    let private needsQuotedString (text: string) =
        text = ""
        || text = "null"
        || text = "~"
        || text = "true"
        || text = "false"
        || isYamlNumberToken text
        || not (safePlainStringPattern.IsMatch(text))

    let private quoteYamlString (text: string) =
        let escaped =
            text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t")

        "\"" + escaped + "\""

    let private renderScalar (value: JsonValue) =
        match value with
        | JNull -> "null"
        | JBool flag -> if flag then "true" else "false"
        | JNumber token -> token
        | JString text ->
            if needsQuotedString text then
                quoteYamlString text
            else
                text
        | _ -> failwith "Expected scalar YAML value"

    let private parseQuotedString (text: string) =
        if text.Length >= 2 && text[0] = '"' && text[text.Length - 1] = '"' then
            let content = text.Substring(1, text.Length - 2)
            let builder = StringBuilder()
            let mutable i = 0

            while i < content.Length do
                if content[i] = '\\' then
                    if i + 1 >= content.Length then
                        failwith "Unterminated YAML string escape"

                    match content[i + 1] with
                    | '\\' -> builder.Append('\\') |> ignore
                    | '"' -> builder.Append('"') |> ignore
                    | 'n' -> builder.Append('\n') |> ignore
                    | 'r' -> builder.Append('\r') |> ignore
                    | 't' -> builder.Append('\t') |> ignore
                    | other -> builder.Append(other) |> ignore

                    i <- i + 2
                else
                    builder.Append(content[i]) |> ignore
                    i <- i + 1

            builder.ToString()
        elif text.Length >= 2 && text[0] = '\'' && text[text.Length - 1] = '\'' then
            text.Substring(1, text.Length - 2).Replace("''", "'")
        else
            failwith "Expected quoted YAML string"

    let private parseScalar (text: string) =
        let trimmed = text.Trim()

        if trimmed = "[]" then
            JArray []
        elif trimmed = "{}" then
            JObject []
        elif trimmed = "null" || trimmed = "~" then
            JNull
        elif trimmed = "true" then
            JBool true
        elif trimmed = "false" then
            JBool false
        elif trimmed.StartsWith("\"") || trimmed.StartsWith("'") then
            JString(parseQuotedString trimmed)
        elif isYamlNumberToken trimmed then
            JNumber trimmed
        else
            JString trimmed

    let private parseKey (text: string) =
        let trimmed = text.Trim()

        if trimmed.StartsWith("\"") || trimmed.StartsWith("'") then
            parseQuotedString trimmed
        else
            trimmed

    let private parseLines (yaml: string) =
        (normalizeNewlines yaml).Split('\n')
        |> Array.choose (fun rawLine ->
            let trimmed = rawLine.Trim()

            if trimmed = "" || trimmed.StartsWith("#") then
                None
            else
                let mutable indent = 0

                while indent < rawLine.Length && rawLine[indent] = ' ' do
                    indent <- indent + 1

                if indent < rawLine.Length && rawLine[indent] = '\t' then
                    failwith "Tabs are not supported in YAML indentation"

                if indent % 2 <> 0 then
                    failwith "YAML indentation must use multiples of two spaces"

                Some {
                    Indent = indent
                    Content = rawLine.Substring(indent)
                })

    let private isMappingLine (content: string) =
        let colonIndex = content.IndexOf(':')
        colonIndex >= 0

    let rec private parseNode (lines: Line array) (indent: int) (index: int) : struct (JsonValue * int) =
        if index >= lines.Length then
            failwith "Unexpected end of YAML input"

        let line = lines[index]

        if line.Indent <> indent then
            failwithf "Unexpected indentation at YAML line %d" (index + 1)

        if line.Content = "-" || line.Content.StartsWith("- ") then
            parseArray lines indent index
        elif isMappingLine line.Content then
            parseObject lines indent index
        else
            struct (parseScalar line.Content, index + 1)

    and private parseObjectFromEntries
        (lines: Line array)
        (indent: int)
        (index: int)
        (entries: ResizeArray<string * JsonValue>)
        : struct (JsonValue * int) =
        let mutable i = index

        while i < lines.Length && lines[i].Indent = indent do
            let line = lines[i]

            if line.Content = "-" || line.Content.StartsWith("- ") then
                failwithf "Unexpected sequence item at YAML line %d" (i + 1)

            let colonIndex = line.Content.IndexOf(':')

            if colonIndex < 0 then
                failwithf "Expected mapping entry at YAML line %d" (i + 1)

            let key = parseKey (line.Content.Substring(0, colonIndex))
            let rest = line.Content.Substring(colonIndex + 1).TrimStart()

            if rest = "" then
                if i + 1 < lines.Length && lines[i + 1].Indent > indent then
                    let struct (value, nextIndex) = parseNode lines (indent + 2) (i + 1)
                    entries.Add(key, value)
                    i <- nextIndex
                else
                    entries.Add(key, JNull)
                    i <- i + 1
            else
                entries.Add(key, parseScalar rest)
                i <- i + 1

        struct (JObject(List.ofSeq entries), i)

    and private parseInlineObject
        (lines: Line array)
        (indent: int)
        (inlineContent: string)
        (nextIndex: int)
        : struct (JsonValue * int) =
        let entries = ResizeArray<string * JsonValue>()
        let colonIndex = inlineContent.IndexOf(':')

        if colonIndex < 0 then
            failwith "Expected inline YAML object entry"

        let key = parseKey (inlineContent.Substring(0, colonIndex))
        let rest = inlineContent.Substring(colonIndex + 1).TrimStart()

        if rest = "" then
            if nextIndex < lines.Length && lines[nextIndex].Indent > indent then
                let struct (value, afterInline) = parseNode lines (indent + 2) nextIndex
                entries.Add(key, value)
                parseObjectFromEntries lines (indent + 2) afterInline entries
            else
                entries.Add(key, JNull)
                parseObjectFromEntries lines (indent + 2) nextIndex entries
        else
            entries.Add(key, parseScalar rest)
            parseObjectFromEntries lines (indent + 2) nextIndex entries

    and private parseObject (lines: Line array) (indent: int) (index: int) =
        parseObjectFromEntries lines indent index (ResizeArray())

    and private parseArray (lines: Line array) (indent: int) (index: int) : struct (JsonValue * int) =
        let items = ResizeArray<JsonValue>()
        let mutable i = index

        while i < lines.Length && lines[i].Indent = indent do
            let line = lines[i]

            if line.Content = "-" then
                if i + 1 >= lines.Length || lines[i + 1].Indent <= indent then
                    items.Add(JNull)
                    i <- i + 1
                else
                    let struct (value, nextIndex) = parseNode lines (indent + 2) (i + 1)
                    items.Add(value)
                    i <- nextIndex
            elif line.Content.StartsWith("- ") then
                let rest = line.Content.Substring(2).TrimStart()

                if isMappingLine rest then
                    let struct (value, nextIndex) = parseInlineObject lines indent rest (i + 1)
                    items.Add(value)
                    i <- nextIndex
                else
                    items.Add(parseScalar rest)
                    i <- i + 1
            else
                failwithf "Expected YAML sequence item at line %d" (i + 1)

        struct (JArray(List.ofSeq items), i)

    let private parseYamlValue (yaml: string) =
        let lines = parseLines yaml

        if lines.Length = 0 then
            failwith "Empty YAML input"

        let struct (value, nextIndex) = parseNode lines lines[0].Indent 0

        if nextIndex <> lines.Length then
            failwith "Trailing YAML content after top-level value"

        value

    let rec private renderYamlValue (indent: int) (value: JsonValue) =
        let prefix = String.replicate indent " "

        match value with
        | JNull
        | JBool _
        | JNumber _
        | JString _ -> prefix + renderScalar value
        | JArray [] -> prefix + "[]"
        | JObject [] -> prefix + "{}"
        | JArray items ->
            items
            |> List.map (fun item ->
                match item with
                | JNull
                | JBool _
                | JNumber _
                | JString _ -> prefix + "- " + renderScalar item
                | _ -> prefix + "-\n" + renderYamlValue (indent + 2) item)
            |> String.concat "\n"
        | JObject properties ->
            properties
            |> List.map (fun (key, item) ->
                let renderedKey = if needsQuotedString key then quoteYamlString key else key

                match item with
                | JNull
                | JBool _
                | JNumber _
                | JString _ -> prefix + renderedKey + ": " + renderScalar item
                | _ -> prefix + renderedKey + ":\n" + renderYamlValue (indent + 2) item)
            |> String.concat "\n"

    /// Compiles a schema into a reusable YAML codec.
    ///
    /// The YAML surface is intentionally small and config-oriented. It reuses
    /// the compiled JSON codec and a `JsonValue` projection instead of adding
    /// a second full schema compiler.
    let compile (schema: Schema<'T>) : Codec<'T> =
        let jsonCodec = Json.compile schema

        {
            Encode =
                (fun value ->
                    let json = Json.serialize jsonCodec value
                    let jsonValue = parseJsonValueText json
                    renderYamlValue 0 jsonValue)
            Decode =
                (fun yaml ->
                    try
                        let jsonValue =
                            wrapYamlFailure "$" "Failed to parse YAML payload" (fun () -> parseYamlValue yaml)

                        let json = renderJsonValueText jsonValue
                        Json.deserialize jsonCodec json
                    with
                    | :? YamlDecodeException as ex -> raise ex
                    | :? Json.JsonDecodeException as ex -> raiseDecodeFailure (renderJsonPath ex.Path) ex.Detail ex
                    | ex -> raiseDecodeFailure "$" ex.Message ex)
        }

    ///
    /// Inline schema pipelines read more clearly when the final `build` and
    /// YAML compile step collapse into one terminal pipeline stage.
    let inline buildAndCompile (builder: Builder<'T, 'T>) : Codec<'T> = builder |> Schema.build |> compile

    ///
    /// `codec` mirrors the other format modules for callers that still prefer
    /// the shorter schema-to-codec alias over the longer `compile` name.
    let codec (schema: Schema<'T>) : Codec<'T> = compile schema

    /// Serializes a value to YAML using a previously compiled codec.
    let serialize (codec: Codec<'T>) (value: 'T) = codec.Encode value

    /// Deserializes a YAML payload using a previously compiled codec.
    let deserialize (codec: Codec<'T>) (yaml: string) = codec.Decode yaml
