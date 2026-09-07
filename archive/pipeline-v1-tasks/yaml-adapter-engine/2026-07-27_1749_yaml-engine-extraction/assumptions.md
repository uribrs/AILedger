# Assumptions

## A1 — Test project reorganises to mirror concepts, without the layer split
**Status: OPEN (proceeding on default)**
Tests are not a concept with contracts and logic. The test tree mirrors the nine
concept folders (`Authentication/`, `Pagination/`, …) flat, with no
`Contracts/`/`Logic/` subdivision. Existing test folders map: `Auth`→`Authentication`,
`Retry`→`Resilience`, `Loader`+`Validation`→`Definition`, `Engine`→`Execution`.
*Reject if the operator wants the test tree left exactly as-is in phase 2.*

## A2 — Engine assembly loses its collector identity
**Status: VALIDATED**
In the monorepo the engine sits under `Collectors/` and therefore inherits
`Collectors/Directory.Build.props`: `Version = CollectorVersion (5.1.0)`,
`AssemblyVersion 5.1.0.7`, and an `AssemblyMetadata("IsCollector","true")` attribute.
In the new repo it gets `VersionPrefix 1.0.0` and no collector metadata. This is
intended — the engine is a library, not a collector — but it *is* an observable
change in assembly metadata and version.

## A3 — No `InternalsVisibleTo` needed for phases 1–2
**Status: VALIDATED**
Confirmed by inspection: the engine csproj has no `InternalsVisibleTo`, and
`YamlToJsonNodeEdgeTests.cs` documents in its header that it exercises the internal
`YamlToJsonNode` indirectly through the public surface precisely because no
`InternalsVisibleTo` exists. Phase 3 (trimming the public surface) will need one;
phases 1–2 must not add one.

## A4 — The rule-0 guard does not come with the engine
**Status: VALIDATED — creates a gap**
`EngineIsolationTests.cs` — the test asserting the engine has no `Cymulate.*`
dependencies — lives in `Cymulate.Integration.Adapters.Collectors.YamlCollector.Test`,
the *adapter's* test project, and stays in the monorepo. After extraction the new
repo has **no automated guard for its most load-bearing invariant**.
*Recommendation: port an equivalent assembly-reference assertion into the new test
project. Small, directly protects rule 0. Flagged for operator approval as it is
technically new code rather than a move.*

## A5 — Test project renamed `.Test` → `.Tests` in phase 2
**Status: OPEN (proceeding on default)**
The source project is `Cymulate.Integration.Yaml.Engine.Test` (singular). `README.md`
already documents `Cymulate.Integration.Yaml.Engine.Tests` (plural), which is also the
more common convention. Phase 1 moves it verbatim as `.Test` to keep the move clean;
phase 2 renames to `.Tests`. Nothing depends on the name.

## A6 — `ai/active/` is gitignored, matching the monorepo
**Status: VALIDATED**
The monorepo ignores `ai/active/` and `ai/done/`. The new repo's `.gitignore` does not
yet. It should be updated so task state is not committed.

## A7 — Baseline test count is measured, not assumed
**Status: OPEN — resolved by step S0**
The gate "same test count as the monorepo" is only meaningful against a recorded
number. S0 runs `dotnet test` on the monorepo engine test project and records the
exact count before anything moves. If the monorepo baseline is not green, stop and
report — extraction cannot be validated against a broken baseline.

## A8 — Directory.Packages.props already covers every dependency
**Status: OPEN — verified during S1/S2**
Versions were transcribed from the monorepo for the five engine packages and seven
test packages. If restore reports a missing `PackageVersion`, that means a transitive
or overlooked dependency exists — stop and report rather than inventing a version.
