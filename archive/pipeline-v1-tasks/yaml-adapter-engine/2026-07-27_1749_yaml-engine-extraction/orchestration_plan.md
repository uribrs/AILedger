# Orchestration Plan

## Complexity Decision

- **Path: direct**
- Rationale: Broad in file count (138 files) but narrow in judgment — every placement
  decision is already fixed in `ARCHITECTURE.md`. Separability is low and coupling is
  high: the phase-2 namespace change ripples into every file's `using` list and into the
  shared csproj and `.slnx`, so no concept can be reshaped to a compiling state on its
  own. Dependency order S0→S9 is strictly sequential. Worker clarity is low for the same
  reason — "reshape Pagination" is not a deliverable that builds. Every rubric axis
  favours direct; decomposition would buy nothing but merge conflicts.

## Research Decisions

None needed. No OPEN assumption depends on external-system behaviour. A7 (baseline test
count) and A8 (package version coverage) resolve by running `dotnet test` and
`dotnet restore` — that is execution, not investigation. A1 (test tree layout) and A5
(test project rename) are internal conventions with recorded defaults and are trivially
reversible.

## Worker Plan

Not applicable — direct path.

## Synthesis Approach

Not applicable — direct path.

## Verification Obligations

Beyond the contract Success Criteria, the verifier must specifically confirm:

1. **Baseline parity is real, not asserted.** `execution_notes.md` records an actual S0
   number from the monorepo, and post-phase-1 and post-phase-2 counts match it exactly.
   A test that silently stopped being discovered reads as "passing".
2. **The EmbeddedResource `LogicalName` survived verbatim.** Confirm by inspecting the
   csproj *and* by confirming `IntegrationSchemaValidator` tests actually ran and passed
   — this failure mode is silent at runtime.
3. **Phase 2 changed no method bodies.** Inspect `git diff` between the two commits for
   changes outside namespace lines, using directives, file moves, and file splits.
4. **`Models/` does not exist** and no namespace contains a `.Contracts` or `.Logic`
   segment.
5. **The monorepo is untouched** — `git -C /Users/user/Dev/cymulate-integration-adapters
   status --porcelain` returns empty.
6. **No phase-3 work leaked in** — no `ExecutionRequest`, no `PageOutcome`, no
   `IExecutionSink` split, no visibility trimming, `IntegrationEngine.cs` still intact at
   its original size.
7. **Type accounting.** Every one of the 96 source types is present in the target; none
   dropped during the file splits.

## Known Gap Carried Into Execution

A4 (VALIDATED): `EngineIsolationTests` — the only automated guard for rule 0 — stays in
the monorepo with the adapter's test project. The new repo inherits no protection for its
most load-bearing invariant. Porting an equivalent assertion is **new code, not a move**,
and is therefore out of scope for this branch without explicit operator approval. Execution
must not add it silently. Recorded here so it is not lost.
