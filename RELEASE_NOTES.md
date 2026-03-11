# Release Notes

## 0.1.0 - 2026-03-11

- Initial public `CodecMapper` repository setup
- Typed pipeline schema DSL
- JSON and XML codecs from one schema model
- YAML and key/value projections from the same authored schema model
- JSON Schema export and import support
- .NET bridge importers for `System.Text.Json`, `Newtonsoft.Json`, and `DataContract`
- Fable-ready NuGet packaging validated through local packed-package consumer checks
- Public getting-started, config-contract, and API reference docs

## Release Process

Published versions should be cut as Git tags and GitHub Releases.

When the first real release is published, move the shipped entries from
`Unreleased` into a versioned section such as `0.1.0`.
