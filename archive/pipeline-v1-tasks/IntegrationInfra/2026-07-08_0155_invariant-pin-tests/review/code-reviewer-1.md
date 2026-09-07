# Code Review — invariant-pin tests

## Scope & calibration

- **Change type:** test-only code (xUnit). Four files touched: one new (`DeferredRecoveryCheckpointPinTests.cs`, 222 lines), three modified (`AdapterBusEntrypointRunnerInvariantTests.cs`, `RecoveryBudgetAndBackoffTests.cs`, `HttpTransportFailureClassifierTests.cs`).
- **Risk level:** Low as code (tests don't ship). But the *subject* under pin is High-risk (recovery-budget accounting, checkpoint round-trip, transport retry classification, run-invariant publish semantics), so a weak or wrong test here is a false safety signal on load-bearing behavior. I reviewed the tests against the production code they exercise (`AdapterRecoveryBudget`, `RecoveryBudgetEvaluator`, `AdapterFailureDecisionExecutor`, `HttpTransportFailureClassifier`, `AdapterBusEntrypointRunner`, `AdapterBusPartialSuccessPublisher`).

## Verification performed

- Traced every assertion against production behavior; all arithmetic and expected values are correct (see notes below).
- Built all three test projects (`-f net8.0`): **build succeeded**, 0 errors.
- Ran the new/modified tests in isolation: **35/35 pass** (7 FaultGovernance, 3 Conducting invariant, 25 Kernel transport-classifier).

## Assessment

**No material findings.** These are competent, correctly-calibrated tripwire tests. The invariant-test edits are genuine *strengthenings* (a weak `Times.AtLeastOnce` replaced by an exact `Assert.Single` + success-flag check; a cancellation test that previously only checked "no publish" now also pins the `OPERATION_CANCELLED` / `Cancelled` discriminator the host requeues on). Arithmetic is right, there are no tautologies or accidental always-pass shapes, and the tests are deterministic. What follows is observation-level only.

## Findings

### 1. Depth-cap tests pin an internal tuning constant by exact value — Observation

`IsRetryableTransportFailure_MarkerBeyondDepthCap_IsNotReached` (and the circuit-breaker twin) assert that a marker at chain depth 10 is *not* reached, pinning the `depth < 10` guard in `EnumerateExceptionChain` exactly. That guard is a defensive bound against pathological/cyclic inner-exception chains, not a documented contract. Any deliberate retune of the cap will break these tests in lockstep and require a coordinated edit.

- **Impact:** none today; the boundary (depth 9 reachable, depth 10 not) is verified correct against the current implementation, and the test comments state the intent explicitly ("If this starts failing, the walk's depth cap changed"), so a future failure is self-explaining rather than mysterious.
- **Recommendation:** keep as-is. The explicit comment is the right mitigation for a tripwire on a magic constant. No change needed.

### 2. `WrapInPlainLayers` filler is inert by design — Observation (confirming, not a defect)

The helper wraps with `InvalidOperationException`, which is neither `IOException`/`HttpRequestException` nor `SocketException`, so the marker-match path is skipped for filler regardless of message text. This is what makes "marker at depth N, filler above it" a clean test of *depth reach* rather than of message matching. Correct as written — flagging only because the inertness is load-bearing and non-obvious, so a future edit that switches the filler type to something the classifier inspects would silently invalidate the depth tests.

### 3. Budget round-trip is a hand-rolled simulation of the host path — Observation

`BudgetFields_SurviveAdapterCheckpointRoundTrip...` and `SecondDeferAtSameCoordinate...` reconstruct the resume hop manually (build `AdapterCheckpoint` → `CheckpointAdapter.GetData` → `AdapterRecoveryBudget.SeedFromPersistedState`), commented as "the way `CollectorResumeSetup` does". The test therefore pins the *contract* (only `_resilience.*` keys survive, collector business keys like `lastWatermark` are not seeded) rather than the actual host wiring. If `CollectorResumeSetup` ever stops calling `SeedFromPersistedState` (or seeds differently), these tests stay green while the real resume path regresses.

- **Impact:** coverage boundary, not a correctness flaw. The seed/round-trip contract itself is well-pinned, and the "seed copies ONLY resilience keys" assertion (`lastWatermark` ends up empty) is a genuinely valuable negative check.
- **Recommendation:** acceptable for unit level; if an integration test already covers `CollectorResumeSetup` end-to-end, note that so the boundary is intentional.

### 4. `SecondDeferAtSameCoordinate` proves the count increment but not coordinate restoration — Observation

Both legs trivially compute coordinate `0:0:0` (fresh contexts, no progress). The test correctly verifies that the consecutive-no-progress count accumulates across the persisted budget round-trip. It does *not* exercise the case where progress counters must be restored so that a real advance produces a *different* coordinate (that path depends on the host's `RestoreProgress`, which the test doesn't invoke). So a regression that broke progress-coordinate restoration on resume would not be caught here.

- **Impact:** the test's title claim ("across the round trip") is satisfied for budget state; the coordinate-equality half is incidental (both zero) rather than exercised.
- **Recommendation:** optional — a companion case that advances `ProcessedItems` on the resume leg and asserts the count *resets* to 1 would close the "did the coordinate actually travel" gap. Not required; the reset-on-progress behavior is already covered purely in `RecoveryBudgetAndBackoffTests.ForwardProgress_ResetsConsecutiveCount`.

### 5. Boxed-bool equality assertions — Nit

`Assert.Equal(true, partialCompletion.AdditionalData["deferredRecovery"])` and `Assert.Equal(false, result.Data!["useRecoveryBudget"])` compare boxed `object` values. They work and pass, but `Assert.True(...)` / `Assert.False(...)` after an explicit cast reads more clearly and gives a better failure message on regression. Style only.

## Notes on the parts that are done well

- The `BackstopAndStuckGateBothFire_BackstopReasonWins` ordering pin is meaningful: with `count=6 > maxRetries=3` **and** `total=51 > 50`, both gates would fire, and the test confirms the backstop (checked first in `RecoveryBudgetEvaluator.Evaluate`) wins the fallback reason. Reversing the evaluation order would flip it to `stuck-no-progress` and fail the test — a real behavioral guard, not decoration. It also matches the existing file conventions (`Now`, `"page-1"`, `MaxTotalBudgetedDeferrals`).
- `MalformedPersistedCoordinate_FailsOpen_AsIfProgressed` correctly demonstrates the safe-direction coercion (corrupt coordinate → `null` → treated as progress → count resets to 1, no fallback), and anchors it with distinct non-default prior state so it can't pass accidentally.
- `UnbudgetedWait_ClearsEpisode_ButPreservesBackstop` is not an accidental always-pass: the `TotalDeferralCount==1` + `FirstBudgetedDeferAtUtc != null` assertions prove the first budgeted defer really wrote state, so the "episode cleared, backstop preserved" distinction between `ClearEpisode` and `Clear` is genuinely exercised.
- The `PartialSuccessWins` edit tightens a real gap: the old `Times.AtLeastOnce` would have passed even on a double-publish; `Assert.Single` + `completion.Success` now pins exactly-one-success-completion, which is the actual invariant.
