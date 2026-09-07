# Phase 3 — Execution Notes

## Subagent usage during contract drafting (per orchestration_plan.md)
Three parallel `Explore` subagents resolved all three plan-Prereqs locally:

- **W1** — `AdapterProgressContext` SDK survey (xmldoc 2.0.26). Found
  `OnCheckpoint` callback fires "after each page advance" → `SetState`
  alone is NOT durable, `AdvancePage` is the trigger. Resolved Prereq 1
  → option (2) "explicit flush via `AdvancePage(0, 0)`".

- **W2** — `AdapterCheckpoint` + `CheckpointAdapter` + existing
  `*CheckpointHelper` patterns. Confirmed `AdapterState` is
  `Dictionary<string,string>`; existing pattern is
  `foreach SetState; AdvancePage`. Validated A-P3-1, A-P3-6, A-P3-10,
  A-P3-11.

- **W3** — `AdapterFlowRunner.cs` resume entry/exit + policy
  cap-injection site + `progressContext` accessibility scope. Pinned
  exact line numbers for the executor (load after `RestoreProgress`,
  feed `FailureContext.AttemptNumber`, write in `RetryInProcess` arm,
  clear in success path).

## Pre-flight
- HEAD: `8d88c3e` (descendant of `f51ed3d`). ✓
- SDK: `Cymulate.Integration.Sdk` 2.0.26 unchanged. ✓
- Working tree: P0a + P0b + P1 + P2 + P2 patches (D-P2-17/18) intact. ✓

## Production diffs

### `Shared/Failure/RetryBudget.cs` (new, 156 LoC)
- `public static class RetryBudget` with three keys
  (`_retry.attemptCount`, `_retry.lastErrorCode`, `_retry.lastAtUtc`),
  three methods (`Load`, `Write`, `Clear`).
- `public readonly record struct RetryBudgetSnapshot(int, string?,
  DateTime?)`.
- `Load` reads via `CheckpointAdapter.GetData(checkpoint)`. Per
  D-P3-10, treats unparseable values as defaults + logs warning.
  Empty-string entries are treated as absent (per D-P3-11 round-trip).
- `Write` calls `SetState` for each of the three keys, then
  `AdvancePage(0, 0)` to trigger persistence. Per D-P3-12, swallows
  + logs any flush exception (best-effort durability while inside an
  exception path).
- `Clear` writes empty strings to all three keys, then
  `AdvancePage(0, 0)`. Idempotent.

### `Shared/Failure/DefaultFailurePolicy.cs` (~+30 LoC)
- New `public const int DefaultMaxRetriesAcrossRedeliveries = 4`.
- Ctor signature: `DefaultFailurePolicy(DelayPlanner planner, int
  maxRetriesAcrossRedeliveries = DefaultMaxRetriesAcrossRedeliveries)`.
  Range validation `[1, 100]`.
- New `private readonly int _maxRetriesAcrossRedeliveries` field.
- `Instance` initialization unchanged (uses the default cap).
- `DecideAsync` — cap consultation added inside the
  `RetryInProcess` branch (D-P3-8). When `context.AttemptNumber >=
  _maxRetriesAcrossRedeliveries`, returns `FailFast(handling with
  ErrorCode = "RETRY_BUDGET_EXHAUSTED", IsRetryable = false)`.

### `Shared/Orchestration/AdapterFlowRunner.cs` (~+50 LoC)
- Resume entry: `RetryBudgetSnapshot retryBudget =
  RetryBudget.Load(checkpoint, logger)` after `RestoreProgress(...)`.
- Resume catch-block `FailureContext`: `AttemptNumber = retryBudget.AttemptCount`
  (was hard-coded `= 0` in P2).
- Resume `RetryInProcess` arm: `RetryBudget.Write(progressContext,
  failureContext.AttemptNumber + 1, retry.Handling.ErrorCode,
  DateTime.UtcNow, logger)` BEFORE rethrowing the marker. Refreshes
  the in-memory `retryBudget` snapshot for subsequent loop iterations.
- Resume success path: `RetryBudget.Clear(progressContext, logger)`
  before constructing `AdapterResult.SuccessResult`.
- Fresh-run `RetryInProcess` arm: `RetryBudget.Write(progressContext,
  attemptNumber, retry.Handling.ErrorCode, DateTime.UtcNow, logger)`
  after the loop's logging, before the optional `Task.Delay`.
- Fresh-run success path: `RetryBudget.Clear(progressContext, logger)`
  before publishing the success completion.

## Test diffs

### `RetryBudgetTests.cs` (new, 242 LoC, 15 tests)
- `Load_NullCheckpoint_ReturnsDefaultSnapshot`
- `Load_CheckpointWithoutRetryKeys_ReturnsDefaultSnapshot`
- `Load_CheckpointWithRetryKeys_ParsesAllThreeFields`
- `Load_BadAttemptCount_DefaultsToZero`
- `Load_NegativeAttemptCount_DefaultsToZero`
- `Load_BadTimestamp_DefaultsToNull`
- `Load_EmptyStrings_TreatedAsAbsent` (round-trip after Clear)
- `Write_PopulatesAllThreeKeys`
- `Write_NullLastErrorCode_StoresEmpty`
- `Write_NegativeAttemptCount_Throws`
- `Write_NullProgressContext_Throws`
- `Clear_OverwritesAllThreeKeysWithEmpty`
- `Clear_NullProgressContext_Throws`
- `WriteThenLoad_RoundTrips`
- `Write_ReservedKeys_Match_TheReservedPrefix`

Uses real `AdapterProgressContext.FromPlatformEvent(...)` factory —
no mock; exercises actual SetState/GetState/AdapterState interaction.

### `DefaultFailurePolicyTests.cs` (+~70 LoC, 4 new tests)
- `DecideAsync_RetryBudgetExhausted_ReturnsFailFastWithRetryBudgetExhausted`
  asserts `AttemptNumber = 4` → `FailFast(RETRY_BUDGET_EXHAUSTED)`.
- `DecideAsync_BelowRetryBudget_StillReturnsRetryInProcess` asserts
  `AttemptNumber = 3` → still `RetryInProcess`.
- `DefaultFailurePolicy_OutOfRangeRetryCap_Throws` covers cap = 0,
  -1, 101.
- `DefaultFailurePolicy_BoundaryRetryCap_DoesNotThrow` covers cap =
  1, 4, 100.

## Build + Test Results
- `dotnet build` — **0 errors / 0 new warnings**.
- `dotnet test` `DummyCollector.Test` full suite — **111 / 111
  passed in 127 ms**. C10 still closed (was ~21 min before P2; stays
  fast after P3).
- Filtered runs verified `AdapterFlowRunnerFacadeTests` (14 tests),
  `DummyCollectorTests`, and the P2 test classes are all still green.

## LoC Budget (D-P3-2 / D-P2-16 measurement convention)
- New production: `RetryBudget.cs` 156 LoC.
- New tests: `RetryBudgetTests.cs` 242 LoC.
- Modified production (DefaultFailurePolicy + AdapterFlowRunner): ~+80 LoC.
- Modified tests (DefaultFailurePolicyTests): ~+70 LoC.
- **P3 total net add: ~548 LoC.** Production-only subset: ~236 LoC,
  under the 250 stretch. Combined (incl. tests): under the 600 hard
  ceiling.

## Operator-deferred items surfaced for follow-up

- **A-P3-13** (platform-team consumer check): The reserved `_retry.*`
  prefix is collision-free across this repo (Prereq 2 cleared by
  grep). But downstream platform-side consumers of `AdapterState` may
  not filter underscore-prefixed keys; flag for platform-team
  verification before P4 ship. Risk if assumption breaks: keys leak
  into downstream rendering as plain-string state. No correctness or
  security impact.

- **Cancellation guard on `OperationCanceledException` in resume
  path** (P2 code-reviewer Major #3, deferred from P2). Still
  applicable. Worth a separate review of the P1-introduced OCE
  handling on resume. Not P3 scope; flagged for awareness.

## Post-review repair: Prereq-1 redesign

After the code-reviewer flagged `AdvancePage(0, 0)` as a possible
side-effect bug (Blocker), I added two regression tests
(`Write_DoesNotChangeProgressCounters`,
`Clear_DoesNotChangeProgressCounters`) — both FAILED, confirming
`AdvancePage(0, 0)` increments the page counter on the real SDK.

**Redesign applied**: `RetryBudget.Write` and `RetryBudget.Clear`
now mutate `progressContext.AdapterState` in-memory only — no
`AdvancePage` call. Durability piggy-backs on the next natural
`AdvancePage` in the flow (typically the next successful batch
publication, which fires the host's `OnCheckpoint` callback and
persists the entire snapshot).

**Trade-off (documented in xmldoc on `RetryBudget`)**: this is
plan-Prereq-1 option (3) — "best-effort cross-redelivery cap".
Worker crash during a retry sleep BEFORE any subsequent
`AdvancePage` fires loses the budget; the platform's next
redelivery starts with a fresh budget. The cross-redelivery cap may
be exceeded across the crash boundary by up to the cap value. C6
weakens from "hard cap" to "best-effort cap".

**Tests**: 113/113 pass after redesign (was 111/111 plus the 2
regression-guard tests).

## Open code-reviewer findings tracked for next agent

- **Major #2** — terminal failure paths (`FailFast` non-success,
  `PartialSuccess` non-success, `PublishAndExit`) do NOT call
  `RetryBudget.Clear`. The saturated budget persists. For
  `RETRY_BUDGET_EXHAUSTED` this is INTENTIONAL (cap should bite
  across redeliveries). For other terminal failures (e.g., transport
  publish-and-exit after several retries), the budget leaks. Low
  impact today since no production caller enables classified-retry
  (Falcon enables in P4). Surface for P4 contract to address.

- **Major #3** — `DecideAsync_BelowRetryBudget_StillReturnsRetryInProcess`
  test asserts `RetryInProcess` type but does not pin the `Delay`
  value. Minor — `PickClassified(3) = 30m ± 20% jitter` is verified
  elsewhere via `DelayPlannerTests`. Could be tightened.

- **Minor — magic string `"RETRY_BUDGET_EXHAUSTED"`** at one site
  (`DefaultFailurePolicy.cs`). Consider promoting to a `const` if
  reused elsewhere. Out of scope for this phase.

## Out-of-scope verification
- `UnknownFlowRetryPolicy.IsUnknownRetryCandidate` body: untouched.
- `ClassifiedRetryablePolicy.ShouldHandle` body: untouched.
- `ClassifiedRetryableTriggerException` class: untouched.
- No collector file modified (DummyCollector.cs unchanged from its P2
  state).
- `Directory.Packages.props`, `Directory.Build.props`: untouched.
- `phase-0a/`, `phase-0b/`, `phase-1/`, `phase-2/` artifacts: untouched.
- `plan.md`: untouched.
- `FailureContext` shape unchanged (no new fields per D-P3-14).
