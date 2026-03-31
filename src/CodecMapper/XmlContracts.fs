namespace CodecMapper

open CodecMapper

module Xml =
    type XmlSource = CodecMapper.XmlBackend.XmlSource
    type XmlWriter = CodecMapper.XmlBackend.XmlWriter
    type Codec<'T> = CodecMapper.XmlBackend.Codec<'T>

    let compile (codec: CodecMapper.Codec<'T>) : Codec<'T> = CodecMapper.XmlBackend.compile codec
    let compileSchema (codec: CodecMapper.Codec<'T>) : Codec<'T> = compile codec

    let buildAndCompile (builder: SchemaBuilder<'Record, 'Ctor, 'Record, 'Chain>) : Codec<'Record>
        when 'Chain :> IChainNode<'Record, 'Ctor, 'Record> =
        builder |> Schema.build |> compile

    let serialize (codec: Codec<'T>) (value: 'T) = CodecMapper.XmlBackend.serialize codec value
    let deserialize (codec: Codec<'T>) (xml: string) = CodecMapper.XmlBackend.deserialize codec xml
    let deserializeBytes (codec: Codec<'T>) (bytes: byte[]) = CodecMapper.XmlBackend.deserializeBytes codec bytes
