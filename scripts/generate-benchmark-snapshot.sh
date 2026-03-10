#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runner_project="benchmarks/CodecMapper.Benchmarks.Runner/CodecMapper.Benchmarks.Runner.fsproj"
tmp_output="$(mktemp "$repo_root/.benchmark-output.XXXXXX")"
tmp_snapshot="$(mktemp "$repo_root/.benchmark-snapshot.XXXXXX")"
tmp_readme="$(mktemp "$repo_root/.benchmark-readme.XXXXXX")"
write_readme=true

if [[ "${1:-}" == "--stdout-only" ]]; then
    write_readme=false
elif [[ $# -gt 0 ]]; then
    echo "Usage: $0 [--stdout-only]" >&2
    exit 1
fi

cleanup() {
    rm -f "$tmp_output" "$tmp_snapshot" "$tmp_readme"
}

trap cleanup EXIT

cd "$repo_root"

dotnet run -c Release --project "$runner_project" >"$tmp_output"

snapshot_date="$(date '+%B %-d, %Y')"
runner_command="dotnet run -c Release --project $runner_project"

awk -v snapshot_date="$snapshot_date" -v runner_command="$runner_command" '
function trim(value) {
    sub(/^[[:space:]]+/, "", value)
    sub(/[[:space:]]+$/, "", value)
    return value
}

function label_for(name) {
    if (name == "CodecMapper serialize") return "CodecMapper"
    if (name == "STJ serialize") return "System.Text.Json"
    if (name == "Newtonsoft serialize") return "Newtonsoft.Json"
    if (name == "CodecMapper deserialize bytes") return "CodecMapper deserialize bytes"
    if (name == "STJ deserialize") return "System.Text.Json"
    if (name == "Newtonsoft deserialize") return "Newtonsoft.Json"
    return name
}

/\|/ && /ns\/op/ && /B\/op/ {
    split($0, parts, "|")
    name = trim(parts[1])
    ns = trim(parts[2])
    sub(/ ns\/op$/, "", ns)
    alloc = trim(parts[3])
    sub(/ B\/op$/, "", alloc)

    if (index(name, "deserialize") > 0) {
        decode_names[++decode_count] = label_for(name)
        decode_ns[decode_count] = ns
        decode_alloc[decode_count] = alloc
    } else if (index(name, "serialize") > 0) {
        encode_names[++encode_count] = label_for(name)
        encode_ns[encode_count] = ns
        encode_alloc[encode_count] = alloc
    }
}

END {
    for (i = 1; i <= encode_count; i++) {
        for (j = i + 1; j <= encode_count; j++) {
            if ((encode_ns[j] + 0.0) < (encode_ns[i] + 0.0)) {
                tmp_name = encode_names[i]
                tmp_ns = encode_ns[i]
                tmp_alloc = encode_alloc[i]
                encode_names[i] = encode_names[j]
                encode_ns[i] = encode_ns[j]
                encode_alloc[i] = encode_alloc[j]
                encode_names[j] = tmp_name
                encode_ns[j] = tmp_ns
                encode_alloc[j] = tmp_alloc
            }
        }
    }

    for (i = 1; i <= decode_count; i++) {
        for (j = i + 1; j <= decode_count; j++) {
            if ((decode_ns[j] + 0.0) < (decode_ns[i] + 0.0)) {
                tmp_name = decode_names[i]
                tmp_ns = decode_ns[i]
                tmp_alloc = decode_alloc[i]
                decode_names[i] = decode_names[j]
                decode_ns[i] = decode_ns[j]
                decode_alloc[i] = decode_alloc[j]
                decode_names[j] = tmp_name
                decode_ns[j] = tmp_ns
                decode_alloc[j] = tmp_alloc
            }
        }
    }

    print "Latest local manual snapshot, measured on " snapshot_date "."
    print ""
    print "Encode, fastest to slowest:"
    print ""
    print "| Library | Mean ns/op | Mean B/op |"
    print "| --- | ---: | ---: |"

    for (i = 1; i <= encode_count; i++) {
        print "| " encode_names[i] " | " encode_ns[i] " | " encode_alloc[i] " |"
    }

    print ""
    print "Decode, fastest to slowest:"
    print ""
    print "| Library | Mean ns/op | Mean B/op |"
    print "| --- | ---: | ---: |"

    for (i = 1; i <= decode_count; i++) {
        print "| " decode_names[i] " | " decode_ns[i] " | " decode_alloc[i] " |"
    }

    print ""
    print "These numbers came from:"
    print ""
    print "```bash"
    print runner_command
    print "```"
}
' "$tmp_output" >"$tmp_snapshot"

if [[ "$write_readme" == true ]]; then
    awk -v snapshot_file="$tmp_snapshot" '
    BEGIN {
        while ((getline line < snapshot_file) > 0) {
            snapshot = snapshot line ORS
        }
        close(snapshot_file)
        in_block = 0
    }

    /<!-- benchmark-snapshot:start -->/ {
        print
        printf "%s", snapshot
        in_block = 1
        next
    }

    /<!-- benchmark-snapshot:end -->/ {
        in_block = 0
        print
        next
    }

    !in_block {
        print
    }
    ' README.md >"$tmp_readme"

    mv "$tmp_readme" README.md
fi

cat "$tmp_snapshot"
