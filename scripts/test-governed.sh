#!/bin/sh
# Keep all build writes outside the checkout; run real xUnit without VSTest's IPC socket.
set -eu

suite=${1:-all}
if [ "$#" -gt 0 ]; then shift; fi
case "$suite" in
    all) suites='AILedger.Memory.Tests AILedger.Tests' ;;
    AILedger.Tests|AILedger.Memory.Tests) suites=$suite ;;
    *) echo 'Usage: test-governed.sh [all|AILedger.Tests|AILedger.Memory.Tests] [name-contains]' >&2; exit 2 ;;
esac
if [ "$#" -gt 1 ]; then echo 'Only one name filter is supported.' >&2; exit 2; fi

repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd -P)
cd "$repo"
scratch_parent=$(CDPATH= cd -- "${TMPDIR:-/tmp}" && pwd -P)
case "$scratch_parent/" in
    "$repo/"*) echo 'TMPDIR must be outside the repository.' >&2; exit 2 ;;
esac
scratch=$(mktemp -d "$scratch_parent/ailedger-tests.XXXXXX")
echo "Build artifacts and logs: $scratch"

build() {
    if ! dotnet build "$1" --artifacts-path "$scratch/artifacts" -m:1 -p:NuGetAudit=false > "$scratch/$2.log" 2>&1; then
        dd if="$scratch/$2.log" bs=65536 count=1 2>/dev/null >&2
        echo "Build failed; full log: $scratch/$2.log" >&2
        exit 1
    fi
}

build "$repo/AILedger.sln" solution-build
build "$repo/tools/GovernedTests/Runner/GovernedTests.csproj" runner-build
runner="$scratch/artifacts/bin/GovernedTests/debug/GovernedTests.dll"
# The smaller memory suite exposes application-data permission requirements before the long suite.
for suite in $suites; do
    directory="$scratch/artifacts/bin/$suite/debug"
    cp "$runner" "$directory/GovernedTests.dll"
    # Use the suite's dependency manifest and output directory, including project/native assets.
    if dotnet exec --depsfile "$directory/$suite.deps.json" \
        --runtimeconfig "$directory/$suite.runtimeconfig.json" \
        "$directory/GovernedTests.dll" "$directory/$suite.dll" "$@" > "$scratch/$suite.log" 2>&1; then
        grep "^$suite: total=" "$scratch/$suite.log"
    else
        dd if="$scratch/$suite.log" bs=65536 count=1 2>/dev/null >&2
        echo "Suite failed; full log: $scratch/$suite.log" >&2
        exit 1
    fi
done
exit 0
