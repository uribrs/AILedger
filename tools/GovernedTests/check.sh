#!/bin/sh
# Regression probes for the runner itself. Deliberately failing fixtures are not product tests.
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
repo=$(CDPATH= cd -- "$root/../.." && pwd -P)
scratch_parent=$(CDPATH= cd -- "${TMPDIR:-/tmp}" && pwd -P)
case "$scratch_parent/" in
    "$repo/"*) echo 'TMPDIR must be outside the repository.' >&2; exit 2 ;;
esac
scratch=$(mktemp -d "$scratch_parent/ailedger-runner-check.XXXXXX")
echo "Runner check logs: $scratch"
for project in Runner/GovernedTests Fixtures/GovernedTests.Fixtures; do
    if ! dotnet build "$root/$project.csproj" --artifacts-path "$scratch" -m:1 -p:NuGetAudit=false > "$scratch/build.log" 2>&1; then
        dd if="$scratch/build.log" bs=65536 count=1 2>/dev/null >&2
        exit 1
    fi
done
directory="$scratch/bin/GovernedTests.Fixtures/debug"
cp "$scratch/bin/GovernedTests/debug/GovernedTests.dll" "$directory/GovernedTests.dll"
check() {
    actual=0
    dotnet exec --depsfile "$directory/GovernedTests.Fixtures.deps.json" \
        --runtimeconfig "$directory/GovernedTests.Fixtures.runtimeconfig.json" \
        "$directory/GovernedTests.dll" "$directory/GovernedTests.Fixtures.dll" "$1" \
        > "$scratch/$1.log" 2>&1 || actual=$?
    if [ "$actual" -ne "$2" ] || ! grep -F "$3" "$scratch/$1.log"; then
        dd if="$scratch/$1.log" bs=65536 count=1 2>/dev/null >&2
        echo "$1: expected exit $2, got $actual" >&2
        exit 1
    fi
    echo "$1: expected exit $actual confirmed"
}
check LifecycleCases 0 'total=7, failed=0, skipped=1, errors=0'
check BriefingCases 0 'total=2, failed=0, skipped=0, errors=0'
check FailingCases 1 'total=1, failed=1, skipped=0, errors=0'
check CleanupCases 1 'total=1, failed=0, skipped=0, errors=1'
check NO_SUCH_CASE 2 'no tests matched'
