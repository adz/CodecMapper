module Tests

open Xunit
open Swensen.Unquote
open cmap

type Address = { Street: string; City: string }
type Person = { Id: int; Name: string; Home: Address }

type PersonId = PersonId of int
type WrappedPerson = { Id: PersonId; Tags: string list }

[<Fact>]
let ``One schema, multiple formats (JSON and XML)`` () =
    let addressSchema = 
        Schema.record<Address, _> (fun a -> {| Street = a.Street; City = a.City |})
    
    let personSchema = 
        Schema.recordWith<Person, _> 
            (fun p -> {| Id = p.Id; Name = p.Name; Home = p.Home |})
            (Map.ofList ["Home", addressSchema :> ISchema])
    
    let person = { 
        Id = 42
        Name = "Adam"
        Home = { Street = "123 F# Lane"; City = "AOT City" } 
    }

    // JSON (Alphabetical order from anonymous record)
    let jsonCodec = Json.compile personSchema
    let json = Json.serialize jsonCodec person
    test <@ json = "{\"Home\":{\"City\":\"AOT City\",\"Street\":\"123 F# Lane\"},\"Id\":42,\"Name\":\"Adam\"}" @>
    
    // XML (Naive proof of concept, also alphabetical)
    let xmlCodec = Xml.compile personSchema
    let xml = Xml.serialize xmlCodec person
    test <@ xml = "<person><Home><City>AOT City</City><Street>123 F# Lane</Street></Home><Id>42</Id><Name>Adam</Name></person>" @>

[<Fact>]
let ``Round-trip nested record JSON`` () =
    let addressSchema = 
        Schema.record<Address, _> (fun a -> {| Street = a.Street; City = a.City |})
    
    let personSchema = 
        Schema.recordWith<Person, _> 
            (fun p -> {| Id = p.Id; Name = p.Name; Home = p.Home |})
            (Map.ofList ["Home", addressSchema :> ISchema])
    
    let codec = Json.compile personSchema
    let person = { 
        Id = 42
        Name = "Adam"
        Home = { Street = "123 F# Lane"; City = "AOT City" } 
    }
    
    let json = Json.serialize codec person
    let decoded = Json.deserialize codec json
    test <@ decoded = person @>

[<Fact>]
let ``Round-trip list of strings JSON`` () =
    let listSchema = Schema.list Schema.string
    let codec = Json.compile listSchema
    
    let value = ["a"; "b"; "c"]
    let json = Json.serialize codec value
    test <@ json = "[\"a\",\"b\",\"c\"]" @>
    
    let decoded = Json.deserialize codec json
    test <@ decoded = value @>

[<Fact>]
let ``Round-trip mapped type (PersonId) JSON`` () =
    let personIdSchema = 
        Schema.int |> Schema.map PersonId (fun (PersonId id) -> id)
    
    let wrappedPersonSchema =
        Schema.recordWith<WrappedPerson, _>
            (fun p -> {| Id = p.Id; Tags = p.Tags |})
            (Map.ofList ["Id", personIdSchema :> ISchema])
            
    let codec = Json.compile wrappedPersonSchema
    
    let p = { Id = PersonId 123; Tags = ["fsharp"; "aot"] }
    let json = Json.serialize codec p
    let decoded = Json.deserialize codec json
    test <@ decoded = p @>
