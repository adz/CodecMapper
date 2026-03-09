#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FSDOCS_DLL="${HOME}/.nuget/packages/fsdocs-tool/21.0.0/tools/net8.0/any/fsdocs.dll"

if [[ ! -f "${FSDOCS_DLL}" ]]; then
    echo "fsdocs-tool 21.0.0 is not available in the local NuGet cache." >&2
    echo "Run 'dotnet tool restore' or install fsdocs-tool before generating docs." >&2
    exit 1
fi

cd "${ROOT_DIR}"

dotnet "${FSDOCS_DLL}" build \
    --input "${ROOT_DIR}/docs" \
    --projects "${ROOT_DIR}/src/CodecMapper/CodecMapper.fsproj" \
    --output "${ROOT_DIR}/output" \
    --clean \
    --strict \
    --mdcomments \
    --sourcefolder "${ROOT_DIR}" \
    --parameters root ./ fsdocs-logo-src img/logo.png fsdocs-logo-link index.html fsdocs-collection-name CodecMapper
