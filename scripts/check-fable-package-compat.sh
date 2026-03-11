#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out_dir="/tmp/codecmapper-fable-package-check"
package_cache="$repo_root/tests/CodecMapper.FablePackageTests/.packages"

cd "$repo_root"

rm -rf "$out_dir" "$package_cache"
dotnet pack src/CodecMapper/CodecMapper.fsproj --nologo -v minimal
dotnet tool run fable -- tests/CodecMapper.FablePackageTests -o "$out_dir" --noRestore --silent
node "$out_dir/Program.js"
