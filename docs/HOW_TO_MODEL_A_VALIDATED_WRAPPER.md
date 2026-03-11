# How To Model A Validated Wrapper

Use this pattern when the wire value is simple but the in-memory model should enforce a smart-constructor rule.

```fsharp
open CodecMapper.Schema

type UserId = UserId of int

module UserId =
    let create value =
        if value > 0 then Ok(UserId value)
        else Error "UserId must be positive"

    let value (UserId value) = value

type Account = { Id: UserId; Name: string }
let makeAccount id name = { Id = id; Name = name }

let userIdSchema =
    int
    |> tryMap UserId.create UserId.value

let accountSchema =
    define<Account>
    |> construct makeAccount
    |> fieldWith "id" _.Id userIdSchema
    |> field "name" _.Name
    |> build
```

Extract the `tryMap` pipeline into a named schema when the same wrapper rule appears across multiple contracts.
