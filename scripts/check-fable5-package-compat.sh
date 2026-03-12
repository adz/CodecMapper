#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tool_dir="/tmp/codecmapper-fable5-package-tool"
out_dir="/tmp/codecmapper-fable5-package-check"
temp_project_dir="$repo_root/tests/.tmp-codecmapper-fable5-package-tests"
temp_package_dir="$temp_project_dir/package"
dotnet_root="$(dirname "$(command -v dotnet)")"
package_version="0.1.0-local-ci"
nupkg_path="$repo_root/src/CodecMapper/bin/Release/CodecMapper.$package_version.nupkg"

cd "$repo_root"

cleanup() {
    rm -rf "$temp_project_dir"
}

trap cleanup EXIT

rm -rf "$tool_dir" "$out_dir" "$temp_project_dir"
dotnet pack src/CodecMapper/CodecMapper.fsproj -p:Version="$package_version" --nologo -v minimal
dotnet tool install fable --version 5.0.0-rc.2 --tool-path "$tool_dir"

mkdir -p "$temp_package_dir"
unzip -q -o "$nupkg_path" -d "$temp_package_dir"
cp tests/CodecMapper.FablePackageTests/Program.fs "$temp_project_dir/Program.fs"

cat >"$temp_project_dir/CodecMapper.FablePackageTests.fsproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="package/fable/Core.fs" />
    <Compile Include="package/fable/Schema.fs" />
    <Compile Include="package/fable/Json.fs" />
    <Compile Include="package/fable/JsonSchema.fs" />
    <Compile Include="package/fable/Xml.fs" />
    <Compile Include="package/fable/KeyValue.fs" />
    <Compile Include="package/fable/Yaml.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>

</Project>
EOF

DOTNET_ROOT="$dotnet_root" PATH="$dotnet_root:$PATH" \
    "$tool_dir/fable" "$temp_project_dir" -o "$out_dir" --noRestore --silent
node "$out_dir/Program.js"
