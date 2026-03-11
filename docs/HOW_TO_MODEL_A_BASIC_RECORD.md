# How To Model A Basic Record

Use this pattern when the wire contract is a single flat object and your F# record already matches that shape.

```fsharp
open CodecMapper
open CodecMapper.Schema

type Person = { Id: int; Name: string }
let makePerson id name = { Id = id; Name = name }

let personSchema =
    define<Person>
    |> construct makePerson
    |> field "id" _.Id
    |> field "name" _.Name
    |> build

let codec = Json.codec personSchema
```

This is the smallest authored schema shape: define the record target, provide the constructor, then map each field explicitly.
