# Release Notes

## 0.2.0 - 2026-03-15

- Fixed `DateTime`, `DateTimeOffset`, and `TimeSpan` parsing for Fable when consumed as a published NuGet package
- Major documentation overhaul: tightened `README.md` and `GETTING_STARTED.md` and added new `Introduction` and `Benchmarks` pages
- Hardened Fable package compatibility checks to exercise the published runtime rather than local source builds
- Stabilized Fable package restore on CI
- Streamlined repository metadata and removed archived benchmark references

## 0.1.0 - 2026-03-11

- Initial public `CodecMapper` repository setup
- Split the core library into explicit dependency-ordered source files to keep the portable runtime easier to navigate
- Typed pipeline schema DSL with compile aliases plus validated schema helpers such as `Schema.nonEmptyString`, `Schema.trimmedString`, `Schema.positiveInt`, and `Schema.nonEmptyList`
- Path-aware decode diagnostics across JSON, XML, YAML, and key/value projections so errors carry field and collection context
- JSON and XML codecs from one schema model with property-based round-trip coverage for representative codec laws
- YAML and key/value projections from the same authored schema model
- JSON Schema export and import support
- .NET bridge importers for `System.Text.Json`, `Newtonsoft.Json`, and `DataContract`
- Fable-ready NuGet packaging validated through local packed-package consumer checks
- Comprehensive documentation: focused guides for basic, nested, validated, versioned, config, JSON Schema, and C# bridge scenarios
- Repeatable benchmark hot-path profiling workflow and docs around the manual benchmark runner

## Release Process

Published versions should be cut as Git tags and GitHub Releases.

When the first real release is published, move the shipped entries from
`Unreleased` into a versioned section such as `0.1.0`.
