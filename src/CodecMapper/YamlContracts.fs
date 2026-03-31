namespace CodecMapper

open CodecMapper

module Yaml =
    type Codec<'T> = CodecMapper.YamlBackend.Codec<'T>

    let compile (codec: CodecMapper.Codec<'T>) : Codec<'T> = CodecMapper.YamlBackend.compile codec
    let compileSchema (codec: CodecMapper.Codec<'T>) : Codec<'T> = compile codec

    let buildAndCompile (builder: SchemaBuilder<'Record, 'Ctor, 'Record, 'Chain>) : Codec<'Record>
        when 'Chain :> IChainNode<'Record, 'Ctor, 'Record> =
        builder |> Schema.build |> compile

    let serialize (codec: Codec<'T>) (value: 'T) = CodecMapper.YamlBackend.serialize codec value
    let deserialize (codec: Codec<'T>) (yaml: string) = CodecMapper.YamlBackend.deserialize codec yaml
