#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "${ROOT_DIR}"

dotnet fsdocs build \
    --input "${ROOT_DIR}/docs" \
    --output "${ROOT_DIR}/output" \
    --clean \
    --strict \
    --sourcefolder "${ROOT_DIR}"
