#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out_dir="/tmp/codecmapper-fable-check"

cd "$repo_root"

rm -rf "$out_dir"
dotnet tool run fable -- tests/CodecMapper.FableTests -o "$out_dir" --noRestore --silent
