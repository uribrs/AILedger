# Execution Notes

## Pre-implementation investigation (assumption resolution) — paused on a fork

Resolved the OPEN assumptions against authoritative source (SDK source lives inside the ISB repo at
`/Users/user/Dev/IntegrationServiceBus/.../Sdk/Cymulate.Integration.Sdk`, plus the ISB host).

Confirmed:
- **Counters are NOT in `AdapterState`** — `AdapterCheckpoint` has `CurrentPage`/`ProcessedItems`/`ProcessedFindings`/`CursorToken`/`LastProcessedId` as first-class fields; `AdapterState` is a separate dict. Hashing `AdapterState` does not double-count them. (A1 VALIDATED)
- **`AdvancePage(itemsInBatch, findingsInBatch)` always increments the page** — `IAdapterExecutionContext.cs:318` `Interlocked.Increment(ref _currentPage)`, then `OnCheckpoint?.Invoke` (`:325`). `(0,0)` adds 0 items/findings (A2 VALIDATED: item/finding counters NOT bumped) but DOES bump page.
- **The page bump persists across resume.** ISB `OnCheckpoint` handler snapshots `progressCtx.CurrentPage` into the row (`AdapterExecutionContext.cs:83`); the PartialWait path persists that row then `TransitionToScheduledWaitAsync` flips only status, preserving page/state (`ProcessEventCommandHandler.cs:438-441`); resume reconstructs `CurrentPage = entry.CurrentPage` (`:828`) and `RestoreProgress(currentPage:...)` restores it. So every budgeted (and unbudgeted) defer permanently +1's `CurrentPage`.

Consequence for the design: including raw `CurrentPage` in the coordinate makes every consecutive no-progress defer look like progress (page keeps +1ing), defeating the stuck counter. `ProcessedItems`/`ProcessedFindings` do NOT have this problem (not bumped by `AdvancePage(0,0)`).

## Fork discovered (the page bump is self-inflicted, not contractual)

The per-defer page bump comes from **our** resilience code choosing `AdvancePage(0,0)` as the snapshot trigger
(`AdapterFailureDecisionExecutor.BuildPartialWaitResult`, ~line 290). The SDK/ISB contract sanctions a
**non-advancing snapshot**: call `OnCheckpoint?.Invoke(progressContext)` directly (optionally setting
`CheckpointKind`/`CheckpointReason`/`itemsInBatch=0` metadata first). **Falcon's own checkpoint writer already
does exactly this** — `FalconFindingsCheckpointWriter.cs:246` ("no AdvancePage — no new output page was produced").

So "the bump is unavoidable" is FALSE. Two viable paths (surfaced to the operator for decision):
- **Path A (tight):** exclude `CurrentPage` from the coordinate. Use `hash(AdapterState − _resilience.*) + ProcessedItems + ProcessedFindings`. Leaves the per-defer page inflation as a pre-existing latent issue. Loses empty-page-only progress detection (operator pre-accepted this as "not a showstopper").
- **Path B (deeper, cleaner):** change the resilience defer snapshot from `AdvancePage(0,0)` to `OnCheckpoint?.Invoke` (mirroring Falcon). Stops per-defer page inflation for ALL collectors (latent global fix) and makes `CurrentPage` a clean progress signal → include it as originally designed. Bigger blast radius (shared executor; flips an existing test asserting `CurrentPage == pageBefore+1`); needs its own verification.

Status: PAUSED awaiting operator decision on Path A vs Path B before writing product code.

## Resolution — Path B implemented (operator chose Path B; ISB keeps consuming CurrentPage)

Fork resolved by operator: fix the snapshot mechanism so CurrentPage stays a clean progress signal, rather than dropping it.

Implemented:
- **AdapterRecoveryBudget**: new keys (`lastProgressCoordinate`, `totalDeferralCount`, `firstBudgetedDeferAtUtc`); `AttemptCount` reinterpreted as consecutive-no-progress count; `ComputeProgressCoordinate(adapterState − _resilience.*, currentPage, processedItems, processedFindings)` = SHA-256 of a deterministic, culture-invariant canonical string (sorted keys); `Load`/`Write`/`Clear` extended; `Clear` wipes all 8 keys; `SeedFromPersistedState` copies `_resilience.*` from a restored checkpoint dict.
- **AdapterFailureDecisionExecutor**: `EvaluateBudget` (progress coordinate → progressed? reset to 1 : prev+1; backstop total>50 OR age>24h; stuck candidate>MaxRetries); reset-aware delay `GetDelay(candidateCount-1)`; `PersistDeferredWaitSnapshot` replaces `AdvancePage(0,0)` with metadata + `OnCheckpoint?.Invoke` (Path B — no page bump). Backstop knobs are `public static readonly` (defaults 50 / 24h).
- **CollectorResumeSetup**: seeds `_resilience.*` into the live context after `RestoreProgress` so the resume leg reads prior budget from `AdapterState` (the executor runs once per invocation — the in-runner self-heal is a `continue` loop, not an executor call — so reading at defer time equals the flow-start budget; no in-process accumulation).

CurrentPage IS in the coordinate and is now trustworthy: with the OnCheckpoint-based snapshot the page is no longer bumped per defer, so a defer can't masquerade as progress.

OPEN assumptions resolved (verified against SDK source inside the ISB repo): counters are not in AdapterState (A1 ✓); AdvancePage(0,0) bumps page but not item/finding counters and the bump persists across resume via ISB (A2 ✓, drove the Path B fix); backstop config = public static knobs (A3 resolved).

### Verification
- `dotnet build` net8.0: Shared, Falcon test, DummyCollector test all succeed.
- DummyCollector resilience tests: 19/19 (6 required scenarios + coordinate/round-trip units + updated existing).
- Full DummyCollector suite: 67/67.
- Full Falcon suite: 170/170.

### Note for verifier/reviewer
The user live-edited the untracked `FalconStagedSpotlightFlowTests.cs` during execution (replaced a 503 "FreshServerError" test with a 401 "FreshUnauthorized" one). An intermediate full-suite run caught that file mid-edit and showed 1 failure; after the user's edit settled, the suite is 170/170. The 401 and 503 paths share the same budgeted scheduled-recovery honor path, so this was a test-file race, not a regression. No git operation touched user files (the test file is untracked; stash/checkout were scoped to my 4 Shared files and reverted cleanly).

## Post-review round 2 — P1/P2/P3 (external review after handoff)

A later review caught a real BLOCKER that verifier-1 and code-reviewer-1/2 all missed (they reviewed the executor in isolation):

- **P1 (BLOCKER) — fixed.** The policy layer ALSO gated on the raw attempt count before the executor ran:
  `MappedFailurePolicy.CanRequestDeferredRecovery` and `UnknownFlowFailurePolicy` both checked
  `RetryAttemptNumber < MaxRetries` and short-circuited to `PublishFailure`/`Rethrow` at the boundary — so a
  scan that hit count==MaxRetries, then made real progress, then failed again would be terminated before the
  executor's progress-aware reset could run (the original bug, preserved at the exact boundary). Removed both
  policy count-gates so the executor's `EvaluateBudget` is the single, progress-aware owner. `UnknownFlowFailurePolicy`
  now emits `RecoverAndRetry` with a `RethrowForUnknownRetry` fallback to preserve its exhaustion-rethrow semantics.
  `AdapterFailureContext.RetryAttemptNumber` is now vestigial (set, never read for gating) — follow-up removal candidate.
- **P2 — fixed.** Added two end-to-end strategy→executor tests in `AdapterFailureDecisionExecutorTests`:
  `StrategyToExecutor_PriorCountAtMaxRetriesButProgressed_DefersAttemptOne_NotPublishFailure` (the P1 regression)
  and `..._AndNoProgress_FallsBackToPublishFailure` (exhaustion still bounded, now via the executor). These go
  through `AdapterResilienceStrategy.DecideAsync` + the executor, which the prior executor-only tests bypassed.
- **P3 — fixed.** `decisions.md` updated (coordinate is counters-only, not an AdapterState hash; gate ownership).
  NOTE: `review/verifier-1.md` and `review/code-reviewer-1.md` predate the B1 coordinate simplification and the
  P1 policy fix — they describe the AdapterState-hash coordinate that no longer exists. `code-reviewer-2.md` reflects
  the counters-only coordinate. Current source of truth = this file + the tests.

### Verification (round 2)
- Resilience executor+budget tests: 22/22 (incl. 2 new end-to-end P1 regression tests).
- AdapterResilienceStrategyTests: 13/13 (no existing strategy test encoded the old count-gate — why P1 slipped).
- Full DummyCollector suite: 70/70. Full Falcon suite: 177/177.
