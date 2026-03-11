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
        CodecMapperDeserializeBytes: unit -> obj
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
            CodecMapperDeserializeBytes =
                (fun () -> box (Json.deserializeBytes codec (Encoding.UTF8.GetBytes(decodeJson))))
            StjDeserialize = (fun () -> box (System.Text.Json.JsonSerializer.Deserialize<'T>(decodeJson, stjOptions)))
            NewtonsoftDeserialize = (fun () -> box (JsonConvert.DeserializeObject<'T>(decodeJson)))
            HashSerialized = String.length
            HashValue = (fun boxed -> hashValue (unbox boxed))
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

        [|
            makeWorkload
                "small-message"
                "One shallow command-sized object."
                400000
                300000
                smallMessage
                (Data.serializeJson smallMessage)
                smallMessageCodec
                (fun value -> value.Id ^^^ value.Kind.Length ^^^ value.TraceId.Length)

            makeWorkload
                "person-batch-25"
                "Medium nested-record batch similar to API list responses."
                120000
                50000
                people25
                (Data.serializeJson people25)
                personListCodec
                hashPeople

            makeWorkload
                "person-batch-250"
                "Large nested-record batch to show throughput under load."
                10000
                5000
                people250
                (Data.serializeJson people250)
                personListCodec
                hashPeople

            makeWorkload
                "escaped-articles-20"
                "String-heavy records with quotes, slashes, newlines, and nested authors."
                15000
                6000
                articles20
                (Data.serializeJson articles20)
                articleListCodec
                hashArticles

            makeWorkload
                "telemetry-500"
                "Numeric-heavy flat objects with float, decimal, and wider integers."
                12000
                6000
                telemetry500
                (Data.serializeJson telemetry500)
                telemetryListCodec
                hashTelemetry

            makeWorkload
                "person-batch-25-unknown-fields"
                "Decode path uses a wider incoming JSON contract with ignored fields."
                120000
                40000
                people25
                (Data.serializeJson incomingPeople25)
                personListCodec
                hashPeople
        |]

    let names = standard |> Array.map _.Name

    let tryFind name =
        standard |> Array.tryFind (fun workload -> workload.Name = name)
