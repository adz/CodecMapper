# Release Notes

## 0.2.0 - 2026-03-15

- Split the core library into explicit dependency-ordered source files to keep the portable runtime easier to navigate without changing behavior
- Added compile aliases plus validated schema helpers such as `Schema.nonEmptyString`, `Schema.trimmedString`, `Schema.positiveInt`, and `Schema.nonEmptyList`
- Added path-aware decode diagnostics across JSON, XML, YAML, and key/value projections so errors carry field and collection context
- Expanded contract-pattern documentation with focused guides for basic, nested, validated, versioned, config, JSON Schema, and C# bridge scenarios
- Added property-based round-trip coverage for representative JSON and XML codec laws
- Added a repeatable benchmark hot-path profiling workflow and docs around the manual benchmark runner

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
