#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "${ROOT_DIR}"

rm -rf "${ROOT_DIR}/.fsdocs/cache" "${ROOT_DIR}/output"

dotnet build src/CodecMapper/CodecMapper.fsproj --nologo -v minimal
dotnet build src/CodecMapper.Bridge/CodecMapper.Bridge.fsproj --nologo -v minimal

dotnet fsdocs build \
    --input "${ROOT_DIR}/docs" \
    --output "${ROOT_DIR}/output" \
    --clean \
    --strict \
    --sourcefolder "${ROOT_DIR}"

if rg -n 'https://github.com/adz/CodecMapper/(content|logo-mark\.png|index\.json|reference/)' "${ROOT_DIR}/output" > /dev/null; then
    echo "Generated docs still contain github.com asset roots" >&2
    exit 1
fi
