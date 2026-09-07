# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: Change is concentrated in `AdapterRecoveryBudget.cs` + `AdapterFailureDecisionExecutor.cs` (coordinate compute, gate, backstop) plus a single test project. The coordinate-hash and gate logic are tightly coupled and benefit from one iteration context. Worker boundaries would be fuzzy; decomposition would add coordination overhead with no parallelism gain.

## Research Decisions
- None needed. The three OPEN assumptions (CurrentPage/ProcessedItems/ProcessedFindings not in the `AdapterState` dict; `AdvancePage(0,0)` does not mutate item/finding counters; backstop config seam) are confirmable by reading in-repo Shared code and SDK usage. The only non-source item (exact `AdvancePage` internals in the compiled SDK) is deliberately moot — the design hashes `AdapterState` and captures counters before the call, so it does not depend on that behavior.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Verification Obligations
- Cross-check against every Success Criterion in `prompt_contract.md`.
- Confirm the three OPEN assumptions were resolved against code during execution (not relied upon blind).
- Confirm budgeted vs unbudgeted paths: only `UseRecoveryBudget=true` recoveries touch the new counting; unbudgeted waits stay budget-neutral and still clear.
- Confirm `Clear` wipes ALL new keys and the two success-only callsites are preserved.
- Confirm coordinate hash is deterministic + culture-invariant and excludes `_resilience.*`.
- Confirm counters captured before `AdvancePage(0,0)` (artificial bump ≠ progress) — regression test present.
- Confirm migration path (missing coordinate ⇒ progress ⇒ count 1).
- Build pinned to net8.0; Falcon resilience tests + ≥1 non-Falcon collector test project pass (or, if `dotnet test` hangs, rely on verifier per saved memory).
