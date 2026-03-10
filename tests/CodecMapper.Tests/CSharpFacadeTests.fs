module CodecMapper.Tests.CSharpFacadeTests

open Swensen.Unquote
open Xunit
open CodecMapper
open CodecMapper.Bridge
open CodecMapper.CSharpModels

[<Fact>]
let ``CSharpSchema fluent builder authors nested JSON contracts`` () =
    let codec = CSharpSchema.Json FluentSchemas.User

    let value =
        let home = FluentAddress()
        home.Street <- "Main"
        home.City <- "Adelaide"

        let user = FluentUser()
        user.Id <- 7
        user.DisplayName <- "Ada"
        user.Home <- home
        user

    let json = Json.serialize codec value
    let roundTrip = Json.deserialize codec json

    test <@ json = """{"id":7,"display_name":"Ada","home":{"street":"Main","city":"Adelaide"}}""" @>
    test <@ roundTrip.Id = 7 @>
    test <@ roundTrip.DisplayName = "Ada" @>
    test <@ roundTrip.Home.Street = "Main" @>
    test <@ roundTrip.Home.City = "Adelaide" @>

[<Fact>]
let ``CSharpSchema fluent builder works with KeyValue and Yaml compile helpers`` () =
    let keyValueCodec = CSharpSchema.KeyValue FluentSchemas.User
    let yamlCodec = CSharpSchema.Yaml FluentSchemas.User

    let value =
        let home = FluentAddress()
        home.Street <- "North"
        home.City <- "Perth"

        let user = FluentUser()
        user.Id <- 9
        user.DisplayName <- "Lin"
        user.Home <- home
        user

    let keyValues = KeyValue.serialize keyValueCodec value
    let yaml = Yaml.serialize yamlCodec value
    let fromKeyValues = KeyValue.deserialize keyValueCodec keyValues
    let fromYaml = Yaml.deserialize yamlCodec yaml

    let expected =
        Map.ofList [
            "id", "9"
            "display_name", "Lin"
            "home.street", "North"
            "home.city", "Perth"
        ]

    test <@ keyValues = expected @>
    test <@ yaml = "id: 9\ndisplay_name: Lin\nhome:\n  street: North\n  city: Perth" @>
    test <@ fromKeyValues.DisplayName = "Lin" @>
    test <@ fromKeyValues.Home.City = "Perth" @>
    test <@ fromYaml.DisplayName = "Lin" @>
    test <@ fromYaml.Home.Street = "North" @>
