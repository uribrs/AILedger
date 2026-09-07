# Execution Notes

Branch: `tests/invariant-pins` (off `dev`).

## (a) SDK checkpoint-key pin — DELIVERED (core)
New `FaultGovernance.Tests/DeferredRecoveryCheckpointPinTests.cs`, 6 tests, driven through the REAL
`AdapterFailureDecisionExecutor` with a real `AdapterProgressContext.FromPlatformEvent(...)` (no mocks needed):
- `DeferredWaitSnapshot_WritesSdkPrivateCheckpointKeys_AndFiresOnCheckpointOnce` — `_checkpoint.kind=StateSnapshot`,
  `_checkpoint.reason=deferred-recovery:*`, items/findingsInBatch="0", OnCheckpoint exactly once, PartialResult
  shape (WaitReason, ResumeAfter=plan delay, `partialCompletion` metadata).
- `BudgetFields_SurviveAdapterCheckpointRoundTrip_AndSeedCopiesOnlyResilienceKeys` — real `AdapterCheckpoint`
  round trip; all `_resilience.recovery.*` fields survive; business key (`lastWatermark`) NOT seeded.
- `SecondDeferAtSameCoordinate_IncrementsConsecutiveCount_AcrossTheRoundTrip` — count 1→2 across the hop.
- `UnbudgetedWait_ClearsEpisode_ButPreservesBackstop`.
- `ClearRemovesEverything_ClearEpisodePreservesBackstop`.
- `MalformedPersistedCoordinate_FailsOpen_AsIfProgressed` (also closes the (e) fails-open gap).

## (b) Classifier audit — verdict: ONE gap (filled), rest covered
- Covered already (evidence): all 9 transient SocketError InlineData rows; marker theory + nested-marker;
  circuit-breaker by local type-name double + nested + unrelated; OCE/TaskCanceled false; Timeout true;
  null-arg throws; `RetryParameters_AreConsistent` pins exact [30s,60s,120s] + MaxAttempts=4.
- GAP FILLED: inner-chain depth cap — 3 new tests in `HttpTransportFailureClassifierTests`
  (marker at depth 9 reached / depth 10 not; circuit type beyond cap not reached).
- Accepted (not tested): the Polly pipeline's delay-index clamp in FaultGovernance's `UnknownFlowRetryPolicy`
  — parameters are pinned in Kernel; pipeline delay selection needs Polly time control, disproportionate.

## (e) Budget audit — verdict: TWO gaps (filled)
- Covered already: defer@1, stuck gate past MaxRetries, forward-progress reset, both backstops, GetDelay family.
- GAP FILLED: `BackstopAndStuckGateBothFire_BackstopReasonWins` (ordering pin) in RecoveryBudgetAndBackoffTests.
- GAP FILLED: malformed-coordinate fails-open + the whole persistence layer (Load/Write/Clear/ClearEpisode/Seed)
  — in the new pin file.

## (f) Emission audit — verdict: NO-OP (evidence)
Contract points covered by existing `AtomicStreamedObjectsTests`:
`MidStreamPartFailure_AbortsExactlyOnce_NoComplete_ThenRetrySucceeds`,
`CompleteFailure_AbortsExactlyOnce_AndSurfacesFailure`, `FailedPublish_ProducesNoSuccessfulResult_AndNeverCompletes`,
`DisposeWithoutComplete_AbortsInFlightMultipart`, `Cancellation_MidStream_AbortsInFlightMultipart`,
`MultipartEndingOnPostAppendFlush_StillCompletesOnce_AndDoesNotAbort`.
The `CommitIncomplete` guard's own branch is defense-in-depth: unreachable through honest session behavior
(FinalizeAsync completes-or-throws); triggering would require a pathological double. No test added.

## (c)/(d) Runner deepening — DONE in place
- PartialSuccessWins: now captures published CompletionRequests → asserts EXACTLY ONE with `Success == true`
  (was Times.AtLeastOnce with no Success assertion); ErrorRequest never.
- Cancellation: now also pins `ErrorCode == "OPERATION_CANCELLED"` and `Status == AdapterResultStatus.Cancelled`.
  NOTE: first attempt asserted `IsTransient == true` — WRONG assumption; `CancelledResult` is not
  transient-flagged. Retryability of cancellation is carried by the no-publish + host-NACK semantics (already
  asserted), the result carries the Cancelled discriminator. Pinned actual behavior; not a product bug.

## Assumptions resolved
- AdapterCheckpoint constructible in tests: VALIDATED (object initializer; required members CurrentPage,
  ProcessedItems, CreatedAtUtc).
- Executor drivable with fake context: VALIDATED (plain delegates + real FromPlatformEvent context; no Moq in FG.Tests).

## Verification
- Build: 0 errors. Full suite: 237/237 (was 227): Kernel 69, FaultGovernance 27, Conducting 16, Job 50,
  Emission 49, Reporting 15, Conversation 11. No product code touched.
