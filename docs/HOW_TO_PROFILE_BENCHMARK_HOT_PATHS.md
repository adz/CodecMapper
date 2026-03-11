# How To Profile Benchmark Hot Paths

Use this guide when benchmark ratios point at a real hotspot and you need call-stack evidence before changing the runtime.

## What this workflow does

The repo ships a focused benchmark-runner profile mode plus a `perf` wrapper script:

```bash
bash scripts/profile-benchmark-hot-path.sh codecmapper-serialize 120000 person-batch-25
```

That command:

- runs one deterministic benchmark operation repeatedly in `Release`
- captures `perf stat` counters into `perf.stat.txt`
- captures sampled stacks into `perf.data`
- emits both `.NET` perf maps and JIT dump metadata so managed frames can be symbolized
- renders a text call-stack report into `perf.report.txt`

Artifacts land under `.artifacts/profiling/<operation>-<scenario-or-records>-<iterations>iters/`.

## Supported operations

- `codecmapper-serialize`
- `codecmapper-deserialize-bytes`
- `stj-serialize`
- `stj-deserialize`
- `newtonsoft-serialize`
- `newtonsoft-deserialize`

## Typical workflow

Profile the `CodecMapper` path first:

```bash
bash scripts/profile-benchmark-hot-path.sh codecmapper-serialize 120000 person-batch-25
bash scripts/profile-benchmark-hot-path.sh codecmapper-deserialize-bytes 40000 person-batch-25-unknown-fields
```

Then capture the `System.Text.Json` baseline on the same scenario:

```bash
bash scripts/profile-benchmark-hot-path.sh stj-serialize 120000 person-batch-25
bash scripts/profile-benchmark-hot-path.sh stj-deserialize 40000 person-batch-25-unknown-fields
```

Read `perf.stat.txt` for high-level counters and `perf.report.txt` for the hottest call paths.

## Notes

- The profile wrapper now defaults to the `person-batch-25` scenario from the shared benchmark matrix.
- Pass a scenario name such as `telemetry-500` or `escaped-articles-20` to profile one of the standard workloads.
- Passing a plain integer as the third argument still uses the legacy nested-record batch with `--records <n>`.
- The wrapper sets `DOTNET_PerfMapEnabled=3` and `COMPlus_PerfMapEnabled=3` so `perf inject --jit` has the metadata it needs for managed symbol names.
- If `perf record` is blocked by local kernel permissions, the script will fail before writing `perf.report.txt`. In that case, fix local `perf` permissions first and rerun the same command.
- Keep comparisons on the same power profile and CPU governor, otherwise the counter deltas are noisy enough to mislead.
