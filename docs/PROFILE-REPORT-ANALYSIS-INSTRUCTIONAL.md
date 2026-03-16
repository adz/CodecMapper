# Profile Report Analysis Instructional

This note explains how to read the current `perf`-based hotspot reports for
`CodecMapper`, using the first `Task 42` profiling pass as the concrete
example.

The goal is not to produce a perfect low-level CPU narrative on day one. The
goal is to decide whether the next performance experiment should focus on:

- erased `obj` traffic
- allocation churn
- field lookup and object assembly
- string and numeric parsing
- or something else entirely

## Why this comes first

The non-erased typed-codec task is high risk. It is easy to drift into a broad
runtime rewrite before proving that the erased path is actually the dominant
cost.

So the first gate is:

1. profile the current weak scenarios
2. identify the main cost buckets
3. choose one narrow prototype lane
4. only then change architecture

## Commands used in this pass

This pass used the existing profiling harness exactly as-is:

```bash
bash scripts/profile-benchmark-hot-path.sh codecmapper-deserialize-bytes 50000 escaped-articles-20
bash scripts/profile-benchmark-hot-path.sh codecmapper-deserialize-bytes 50000 telemetry-500
```

Those commands write artifacts under:

```text
.artifacts/profiling/
```

In this run the relevant directories were:

```text
.artifacts/profiling/codecmapper-deserialize-bytes-escaped-articles-20-50000iters
.artifacts/profiling/codecmapper-deserialize-bytes-telemetry-500-50000iters
```

Each directory contains:

- `command.txt`
- `perf.stat.txt`
- `perf.data`
- `perf.jitted.data`
- `perf.report.txt`

## What each artifact tells you

### `perf.stat.txt`

This is the first thing to read.

It answers:

- how long the operation took
- whether the run was stable across repeats
- how many instructions retired
- how branchy the workload is
- whether the CPU was mostly busy or stalled

For this pass:

- `escaped-articles-20` took about `4.69s` total for the configured run
- `telemetry-500` took about `37.24s` total for the configured run

The telemetry case is much more expensive, which matches the benchmark story
already seen in the README and benchmark page.

The two useful top-line counters here were:

- `instructions`
- `cycles`

Those let you compute whether the CPU is spending its time mostly doing useful
work or mostly waiting. In these runs the `insn per cycle` values were not
terrible:

- about `2.14` for `escaped-articles-20`
- about `2.45` for `telemetry-500`

That does **not** look like a workload dominated by branch misprediction or
front-end collapse. It looks more like a workload doing a lot of actual work,
with some memory movement and allocation overhead mixed in.

### `perf.report.txt`

This is the second thing to read.

It answers:

- where samples actually landed
- whether time is in library code, JITted runtime code, GC helpers, libc
  memory routines, or project code
- whether the costs look dominated by parsing, copying, allocation, or control
  flow

## How I read the report

I read the report in layers.

### 1. Look for obvious non-project buckets first

In both reports, some of the biggest early buckets were:

- `libcoreclr.so`
- `memfd:doublemapper (deleted)`
- `libc.so.6`
- `__memset_avx2_unaligned_erms`

That matters immediately.

It means the hottest work is not obviously showing up as clean named
`CodecMapper.Json...` functions at the top. Instead, a lot of samples are in:

- runtime helper code
- JITted code blobs
- raw memory clearing/copying

That usually points to:

- allocation churn
- object/array initialization
- buffer clearing
- or other runtime support work around the decode loop

It is **not** yet evidence that erased `obj` dispatch is the dominant cost.

### 2. Treat `__memset...` as a strong signal

In both runs, `__memset_avx2_unaligned_erms` showed up near the top.

That is one of the clearest signals in this first pass.

Interpretation:

- some hot decode paths are zeroing or clearing memory a lot
- that often means new arrays, object buffers, or other temporary storage are
  being allocated and initialized frequently
- that is especially plausible in the current JSON record decode path because
  it rents and fills object arrays and repeatedly materializes intermediate
  payload structures

So the first profiling read says:

> allocation and buffer initialization pressure is definitely part of the
> problem

### 3. Be careful with unresolved JIT blobs

The reports also include entries like:

```text
memfd:doublemapper (deleted)
```

That means some JITted code was sampled, but the report is not yet giving us a
friendly managed-symbol name for every hot frame.

Interpretation:

- there is real managed hot code here
- but this report alone does not yet tell us exactly which `CodecMapper`
  function owns each sample

So do **not** overclaim from those entries.

At this stage they support a directional conclusion:

- managed decode code is hot
- runtime helper overhead is also hot
- symbol resolution is not yet sharp enough to blame one exact function

### 4. Compare the scenario shape to the hotspot shape

This is where the scenario choice matters.

#### `escaped-articles-20`

This workload is string-heavy.

If string parsing and string allocation are expensive, you expect:

- lots of time in runtime helpers
- string-related JITted code
- some memory movement or clearing

That is broadly consistent with the report.

#### `telemetry-500`

This workload is wide and numeric-heavy.

If numeric parsing plus wide object assembly are expensive, you expect:

- more total work than the string case
- repeated per-field decode overhead
- more object-buffer and record-assembly churn
- more runtime helper cost if temporary arrays or object slots are involved

That is also broadly consistent with the report.

So the current data says:

- `telemetry-500` is not just “strings are slow”
- the wider decode pipeline itself has substantial overhead

## What I infer from this pass

This pass supports these conclusions.

### Conclusion 1: allocation and initialization pressure is real

The repeated presence of `__memset...` and runtime-helper-heavy top buckets is
the clearest signal in this data.

That means any prototype that reduces:

- temporary object arrays
- repeated buffer clearing
- intermediate payload materialization
- generic object assembly

is worth testing.

### Conclusion 2: a typed prototype should start on JSON record decode

This first pass does **not** justify starting with unions, XML, YAML, or
KeyValue.

Why:

- the release-facing pain is already visible in JSON decode
- the wide-record scenario is clearly expensive
- JSON record decode is where typed field storage and typed constructor
  assembly can plausibly remove object-array churn

So the first prototype should be:

- JSON only
- decode only
- record-heavy path first

### Conclusion 3: the first prototype should target object-array churn, not “all erasure everywhere”

The profiling does not yet say:

> the entire erased schema model must be replaced

It says something narrower:

> the current decode path likely pays too much for erased intermediate storage
> and runtime helper work during record assembly

That is a much better first prototype target.

## What I would do next

The next experiment should stay narrow.

### Prototype scope

- keep the public DSL unchanged
- keep the existing compiler path intact
- add one internal alternate JSON record-decode lane
- target records composed mostly of primitives and strings
- avoid unions, options, maps, and secondary formats in the first pass

### What to try removing

- `obj[]` staging in record decode
- repeated generic object-slot writes where possible
- unnecessary clearing or reinitialization work
- some boxing/unboxing on the hottest field path

### What to measure afterward

Rerun:

- `escaped-articles-20`
- `telemetry-500`
- `person-batch-250`
- `person-batch-25-unknown-fields`
- `small-message`

and ask:

- did decode improve materially on at least two target scenarios?
- did allocations drop?
- did `small-message` stay stable?
- did AOT and Fable stay green?

## What I would not conclude yet

This first pass is **not** enough to conclude:

- that erased typing is the only or main bottleneck
- that string parsing is solved by a typed path
- that unions should be part of the first typed prototype
- that the whole runtime should be rewritten

It is enough to conclude:

- record-decode object assembly is a credible first target
- allocation and memory-initialization pressure are clearly part of the cost
- the next step should be a narrow typed JSON record-decode experiment

## Short version

How I interpret this profiling pass:

- `telemetry-500` confirms the wide-record decode path is expensive
- `escaped-articles-20` confirms the string-heavy path is still weak
- both reports show meaningful runtime-helper and memory-clear cost
- that points first toward reducing intermediate object storage and decode-path
  churn, not toward rewriting every schema/backend path

So the disciplined next move is:

1. prototype a typed JSON record-decode lane
2. compare it against the current erased path
3. only expand further if the benchmark wins are clear

## First typed experiment result

The first experiment followed that plan narrowly:

- keep the production JSON runtime unchanged
- keep using the handwritten `JsonSource` parser
- add a benchmark-only typed decode lane for the existing benchmark record
  shapes
- compare that lane directly against `codecmapper-deserialize-bytes`

### Commands used for the first comparison

These focused checks were run after the benchmark-only typed lane compiled:

```bash
dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile codecmapper-deserialize-bytes --scenario small-message --iterations 1000
dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile typed-experiment-deserialize-bytes --scenario small-message --iterations 1000

dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile codecmapper-deserialize-bytes --scenario person-batch-250 --iterations 2000
dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile typed-experiment-deserialize-bytes --scenario person-batch-250 --iterations 2000

dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile codecmapper-deserialize-bytes --scenario escaped-articles-20 --iterations 2000
dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile typed-experiment-deserialize-bytes --scenario escaped-articles-20 --iterations 2000

dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile codecmapper-deserialize-bytes --scenario telemetry-500 --iterations 2000
dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile typed-experiment-deserialize-bytes --scenario telemetry-500 --iterations 2000

dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile codecmapper-deserialize-bytes --scenario person-batch-25-unknown-fields --iterations 5000
dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile typed-experiment-deserialize-bytes --scenario person-batch-25-unknown-fields --iterations 5000
```

### What the first experiment showed

Focused profile results:

- `small-message`: `3.272 ms` -> `2.450 ms` for `1000` iterations, about
  `25%` faster
- `person-batch-250`: `1741.971 ms` -> `1640.387 ms` for `2000` iterations,
  about `6%` faster
- `escaped-articles-20`: `754.797 ms` -> `733.219 ms` for `2000` iterations,
  about `3%` faster
- `telemetry-500`: `5663.483 ms` -> `5285.568 ms` for `2000` iterations,
  about `7%` faster
- `person-batch-25-unknown-fields`: `775.955 ms` -> `665.119 ms` for `5000`
  iterations, about `14%` faster

The manual summary run also showed lower allocations for the typed lane on the
same scenarios, for example:

- `small-message`: about `1048 B/op` -> `584 B/op`
- `person-batch-25`: about `36688 B/op` -> `20704 B/op`
- `person-batch-250`: about `367656 B/op` -> `209416 B/op`
- `escaped-articles-20`: about `129960 B/op` -> `95096 B/op`

### How to interpret those numbers

This is enough to conclude that the erased record-decode path is costing real
time and allocation.

It is not enough to conclude that a fully typed rewrite will automatically
close the entire gap to STJ.

What the result means:

- removing `obj[]` staging and reflective record assembly is directionally
  correct
- the gain is strongest where record assembly and unknown-field skipping are a
  larger part of the total work
- the gain is smaller when string parsing, float parsing, decimal parsing, and
  list construction still dominate the workload

So the current typed result is a **go** for continued investigation, but still
in a narrow lane.

### What I would do next after this result

- keep the experiment isolated from the production runtime for now
- generalize the typed record-decode lane beyond the hand-written benchmark
  shapes
- keep using the handwritten parser so parser-vs-runtime effects stay
  separated
- rerun the same scenario set after each generalization step
- only consider replacing the erased production path once the typed version is
  both simpler and still measurably faster

## Parser-only comparison

After the first typed experiment, the next question was narrower:

> is the handwritten parser itself faster than `Utf8JsonReader`, or are the
> main wins and losses happening after parsing?

To answer that, the benchmark runner now includes two pure scan operations:

- `our-parser-scan-bytes`
- `utf8jsonreader-scan-bytes`

Both operations walk the payload, hash structural events, and avoid object
construction so the comparison stays parser-heavy rather than decode-heavy.

### Commands used

```bash
dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile our-parser-scan-bytes --scenario small-message --iterations 5000
dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile utf8jsonreader-scan-bytes --scenario small-message --iterations 5000

dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile our-parser-scan-bytes --scenario person-batch-25-unknown-fields --iterations 5000
dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile utf8jsonreader-scan-bytes --scenario person-batch-25-unknown-fields --iterations 5000

dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile our-parser-scan-bytes --scenario person-batch-250 --iterations 1000
dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile utf8jsonreader-scan-bytes --scenario person-batch-250 --iterations 1000

dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile our-parser-scan-bytes --scenario escaped-articles-20 --iterations 3000
dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile utf8jsonreader-scan-bytes --scenario escaped-articles-20 --iterations 3000

dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile our-parser-scan-bytes --scenario telemetry-500 --iterations 1000
dotnet run --project benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj -- profile utf8jsonreader-scan-bytes --scenario telemetry-500 --iterations 1000
```

### Results

- `small-message`: our parser `9.219 ms`, `Utf8JsonReader` `13.455 ms`
- `person-batch-25-unknown-fields`: our parser `556.524 ms`,
  `Utf8JsonReader` `351.047 ms`
- `person-batch-250`: our parser `524.173 ms`, `Utf8JsonReader` `243.671 ms`
- `escaped-articles-20`: our parser `519.997 ms`, `Utf8JsonReader`
  `410.947 ms`
- `telemetry-500`: our parser `1658.459 ms`, `Utf8JsonReader` `607.815 ms`

### Interpretation

This is not a close overall parser race.

The handwritten parser wins on the tiny shallow object, but once the payloads
get wider or more numerous, `Utf8JsonReader` is clearly faster as a parser.

That matters because it rules out one comforting story:

> maybe our parser is already faster, and most of the remaining gap is only in
> object assembly

The current data does not support that.

Instead, the more accurate read is:

- our typed decode experiment proves the erased decode path is adding real
  overhead
- but our handwritten parser is also materially slower than
  `Utf8JsonReader` on realistic larger payloads

### What this means for the design choice

This result still does **not** mean the core runtime should switch to
`Utf8JsonReader`.

Why not:

- `Utf8JsonReader` is still a `ref struct`, which makes generic functional
  composition harder and more brittle
- the repo still values one portable conceptual runtime, including Fable
  compatibility
- the CPS experiment history already showed that a very fast `Utf8JsonReader`
  design can also become difficult to evolve

So the current design conclusion is:

- keep the handwritten parser as the strategic direction
- do not assume its current performance is good enough
- improve the handwritten parser and typed decode path in parallel
- only revisit a `Utf8JsonReader`-based core if the handwritten parser remains
  far behind after focused optimization
