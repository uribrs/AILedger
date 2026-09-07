# Verifier-1 Report — Invariant Pin Tests

Branch: `tests/invariant-pins` @ 57722ac (diff base: `dev`). Verified independently against source, not executor claims.

## Verdict: PASS

The core deliverable and every audit obligation are satisfied. One MINOR interpretation note on a (c)/(d) sub-item — the invariant is net-covered, the placement differs from the contract's literal wording. Not a defect; recorded for transparency.

## Criterion Table

| # | Success Criterion | Result | Evidence |
|---|---|---|---|
| a1 | `_checkpoint.kind` = "StateSnapshot" | PASS | Test L88 vs executor L345 |
| a2 | `_checkpoint.reason` starts "deferred-recovery:" | PASS | Test L89 asserts full `deferred-recovery:pin-reason`; executor L346 `$"deferred-recovery:{reason}"` |
| a3 | `itemsInBatch`/`findingsInBatch` = 0 | PASS | Test L90-91 vs executor L347-348 |
| a4 | `_resilience.recovery.*` survive real AdapterCheckpoint→GetData→Seed round trip | PASS | Test L104-140; constructs REAL `AdapterCheckpoint` (L117), `CheckpointAdapter.GetData` (L126), `SeedFromPersistedState` (L128), `Load` (L130); asserts count/total/coordinate/errorCode/firstDefer/resumeAfter |
| a5 | Seed copies ONLY `_resilience.*` keys | PASS | Test L139 asserts `lastWatermark` NOT seeded; budget `AllKeys` loop (AdapterRecoveryBudget L169) copies only resilience keys |
| a6 | Clear vs ClearEpisode semantics | PASS | Test `ClearRemovesEverything_ClearEpisodePreservesBackstop` L182-201; ClearEpisode preserves backstop, Clear wipes both |
| b | (b) classifier audit verdict recorded + gap filled | PASS | execution_notes (b); 3 depth-cap tests added; verified below |
| e | (e) budget audit verdict recorded + gaps filled | PASS | execution_notes (e); ordering pin + fails-open + persistence layer |
| f | (f) emission no-op verdict with evidence | PASS | All 6 cited `AtomicStreamedObjectsTests` names exist (grep confirmed) |
| c/d | runner: exactly-1 completion, Success:true, partialCompletion present, no failure; cancel→0 publish + CancelledResult retryable | PASS (1 minor note) | See Findings F1 |
| build | `dotnet build IntegrationInfra.slnx` 0 errors | PASS | 0 Error(s), 19 warnings (pre-existing NU1507/CS1574) |
| test | full suite green incl. new | PASS | 237/237 (Kernel 69, Job 50, Emission 49, FG 27, Conducting 16, Reporting 15, Conversation 11) |
| tests-only | no product code touched | PASS | `git diff dev...HEAD --stat` = 4 files, all under `tests/` |
| no dup | no duplicated tests | PASS | No dup method names in FG.Tests; only pin file references checkpoint-key/seed scope |

## Findings

### F1 (MINOR) — "partialCompletion metadata present" is pinned at the executor seam, not the runner test
The contract lists "partialCompletion metadata present" among the (c)/(d) *runner* assertions. `PartialSuccessWins_...` (Conducting test L72) proves partial-wins via `result.Data["marker"] == "partial"` — the synthetic test-double partial payload — plus `Assert.Single(completions)` + `completion.Success` + ErrorRequest Never. The literal `partialCompletion` key is NOT asserted here.

This is correct, not a gap: the `partialCompletion` key is attached by the executor's `CompletePartialOrPublishAsync` (AdapterFailureDecisionExecutor L85), not by the runner's `TryBuildPartialSuccessResult` seam the test drives — so it cannot exist on this synthetic path. The real `partialCompletion` metadata shape IS pinned in `DeferredRecoveryCheckpointPinTests` L98-100 (`Assert.IsType<AdapterPartialCompletionMetadata>(result.Data["partialCompletion"])` + Reason + `deferredRecovery`=true). The invariant is net-covered; only its placement differs from the contract's literal phrasing. No action required.

### Depth-cap off-by-one (the flagged risk) — CORRECT, no off-by-one
Classifier walk `EnumerateExceptionChain` yields depth 0..9 (`while current != null && depth < 10`), i.e. 10 exceptions examined. `WrapInPlainLayers(inner, 9)` places the marker/circuit type at depth 9 → reached → assert True. `WrapInPlainLayers(inner, 10)` places it at depth 10 → never examined → assert False. Both `IsRetryableTransportFailure` marker and `IsCircuitBreakerException` variants genuinely exercise the cap boundary. Local `BrokenCircuitException` FullName contains the matched token. Verified against HttpTransportFailureClassifier.cs L81-92.

### (e) ordering pin — CORRECT
`BackstopAndStuckGateBothFire_BackstopReasonWins` sets `TotalDeferralCount = MaxTotalBudgetedDeferrals` (→ 51 > 50) with `ConsecutiveNoProgressCount = 5 > maxRetries 3`. Evaluator checks backstop (RecoveryBudgetEvaluator L38) BEFORE stuck gate (L50), so `backstop-total-deferrals` wins. Pin matches source order.

### Cancellation retryability note — accurately recorded, behavior pinned as-is
Runner builds `AdapterResult.CancelledResult("Operation cancelled", "OPERATION_CANCELLED")` (AdapterBusEntrypointRunner L152). Test L92-96 pins `Success=false`, `ErrorCode="OPERATION_CANCELLED"`, `Status=Cancelled`, zero completion/error publications. execution_notes (c)/(d) correctly records the abandoned `IsTransient==true` assumption as wrong (CancelledResult carries the Cancelled discriminator, not a transient flag; retryability lives in no-publish + host NACK). No product change made — correct discipline for a pinning task.

### (f) CommitIncomplete guard untested — acceptable
The `DataPipelineException` commit-incomplete guard branch is defense-in-depth (unreachable through honest FinalizeAsync). Not pinning it is a defensible altitude call for a behavior-pin task; the surrounding abort/no-complete invariants ARE pinned by the 6 cited tests.

## Evidence
- Diff: 4 files, +294/-3, all `tests/` (AdapterBusEntrypointRunnerInvariantTests, DeferredRecoveryCheckpointPinTests [new], RecoveryBudgetAndBackoffTests, HttpTransportFailureClassifierTests). No `src/` touched. `src/Directory.Build.props` untouched (no version bump).
- Build: 0 errors.
- Full suite: 237/237 passed, 0 skipped.
- Existing tests preserved: `ThrowingHostCallback_...` unchanged; the two deepened tests added assertions only, none weakened/renamed.
