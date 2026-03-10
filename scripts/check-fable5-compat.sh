#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tool_dir="/tmp/codecmapper-fable5-tool"
out_dir="/tmp/codecmapper-fable5-check"
dotnet_root="$(dirname "$(command -v dotnet)")"

cd "$repo_root"

rm -rf "$tool_dir" "$out_dir"

dotnet tool install fable --version 5.0.0-rc.2 --tool-path "$tool_dir"

DOTNET_ROOT="$dotnet_root" PATH="$dotnet_root:$PATH" \
    "$tool_dir/fable" tests/CodecMapper.FableTests -o "$out_dir" --noRestore --silent
