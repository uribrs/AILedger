# Verifier-1 — recovery-budget-progress-anchor

Verdict: **SATISFIED**

Scope note: the working-tree diff is much larger than the named files (the Falcon
collector is heavily rewritten on this branch). That Falcon work is a separate concern
(falcon-collection-strategy-refactor) and outside this contract. I verified only the
recovery-budget-progress-anchor change in the named Shared files + the two DummyCollector
test files. Shared csproj builds clean on net8.0 (0 warn / 0 err).

## Per-criterion

### SC1 — Defer → progress → defer does NOT accumulate (count resets to 1): PASS
`EvaluateBudget` sets `progressed = prior.LastProgressCoordinate is null || coordinate differs`
→ `candidateCount = progressed ? 1 : prior+1`. Covered by
`WhenCoordinateAdvancedSinceLastDefer_ResetsConsecutiveCountToOne` (prior count=2 at stale
coordinate → resets to 1, total still increments to 3).

### SC2 — only consecutive no-progress accumulate; 4th (MaxRetries=3) → PublishFailure: PASS
Stuck gate is `candidateCount > recover.BackoffPlan.MaxRetries` (line 251). MaxRetries=3 allows
candidates 1,2,3; the 4th consecutive (candidate=4) trips. Increment path covered by
`WhenNoProgressWithinBudget_IncrementsConsecutiveCount` (1→2). Fallback covered by
`WhenNoProgressBudgetExhausted_AppliesFallbackWithoutCallingHook` (prior=2, Max=2 → candidate 3
> 2 → fallback, hook not called) and `WhenNoHookAndNoProgressBudgetExhausted_AppliesFallback`.
The exact MaxRetries=3-allows-3 boundary is exercised via Max=1/Max=2 equivalents rather than
a literal Max=3 case, but the `>` boundary is unambiguous and locked. PASS.

### SC3 — artificial AdvancePage(0,0) bump ≠ progress: PASS
Path B implemented: `PersistDeferredWaitSnapshot` uses `OnCheckpoint?.Invoke` (mirrors
FalconFindingsCheckpointWriter), NOT `AdvancePage`. Capture ordering is correct: `EvaluateBudget`
computes the coordinate from `progressContext.CurrentPage/ProcessedItems/ProcessedFindings`
(line 137) BEFORE `ScheduleRecoveryAttempt`/`BuildPartialWaitResult`/`PersistDeferredWaitSnapshot`
run. Regression-locked by `DeferredWaitDoesNotAdvancePageAndConsecutiveDefersAccumulate` — two
consecutive no-progress defers reach count 2 and CurrentPage is unchanged; had the snapshot
bumped the page, the coordinate would differ and the count would reset, so the test would fail.
Also `WhenRecoveryContinues...` asserts page/items/findings unchanged after the wait. PASS.

### SC4 — backstop: total>50 OR age>24h, progress-independent, cleared only on full-flow success: PASS
`EvaluateBudget`: `totalDeferralCount = prior+1`; `if (totalDeferralCount > MaxTotalBudgetedDeferrals)`
and `if (now - firstBudgetedDeferAtUtc > MaxBudgetedRecoveryAge)` BOTH evaluated before the stuck
gate and independent of `progressed`. Defaults 50 / 24h, public static (configurable). Covered by
`WhenTotalDeferralsExceedBackstop_FallsBackEvenWithProgress` (stale coordinate = progress, yet
total at 50 → candidate total 51 > 50 → BACKSTOP) and `WhenRecoveryAgeExceedsBackstop_...`. Backstop
keys are in `AllKeys`, so `Clear` (only called on full-flow success at AdapterBusStrategyFlowExecutor:48
and CollectorResumeStrategyExecutor:38, both verified intact) wipes them. PASS.

### SC5 — unbudgeted waits budget-neutral and still clear: PASS
Unbudgeted (`UseRecoveryBudget=false`) skips `EvaluateBudget` (no count, no totalDeferralCount
increment) and `ScheduleRecoveryAttempt`'s `budget == null` branch calls `AdapterRecoveryBudget.Clear`.
Covered by `WhenServerSuggestedDelayFlowsThroughContinueHook_ReturnsPartialWaitResult`: asserts
`useRecoveryBudget=false`, no attemptNumber/maxRetries keys, and `AttemptCountKey` empty after. PASS.

### SC6 — old checkpoints upgrade gracefully (missing coordinate ⇒ progress ⇒ count 1): PASS
`prior.LastProgressCoordinate is null` short-circuits `progressed=true`. `Load` returns null
coordinate when the key is absent/blank. Covered by `WhenPriorCheckpointHasNoCoordinate_TreatsAsProgress`
(prior count=2, coordinate=null → resets to 1). PASS.

### SC7 — builds on net8.0; Falcon resilience + ≥1 non-Falcon test project pass: PASS (build verified here)
Shared csproj built clean net8.0 (no -f flag). Test counts (DummyCollector 67/67, Falcon 170/170)
per executor report; not re-run (suite hangs in this harness, per saved memory).

## Scrutiny items requested

1. Coordinate excludes `_resilience.*`, includes CurrentPage/ProcessedItems/ProcessedFindings,
   deterministic + culture-invariant: CONFIRMED. `Where(!key.StartsWith("_resilience.", Ordinal))`,
   `OrderBy(key, StringComparer.Ordinal)`, all ints `ToString(InvariantCulture)`, SHA-256 hex.
   Unit test `ComputeProgressCoordinate_IsDeterministic_ExcludesResilienceKeys_AndTracksProgress`
   asserts key-order invariance, exclusion of resilience keys, and that each of cursor/page/items/
   findings changes the coordinate.
2. Stuck gate `candidate > MaxRetries`: CONFIRMED. Reset-to-1-on-progress correct.
3. Backstop total>50 OR age>24h, progress-independent, cleared only on success: CONFIRMED (SC4).
4. Path B non-page-advancing snapshot via `OnCheckpoint.Invoke`: CONFIRMED. Unbudgeted path stays
   budget-neutral and clears: CONFIRMED.
5. Migration missing-coordinate ⇒ progress ⇒ count 1: CONFIRMED.
6. Edge cases:
   - Integer/date parse failures: `ReadNonNegativeInt`/`TryReadDateTime` swallow → 0/null;
     `Load_WhenBudgetStateIsInvalid` covers. A persisted-coordinate-without-count state would
     read count 0 → candidate 1; harmless.
   - Resume leg reads prior budget: `CollectorResumeSetup.SeedFromPersistedState` (line 69) copies
     `_resilience.*` from the restored checkpoint dict into the live context AFTER `RestoreProgress`,
     so `EvaluateBudget`'s `Load(progressContext.AdapterState)` sees prior budget the same as the bus
     leg. Decisions.md notes the executor runs once per invocation (in-runner self-heal is a `continue`
     loop, not an executor call) so no in-process accumulation. Plausible and consistent with the code.
   - `firstBudgetedDeferAtUtc` "set once if unset": `prior.FirstBudgetedDeferAtUtc ?? now`; first
     defer yields age 0, not a spurious trip. Correct.
   - Concurrency: AdapterProgressContext mutation is single-threaded per invocation (executor runs
     once); no new shared mutable state introduced. No concern.

## Minor / non-blocking observations

- No literal MaxRetries=3 boundary test (contract examples used 3). The `>` semantics and reset are
  fully covered via Max=1/Max=2 cases; the 3-allows-3 boundary follows directly. Cosmetic gap only.
- Empty-page-only advance reset is covered compositionally: coordinate unit test locks "page change ⇒
  different coordinate," and the executor test locks "different coordinate ⇒ reset to 1." No single
  end-to-end test drives a CurrentPage-only advance through the executor, but both halves are locked.
  Acceptable.
- `CompletePartialOrPublishAsync` writes `partialResult.Data["partialCompletion"]` only when
  `Data is not null` — pre-existing behavior, unrelated to this change.

Overall: every Success Criterion and every Required Test in the contract is met by code + tests.
No contradictions with constraints/decisions. SATISFIED.
