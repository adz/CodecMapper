namespace CodecMapper.LegacyShim

open System.Text
open System.Text.Json
open CodecMapper.Core

type LegacyAddress = { City: string; PostCode: string }

type LegacyPerson = {
    Id: int
    Name: string
    IsActive: bool
    Score: float
    Home: LegacyAddress
    Tags: string array
}

module private LegacySchemas =
    let private makeAddress city postCode : LegacyAddress = { City = city; PostCode = postCode }

    let private makePerson id name isActive score home tags : LegacyPerson = {
        Id = id
        Name = name
        IsActive = isActive
        Score = score
        Home = home
        Tags = tags
    }

    let address =
        codec {
            construct makeAddress
            string "City" _.City
            string "PostCode" _.PostCode
        }

    let person =
        codec {
            construct makePerson
            int "Id" _.Id
            string "Name" _.Name
            bool "IsActive" _.IsActive
            float "Score" _.Score
            linkVia (Codec.sub address) "Home" _.Home
            linkVia (Codec.array Codec.string) "Tags" _.Tags
        }

    let people = Codec.list person

module LegacyJson =
    let encodePeople (people: LegacyPerson list) =
        JsonRunner.encodeString LegacySchemas.people people

    let decodePeopleDoc (json: string) =
        use doc = JsonDocument.Parse(json)
        JsonRunner.decodeDoc LegacySchemas.people doc.RootElement

    let decodePeopleStream (jsonBytes: byte[]) =
        let mutable reader = Utf8JsonReader(jsonBytes)
        reader.Read() |> ignore
        JsonRunner.decodeReader LegacySchemas.people &reader

module Fixtures =
    let createPeople count : LegacyPerson list = [
        for i in 1..count ->
            {
                Id = i
                Name = $"User-{i}"
                IsActive = i % 2 = 0
                Score = float i * 1.25
                Home = {
                    City = "Adelaide"
                    PostCode = sprintf "50%02d" (i % 100)
                }
                Tags = [| "alpha"; "beta"; $"tag-{i % 10}" |]
            }
    ]

    let toUtf8Bytes (json: string) = Encoding.UTF8.GetBytes(json)
