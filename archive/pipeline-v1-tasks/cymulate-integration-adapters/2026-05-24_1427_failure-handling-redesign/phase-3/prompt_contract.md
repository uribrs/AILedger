## Role

Senior .NET backend engineer in the post-Phase-2 + post-D-P2-17/18
Cymulate integration-adapters codebase. You know:
- The merged `AdapterFlowRunner` (post-P0b) with the runner-level
  `IFailurePolicy` gating that now invokes the policy for programmer
  bugs (D-P2-17).
- `FailureContext.AttemptNumber` semantic = 0-based retry index
  (D-P2-18).
- `DelayPlanner.Default` (jitter 0.20) drives both Polly pipelines via
  optional `DelayPlanner?` parameters on `CreatePipeline`.
- The SDK's `AdapterProgressContext.SetState/GetState/AdvancePage`
  pattern: `SetState` writes to an in-memory dictionary;
  `AdvancePage(...)` fires the host's `OnCheckpoint` callback that
  actually persists the snapshot.

## Goal

Introduce a persistent retry budget (`RetryBudget`) keyed under
`_retry.*` in `AdapterCheckpoint.AdapterState`. The budget survives
worker restarts and redeliveries. `DefaultFailurePolicy` consults the
budget and converts `RetryInProcess` to
`FailFast(RETRY_BUDGET_EXHAUSTED)` when the cross-redelivery cap is
hit.

## Context

### Concrete deliverables

**New production files (1):**

1. `Shared/Failure/RetryBudget.cs` — `public static class RetryBudget`
   with three methods (`Load`, `Write`, `Clear`) and three reserved
   keys (`_retry.attemptCount`, `_retry.lastErrorCode`,
   `_retry.lastAtUtc`). Also a `public readonly record struct
   RetryBudgetSnapshot(int AttemptCount, string? LastErrorCode,
   DateTime? LastAtUtc)`. Per D-P3-6, D-P3-7, D-P3-9, D-P3-10,
   D-P3-11, D-P3-12.

**Modified production files (2):**

2. `Shared/Orchestration/AdapterFlowRunner.cs`:
   - Resume path: after `RestoreProgress(...)` (~line 1095), load
     `var budget = RetryBudget.Load(checkpoint)`.
   - Resume catch block (~line 1149): change
     `AttemptNumber = 0` to
     `AttemptNumber = budget.AttemptCount`.
   - Resume `RetryInProcess` arm (search inside the resume switch on
     `FailureAction`): before throwing the marker exception, call
     `RetryBudget.Write(progressContext, context.AttemptNumber + 1,
     retry.Handling.ErrorCode, DateTime.UtcNow)`.
   - Resume success path (~lines 1362-1367): before constructing
     `AdapterResult.SuccessResult`, call
     `RetryBudget.Clear(progressContext)`.
   - Fresh-run path: skip `RetryBudget.Load` (no checkpoint). In the
     `RetryInProcess` arm of `ActionFreshAsync` (~line 786), call
     `RetryBudget.Write(progressContext, attemptNumber,
     retry.Handling.ErrorCode, DateTime.UtcNow)` before
     `Task.Delay`. (`attemptNumber` is the runner-local counter,
     already 1-based "attempts done"; equivalent to "retries
     initiated so far".) On fresh-run success (~line 330), call
     `RetryBudget.Clear(progressContext)` before publishing.

3. `Shared/Failure/DefaultFailurePolicy.cs`:
   - Ctor now accepts optional `int
     maxRetriesAcrossRedeliveries = 4` parameter:
     `public DefaultFailurePolicy(DelayPlanner planner, int
     maxRetriesAcrossRedeliveries = 4)`. Range validation
     `[1, 100]`.
   - Field `private readonly int _maxRetries`.
   - In the `RetryInProcess` arm (line ~104), BEFORE computing the
     planner delay: if `context.AttemptNumber >= _maxRetries`, return
     `FailFast(new FlowExceptionHandling(
       $"Retry budget exhausted after {_maxRetries} retries (last
       error: {context.VendorClassification?.ErrorCode ?? handling.ErrorCode}).",
       "RETRY_BUDGET_EXHAUSTED",
       ErrorSeverity.Error,
       IsRetryable: false))`. Otherwise continue with the existing
     `RetryInProcess(_planner.PickClassified(context.AttemptNumber),
     "classified-retryable", handling)`.

**Modified test files (2) + new test file (1):**

4. `UnitTests/.../RetryBudgetTests.cs` (new) — Load/Write/Clear
   per-method coverage + round-trip on a hand-built
   `AdapterCheckpoint`.

5. `UnitTests/.../DefaultFailurePolicyTests.cs` — add
   cap-exhaustion test: `AttemptNumber = maxRetries`, classified
   retryable + enabled → `FailFast` with `RETRY_BUDGET_EXHAUSTED`.
   Add ctor-param boundary test.

6. `UnitTests/.../DummyCollectorTests.cs` — keep existing tests
   green. C10 zero-delay planner still injected via mock service
   provider.

### Out of scope (do NOT touch)

- `UnknownFlowRetryPolicy.IsUnknownRetryCandidate` body (P5).
- `ClassifiedRetryablePolicy.ShouldHandle` body (P5).
- `ClassifiedRetryableTriggerException` type (P5 deletion).
- Falcon classified-retry enable (P4).
- Any collector file besides `DummyCollector.cs` (no changes expected
  on Dummy for P3).
- `Directory.Packages.props`, `Directory.Build.props`.
- `phase-0a/`, `phase-0b/`, `phase-1/`, `phase-2/`, `plan.md`.
- Polly pipeline files (no DelayPlanner changes).
- `FailureContext` shape (no new fields — D-P3-14).

## Constraints

See `constraints.md`.

## Success Criteria

1. `dotnet build` — 0 errors, 0 new warnings.
2. `dotnet test` filtered to `DummyCollector.Test` project — full
   suite passes, total runtime under 30 seconds.
3. `RetryBudgetTests.cs` exercises Load/Write/Clear:
   - Empty checkpoint → snapshot `(0, null, null)`.
   - Round-trip: Write → in-memory dictionary state → Load reproduces
     the same snapshot.
   - Clear writes empty strings + AdvancePage(0, 0) called.
   - Bad checkpoint values (non-int attempt count) → snapshot
     defaults `(0, null, null)` and warning logged.
4. `DefaultFailurePolicyTests.cs` cap-exhaustion test:
   `AttemptNumber = 4`, classified retryable, classified-retry
   enabled → `FailFast { ErrorCode = "RETRY_BUDGET_EXHAUSTED",
   IsRetryable = false }`. With `maxRetriesAcrossRedeliveries = 4`.
5. `DefaultFailurePolicy` ctor with `maxRetriesAcrossRedeliveries =
   0` throws `ArgumentOutOfRangeException`; with `1` and `100` does
   not throw.
6. All Phase 2 tests still pass unchanged
   (`DefaultFailurePolicyTests`, `DelayPlannerTests`,
   `ProgrammerBugClassifierTests`, `UnknownFlowRetryPolicyDelaysTests`,
   `ClassifiedRetryablePolicyTests`, `CurrentSequenceIdGuardTests`,
   `AdapterFlowRunnerFacadeTests`, `DummyCollectorTests`).
7. Resume path: after `runAsync` throws on a `RetryInProcess`-classified
   exception, `_retry.attemptCount` in the progress context's
   `AdapterState` increments by 1 and `_retry.lastErrorCode` /
   `_retry.lastAtUtc` are set. (Spot-checked by a focused test using
   a Moq'd `AdapterProgressContext`.)
8. Resume path success: after `runAsync` returns normally,
   `_retry.attemptCount`, `_retry.lastErrorCode`, `_retry.lastAtUtc`
   all read as empty strings in the progress context. (Test asserts
   this.)
9. LoC budget: net add ≤ 250 LoC stretch / ≤ 600 LoC hard. Measure
   via `git diff --numstat | awk '{add+=$1; del+=$2} END {print add -
   del}'`.
10. No edits to out-of-scope files. `git status` shows only the
    expected files modified.

## Execution Rules

- Use `Edit` (surgical) for existing files. `Write` only for new
  files.
- Run `dotnet build` after edits, `dotnet test` after build.
- If a test hangs (>2 min), kill it and move to verifier; don't poll.
- Stop and surface if an out-of-scope file is unavoidable.
- Stop and surface if `AdapterProgressContext` cannot be mocked
  cleanly in `RetryBudgetTests` — the SDK type may need a thin
  wrapper interface; flag it as a design follow-up rather than
  inventing a leaky test scaffold.

## Output Format

- Code changes on disk.
- `phase-3/execution_notes.md` documenting files touched, LoC,
  test results, any A-P3-N assumptions resolved during execution,
  any surprises.
- Updated `phase-3/state.json` (`status: executed`).

## Stop Conditions

- LoC delta exceeds 600 net.
- Build introduces a new warning or error.
- A `dotnet test` failure that is NOT a pre-existing hang.
- An out-of-scope file is touched.
- `AdapterProgressContext` cannot be exercised by tests without an
  invasive SDK change.
