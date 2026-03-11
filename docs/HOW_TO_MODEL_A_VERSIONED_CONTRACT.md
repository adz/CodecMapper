# How To Model A Versioned Contract

Use this pattern for config files or messages that evolve over time and need an explicit version marker in the wire shape.

```fsharp
open CodecMapper.Schema

type SettingsV2 = {
    Version: int
    Mode: string
    Region: string option
}

let makeSettingsV2 version mode region =
    { Version = version; Mode = mode; Region = region }

let settingsV2Schema =
    define<SettingsV2>
    |> construct makeSettingsV2
    |> fieldWith "version" _.Version (
        int
        |> tryMap
            (fun value ->
                if value > 0 then Ok value
                else Error "version must be positive")
            id
    )
    |> field "mode" _.Mode
    |> field "region" _.Region
    |> build
```

Keep the version field explicit so contract evolution stays visible in one authored schema. If you also need omission policies or defaults, compose those helpers directly at the field boundary.
