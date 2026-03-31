namespace CodecMapper

open CodecMapper

module JsonSchema =
    module ImportOptions =
        let empty = CodecMapper.JsonSchemaBackend.ImportOptions.empty
        let defaults = CodecMapper.JsonSchemaBackend.ImportOptions.defaults
        let withFormat name validator options =
            CodecMapper.JsonSchemaBackend.ImportOptions.withFormat name validator options

    let generate (codec: Codec<'T>) = CodecMapper.JsonSchemaBackend.generate codec
    let generateSchema (codec: Codec<'T>) = generate codec
    let importWithReportUsing (options: CodecMapper.JsonSchemaBackend.ImportOptions) (jsonSchemaText: string) =
        CodecMapper.JsonSchemaBackend.importWithReportUsing options jsonSchemaText
    let importWithReport (jsonSchemaText: string) = CodecMapper.JsonSchemaBackend.importWithReport jsonSchemaText
    let importUsing (options: CodecMapper.JsonSchemaBackend.ImportOptions) (jsonSchemaText: string) =
        CodecMapper.JsonSchemaBackend.importUsing options jsonSchemaText
    let import (jsonSchemaText: string) = CodecMapper.JsonSchemaBackend.import jsonSchemaText
