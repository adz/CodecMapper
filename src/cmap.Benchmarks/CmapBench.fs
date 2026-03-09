namespace cmap.Benchmarks

open cmap

type Address = { Street: string; City: string }
type Person = { Id: int; Name: string; Home: Address }

module Schemas =
    let address = Schema.record<Address, _> (fun a -> {| Street = a.Street; City = a.City |})
    let person = Schema.recordWith<Person, _> 
                    (fun p -> {| Id = p.Id; Name = p.Name; Home = p.Home |})
                    (Map.ofList ["Home", address :> ISchema])

module CmapBench =
    let codec = Json.compile Schemas.person
    
    let serialize p = Json.serialize codec p
    let deserializeBytes bytes = Json.deserializeBytes codec bytes
