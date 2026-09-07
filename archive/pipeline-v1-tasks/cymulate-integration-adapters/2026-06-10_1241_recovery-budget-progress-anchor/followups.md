# Follow-ups

## CLEANUP: remove or rename the vestigial recovery attempt-count fields

**Rule (invariant for the resilience layer):** No `IAdapterFailurePolicy` may gate deferred recovery by a
raw attempt count. Only the executor (`AdapterFailureDecisionExecutor.EvaluateBudget`) may enforce budget
exhaustion, and it does so progress-aware. Encoded now as doc-comments on `IAdapterFailurePolicy.DecideAsync`
and `AdapterFailureContext.RetryAttemptNumber`.

**Why deferred (not done in this task):**
- `AdapterFailureContext.RetryAttemptNumber` is now read in exactly one place — Falcon's `recover.decision`
  log (`FalconResilienceStrategyFactory.cs:140,142`). That file is inside the in-flight Falcon tactical-logging
  work (`FalconTacticalLoggingTests`), so renaming/removing it now risks colliding with active edits.
- `AdapterRecoveryContext.AttemptNumber` is fully dead (the `args.AttemptNumber` reads in `FalconFlowRetryPolicy`
  / `UnknownFlowRetryPolicy` are Polly's retry args, not ours). Removing it unwinds threading through
  `CollectorResumeSetup` → `CollectorResumeExecutionContext`, `AdapterBusStrategyFlowExecutor` →
  `StrategyExecutionState`, and both failure-context factories — ~6 files of orchestration churn.

**Action when picked up:**
1. Remove `AdapterRecoveryContext.AttemptNumber` and its threading (it has no readers).
2. Remove `AdapterFailureContext.RetryAttemptNumber`, OR rename it to signal observability-only
   (e.g. `RecoveryAttemptCountAtDecision`) if a reader still wants it.
3. Make Falcon's `recover.decision` log source `Attempt`/`DelaySec` from the executor's actual decision
   (the reset-aware `candidateCount` / `GetDelay(candidateCount-1)`), not the pre-reset raw count — the
   current log can show an attempt/delay that the executor then overrides on progress.
4. Drop the now-unused budget loaders if nothing else consumes them.

**Guardrail:** any new policy that wants to bound retries must do it via the executor's budget, not its own
count check. The doc-comment on `IAdapterFailurePolicy.DecideAsync` states this.

## Round-3 review (code-reviewer-3) — edge cases + structure

FIXED now:
- **M2 (Major, real bug):** unbudgeted continuity waits cleared the whole-collection backstop via `Clear`
  (wiped `TotalDeferralCount`/`firstBudgetedDeferAtUtc`). Split into `Clear` (full, success only) vs
  `ClearEpisode` (consecutive fields only); unbudgeted path now uses `ClearEpisode`. Regression test added.
- **m3:** added a test that a page-only advance (0 items/findings) resets the consecutive count.
- **M1:** reworded the backstop concurrency comment (it overclaimed subsystem safety; now scopes the claim to
  immutable constants + single-threaded-per-context invariant).
- Stale XML doc on `RecoverOrFallbackAsync` ("once AttemptNumber reaches MaxRetries") corrected; n1 blank line.

DEFERRED (structural — pending operator decision):
- **S1 — honest naming:** `AdapterRecoveryBudgetSnapshot.AttemptCount`, `Write`'s `attemptCount` param, and the
  `AttemptCountKey` const NAME mean "consecutive-no-progress count," not "attempts." Rename the in-memory
  identifiers (keep the persisted key STRING for checkpoint compat). Reviewer: biggest comprehension trap. Low risk.
- **S2 — extract the budget decision:** `EvaluateBudget` + `BudgetEvaluation` (pure: coordinate compare +
  stuck gate + backstop) live as private members inside the 470-line executor. Extract to a pure
  `RecoveryBudgetEvaluator` (move the backstop constants onto it) so the budget is one named, directly
  unit-testable concept. Reviewer: worth doing; improves testability of the riskiest logic.
- **S5 — reason-precedence test:** after S2, add a direct test for backstop-vs-stuck precedence
  (`backstop-total` > `backstop-age` > `stuck`) and the OR-combination ordering.
- **m4 (minor):** a malformed persisted coordinate is silently coerced (fails open — safe — but unlogged).
- **n3 (minor):** published `data["attemptNumber"]` now carries consecutive-no-progress semantics; consumers
  reading it as a lifetime attempt count would misinterpret. Reconcile if any consumer depends on it.

## Round-3 structural items — APPLIED (S1, S2, S5)
- **S1 done:** renamed in-memory identifiers — `AdapterRecoveryBudgetSnapshot.AttemptCount` →
  `ConsecutiveNoProgressCount`, `Write`'s `attemptCount` param → `consecutiveNoProgressCount`, const
  `AttemptCountKey` → `ConsecutiveNoProgressCountKey` (persisted key STRING unchanged for checkpoint compat).
- **S2 done:** extracted the pure budget decision from the executor into
  `Resilience/Logic/RecoveryBudgetEvaluator.cs` (carries the two backstop constants) + result type
  `Resilience/Models/RecoveryBudgetDecision.cs` (with `Exhausted`/`Defer` factories). The executor's
  `EvaluateBudget` is now a thin IO wrapper (load snapshot + compute coordinate) delegating to the evaluator.
- **S5 done:** added 7 direct `RecoveryBudgetEvaluator` unit tests, incl. reason precedence
  (backstop-total > backstop-age > stuck) and backstop-trips-even-with-progress.
- Remaining open: the `RetryAttemptNumber`/`AttemptNumber` fossil removal (top of this file), m4 (log on
  malformed coordinate), n3 (published `data["attemptNumber"]` semantics).

Verification after S1/S2/S5: Shared builds clean; resilience 44/44; full DummyCollector 79/79; Falcon 181/181.

## Filed items — DONE (fossil + m4 + n3)
- **Fossil removed/renamed:** `AdapterRecoveryContext.AttemptNumber` removed (was fully dead — its only use was a hook-log label, now dropped). `AdapterFailureContext.RetryAttemptNumber` RENAMED → `PriorRecoveryAttemptCount` (observability-only; it's the channel that feeds Falcon's `recover.decision` log + tactical-logging tests, so it has a real reader — rename per the filed "or rename if a reader still wants it" branch, not full removal). The `IAdapterFailurePolicy` rule comment + the doc-comment forbid gating on it.
- **m4 done:** `AdapterRecoveryBudget.Load` validates the persisted coordinate (`page:items:findings`, three non-negative ints); a malformed value is logged and treated as no-prior-coordinate (fails open → progress reset, the safe direction).
- **n3 done:** the published diagnostic key `data["attemptNumber"]` / `partialCompletion.AdditionalData["attemptNumber"]` renamed → `consecutiveNoProgressCount` (honest semantics). README + all asserting tests updated.

Verification: resilience 44/44; full DummyCollector 79/79; Falcon 179/179; Shared builds clean. Zero stale `RetryAttemptNumber` / `AttemptNumber` (context) / `"attemptNumber"` references remain.
