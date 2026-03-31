namespace CodecMapper

open CodecMapper

module KeyValue =
    type Codec<'T> = CodecMapper.KeyValueBackend.Codec<'T>

    module Options =
        let defaults = CodecMapper.KeyValueBackend.Options.defaults
        let environment = CodecMapper.KeyValueBackend.Options.environment

    let compileUsing (options: CodecMapper.KeyValueBackend.Options) (codec: CodecMapper.Codec<'T>) : Codec<'T> =
        CodecMapper.KeyValueBackend.compileUsing options codec

    let compile (codec: CodecMapper.Codec<'T>) : Codec<'T> = compileUsing CodecMapper.KeyValueBackend.Options.defaults codec
    let compileSchemaUsing (options: CodecMapper.KeyValueBackend.Options) (codec: CodecMapper.Codec<'T>) : Codec<'T> =
        compileUsing options codec
    let compileSchema (codec: CodecMapper.Codec<'T>) : Codec<'T> =
        compile codec

    let buildAndCompile (builder: SchemaBuilder<'Record, 'Ctor, 'Record, 'Chain>) : Codec<'Record>
        when 'Chain :> IChainNode<'Record, 'Ctor, 'Record> =
        builder |> Schema.build |> compile

    let serialize (codec: Codec<'T>) (value: 'T) = CodecMapper.KeyValueBackend.serialize codec value
    let deserialize (codec: Codec<'T>) (values: Map<string, string>) = CodecMapper.KeyValueBackend.deserialize codec values
    let deserializeSeq (codec: Codec<'T>) (values: seq<string * string>) = CodecMapper.KeyValueBackend.deserializeSeq codec values
