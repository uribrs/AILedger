# Code Review 2 — Recovery-budget progress anchor (re-review of three resolved issues)

## Scope & calibration

- **Change type:** shared library / infrastructure (resilience + recovery, cross-collector).
- **Risk level:** High — retries, distributed coordination, pagination/checkpoint state, idempotency across a resume hop, concurrent multi-collector host.
- **Depth applied:** failure semantics, recovery/idempotency, cross-resume comparability, concurrency, test integrity.
- **Stack:** C# / .NET 8. C# lenses applied.

Reviewed files (working tree vs HEAD):

- `Shared/.../Resilience/Logic/AdapterRecoveryBudget.cs`
- `Shared/.../Resilience/Logic/AdapterFailureDecisionExecutor.cs`
- `UnitTests/.../DummyCollector.Test/AdapterFailureDecisionExecutorTests.cs`
- `UnitTests/.../DummyCollector.Test/AdapterBackoffAndBudgetTests.cs`

Grounding performed beyond the diff: traced the resume wiring (`CollectorResumeSetup`), confirmed SDK runtime semantics by reflection/execution against `Cymulate.Integration.Sdk` 3.1.5, and ran the two affected test classes (20/20 pass).

---

## Verdict on the three prior issues

### Issue 1 (was BLOCKER) — coordinate not comparable across the resume hop — RESOLVED

The coordinate is now `ComputeProgressCoordinate(currentPage, processedItems, processedFindings)` => `"p:i:f"`, built **only** from the three progress counters. Verified these three are exactly what the host restores onto the resume leg:

- `CollectorResumeSetup.TryCreateExecutionContext` calls `progressContext.RestoreProgress(currentPage, processedItems, processedFindings, sequenceId)` from the checkpoint, then `AdapterRecoveryBudget.SeedFromPersistedState(progressContext, data)` copies the `_resilience.*` keys (including the prior coordinate) into `AdapterState`.
- `EvaluateBudget` reads prior state via `AdapterRecoveryBudget.Load(progressContext.AdapterState)` and recomputes the current coordinate from the just-restored counters.

So on the resume leg the prior coordinate (seeded) and the current coordinate (from restored counters) are computed from the same restorable inputs and are genuinely comparable. The previous defect — deriving the coordinate from collector business `AdapterState`, which the host does **not** restore — is eliminated. Confirmed by reflection that `AdapterState` is the live dictionary that both `SetState` (Write/Seed) and `Load` operate on (same reference), so the seed→load round-trip is real, not aspirational.

**Coupled correctness dependency (verified, not a defect):** the counters-only coordinate is only safe because the deferred-wait snapshot no longer advances the page. Confirmed at runtime that `AdvancePage(0,0)` increments `CurrentPage` (1→2) **and** fires `OnCheckpoint`; the old `BuildPartialWaitResult` used `AdvancePage(0,0)`, which means every wait bumped `CurrentPage` and would have read as forward progress against this new coordinate. The new `PersistDeferredWaitSnapshot` triggers persistence via `progressContext.OnCheckpoint?.Invoke(...)` with `StateSnapshot` metadata and zero batch counts, leaving `CurrentPage` untouched. The two edits are mutually necessary and both landed. Under this snapshot, `CurrentPage` advances iff a real page committed — the docstring claim holds.

Confirmed counters-only loses no real progress signal: a flow makes externally-visible progress only by committing a page (`AdvancePage` with non-zero batch), which moves at least one of page/items/findings. There is no progress channel that bypasses these counters yet survives the resume hop, so nothing real is lost by excluding business `AdapterState`.

### Issue 2 (was MAJOR) — mutable static backstop limits — RESOLVED

`MaxTotalBudgetedDeferrals` and `MaxBudgetedRecoveryAge` are now `public static readonly` (get-only, assigned once at type init). Process-wide immutable policy: no torn reads, no cross-test mutation, safe under the concurrent multi-collector host. The test-pollution and concurrency hazard is gone. `int` / `TimeSpan` readonly fields are atomically published; correct.

### Issue 3 (was MAJOR) — tautological no-progress tests — RESOLVED

The new tests seed the prior coordinate independently of the function-over-unchanged-state trick:

- `ExecuteAsync_CoordinateDependsOnlyOnRestoredCounters_NotBusinessAdapterState` restores counters `(7,200,20)`, asserts the prior coordinate against the **literal** `"7:200:20"` (not by re-hashing opaque state), then writes a *divergent* business key (`cursor`) and proves the outcome (count → 2, no progress) is independent of it. This is the genuine cross-resume invariant lock the prior review asked for.
- Negative control `ExecuteAsync_WhenCoordinateAdvancedSinceLastDefer_ResetsConsecutiveCountToOne` seeds `"stale-coordinate"` (≠ current) and proves a *different* coordinate resets the consecutive count to 1.
- `ExecuteAsync_DeferredWaitDoesNotAdvancePageAndConsecutiveDefersAccumulate` is a real regression guard for the coupled dependency above: two consecutive defers on the same context reach count 2 *and* `CurrentPage` stays put — it would fail if the snapshot reverted to `AdvancePage`.

Non-tautological. The invariant is locked from both directions (same coordinate accumulates, different coordinate resets, divergent business state is inert).

---

## New issues introduced by the simplification

### Minor — Unit tests do not exercise the `OnCheckpoint` persistence side-effect

`AdapterProgressContext.FromPlatformEvent(...)` leaves `OnCheckpoint` **null** (verified by reflection/run; the host wires it in production). In the tests, `PersistDeferredWaitSnapshot`'s `OnCheckpoint?.Invoke(...)` therefore no-ops. The budget-state assertions still hold because `AdapterRecoveryBudget.Write` mutates `AdapterState` directly (not through `OnCheckpoint`), and the "page not advanced" assertions are still meaningful (they prove `AdvancePage` was not called). But no test proves that on a real resume the snapshot actually persists the `_resilience.*` keys through the host checkpoint callback and that they survive `SeedFromPersistedState` on the next leg.
- **Impact:** the full persist→restore→seed→load loop for the budget keys is verified only by reading the wiring in `CollectorResumeSetup`, not by a test. The Metadata labeling (`StateSnapshot`, `itemsInBatch=0`) is entirely unasserted.
- **Recommended fix:** add one test that assigns a stub `OnCheckpoint` capturing `AdapterState`, runs a budgeted defer, feeds the captured dict through `SeedFromPersistedState` into a fresh context, and asserts the coordinate/count/total survive. Local patch, not a refactor. Defer-able but worth it given High risk.
- **File:** `AdapterFailureDecisionExecutorTests.cs` (coverage gap); `AdapterFailureDecisionExecutor.cs:228` (`PersistDeferredWaitSnapshot`).

### Observation — Backstops are reset by an interleaved unbudgeted (server-suggested) wait

`ScheduleRecoveryAttempt`'s unbudgeted branch calls `AdapterRecoveryBudget.Clear(...)`, which wipes **all** `_resilience.*` keys, including `TotalDeferralCountKey` and `FirstBudgetedDeferAtUtcKey`. A vendor that alternates a real `Retry-After` (unbudgeted, `UseRecoveryBudget: false` from `ServerSuggestedRetryDelayPolicy`) with budgeted recoverable failures would reset both whole-collection backstops on every server-suggested wait, so the 50-deferral / 24h ceilings only bound an *uninterrupted* budgeted loop.
- **Impact:** the slow-livelock backstop does not bound a budgeted-vs-server-suggested oscillation. Bounded, however: server-suggested delays are real vendor backpressure carrying their own delay, and the clear-on-unbudgeted behavior is **unchanged from HEAD** (pre-existing design), not introduced here. The prior reviewer's failing-open concern was the pure-budgeted no-progress loop, which the backstop does close.
- **Recommended fix:** none required for this change. If the oscillation case matters operationally, track total deferrals / first-defer time in a band that unbudgeted waits do not clear — but that is a separate design decision, out of scope here.
- **File:** `AdapterFailureDecisionExecutor.cs:189-203`; `AdapterRecoveryBudget.cs:107` (`Clear`).

### Observation — `SeedFromPersistedState` skips empty-string values

`SeedFromPersistedState` copies a key only when the value is non-null and non-empty (`!string.IsNullOrEmpty(value)`). `Write` persists a missing coordinate as `string.Empty`. So an empty-string coordinate is not seeded and `Load` returns `null` for it → treated as "progressed" → consecutive count resets to 1. This is consistent with the documented migration path (absent coordinate = progressed) and is harmless (a budgeted defer always writes a real `"p:i:f"` coordinate, never empty), but the empty-vs-absent equivalence is implicit. No action needed.

---

## Other checks (clean)

- **Parsing/validation:** `ReadNonNegativeInt` consolidates the prior inline parse, rejects negatives and non-numerics, logs and floors to 0. `Write` adds `ThrowIfNegative(totalDeferralCount)`. Culture-invariant round-tripping (`"O"` for dates, invariant ints) is consistent across Write/Load. Correct.
- **`AllKeys` / `Clear`:** centralizing the key list and clearing via the array removes the prior drift risk where a new key could be forgotten in `Clear`. Good simplification.
- **Reset-aware delay:** `GetDelay(b.CandidateCount - 1)` with `candidateCount` reset to 1 on progress yields `GetDelay(0)` (first step) on progress and escalates only while stuck — internally consistent. Unbudgeted path `GetDelay(0)` matches HEAD's `currentAttemptNumber=0` behavior; no delay regression.
- **`BudgetEvaluation` as `readonly record struct`:** appropriate, no heap churn, immutable.
- **OR-combined backstops:** evaluated before the stuck gate; `totalDeferralCount > Max` and age `> Max` short-circuit to fallback first. Order is correct (hard ceilings win over the soft stuck gate).
- **Concurrency:** all budget state lives in the per-invocation `AdapterProgressContext`; the only statics are the two readonly limits. No shared mutable state across collectors.
- **Tests run:** `AdapterFailureDecisionExecutorTests` + `AdapterBackoffAndBudgetTests` → 20 passed, 0 failed.

---

## Summary

All three prior issues are correctly resolved. The counters-only coordinate is genuinely comparable across the resume hop (verified end-to-end through `CollectorResumeSetup` + SDK runtime behavior), the page no longer advances on a wait (the dependency that makes counters-only safe), the backstop limits are now immutable statics, and the cross-resume invariant is locked by a non-tautological test plus a regression guard.

No Blocker or Major defects introduced. One **Minor** coverage gap (the `OnCheckpoint` persist→seed→load loop is verified by wiring inspection, not by a test) and two Observations (interleaved-unbudgeted backstop reset — pre-existing, out of scope; empty-vs-absent coordinate equivalence — harmless). Mergeable; the Minor is worth closing given the High risk class but is not merge-blocking.
