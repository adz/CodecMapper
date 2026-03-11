#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tool_dir="/tmp/codecmapper-fable5-package-tool"
out_dir="/tmp/codecmapper-fable5-package-check"
package_cache="$repo_root/tests/CodecMapper.FablePackageTests/.packages"
dotnet_root="$(dirname "$(command -v dotnet)")"

cd "$repo_root"

rm -rf "$tool_dir" "$out_dir" "$package_cache"
dotnet pack src/CodecMapper/CodecMapper.fsproj --nologo -v minimal
dotnet tool install fable --version 5.0.0-rc.2 --tool-path "$tool_dir"

DOTNET_ROOT="$dotnet_root" PATH="$dotnet_root:$PATH" \
    "$tool_dir/fable" tests/CodecMapper.FablePackageTests -o "$out_dir" --noRestore --silent
node "$out_dir/Program.js"
