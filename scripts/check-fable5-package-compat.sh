#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tool_dir="/tmp/codecmapper-fable5-package-tool"
out_dir="/tmp/codecmapper-fable5-package-check"
dotnet_root="$(dirname "$(command -v dotnet)")"
package_version="0.1.0-local-ci"
package_feed="$repo_root/src/CodecMapper/bin/Release"
package_project="$repo_root/tests/CodecMapper.FablePackageTests/CodecMapper.FablePackageTests.fsproj"
package_project_backup="/tmp/codecmapper-fable5-package-tests.fsproj.bak"
package_nuget_config="$repo_root/tests/CodecMapper.FablePackageTests/NuGet.Config"

cd "$repo_root"

cleanup() {
    rm -f "$package_nuget_config"

    if [[ -f "$package_project_backup" ]]; then
        cp "$package_project_backup" "$package_project"
        rm -f "$package_project_backup"
    fi
}

trap cleanup EXIT

rm -rf "$tool_dir" "$out_dir"
dotnet pack src/CodecMapper/CodecMapper.fsproj -p:Version="$package_version" --nologo -v minimal
dotnet tool install fable --version 5.0.0-rc.2 --tool-path "$tool_dir"

cp "$package_project" "$package_project_backup"

sed \
    -e "s#Version=\"[^\"]*\"#Version=\"$package_version\"#" \
    -e '/<CodecMapperPackageVersion /d' \
    "$package_project_backup" \
    >"$package_project"

cat >"$package_nuget_config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$package_feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

DOTNET_ROOT="$dotnet_root" PATH="$dotnet_root:$PATH" \
    "$tool_dir/fable" tests/CodecMapper.FablePackageTests -o "$out_dir" --noRestore --silent
node "$out_dir/Program.js"
