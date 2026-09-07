# Orchestration Plan — Phase 3

## Complexity Decision
- Path: **direct**
- Rationale: One coherent edit cluster — 1 new file + 2 modified
  production files + 2 modified tests + 1 new test. Tightly
  interdependent (the runner's write site depends on
  `RetryBudget`'s API; the policy's cap site depends on
  `FailureContext.AttemptNumber` semantic carried from P2 D-P2-18).
  Decomposition would create coordination overhead with no benefit.

## Subagent Usage (already executed during contract drafting)
- W1 — `Explore` subagent surveyed `AdapterProgressContext` SDK
  API, confirming `SetState` is NOT durable alone and `AdvancePage`
  is the persistence trigger via `OnCheckpoint` callback. Resolved
  Prereq 1 → option (2) — explicit flush.
- W2 — `Explore` subagent surveyed `AdapterCheckpoint`,
  `CheckpointAdapter`, and existing `*CheckpointHelper` patterns.
  Confirmed `AdapterState` is `Dictionary<string,string>`, the
  read-path uses `CheckpointAdapter.GetData(checkpoint)`, and the
  collector pattern is `foreach SetState; AdvancePage`.
- W3 — `Explore` subagent surveyed `AdapterFlowRunner.cs` resume
  path entry/exit points, `DefaultFailurePolicy.DecideAsync` cap
  injection site, `progressContext` accessibility scopes, and
  cleanup patterns. Pinned exact line numbers for executor.

All three subagents ran in parallel during contract drafting.
Saved sequential reads of ~3-5 files each.

## Research Decisions
- No further research needed for execution. The three subagents
  above resolved all external-system assumptions (A-P3-1 through
  A-P3-11) except A-P3-13 (platform-team consumer check) which is
  flagged as a deferred follow-up.

## Worker Plan
- Not applicable — direct path. Single executor implements all
  changes sequentially.

## Execution Sequence
1. Pre-flight (HEAD, SDK pin, build clean).
2. Create `Shared/Failure/RetryBudget.cs` (new file).
3. Edit `Shared/Failure/DefaultFailurePolicy.cs`:
   - Add `int _maxRetries` field + ctor parameter
   - Add cap consultation in `RetryInProcess` arm
   - Add `RETRY_BUDGET_EXHAUSTED` `FailFast` synthesis
4. Edit `Shared/Orchestration/AdapterFlowRunner.cs`:
   - Resume: load budget after `RestoreProgress`
   - Resume catch: feed `budget.AttemptCount` into `FailureContext`
   - Resume `RetryInProcess` arm: call `RetryBudget.Write` before
     throwing marker
   - Resume success path: call `RetryBudget.Clear` before
     `AdapterResult.SuccessResult`
   - Fresh-run `RetryInProcess` arm: call `RetryBudget.Write`
   - Fresh-run success path: call `RetryBudget.Clear`
5. Create `UnitTests/.../RetryBudgetTests.cs`.
6. Edit `UnitTests/.../DefaultFailurePolicyTests.cs` — add 2 tests.
7. `dotnet build` (validate 0/0).
8. `dotnet test` filtered DummyCollector.Test (validate full suite
   passes + under 30s).
9. Update `phase-3/execution_notes.md`.
10. Update `phase-3/state.json` (`status: executed`).
11. Run verifier subagent (full context).
12. Run code-reviewer subagent (minimal context, no contract/plan/
    user-request).
13. Update `phase-3/state.json` (`workflow.verifierRun = true`,
    `workflow.codeReviewerRun = true`).

## Verification Obligations
- Cross-check against `prompt_contract.md` Success Criteria 1-10.
- Verifier subagent: full context, checks SC1-10 + D-P3-N honour.
- Code-reviewer subagent: minimal context, reviews the diff in
  isolation. Code-bearing work, mandatory.
- LoC budget: stretch 250 / hard 600. Stop if exceeded.

## Stop Conditions
- Per `prompt_contract.md` Stop Conditions.
- Pre-flight check fails.
- LoC delta exceeds 600.
- `dotnet build` introduces a new warning.
- `dotnet test` produces a non-flaky failure.
- An out-of-scope file is touched.
- `AdapterProgressContext` cannot be exercised by tests without an
  invasive SDK change.
