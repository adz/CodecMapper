#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$repo_root"

mapfile -t files < <(
    find src tests benchmarks \
        -path 'benchmarks/CodecMapper' -prune -o \
        -path '*/bin' -prune -o \
        -path '*/obj' -prune -o \
        -type f \( -name '*.fs' -o -name '*.fsi' -o -name '*.fsx' \) \
        -print \
        | sort
)

dotnet fantomas "${files[@]}"
