# Performance Review And Plan

This note is the working summary for `Task 42`.

It pulls the performance investigation out of `TASKS.md` so the roadmap stays
short, while the evidence and next steps stay concrete and reviewable.

## Current position

Two things are true at the same time:

- the current erased JSON decode path is adding real overhead
- the handwritten parser is still materially slower than `Utf8JsonReader` on
  realistic larger payloads

So the current goal is not "rewrite everything to typed code now".

The current goal is:

1. keep proving where the current time goes
2. keep the typed experiment narrow and benchmarked
3. improve the handwritten parser and typed decode lane in parallel
4. only touch the production runtime once the replacement is both simpler and
   measurably better

## Findings so far

### 1. Hot-path profiling says record decode and allocation pressure are real

The first `perf` pass on:

- `escaped-articles-20`
- `telemetry-500`

showed meaningful time in:

- runtime helper buckets
- memory clearing
- managed JIT blobs

That supports the conclusion that record assembly and intermediate storage are
costly enough to justify a typed experiment.

Detailed hotspot read:

- [PROFILE-REPORT-ANALYSIS-INSTRUCTIONAL.md](/home/adam/projects/mylibs/CodecMapper/main/docs/PROFILE-REPORT-ANALYSIS-INSTRUCTIONAL.md)

### 2. A parser-backed typed decode experiment is directionally positive

The first benchmark-only typed decode lane:

- reuses the handwritten parser
- avoids the current erased record assembly path
- stays out of the production runtime

Focused results against `codecmapper-deserialize-bytes`:

- `small-message`: `3.272 ms` -> `2.450 ms`
- `person-batch-25-unknown-fields`: `775.955 ms` -> `665.119 ms`
- `telemetry-500`: `5663.483 ms` -> `5285.568 ms`
- `person-batch-250`: `1741.971 ms` -> `1640.387 ms`
- `escaped-articles-20`: `754.797 ms` -> `733.219 ms`

Interpretation:

- the erased decode path is costing real time
- the win size varies by payload shape
- typed decode alone does not close the whole gap to STJ

### 3. Parser-only comparison shows the handwritten parser is not yet competitive overall

Parser-only scan results:

- `small-message`: our parser `9.219 ms`, `Utf8JsonReader` `13.455 ms`
- `person-batch-25-unknown-fields`: our parser `556.524 ms`,
  `Utf8JsonReader` `351.047 ms`
- `person-batch-250`: our parser `524.173 ms`, `Utf8JsonReader` `243.671 ms`
- `escaped-articles-20`: our parser `519.997 ms`, `Utf8JsonReader`
  `410.947 ms`
- `telemetry-500`: our parser `1658.459 ms`, `Utf8JsonReader` `607.815 ms`

Interpretation:

- our parser wins on the tiny shallow object
- `Utf8JsonReader` is materially faster on realistic larger payloads
- the parser itself now has to be treated as a first-class optimization target

## Architectural conclusion so far

Do not switch the core runtime to `Utf8JsonReader` yet.

Reasons:

- `Utf8JsonReader` is a `ref struct`
- that makes generic functional composition harder and more fragile
- the repo still values one portable conceptual runtime
- Fable compatibility still matters
- the old CPS experiment already showed how a very fast design can still be
  difficult to evolve

So the burden of proof remains on a `Utf8JsonReader`-based core.

Current strategic direction:

- keep the handwritten parser
- improve it deliberately
- keep the typed decode experiment moving
- keep measuring both separately

## Evidence files

- hotspot and experiment interpretation:
  [PROFILE-REPORT-ANALYSIS-INSTRUCTIONAL.md](/home/adam/projects/mylibs/CodecMapper/main/docs/PROFILE-REPORT-ANALYSIS-INSTRUCTIONAL.md)
- benchmark experiment wiring:
  [BenchmarkScenarios.fs](/home/adam/projects/mylibs/CodecMapper/main/benchmarks/BenchmarkScenarios.fs)
- benchmark runner operations:
  [Program.fs](/home/adam/projects/mylibs/CodecMapper/main/benchmarks/CodecMapper.Benchmarks.Runner/Program.fs)

## Task list

- [x] Add hotspot profiling workflow and document how to read it.
- [x] Add a benchmark-only typed JSON decode experiment using the handwritten parser.
- [x] Compare typed decode against the current erased decode path.
- [x] Add parser-only scan comparison against `Utf8JsonReader`.
- [x] Add parser diagnostic scenarios that isolate string-heavy, number-heavy, and flat-object traversal without polluting the release summary.
- [x] Use those diagnostics to identify the handwritten parser's biggest losses:
  - whitespace skipping
  - string scanning
  - numeric token scanning
  - object and array loop overhead
- [ ] Apply the first focused handwritten-parser optimization and rerun parser-only comparisons.
- [x] Apply the first focused handwritten-parser optimization and rerun parser-only comparisons.
- [ ] Generalize the typed record-decode lane beyond hand-written benchmark shapes.
- [ ] Compare generalized typed decode against the current erased path on the release scenarios again.
- [ ] Decide whether the production runtime should adopt the typed lane, the parser changes, both, or neither.

## Immediate next steps

1. Add parser diagnostic scenarios that stay profile-only and out of the release summary.
2. Benchmark those diagnostics against `Utf8JsonReader`.
3. Use the result to pick the first parser optimization target.
4. Keep the production runtime unchanged until one optimization pass has a clear measured win.

## Parser diagnostic results

The parser diagnostics now isolate three categories without affecting the
release benchmark summary.

Focused results:

- `parser-strings-1000`: our parser `2177.742 ms`, `Utf8JsonReader`
  `1857.621 ms`
- `parser-numbers-4000`: our parser `1738.543 ms`, `Utf8JsonReader`
  `996.058 ms`
- `parser-flat-objects-400`: our parser `1084.619 ms`, `Utf8JsonReader`
  `481.618 ms`

These diagnostics make the next target clearer than the broader scenarios did.

Current ranking of parser losses:

1. flat object traversal and repeated property-loop overhead
2. numeric token scanning
3. escaped string scanning

So the first parser optimization pass should focus on:

- object and array loop overhead
- repeated whitespace skipping around separators
- numeric token scanning cost in `numberToken`

String work still matters, but it is not the best first target from the
current measurements.

## Accepted parser changes

### Separator and whitespace loop cleanup

The first accepted parser-core optimization did two things:

- added a cheap fast path in `skipWhitespace` when the current byte is already
  non-whitespace
- centralized comma-or-close handling for object and array loops so repeated
  separator checks do less duplicated work

Measured effect on the parser diagnostics:

- `parser-flat-objects-400`: `1084.619 ms` -> `945.349 ms`
- `parser-numbers-4000`: `1738.543 ms` -> `1620.375 ms`

This is not enough to close the gap to `Utf8JsonReader`, but it is a clear
directional win and worth keeping.

### Colon fast path in object property loops

The second accepted optimization adds a direct colon fast path for object
properties and only falls back to whitespace-tolerant handling when needed.

This targets the common authored-contract shape:

```json
{"field":"value"}
```

instead of paying the general whitespace scan before every colon.

Measured effect:

- `person-batch-25-unknown-fields`: `775.955 ms` -> `694.389 ms`
- `telemetry-500`: `5663.483 ms` -> `4240.830 ms`

This is the strongest production JSON decode win so far.

### Record decoder raw-key and colon cleanup

The next accepted pass moved the same style of fast-path work into the compiled
record decoder:

- skip the colon whitespace scan when the colon is already the next byte
- stop rescanning raw property-name bytes for escapes when `stringRaw` already
  told us the key had no escapes

Measured effect:

- `person-batch-25-unknown-fields`: `694.389 ms` -> `638.155 ms`
- `person-batch-250`: `1607.277 ms` -> `1393.216 ms`
- `telemetry-500`: `4240.830 ms` -> `4205.965 ms`

This is a solid follow-up win, especially on normal record-heavy payloads.

## Failed experiments

### `numberToken` digit-scan extraction

A follow-up attempt factored repeated digit loops in `numberToken` into a small
helper.

That change lost:

- numeric diagnostic regressed instead of improving
- flat-object diagnostic also gave back part of the earlier gain

Conclusion:

- do not assume cleaner-looking helpers are free in the parser hot path
- keep future numeric parsing changes benchmarked in isolation

### `stringRaw` search variants

Two `.NET`-only `stringRaw` experiments were tried and rejected:

- a broad `Array.IndexOf`-based search for the next quote or escape
- a bounded quote-first search that only looked for escapes before the next
  quote

Why they were rejected:

- both hurt the top-priority flat-object case
- one version regressed very badly
- the quote-first version helped escaped strings but still lost on the more
  important object-traversal path

Conclusion:

- `stringRaw` is still interesting, but the obvious library-search-based
  approaches are not good enough
- any future `stringRaw` work should be treated as its own measured experiment,
  not bundled into larger parser changes

### List and array separator helper reuse

A follow-up attempt reused the shared separator helper in the compiled `List`
and `Array` decoders.

Why it was rejected:

- it regressed against the new record-decoder baseline
- `person-batch-250` and `telemetry-500` both gave back part of the earlier
  gain
- `escaped-articles-20` stayed roughly flat, so the trade was not worth it

Conclusion:

- the collection decode loop has different enough behavior that the generic
  separator helper is not automatically a win there
- future list/array work should be measured separately from object-loop work

## Measurement discipline note

The benchmark runner should be treated as sequential when comparing close
results.

Running multiple `dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner`
commands in parallel can introduce build-output file contention and noisy
numbers. The accepted comparisons above were rerun sequentially before deciding
to keep or reject a change.
