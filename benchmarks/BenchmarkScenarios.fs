namespace CodecMapper.Benchmarks

open System
open System.Text
open System.Text.Json
open Newtonsoft.Json
open CodecMapper

type Address = { Street: string; City: string }

type Person = { Id: int; Name: string; Home: Address }

type IncomingAddress = {
    Street: string
    City: string
    Country: string
    PostalCode: string
}

type IncomingPerson = {
    Id: int
    Name: string
    Home: IncomingAddress
    Active: bool
    Tags: string list
}

type SmallMessage = {
    Id: int
    Kind: string
    Success: bool
    TraceId: string
}

type Article = {
    Id: int
    Slug: string
    Title: string
    Body: string
    Tags: string list
    Author: Person
}

type TelemetryPoint = {
    SensorId: int
    Timestamp: int64
    Temperature: float
    Humidity: float
    Voltage: decimal
    RetryCount: uint32
    Sequence: uint64
    Healthy: bool
}

module ParserScanExperiment =
    let private mixHash state value = (state * 16777619) ^^^ value

    let private hashBytes (bytes: byte[]) start length =
        let mutable hash = 17
        let mutable index = 0

        while index < length do
            hash <- mixHash hash (int bytes[start + index])
            index <- index + 1

        hash

    let private hashSpan (span: ReadOnlySpan<byte>) =
        let mutable hash = 17
        let mutable index = 0

        while index < span.Length do
            hash <- mixHash hash (int span[index])
            index <- index + 1

        hash

    let rec private scanOurValue (src: Json.JsonSource) =
        let src = Json.Runtime.skipWhitespaceShared src
        let data = src.Data

        if src.Offset >= data.Length then
            failwith "Unexpected end of JSON payload"

        match data[src.Offset] with
        | 123uy ->
            let mutable current = Json.Runtime.skipWhitespaceShared (src.Advance(1))
            let mutable hash = 19
            let mutable continueLoop = true

            if current.Offset < data.Length && data[current.Offset] = 125uy then
                current <- Json.Runtime.skipWhitespaceShared (current.Advance(1))
                continueLoop <- false

            while continueLoop do
                let struct (keyStart, keyLength, _, afterKey) = Json.Runtime.stringRaw current
                let keyHash = hashBytes data keyStart keyLength
                let afterColon = Json.Runtime.skipWhitespaceShared afterKey

                if afterColon.Offset >= data.Length || data[afterColon.Offset] <> 58uy then
                    failwith "Expected :"

                let struct (valueHash, afterValue) = scanOurValue (afterColon.Advance(1))
                hash <- mixHash (mixHash hash keyHash) valueHash

                let afterValue = Json.Runtime.skipWhitespaceShared afterValue

                if afterValue.Offset < data.Length && data[afterValue.Offset] = 44uy then
                    current <- Json.Runtime.skipWhitespaceShared (afterValue.Advance(1))
                elif afterValue.Offset < data.Length && data[afterValue.Offset] = 125uy then
                    current <- Json.Runtime.skipWhitespaceShared (afterValue.Advance(1))
                    continueLoop <- false
                else
                    failwith "Expected , or }"

            struct (hash, current)
        | 91uy ->
            let mutable current = Json.Runtime.skipWhitespaceShared (src.Advance(1))
            let mutable hash = 23
            let mutable continueLoop = true

            if current.Offset < data.Length && data[current.Offset] = 93uy then
                current <- Json.Runtime.skipWhitespaceShared (current.Advance(1))
                continueLoop <- false

            while continueLoop do
                let struct (itemHash, afterItem) = scanOurValue current
                hash <- mixHash hash itemHash

                let afterItem = Json.Runtime.skipWhitespaceShared afterItem

                if afterItem.Offset < data.Length && data[afterItem.Offset] = 44uy then
                    current <- Json.Runtime.skipWhitespaceShared (afterItem.Advance(1))
                elif afterItem.Offset < data.Length && data[afterItem.Offset] = 93uy then
                    current <- Json.Runtime.skipWhitespaceShared (afterItem.Advance(1))
                    continueLoop <- false
                else
                    failwith "Expected , or ]"

            struct (hash, current)
        | 34uy ->
            let struct (start, length, hadEscapes, next) = Json.Runtime.stringRaw src
            let hash = mixHash (hashBytes data start length) (if hadEscapes then 1 else 0)
            struct (hash, next)
        | 116uy
        | 102uy ->
            let struct (value, next) = Json.Runtime.boolDecoder src
            struct ((if value then 31 else 29), next)
        | 110uy ->
            let next = Json.Runtime.nullDecoder src
            struct (37, next)
        | _ ->
            let struct (start, length, next) = Json.Runtime.numberToken true src
            struct (hashBytes data start length, next)

    let scanWithOurParser (bytes: byte[]) =
        let struct (hash, rest) = scanOurValue (ByteSource(bytes, 0))
        let rest = Json.Runtime.skipWhitespaceShared rest

        if rest.Offset <> rest.Data.Length then
            failwith "Trailing JSON content"

        hash

    let scanWithUtf8JsonReader (bytes: byte[]) =
        let mutable reader = Utf8JsonReader(ReadOnlySpan<byte>(bytes))
        let mutable hash = 41

        while reader.Read() do
            match reader.TokenType with
            | JsonTokenType.StartObject -> hash <- mixHash hash 101
            | JsonTokenType.EndObject -> hash <- mixHash hash 103
            | JsonTokenType.StartArray -> hash <- mixHash hash 107
            | JsonTokenType.EndArray -> hash <- mixHash hash 109
            | JsonTokenType.PropertyName -> hash <- mixHash hash (hashSpan reader.ValueSpan)
            | JsonTokenType.String -> hash <- mixHash hash (hashSpan reader.ValueSpan)
            | JsonTokenType.Number -> hash <- mixHash hash (hashSpan reader.ValueSpan)
            | JsonTokenType.True -> hash <- mixHash hash 31
            | JsonTokenType.False -> hash <- mixHash hash 29
            | JsonTokenType.Null -> hash <- mixHash hash 37
            | _ -> ()

        hash

module TypedJsonExperiment =
    ///
    /// This first typed experiment stays benchmark-only and hand-written so we
    /// can measure "our parser + typed object assembly" without committing the
    /// production runtime to a broader architecture change yet.
    type private PropertyName = { Text: string; Utf8: byte[] }

    let private propertyName text = {
        Text = text
        Utf8 = Encoding.UTF8.GetBytes(text)
    }

    let private idName = propertyName "Id"
    let private nameName = propertyName "Name"
    let private homeName = propertyName "Home"
    let private streetName = propertyName "Street"
    let private cityName = propertyName "City"
    let private kindName = propertyName "Kind"
    let private successName = propertyName "Success"
    let private traceIdName = propertyName "TraceId"
    let private slugName = propertyName "Slug"
    let private titleName = propertyName "Title"
    let private bodyName = propertyName "Body"
    let private tagsName = propertyName "Tags"
    let private authorName = propertyName "Author"
    let private sensorIdName = propertyName "SensorId"
    let private timestampName = propertyName "Timestamp"
    let private temperatureName = propertyName "Temperature"
    let private humidityName = propertyName "Humidity"
    let private voltageName = propertyName "Voltage"
    let private retryCountName = propertyName "RetryCount"
    let private sequenceName = propertyName "Sequence"
    let private healthyName = propertyName "Healthy"

    let private expectByte expected label (src: Json.JsonSource) =
        let src = Json.Runtime.skipWhitespaceShared src

        if src.Offset >= src.Data.Length || src.Data[src.Offset] <> expected then
            failwithf "Expected %s" label

        Json.Runtime.skipWhitespaceShared (src.Advance(1))

    let private readPropertyName (current: Json.JsonSource) =
        let struct (start, length, hadEscapes, afterRawKey) = Json.Runtime.stringRaw current
        struct (start, length, hadEscapes, afterRawKey)

    let private propertyEquals
        (expected: PropertyName)
        (current: Json.JsonSource)
        (start: int)
        (length: int)
        (hadEscapes: bool)
        =
        if hadEscapes then
            let struct (name, _) = Json.Runtime.stringDecoder current
            name = expected.Text
        else
            Json.Runtime.bytesEqualShared expected.Utf8 current.Data start length

    let private skipUnknownProperty (afterColon: Json.JsonSource) =
        Json.Runtime.skipWhitespaceShared (Json.Runtime.skipValue afterColon)

    let private requireField fieldName seen =
        if not seen then
            failwithf "Missing required key '%s'" fieldName

    let private readObjectLoop
        (src: Json.JsonSource)
        (decodeField: Json.JsonSource -> int -> int -> bool -> Json.JsonSource -> Json.JsonSource)
        =
        let mutable current = expectByte 123uy "{" src
        let data = current.Data
        let mutable continueLoop = true

        if current.Offset < data.Length && data[current.Offset] = 125uy then
            current <- Json.Runtime.skipWhitespaceShared (current.Advance(1))
            continueLoop <- false

        while continueLoop do
            let struct (keyStart, keyLength, keyHasEscapes, afterRawKey) =
                readPropertyName current

            let afterColon = expectByte 58uy ":" afterRawKey
            let afterValue = decodeField current keyStart keyLength keyHasEscapes afterColon

            if afterValue.Offset < data.Length && data[afterValue.Offset] = 44uy then
                current <- Json.Runtime.skipWhitespaceShared (afterValue.Advance(1))
            elif afterValue.Offset < data.Length && data[afterValue.Offset] = 125uy then
                current <- Json.Runtime.skipWhitespaceShared (afterValue.Advance(1))
                continueLoop <- false
            else
                failwith "Expected , or }"

        current

    let private decodeStringList (src: Json.JsonSource) =
        let mutable current = expectByte 91uy "[" src
        let mutable items = []
        let data = current.Data
        let mutable continueLoop = true

        if current.Offset < data.Length && data[current.Offset] = 93uy then
            current <- Json.Runtime.skipWhitespaceShared (current.Advance(1))
            continueLoop <- false

        while continueLoop do
            let struct (item, afterItem) = Json.Runtime.stringDecoder current
            items <- item :: items

            if afterItem.Offset < data.Length && data[afterItem.Offset] = 44uy then
                current <- Json.Runtime.skipWhitespaceShared (afterItem.Advance(1))
            elif afterItem.Offset < data.Length && data[afterItem.Offset] = 93uy then
                current <- Json.Runtime.skipWhitespaceShared (afterItem.Advance(1))
                continueLoop <- false
            else
                failwith "Expected , or ]"

        struct (List.rev items, current)

    let rec private decodeAddress (src: Json.JsonSource) =
        let mutable street = ""
        let mutable city = ""
        let mutable sawStreet = false
        let mutable sawCity = false

        let current =
            readObjectLoop src (fun current keyStart keyLength keyHasEscapes afterColon ->
                if propertyEquals streetName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.stringDecoder afterColon
                    street <- value
                    sawStreet <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals cityName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.stringDecoder afterColon
                    city <- value
                    sawCity <- true
                    Json.Runtime.skipWhitespaceShared next
                else
                    skipUnknownProperty afterColon)

        requireField "Street" sawStreet
        requireField "City" sawCity
        struct ({ Street = street; City = city }, current)

    and private decodePerson (src: Json.JsonSource) =
        let mutable id = 0
        let mutable name = ""
        let mutable home = Unchecked.defaultof<Address>
        let mutable sawId = false
        let mutable sawName = false
        let mutable sawHome = false

        let current =
            readObjectLoop src (fun current keyStart keyLength keyHasEscapes afterColon ->
                if propertyEquals idName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.intDecoder afterColon
                    id <- value
                    sawId <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals nameName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.stringDecoder afterColon
                    name <- value
                    sawName <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals homeName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = decodeAddress afterColon
                    home <- value
                    sawHome <- true
                    Json.Runtime.skipWhitespaceShared next
                else
                    skipUnknownProperty afterColon)

        requireField "Id" sawId
        requireField "Name" sawName
        requireField "Home" sawHome
        struct ({ Id = id; Name = name; Home = home }, current)

    and private decodeArticle (src: Json.JsonSource) =
        let mutable id = 0
        let mutable slug = ""
        let mutable title = ""
        let mutable body = ""
        let mutable tags = []
        let mutable author = Unchecked.defaultof<Person>
        let mutable sawId = false
        let mutable sawSlug = false
        let mutable sawTitle = false
        let mutable sawBody = false
        let mutable sawTags = false
        let mutable sawAuthor = false

        let current =
            readObjectLoop src (fun current keyStart keyLength keyHasEscapes afterColon ->
                if propertyEquals idName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.intDecoder afterColon
                    id <- value
                    sawId <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals slugName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.stringDecoder afterColon
                    slug <- value
                    sawSlug <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals titleName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.stringDecoder afterColon
                    title <- value
                    sawTitle <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals bodyName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.stringDecoder afterColon
                    body <- value
                    sawBody <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals tagsName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = decodeStringList afterColon
                    tags <- value
                    sawTags <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals authorName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = decodePerson afterColon
                    author <- value
                    sawAuthor <- true
                    Json.Runtime.skipWhitespaceShared next
                else
                    skipUnknownProperty afterColon)

        requireField "Id" sawId
        requireField "Slug" sawSlug
        requireField "Title" sawTitle
        requireField "Body" sawBody
        requireField "Tags" sawTags
        requireField "Author" sawAuthor

        struct ({
                    Id = id
                    Slug = slug
                    Title = title
                    Body = body
                    Tags = tags
                    Author = author
                },
                current)

    let private decodeTelemetryPoint (src: Json.JsonSource) =
        let mutable sensorId = 0
        let mutable timestamp = 0L
        let mutable temperature = 0.0
        let mutable humidity = 0.0
        let mutable voltage = 0M
        let mutable retryCount = 0u
        let mutable sequence = 0UL
        let mutable healthy = false
        let mutable sawSensorId = false
        let mutable sawTimestamp = false
        let mutable sawTemperature = false
        let mutable sawHumidity = false
        let mutable sawVoltage = false
        let mutable sawRetryCount = false
        let mutable sawSequence = false
        let mutable sawHealthy = false

        let current =
            readObjectLoop src (fun current keyStart keyLength keyHasEscapes afterColon ->
                if propertyEquals sensorIdName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.intDecoder afterColon
                    sensorId <- value
                    sawSensorId <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals timestampName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.int64Decoder afterColon
                    timestamp <- value
                    sawTimestamp <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals temperatureName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.floatDecoder afterColon
                    temperature <- value
                    sawTemperature <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals humidityName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.floatDecoder afterColon
                    humidity <- value
                    sawHumidity <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals voltageName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.decimalDecoder afterColon
                    voltage <- value
                    sawVoltage <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals retryCountName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.uint32Decoder afterColon
                    retryCount <- value
                    sawRetryCount <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals sequenceName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.uint64Decoder afterColon
                    sequence <- value
                    sawSequence <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals healthyName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.boolDecoder afterColon
                    healthy <- value
                    sawHealthy <- true
                    Json.Runtime.skipWhitespaceShared next
                else
                    skipUnknownProperty afterColon)

        requireField "SensorId" sawSensorId
        requireField "Timestamp" sawTimestamp
        requireField "Temperature" sawTemperature
        requireField "Humidity" sawHumidity
        requireField "Voltage" sawVoltage
        requireField "RetryCount" sawRetryCount
        requireField "Sequence" sawSequence
        requireField "Healthy" sawHealthy

        struct ({
                    SensorId = sensorId
                    Timestamp = timestamp
                    Temperature = temperature
                    Humidity = humidity
                    Voltage = voltage
                    RetryCount = retryCount
                    Sequence = sequence
                    Healthy = healthy
                },
                current)

    let private decodeList (decodeItem: Json.JsonSource -> struct ('T * Json.JsonSource)) (bytes: byte[]) =
        let mutable current = Json.Runtime.skipWhitespaceShared (ByteSource(bytes, 0))
        current <- expectByte 91uy "[" current

        let mutable values = []
        let data = current.Data
        let mutable continueLoop = true

        if current.Offset < data.Length && data[current.Offset] = 93uy then
            current <- Json.Runtime.skipWhitespaceShared (current.Advance(1))
            continueLoop <- false

        while continueLoop do
            let struct (value, next) = decodeItem current
            values <- value :: values

            if next.Offset < data.Length && data[next.Offset] = 44uy then
                current <- Json.Runtime.skipWhitespaceShared (next.Advance(1))
            elif next.Offset < data.Length && data[next.Offset] = 93uy then
                current <- Json.Runtime.skipWhitespaceShared (next.Advance(1))
                continueLoop <- false
            else
                failwith "Expected , or ]"

        List.rev values

    let deserializeSmallMessageBytes (bytes: byte[]) =
        let mutable id = 0
        let mutable kind = ""
        let mutable success = false
        let mutable traceId = ""
        let mutable sawId = false
        let mutable sawKind = false
        let mutable sawSuccess = false
        let mutable sawTraceId = false

        let current =
            readObjectLoop (ByteSource(bytes, 0)) (fun current keyStart keyLength keyHasEscapes afterColon ->
                if propertyEquals idName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.intDecoder afterColon
                    id <- value
                    sawId <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals kindName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.stringDecoder afterColon
                    kind <- value
                    sawKind <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals successName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.boolDecoder afterColon
                    success <- value
                    sawSuccess <- true
                    Json.Runtime.skipWhitespaceShared next
                elif propertyEquals traceIdName current keyStart keyLength keyHasEscapes then
                    let struct (value, next) = Json.Runtime.stringDecoder afterColon
                    traceId <- value
                    sawTraceId <- true
                    Json.Runtime.skipWhitespaceShared next
                else
                    skipUnknownProperty afterColon)

        requireField "Id" sawId
        requireField "Kind" sawKind
        requireField "Success" sawSuccess
        requireField "TraceId" sawTraceId

        let endOfJson = Json.Runtime.skipWhitespaceShared current

        if endOfJson.Offset <> endOfJson.Data.Length then
            failwith "Trailing JSON content"

        {
            Id = id
            Kind = kind
            Success = success
            TraceId = traceId
        }

    let deserializePeopleBytes (bytes: byte[]) = decodeList decodePerson bytes
    let deserializeArticlesBytes (bytes: byte[]) = decodeList decodeArticle bytes
    let deserializeTelemetryBytes (bytes: byte[]) = decodeList decodeTelemetryPoint bytes

module Schemas =
    let address =
        Schema.define<Address>
        |> Schema.construct (fun street city -> { Street = street; City = city })
        |> Schema.field "Street" _.Street
        |> Schema.field "City" _.City
        |> Schema.build

    let person =
        Schema.define<Person>
        |> Schema.construct (fun id name home -> { Id = id; Name = name; Home = home })
        |> Schema.field "Id" _.Id
        |> Schema.field "Name" _.Name
        |> Schema.fieldWith "Home" _.Home address
        |> Schema.build

    let smallMessage =
        Schema.define<SmallMessage>
        |> Schema.construct (fun id kind success traceId -> {
            Id = id
            Kind = kind
            Success = success
            TraceId = traceId
        })
        |> Schema.field "Id" _.Id
        |> Schema.field "Kind" _.Kind
        |> Schema.field "Success" _.Success
        |> Schema.field "TraceId" _.TraceId
        |> Schema.build

    let article =
        Schema.define<Article>
        |> Schema.construct (fun id slug title body tags author -> {
            Id = id
            Slug = slug
            Title = title
            Body = body
            Tags = tags
            Author = author
        })
        |> Schema.field "Id" _.Id
        |> Schema.field "Slug" _.Slug
        |> Schema.field "Title" _.Title
        |> Schema.field "Body" _.Body
        |> Schema.field "Tags" _.Tags
        |> Schema.fieldWith "Author" _.Author person
        |> Schema.build

    let telemetryPoint =
        Schema.define<TelemetryPoint>
        |> Schema.construct (fun sensorId timestamp temperature humidity voltage retryCount sequence healthy -> {
            SensorId = sensorId
            Timestamp = timestamp
            Temperature = temperature
            Humidity = humidity
            Voltage = voltage
            RetryCount = retryCount
            Sequence = sequence
            Healthy = healthy
        })
        |> Schema.field "SensorId" _.SensorId
        |> Schema.field "Timestamp" _.Timestamp
        |> Schema.field "Temperature" _.Temperature
        |> Schema.field "Humidity" _.Humidity
        |> Schema.field "Voltage" _.Voltage
        |> Schema.field "RetryCount" _.RetryCount
        |> Schema.field "Sequence" _.Sequence
        |> Schema.field "Healthy" _.Healthy
        |> Schema.build

    let personList = Schema.list person
    let articleList = Schema.list article
    let telemetryList = Schema.list telemetryPoint

module Data =
    let private stjOptions = JsonSerializerOptions()

    let createSmallMessage () = {
        Id = 42
        Kind = "user.command"
        Success = true
        TraceId = "01HV6N6S1Y7R5B4K9A3T8M2P1Q"
    }

    let createPeople recordCount =
        [ 1..recordCount ]
        |> List.map (fun id -> {
            Id = id
            Name = $"Benchmark User {id}"
            Home = {
                Street = $"{id} F# Way"
                City = if id % 2 = 0 then "AOT City" else "Fable Town"
            }
        })

    ///
    /// Escaped text and longer bodies exercise the string encoder and decoder
    /// far more realistically than tiny identifier-only records.
    let createArticles recordCount =
        [ 1..recordCount ]
        |> List.map (fun id -> {
            Id = id
            Slug = $"article-{id}"
            Title = $"Incident \"{id}\" at \\\\edge/{id}"
            Body =
                String.replicate
                    3
                    $"Line 1 for item {id}\nLine 2 says \"quoted\" text.\nTabs\tand slashes\\\\ stay visible.\n"
            Tags = [
                "bench"
                "json"
                if id % 2 = 0 then "escaped" else "plain"
            ]
            Author = {
                Id = id
                Name = $"Writer {id}"
                Home = {
                    Street = $"{id} Schema Lane"
                    City = if id % 3 = 0 then "Adelaide" else "Melbourne"
                }
            }
        })

    ///
    /// Numeric-heavy payloads make it easier to see whether byte-level number
    /// parsing and direct writers are actually moving the benchmark needle.
    let createTelemetryPoints recordCount =
        [ 1..recordCount ]
        |> List.map (fun id -> {
            SensorId = id
            Timestamp = 1_700_000_000_000L + int64 (id * 250)
            Temperature = 18.25 + float id / 10.0
            Humidity = 40.0 + float (id % 35)
            Voltage = 3.3M + decimal (id % 7) / 100M
            RetryCount = uint32 (id % 4)
            Sequence = uint64 (id * 10_000)
            Healthy = id % 11 <> 0
        })

    ///
    /// Receive-side services often need to ignore fields they do not model
    /// yet, so keep one benchmark that includes deterministic unknown fields.
    let createIncomingPeople recordCount =
        [ 1..recordCount ]
        |> List.map (fun id -> {
            Id = id
            Name = $"Benchmark User {id}"
            Active = id % 2 = 0
            Tags = [
                "bench"
                if id % 2 = 0 then "even" else "odd"
            ]
            Home =
                ({
                    Street = $"{id} F# Way"
                    City = if id % 2 = 0 then "AOT City" else "Fable Town"
                    Country = "AU"
                    PostalCode = $"500{id % 10}"
                }
                : IncomingAddress)
        })

    let serializeJson<'T> (value: 'T) =
        System.Text.Json.JsonSerializer.Serialize(value, stjOptions)

    let serializeJsonNewtonsoft value = JsonConvert.SerializeObject(value)
    let utf8Bytes (json: string) = Encoding.UTF8.GetBytes(json)

    let createParserStringArray count =
        [ 1..count ]
        |> List.map (fun index ->
            String.replicate 2 $"entry-{index}-with-escapes-\"quoted\"-and-\\\\slashes\\\\-plus-newlines\n")
        |> serializeJson

    let createParserNumberArray count =
        [ 1..count ]
        |> List.map (fun index -> 1_700_000_000_000L + int64 (index * 37))
        |> serializeJson

    let createParserFlatObjectArray count =
        let items =
            [ 1..count ]
            |> List.map (fun index ->
                sprintf
                    """{"Id":%d,"Name":"record-%d","Code":"X%d","Enabled":%s,"Score":%s,"Trace":"01HV%04dABCDEF"}"""
                    index
                    index
                    index
                    (if index % 2 = 0 then "true" else "false")
                    ((18.25 + float index / 10.0).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    index)

        "[" + String.concat "," items + "]"

module Workloads =
    type Workload = {
        Name: string
        Description: string
        SerializeIterations: int
        DeserializeIterations: int
        JsonSizeBytes: int
        CodecMapperSerialize: unit -> string
        StjSerialize: unit -> string
        NewtonsoftSerialize: unit -> string
        OurParserScanBytes: unit -> int
        Utf8JsonReaderScanBytes: unit -> int
        CodecMapperDeserializeBytes: unit -> obj
        TypedExperimentDeserializeBytes: (unit -> obj) option
        StjDeserialize: unit -> obj
        NewtonsoftDeserialize: unit -> obj
        HashSerialized: string -> int
        HashValue: obj -> int
    }

    let private stjOptions = JsonSerializerOptions()
    let private smallMessageCodec = Json.compile Schemas.smallMessage
    let private personListCodec = Json.compile Schemas.personList
    let private articleListCodec = Json.compile Schemas.articleList
    let private telemetryListCodec = Json.compile Schemas.telemetryList

    let private hashAddress (address: Address) =
        address.Street.Length ^^^ (address.City.Length <<< 4)

    let private hashPeople (values: Person list) =
        values
        |> List.fold (fun acc value -> acc ^^^ value.Id ^^^ value.Name.Length ^^^ hashAddress value.Home) 0

    let private hashArticles (values: Article list) =
        values
        |> List.fold
            (fun acc value ->
                acc
                ^^^ value.Id
                ^^^ value.Slug.Length
                ^^^ value.Title.Length
                ^^^ value.Body.Length
                ^^^ value.Tags.Length
                ^^^ hashAddress value.Author.Home)
            0

    let private hashTelemetry (values: TelemetryPoint list) =
        values
        |> List.fold
            (fun acc value ->
                acc
                ^^^ value.SensorId
                ^^^ int value.RetryCount
                ^^^ int (value.Sequence &&& 0xFFFFUL)
                ^^^ int value.Timestamp
                ^^^ System.Decimal.ToInt32(System.Decimal.Truncate(value.Voltage * 100M)))
            0

    let private hashJsonValue (value: JsonValue) =
        let rec loop state current =
            match current with
            | JNull -> state ^^^ 37
            | JBool value -> state ^^^ if value then 31 else 29
            | JNumber value -> state ^^^ value.Length
            | JString value -> state ^^^ value.Length
            | JArray items -> items |> List.fold loop (state ^^^ 23)
            | JObject fields ->
                fields
                |> List.fold (fun acc (name, fieldValue) -> loop (acc ^^^ name.Length) fieldValue) (state ^^^ 19)

        loop 17 value

    let private makeWorkload<'T>
        name
        description
        serializeIterations
        deserializeIterations
        (value: 'T)
        (decodeJson: string)
        (codec: Json.Codec<'T>)
        (hashValue: 'T -> int)
        =
        {
            Name = name
            Description = description
            SerializeIterations = serializeIterations
            DeserializeIterations = deserializeIterations
            JsonSizeBytes = Encoding.UTF8.GetByteCount(decodeJson)
            CodecMapperSerialize = (fun () -> Json.serialize codec value)
            StjSerialize = (fun () -> System.Text.Json.JsonSerializer.Serialize(value, stjOptions))
            NewtonsoftSerialize = (fun () -> JsonConvert.SerializeObject(value))
            OurParserScanBytes = (fun () -> ParserScanExperiment.scanWithOurParser (Encoding.UTF8.GetBytes(decodeJson)))
            Utf8JsonReaderScanBytes =
                (fun () -> ParserScanExperiment.scanWithUtf8JsonReader (Encoding.UTF8.GetBytes(decodeJson)))
            CodecMapperDeserializeBytes =
                (fun () -> box (Json.deserializeBytes codec (Encoding.UTF8.GetBytes(decodeJson))))
            TypedExperimentDeserializeBytes = None
            StjDeserialize = (fun () -> box (System.Text.Json.JsonSerializer.Deserialize<'T>(decodeJson, stjOptions)))
            NewtonsoftDeserialize = (fun () -> box (JsonConvert.DeserializeObject<'T>(decodeJson)))
            HashSerialized = String.length
            HashValue = (fun boxed -> hashValue (unbox boxed))
        }

    let private jsonValueCodec = Json.compile Schema.jsonValue

    ///
    /// These workloads exist only to isolate parser behavior. They stay out of
    /// the release summary so snapshot docs remain focused on end-to-end cases.
    let private makeParserDiagnosticWorkload
        (name: string)
        (description: string)
        (deserializeIterations: int)
        (decodeJson: string)
        =
        let diagnosticOnly () =
            failwith "This diagnostic workload is intended for parser scan operations only."

        {
            Name = name
            Description = description
            SerializeIterations = 1
            DeserializeIterations = deserializeIterations
            JsonSizeBytes = Encoding.UTF8.GetByteCount(decodeJson)
            CodecMapperSerialize = (fun () -> decodeJson)
            StjSerialize = (fun () -> decodeJson)
            NewtonsoftSerialize = (fun () -> decodeJson)
            OurParserScanBytes = (fun () -> ParserScanExperiment.scanWithOurParser (Data.utf8Bytes decodeJson))
            Utf8JsonReaderScanBytes =
                (fun () -> ParserScanExperiment.scanWithUtf8JsonReader (Data.utf8Bytes decodeJson))
            CodecMapperDeserializeBytes = (fun () -> box (Json.deserialize jsonValueCodec decodeJson))
            TypedExperimentDeserializeBytes = None
            StjDeserialize = (fun () -> diagnosticOnly ())
            NewtonsoftDeserialize = (fun () -> diagnosticOnly ())
            HashSerialized = String.length
            HashValue = (fun boxed -> hashJsonValue (unbox boxed))
        }

    let createLegacyPersonBatch recordCount =
        let value = Data.createPeople recordCount
        let decodeJson = Data.serializeJson value

        makeWorkload
            "person-batch-legacy"
            $"Legacy nested-record batch with {recordCount} records."
            200000
            20000
            value
            decodeJson
            personListCodec
            hashPeople

    let standard =
        let smallMessage = Data.createSmallMessage ()
        let people25 = Data.createPeople 25
        let people250 = Data.createPeople 250
        let incomingPeople25 = Data.createIncomingPeople 25
        let articles20 = Data.createArticles 20
        let telemetry500 = Data.createTelemetryPoints 500
        let smallMessageJson = Data.serializeJson smallMessage
        let smallMessageBytes = Data.utf8Bytes smallMessageJson
        let people25Json = Data.serializeJson people25
        let people25Bytes = Data.utf8Bytes people25Json
        let people250Json = Data.serializeJson people250
        let people250Bytes = Data.utf8Bytes people250Json
        let incomingPeople25Json = Data.serializeJson incomingPeople25
        let incomingPeople25Bytes = Data.utf8Bytes incomingPeople25Json
        let articles20Json = Data.serializeJson articles20
        let articles20Bytes = Data.utf8Bytes articles20Json
        let telemetry500Json = Data.serializeJson telemetry500
        let telemetry500Bytes = Data.utf8Bytes telemetry500Json

        [|
            {
                makeWorkload
                    "small-message"
                    "One shallow command-sized object."
                    400000
                    300000
                    smallMessage
                    smallMessageJson
                    smallMessageCodec
                    (fun value -> value.Id ^^^ value.Kind.Length ^^^ value.TraceId.Length) with
                    OurParserScanBytes = (fun () -> ParserScanExperiment.scanWithOurParser smallMessageBytes)
                    Utf8JsonReaderScanBytes = (fun () -> ParserScanExperiment.scanWithUtf8JsonReader smallMessageBytes)
                    TypedExperimentDeserializeBytes =
                        Some(fun () -> box (TypedJsonExperiment.deserializeSmallMessageBytes smallMessageBytes))
            }

            {
                makeWorkload
                    "person-batch-25"
                    "Medium nested-record batch similar to API list responses."
                    120000
                    50000
                    people25
                    people25Json
                    personListCodec
                    hashPeople with
                    OurParserScanBytes = (fun () -> ParserScanExperiment.scanWithOurParser people25Bytes)
                    Utf8JsonReaderScanBytes = (fun () -> ParserScanExperiment.scanWithUtf8JsonReader people25Bytes)
                    TypedExperimentDeserializeBytes =
                        Some(fun () -> box (TypedJsonExperiment.deserializePeopleBytes people25Bytes))
            }

            {
                makeWorkload
                    "person-batch-250"
                    "Large nested-record batch to show throughput under load."
                    10000
                    5000
                    people250
                    people250Json
                    personListCodec
                    hashPeople with
                    OurParserScanBytes = (fun () -> ParserScanExperiment.scanWithOurParser people250Bytes)
                    Utf8JsonReaderScanBytes = (fun () -> ParserScanExperiment.scanWithUtf8JsonReader people250Bytes)
                    TypedExperimentDeserializeBytes =
                        Some(fun () -> box (TypedJsonExperiment.deserializePeopleBytes people250Bytes))
            }

            {
                makeWorkload
                    "escaped-articles-20"
                    "String-heavy records with quotes, slashes, newlines, and nested authors."
                    15000
                    6000
                    articles20
                    articles20Json
                    articleListCodec
                    hashArticles with
                    OurParserScanBytes = (fun () -> ParserScanExperiment.scanWithOurParser articles20Bytes)
                    Utf8JsonReaderScanBytes = (fun () -> ParserScanExperiment.scanWithUtf8JsonReader articles20Bytes)
                    TypedExperimentDeserializeBytes =
                        Some(fun () -> box (TypedJsonExperiment.deserializeArticlesBytes articles20Bytes))
            }

            {
                makeWorkload
                    "telemetry-500"
                    "Numeric-heavy flat objects with float, decimal, and wider integers."
                    12000
                    6000
                    telemetry500
                    telemetry500Json
                    telemetryListCodec
                    hashTelemetry with
                    OurParserScanBytes = (fun () -> ParserScanExperiment.scanWithOurParser telemetry500Bytes)
                    Utf8JsonReaderScanBytes = (fun () -> ParserScanExperiment.scanWithUtf8JsonReader telemetry500Bytes)
                    TypedExperimentDeserializeBytes =
                        Some(fun () -> box (TypedJsonExperiment.deserializeTelemetryBytes telemetry500Bytes))
            }

            {
                makeWorkload
                    "person-batch-25-unknown-fields"
                    "Decode path uses a wider incoming JSON contract with ignored fields."
                    120000
                    40000
                    people25
                    incomingPeople25Json
                    personListCodec
                    hashPeople with
                    OurParserScanBytes = (fun () -> ParserScanExperiment.scanWithOurParser incomingPeople25Bytes)
                    Utf8JsonReaderScanBytes =
                        (fun () -> ParserScanExperiment.scanWithUtf8JsonReader incomingPeople25Bytes)
                    TypedExperimentDeserializeBytes =
                        Some(fun () -> box (TypedJsonExperiment.deserializePeopleBytes incomingPeople25Bytes))
            }
        |]

    let diagnostics =
        let stringArray = Data.createParserStringArray 1000
        let numberArray = Data.createParserNumberArray 4000
        let flatObjects = Data.createParserFlatObjectArray 400

        [|
            makeParserDiagnosticWorkload
                "parser-strings-1000"
                "Parser-only diagnostic: escaped string array."
                3000
                stringArray

            makeParserDiagnosticWorkload
                "parser-numbers-4000"
                "Parser-only diagnostic: numeric array scanning."
                1500
                numberArray

            makeParserDiagnosticWorkload
                "parser-flat-objects-400"
                "Parser-only diagnostic: flat object traversal with repeated property names."
                1500
                flatObjects
        |]

    let names =
        Array.append (standard |> Array.map _.Name) (diagnostics |> Array.map _.Name)

    let tryFind name =
        Array.append standard diagnostics
        |> Array.tryFind (fun workload -> workload.Name = name)
