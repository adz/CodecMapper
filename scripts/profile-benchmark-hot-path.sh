#!/usr/bin/env bash

set -euo pipefail

operation="${1:-codecmapper-serialize}"
iterations="${2:-200000}"
scenario_or_records="${3:-person-batch-25}"

project="benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj"
sanitized_target="${scenario_or_records//[^A-Za-z0-9._-]/_}"
output_dir=".artifacts/profiling/${operation}-${sanitized_target}-${iterations}iters"
runner_dll="benchmarks/CodecMapper.Benchmarks.Runner/bin/Release/net10.0/CodecMapper.Benchmarks.Runner.dll"

mkdir -p "$output_dir"

dotnet build "$project" -c Release --nologo -v minimal >/dev/null

command=(
  dotnet "$runner_dll"
  profile "$operation"
  --iterations "$iterations"
)

if [[ "$scenario_or_records" =~ ^[0-9]+$ ]]; then
  command+=(
    --records "$scenario_or_records"
  )
  target_description="$scenario_or_records records (legacy person batch)"
else
  command+=(
    --scenario "$scenario_or_records"
  )
  target_description="scenario $scenario_or_records"
fi

perf_env=(
  DOTNET_PerfMapEnabled=3
  COMPlus_PerfMapEnabled=3
)

printf 'Profiling %s with %s for %s iterations\n' "$operation" "$target_description" "$iterations"
printf 'Artifacts: %s\n' "$output_dir"

printf '%s\n' "${command[@]}" > "$output_dir/command.txt"

env "${perf_env[@]}" perf stat -r 5 -o "$output_dir/perf.stat.txt" -- "${command[@]}"
env "${perf_env[@]}" perf record -k 1 -F 399 --call-graph dwarf -o "$output_dir/perf.data" -- "${command[@]}"
perf inject --jit -i "$output_dir/perf.data" -o "$output_dir/perf.jitted.data"
perf report --stdio -i "$output_dir/perf.jitted.data" > "$output_dir/perf.report.txt"

printf 'Wrote %s\n' "$output_dir/perf.stat.txt"
printf 'Wrote %s\n' "$output_dir/perf.data"
printf 'Wrote %s\n' "$output_dir/perf.jitted.data"
printf 'Wrote %s\n' "$output_dir/perf.report.txt"
