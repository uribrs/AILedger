# Task: Pre-Reshape Invariant Pin Tests

Repo: /Users/user/Dev/IntegrationInfra. Branch off `dev` (work trunk; `master` is release-only).
Tests-only package: pin load-bearing behavior before the later public-statics reshape (that reshape is OUT of scope).

Contract-design grounding (2026-07-08) found existing coverage is much stronger than assumed, so scope is:

1. **(a) SDK checkpoint-key pin — the core deliverable, zero existing coverage.**
   In `FaultGovernance.Tests`: drive `AdapterFailureDecisionExecutor` through a deferred-recovery decision so
   `PersistDeferredWaitSnapshot` fires; round-trip persisted state through a real SDK `AdapterCheckpoint`;
   resume leg via `CheckpointAdapter.GetData` + `AdapterRecoveryBudget.SeedFromPersistedState`.
   Assert survival of the string-literal keys mirrored from private SDK conventions:
   `_checkpoint.kind=StateSnapshot`, `_checkpoint.reason=deferred-recovery:*`, `itemsInBatch=0`,
   `findingsInBatch=0`, and the `_resilience.recovery.*` budget fields (consecutiveNoProgressCount,
   lastProgressCoordinate, totalDeferralCount, firstBudgetedDeferAtUtc at minimum).
   Also pin `AdapterRecoveryBudget` persistence semantics not covered anywhere: Write/Load round-trip,
   ClearEpisode preserves backstop keys, Clear removes both, SeedFromPersistedState copies ONLY `_resilience.*`.

2. **(b)(e)(f) Coverage audit + gap-fill only** (existing suites are strong; each may be a no-op):
   - (b) `Kernel.Tests/Transport`: classifier suites exist. Known candidate gap: inner-chain **depth-cap-10**
     boundary (marker/circuit type at depth >10 → false). Verify socket-error Theory covers all 9 codes;
     verify RetryDelays clamp behavior is pinned.
   - (e) `FaultGovernance.Tests/RecoveryBudgetAndBackoffTests`: evaluator well covered (defer@1, stuck gate,
     forward-progress reset, both backstops). Candidate gap: explicit backstop-evaluated-BEFORE-stuck-gate
     ordering when both would fire; malformed-coordinate-fails-open (treated as progress).
   - (f) `Emission.Tests/AtomicStreamedObjectsTests`: comprehensive. Candidate gap: the commit-incomplete
     guard's specific `DataPipelineException` ("refusing to report success") surfaced from
     `ResultsBatchPublisher` — verify `FailedPublish_*`/`CompleteFailure_*` pin it; add only if not.

3. **(c)(d) Assertion-depth audit** of `Conducting.Tests/AdapterBusEntrypointRunnerInvariantTests`
   (PartialSuccessWins + Cancellation tests exist). Verify they assert: exactly ONE CompletionRequest,
   `Success:true` with partialCompletion metadata attached, NO failure completion, zero publications on
   cancellation, `CancelledResult` retryable. Deepen assertions where shallow; do not duplicate.
