# How To Model A Versioned Contract

Use this pattern for config files or messages that evolve over time and need an explicit version marker in the wire shape.

```fsharp
open CodecMapper

type SettingsV2 = {
    Version: int
    Mode: string
    Region: string option
}

let makeSettingsV2 version mode region =
    { Version = version; Mode = mode; Region = region }

let settingsV2Schema =
    Schema.define<SettingsV2>
    |> Schema.construct makeSettingsV2
    |> Schema.fieldWith "version" _.Version (
        Schema.int
        |> Schema.tryMap
            (fun value ->
                if value > 0 then Ok value
                else Error "version must be positive")
            id
    )
    |> Schema.field "mode" _.Mode
    |> Schema.field "region" _.Region
    |> Schema.build
```

Keep the version field explicit so contract evolution stays visible in one authored contract. If you also need omission policies or defaults, compose those helpers directly at the field boundary.
