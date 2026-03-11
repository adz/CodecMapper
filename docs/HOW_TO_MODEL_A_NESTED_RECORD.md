# How To Model A Nested Record

Use this pattern when one field is itself another authored contract and you want that boundary to stay explicit.

```fsharp
open CodecMapper.Schema

type Address = { Street: string; City: string }
let makeAddress street city = { Street = street; City = city }

type Person = { Id: int; Name: string; Home: Address }
let makePerson id name home = { Id = id; Name = name; Home = home }

let addressSchema =
    define<Address>
    |> construct makeAddress
    |> field "street" _.Street
    |> field "city" _.City
    |> build

let personSchema =
    define<Person>
    |> construct makePerson
    |> field "id" _.Id
    |> field "name" _.Name
    |> fieldWith "home" _.Home addressSchema
    |> build
```

Use `fieldWith` when the child value has its own explicit schema boundary instead of relying on the built-in auto-resolved cases.
