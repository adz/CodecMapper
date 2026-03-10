module CodecMapper.Tests.CSharpBridgeTests

open System
open System.Collections.Generic
open Swensen.Unquote
open Xunit
open CodecMapper
open CodecMapper.Bridge
open CodecMapper.CSharpModels

let private assertImportFails<'T> (expectedFragment: string) (import: BridgeOptions -> Schema<'T>) =
    let error =
        Assert.Throws<Exception>(fun () -> import BridgeOptions.defaults |> ignore)

    test <@ error.Message.Contains(expectedFragment) @>

[<Fact>]
let ``System.Text.Json bridge imports constructor-bound classes`` () =
    let schema = SystemTextJson.import<StjUser> BridgeOptions.defaults
    let codec = Json.compile schema

    let value =
        StjUser(7, "Ada", StjAddress("Adelaide", "5000"), [| "fsharp"; "json" |], System.Nullable 42)

    let json = Json.serialize codec value
    let roundTrip = Json.deserialize codec json

    test
        <@
            json = """{"user_id":7,"display_name":"Ada","home":{"city":"Adelaide","post_code":"5000"},"tags":["fsharp","json"],"age":42}"""
        @>

    test <@ roundTrip.Id = 7 @>
    test <@ roundTrip.DisplayName = "Ada" @>
    test <@ roundTrip.Home.City = "Adelaide" @>
    test <@ roundTrip.Home.PostCode = "5000" @>
    test <@ roundTrip.Tags = [| "fsharp"; "json" |] @>
    test <@ roundTrip.Age.HasValue && roundTrip.Age.Value = 42 @>

[<Fact>]
let ``System.Text.Json bridge imports setter-bound classes`` () =
    let schema = SystemTextJson.import<StjSettings> BridgeOptions.defaults
    let codec = Json.compile schema

    let value = StjSettings()
    value.RetryCount <- 3
    value.Enabled <- true
    value.Labels <- new List<string>([ "alpha"; "beta" ])
    value.InternalNote <- "skip-me"

    let json = Json.serialize codec value
    let roundTrip = Json.deserialize codec json

    test <@ json = """{"enabled":true,"labels":["alpha","beta"],"retry_count":3}""" @>
    test <@ roundTrip.RetryCount = 3 @>
    test <@ roundTrip.Enabled @>
    test <@ Seq.toList roundTrip.Labels = [ "alpha"; "beta" ] @>
    test <@ roundTrip.InternalNote = "" @>

[<Fact>]
let ``System.Text.Json bridge imports interface collection members`` () =
    let schema = SystemTextJson.import<StjCollectionSettings> BridgeOptions.defaults
    let codec = Json.compile schema

    let value =
        StjCollectionSettings(
            ResizeArray([ "Ada"; "Lin" ]) :> IReadOnlyList<string>,
            ResizeArray([ 1; 2; 3 ]) :> ICollection<int>
        )

    let json = Json.serialize codec value
    let roundTrip = Json.deserialize codec json

    test <@ json = """{"names":["Ada","Lin"],"scores":[1,2,3]}""" @>
    test <@ Seq.toList roundTrip.Names = [ "Ada"; "Lin" ] @>
    test <@ Seq.toList roundTrip.Scores = [ 1; 2; 3 ] @>

[<Fact>]
let ``System.Text.Json bridge imports enum members through numeric wire values`` () =
    let schema = SystemTextJson.import<StjEnumSettings> BridgeOptions.defaults
    let codec = Json.compile schema

    let value = StjEnumSettings(StjStatus.Suspended)

    let json = Json.serialize codec value
    let roundTrip = Json.deserialize codec json

    test <@ json = """{"status":2}""" @>
    test <@ roundTrip.Status = StjStatus.Suspended @>

[<Fact>]
let ``Newtonsoft bridge imports constructor-bound classes`` () =
    let schema = NewtonsoftJson.import<NewtonsoftUser> BridgeOptions.defaults
    let codec = Json.compile schema

    let value =
        NewtonsoftUser(9, "Lin", NewtonsoftAddress("Perth", "6000"), new List<string>([ "bridge"; "newtonsoft" ]))

    let json = Json.serialize codec value
    let roundTrip = Json.deserialize codec json

    test
        <@
            json = """{"user_id":9,"display_name":"Lin","home":{"city":"Perth","post_code":"6000"},"labels":["bridge","newtonsoft"]}"""
        @>

    test <@ roundTrip.Id = 9 @>
    test <@ roundTrip.DisplayName = "Lin" @>
    test <@ roundTrip.Home.City = "Perth" @>
    test <@ roundTrip.Home.PostCode = "6000" @>
    test <@ Seq.toList roundTrip.Labels = [ "bridge"; "newtonsoft" ] @>

[<Fact>]
let ``DataContract bridge imports constructor-bound classes`` () =
    let schema = DataContracts.import<DataContractUser> BridgeOptions.defaults
    let codec = Json.compile schema

    let value = DataContractUser(11, "Quinn", DataContractAddress("Melbourne", "3000"))

    let json = Json.serialize codec value
    let roundTrip = Json.deserialize codec json

    test <@ json = """{"user_id":11,"display_name":"Quinn","home":{"city":"Melbourne","post_code":"3000"}}""" @>
    test <@ roundTrip.Id = 11 @>
    test <@ roundTrip.DisplayName = "Quinn" @>
    test <@ roundTrip.Home.City = "Melbourne" @>
    test <@ roundTrip.Home.PostCode = "3000" @>

[<Fact>]
let ``DataContract bridge imports setter-bound classes`` () =
    let schema = DataContracts.import<DataContractSettings> BridgeOptions.defaults
    let codec = Json.compile schema

    let value = DataContractSettings()
    value.Enabled <- true
    value.Labels <- new List<string>([ "json"; "config" ])
    value.InternalNote <- "skip-me"

    let json = Json.serialize codec value
    let roundTrip = Json.deserialize codec json

    test <@ json = """{"enabled":true,"labels":["json","config"]}""" @>
    test <@ roundTrip.Enabled @>
    test <@ Seq.toList roundTrip.Labels = [ "json"; "config" ] @>
    test <@ roundTrip.InternalNote = "" @>

[<Fact>]
let ``System.Text.Json bridge rejects JsonConverter attributes`` () =
    assertImportFails<StjUnsupportedConverter> "JsonConverter" SystemTextJson.import

[<Fact>]
let ``System.Text.Json bridge rejects JsonExtensionData attributes`` () =
    assertImportFails<StjUnsupportedExtensionData> "JsonExtensionData" SystemTextJson.import

[<Fact>]
let ``System.Text.Json bridge rejects polymorphic contracts`` () =
    assertImportFails<StjAnimal> "Polymorphic System.Text.Json contracts" SystemTextJson.import

[<Fact>]
let ``System.Text.Json bridge rejects mixed constructor and setter binding`` () =
    assertImportFails<StjMixedBinding> "mixes constructor-bound and setter-bound members" SystemTextJson.import

[<Fact>]
let ``Newtonsoft bridge rejects JsonConverter attributes`` () =
    assertImportFails<NewtonsoftUnsupportedConverter> "JsonConverter" NewtonsoftJson.import

[<Fact>]
let ``Newtonsoft bridge rejects JsonExtensionData attributes`` () =
    assertImportFails<NewtonsoftUnsupportedExtensionData> "JsonExtensionData" NewtonsoftJson.import

[<Fact>]
let ``DataContract bridge rejects KnownType polymorphism`` () =
    assertImportFails<DataContractAnimal> "KnownType polymorphism" DataContracts.import
