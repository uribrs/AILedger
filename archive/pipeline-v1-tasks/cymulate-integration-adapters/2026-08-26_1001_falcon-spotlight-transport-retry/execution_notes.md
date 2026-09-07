# Execution Notes

Path: decompose. Phase 0 in the main thread, then W1 and W2 in parallel over disjoint file sets, then docs
and synthesis in the main thread.

## Phase 0 — shared surface (main thread)
- NEW `Exceptions/FalconTransportFailureException.cs`. Sealed, two ctors, mirroring
  `FalconResumeNotPossibleException`. Its remarks carry the no-inner-exception rule and why.
- `FalconCollectorConfiguration`: `SpotlightTransportMaxRetries` (default 3, clamp 0..10) and
  `SpotlightTransportRetryBaseDelaySeconds` (default 2, clamp 1..30), with `private const` defaults per the
  file's idiom. Also corrected the now-false degree-1 sentence in `MaxConcurrentSpotlightBatches`' remarks.
- `FalconCollectorConfigurationBuilder`: extract+clamp, `ExtractedFields` members, fold in
  `ApplyOptionalOverrides`. Not added to `FalconIdentification` — its sibling knob
  `maxConcurrentSpotlightBatches` is not there either, so the precedent is followed.
- Mid-flight rename `FalconSpotlightTransportFailureException` -> `FalconTransportFailureException` once the
  type gained a second throw site.

## Main thread (files owned by no worker)
- `FalconFindingsFlow.cs`: pump ctor call site; a new catch on the Phase 2 consumer loop, after the existing
  `OperationCanceledException` catch, re-throwing any retryable transport failure as
  `FalconTransportFailureException` (re-throw only, no retry) — this is the R5 mitigation.
- `FindingsFlowRunConfig` + `FalconFlowRunPreparer`: carry both knobs as `required` straight carries, no
  second clamp, matching `MaxConcurrentSpotlightBatches`. (Falcon's `FindingsFlowRunConfig` is a distinct
  record from InsightVmCloud's same-named one; no cross-collector impact.)
- `FalconTwoPhaseFindingsTests.cs` and `FalconDeferralPositionObservationTests.cs`: the two other
  construction sites `required` flagged, set to the shipped defaults with a stated reason.
- Docs: `01-collection-strategy.md` (4 passages), `03-current-concerns.md` (new section),
  `ai/skills/collector-execution-and-recovery/SKILL.md` (line 27 rewritten — it asserted Falcon "stays on the
  legacy PublishFailure(IsRetryable=true) shape", which is now false, and the premise behind that shape is
  disproved).

## W1 — pump (owns pump, harness, concurrency tests)
- Retry loop in `FetchAsync`: `for (attempt = 1; ; attempt++)`, exhaustion at `attempt > _transportMaxRetries`.
  Attempt body extracted to `ScrollBatchAsync` so `stats`/`records` are per-attempt.
- `_fetchedBatches` increments once per batch after the successful attempt.
- Exponential-only ladder, `base × 3^(attempt-1)` + <=1s jitter. No `Retry-After` and no 429 rung: both are
  unreachable on this path and a branch that cannot fire reads as coverage that is not there.
- `Task.Delay` honours the cancellation token.
- `PumpSequentiallyAsync` (degree 1) now routes through `FetchAsync` + `ReplayAsync`; `CountCompletedScrollAsync`
  deleted as dead. Class-level and method-level docs rewritten to say degree 1 no longer restores the old
  memory profile and to point at `aidBatchSize` as the knob that moves per-batch cost.
- 5 new tests (see disposition table in review/).

## W2 — classification and routing (owns classifier, factory, 3 test files)
- Classifier arm -> `FALCON_TRANSPORT_FAILURE`, retryable, placed above the guarded base-`Exception` arm.
- Third named branch in `CreateRetryableDecision`, `Reason: "falcon-transport-failure"`,
  `UseRecoveryBudget: false`, reusing the existing plan factory — renamed
  `CreateServerErrorRecoveryBackoff` -> `CreateUnbudgetedFlatRecoveryBackoff` since it now serves three
  conditions. Calls `LogRecoverDecision` like every other branch.
- 3 new strategy tests incl. both tripwires, plus classifier and config-builder tests.

## Test gate
`dotnet build` on the test project: 0 warnings, 0 errors.
Filtered run (FalconSpotlightConcurrencyTests | FalconResilienceStrategyTests |
FalconFlowExceptionClassifierTests | FalconCollectorConfigurationBuilderTests |
FalconDeferralPositionObservationTests): **80 passed, 0 failed, 18s.**
Full suite deliberately NOT run (operator rule: risky simulation classes hang).

## Not done, deliberately
- No `.csproj` touched; no version bump. `CollectorVersion` is the operator's call —
  **suggested magnitude: minor**, a behaviour change to a shipped recovery path plus a config surface, not
  a patch.
- `FalconHttpFailureClassifier` still omits `retryAfter` when constructing
  `AdapterHttpRequestFailedException`, so Falcon can never see a vendor `Retry-After`. Real, small, and a
  separate change.
- Staged-page and publish transport faults defer but are not retried (R5, partial).

## Review cycles

**verifier-1** — pass with findings (6). Repaired all six: the stale degree-1 claim in `FalconFindingsFlow.cs`;
**R4's ladder had no per-attempt cap** (clamps bound the inputs, not the exponential output — 6.8 days at the
legal maximum) so a 30s cap was added and the arithmetic made directly testable; the R5 guard got a test via a
new `ReadFault` seam on the staging double; the Phase 1 / assets residual was recorded; the circuit-open relabel
was recorded; the two-arg ctor was deleted and the docs corrected to three recovery classes.

**verifier-2** — **pass**. All six closed, each checked at its citation. It re-derived the R4 bound
independently and attacked the R5 test rather than trusting it (removing the guard makes it fail). Three
non-blocking wording notes, all taken.

**code-reviewer-1** (isolated, no contract/plan/verifier output) — 1 Major, 3 Minor, 2 Nits. All addressed:

- **Major — stopped retrying an open circuit.** The pump's predicate ORed in `IsCircuitBreakerException`, but
  Falcon's break duration is 300s against a ~26s ladder, so every such retry was certain to fail: it burned the
  whole stall budget, held `degree` record sets resident, issued doomed calls, and then took the deferral
  anyway — strictly worse than deferring, which is the one thing the cap exists to prevent. Dropped the clause.
  An open circuit now escapes raw, position 4 declines it, and Falcon's classifier keeps the
  `falcon-server-error` label — which also **undoes the relabel** verifier-1 had asked be documented, so
  `02-decision-making.md` was reverted accordingly.
- Minor — jitter was added AFTER the cap, so the "5 minutes" bound was really 310s. Jitter is now subtracted
  from the cap instead, keeping jitter at the ceiling (batches hitting the cap together would otherwise
  re-issue in lockstep against a shared per-customer rate budget) and making the bound exactly true. Test
  allowance removed.
- Minor — every stall guarantee was phrased against "the worst LEGAL configuration", i.e. enforced in a
  different type. Added `MaxTotalTransportRetryStall` (5 min) enforced in the loop, so the invariant holds
  whatever a caller passes. Not separately tested: unreachable through the builder's clamps, and the pump takes
  a concrete scroller, so a direct test would need a harness disproportionate to an unreachable path.
- Minor — the R5 test's docstring claimed an ordering guarantee it cannot provide (the two catches are disjoint
  by construction). Claim and decorative assertion removed; replaced with a pinned publish count and a real
  `result.Success` assertion. **Pinning immediately caught that my expected value was wrong** — 2, not 1.
- Nits — `ExhaustedTransportRetries` → `CreateExhaustedTransportRetriesFailure`; garbled counter comment
  rewritten; the pump/flow predicate asymmetry documented on the predicate itself.

Final gate: build 0 warnings / 0 errors; filtered suite **88 passed, 0 failed**.
