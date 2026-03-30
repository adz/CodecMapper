# How To Model A Basic Record

Use this pattern when the wire contract is a single flat object and your F# record already matches that shape.

```fsharp
open CodecMapper

type Person = { Id: int; Name: string }
let makePerson id name = { Id = id; Name = name }

let codec =
    Schema.define<Person>
    |> Schema.construct makePerson
    |> Schema.field "id" _.Id
    |> Schema.field "name" _.Name
    |> Json.buildAndCompile
```

This is the smallest authored contract shape: define the record target, provide the constructor, then map each field explicitly and finish with `Json.buildAndCompile`.
