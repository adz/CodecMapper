#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runner_project="benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj"
tmp_output="$(mktemp "$repo_root/.benchmark-output.XXXXXX")"
tmp_snapshot="$(mktemp "$repo_root/.benchmark-snapshot.XXXXXX")"
tmp_readme="$(mktemp "$repo_root/.benchmark-readme.XXXXXX")"
write_readme=true

if [[ "${1:-}" == "--stdout-only" ]]; then
    write_readme=false
elif [[ $# -gt 0 ]]; then
    echo "Usage: $0 [--stdout-only]" >&2
    exit 1
fi

cleanup() {
    rm -f "$tmp_output" "$tmp_snapshot" "$tmp_readme"
}

trap cleanup EXIT

cd "$repo_root"

dotnet run -c Release --project "$runner_project" >"$tmp_output"

snapshot_date="$(date '+%B %-d, %Y')"
runner_command="dotnet run -c Release --project $runner_project"
{
    printf 'Latest local manual scenario-matrix snapshot, measured on %s.\n\n' "$snapshot_date"
    printf 'These numbers came from:\n\n'
    printf '```bash\n%s\n```\n\n' "$runner_command"
    printf '```text\n'
    cat "$tmp_output"
    printf '```\n'
} >"$tmp_snapshot"

if [[ "$write_readme" == true ]]; then
    awk -v snapshot_file="$tmp_snapshot" '
    BEGIN {
        while ((getline line < snapshot_file) > 0) {
            snapshot = snapshot line ORS
        }
        close(snapshot_file)
        in_block = 0
    }

    /<!-- benchmark-snapshot:start -->/ {
        print
        printf "%s", snapshot
        in_block = 1
        next
    }

    /<!-- benchmark-snapshot:end -->/ {
        in_block = 0
        print
        next
    }

    !in_block {
        print
    }
    ' README.md >"$tmp_readme"

    mv "$tmp_readme" README.md
fi

cat "$tmp_snapshot"
