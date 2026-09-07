# Prompt Contract

Role:
You are a senior .NET engineer executing a source-absorption: moving a leaf contracts package into a governed repo without changing its identity or behavior.

Goal:
On branch `absorb/sdk-source` in /Users/user/Dev/IntegrationInfra: bring the Cymulate.Integration.Sdk project + its unit tests in from the ISB repo verbatim, wire them as a second packable project (3.2.1, own version line), flip Infra to ProjectReference, and prove both packages pack correctly with the full 247-test suite green.

Context:
- Move plan + packaging details: task.md. Rulings: decisions.md. Fences: constraints.md. Grounded/OPEN facts: assumptions.md.
- Source paths: /Users/user/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Sdk/Cymulate.Integration.Sdk and .../UnitTests/Cymulate.Integration.Sdk.UnitTests. ISB repo is read-only.
- Dest: src/Cymulate.Integration.Sdk/, tests/Cymulate.Integration.Sdk.UnitTests/, IntegrationInfra.slnx.

Constraints:
- All of constraints.md verbatim. Highlights: SDK .cs byte-identical (verify with diff -r); packaging files are the only authored edits; no namespace/PackageId/version changes; strip IsAdapter cosplay; fix RepositoryUrl; ISB untouched; no Infra version bump; feed pin removed without dangling.

Success Criteria:
- `dotnet pack src/Cymulate.Integration.Sdk` → Cymulate.Integration.Sdk.3.2.1.nupkg + snupkg; nuspec shows the 3 dependencies, RepositoryUrl=cymulate-rnd/IntegrationInfra, and NO IsAdapter assembly metadata (inspect the nupkg).
- `dotnet pack src/IntegrationInfra` → nuspec declares a dependency on Cymulate.Integration.Sdk (>= 3.2.1).
- `dotnet build IntegrationInfra.slnx` 0 errors; full suite green 247 (238 existing untouched + 9 SDK).
- `diff -r` of dest SDK .cs tree vs ISB source .cs tree: clean (only csproj/props/README differ).
- Directory.Packages.props: 3 pins added, SDK feed pin removed, no project left package-referencing the SDK.
- Docs updated: README + ARCHITECTURE (two-package shape), DESIGN clarifier, CHANGELOG [Unreleased].
- execution_notes.md records: Build.props import decision, Polly reconciliation outcome, any 3.2.0→3.2.1 observations; state.json updated; archive mirrored.

Execution Rules:
- Resolve the OPEN assumptions (Polly coexistence, test-csproj SDK refs, 3.2.1 compatibility, test-pin alignment) by verification before/at the relevant step.
- A 3.2.0→3.2.1 incompatibility that would require editing SDK .cs or changing Infra behavior is a STOP, not a fix.
- Fix genuine bugs in PACKAGING wiring directly; SDK product code is untouchable this task.

Output Format:
- Commits on the branch (suggested: one for the carry+wiring, one for docs — executor may adjust), execution_notes.md, updated state.json.
- Final summary: branch, commits, pack evidence (nuspec dependency lines), suite count, verbatim-gate result, resolved assumptions.

Stop Conditions:
- Goal achieved and verified.
- SDK 3.2.1 source breaks Infra compile/tests (surface delta) — stop, surface options.
- Polly/central-pin conflict unresolvable without changing an existing Infra pin — stop, surface.
