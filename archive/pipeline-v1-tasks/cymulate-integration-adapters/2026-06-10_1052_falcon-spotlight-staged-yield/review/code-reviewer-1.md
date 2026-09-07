# Code Review — Falcon staged Spotlight collection + planned yields + cursor TTL

**Change type:** feature logic on a shared, resumable collector (findings flow).
**Risk level:** High — touches pagination state, checkpoint round-trip, retry/recovery classification, distributed resume across pods. Small logic errors here mean silent data loss or runaway waits.
**Stack:** C# / .NET 8, xUnit + Moq + FluentAssertions.

Build of the FalconCollector project succeeds (only offline `NU1900` feed warnings). Review is on runtime correctness, not compilation.

Severity legend: Blocker / Major / Minor / Nit / Observation.

---

## Findings

### 1. [Major] Cross-lane resume on the user-filter path collects zero findings for every lane after the first

**File:** `Flows/Findings/FalconFindingsFlow.cs` (lane loop ~L209-258 + `FillAidBatchesAsync` L659-662); interacts with `Flows/Findings/FalconFindingsCheckpointWriter.cs:WriteCursorlessYieldCheckpoint` and `Recovery/FalconCursorSanitizer.cs`.

**Problem.** When a user FQL filter is present (`hasUserFilter == true`), the flow treats it as an *asset* filter: it paginates the assets endpoint to collect AIDs, then queries Spotlight scoped by those AIDs. When lane `open` completes, its AID channel is fully drained, so the instance fields end the lane with `_assetsPaginationCompleted == true`, `_pendingAids` empty, and the assets cursor consumed.

The status-stage yield then persists a checkpoint with `assetsPaginationCompleted: _assetsPaginationCompleted` (= `true`), and the cursor sanitizer additionally nulls `AssetsAfterToken`/`AssetsStageAfterToken`/`AssetsAfterToken`. On the resumed invocation for lane `reopen`, the flow restores `_assetsPaginationCompleted = true` (L165) and calls `FillAidBatchesAsync(..., assetsPaginationAlreadyCompleted: true, ...)`, which returns immediately (L659-662) and emits no AID batches. With no AIDs and no pending AIDs, the `reopen` and `closed` lanes query Spotlight for nothing and publish zero findings.

**Why it matters.** Silent under-collection on a real, supported configuration (user-supplied asset filter). The non-user-filter path — which queries Spotlight directly per lane with `aidBatch: null` — is unaffected, and that is the only path the new tests exercise, so the bug is invisible to the suite.

**Impact.** Data loss (missing reopen/closed vulnerabilities) for filtered findings runs whenever a status-stage yield occurs. No error is raised; the run reports success.

**Evidence threshold:** Confirmed by the pagination logic; the cross-lane user-filter resume is not covered by any test, so I could not observe it green/red, but the control flow is unambiguous.

**Recommended fix.** Decide the intended semantics and encode it:
- The AID set is independent of vulnerability status, so the natural design is to collect AIDs once and reuse them across lanes. Persist the full resolved AID list (or the assets resume position with `AssetsPaginationCompleted=false` and a live re-anchor) in the checkpoint so later lanes can re-derive the same AID set, **or**
- explicitly re-run asset pagination per lane by *not* carrying `AssetsPaginationCompleted=true` across a lane boundary (reset assets-stage state when advancing lanes for the user-filter path).

Either way add a multi-lane resume test **with a user FQL filter** asserting that `reopen`/`closed` each query Spotlight with a non-empty AID clause. Local patch on the flow + checkpoint-writer; not an architectural refactor.

---

### 2. [Major] Invalid `spotlightStatusStages` config is not validated; it fails the run via blind retries at request time

**File:** `Processing/Configuration/FalconCollectorConfigurationBuilder.cs:ParseSpotlightStatusStages` (L343-361); manifests in `FalconSpotlightLaneFilter.ApplyStatus` and `FalconFlowRetryPolicy`.

**Problem.** `ParseSpotlightStatusStages` splits, trims, and lowercases the comma string but performs **no** validation against `FalconSpotlightLaneFilter.SupportedStatuses`. An operator typo or an unsupported value (e.g. `"expired"`, `"opne"`) is accepted as a lane. At run time `FalconSpotlightLaneFilter.ApplyStatus` throws `ArgumentException` for the unsupported lane. That exception is **not** a `FalconPlannedYieldException`, so `FalconFlowRetryPolicy` classifies it as an unknown candidate and blind-retries it (30s/60s/120s) before the run hard-fails.

**Impact.** A pure configuration mistake costs ~3.5 minutes of pointless retries and then fails the whole findings flow — and the failure surfaces deep in the flow rather than at config-build time where it belongs. Determinstic input error retried as if transient (violates the retry-only-transient rule).

**Recommended fix.** In `ParseSpotlightStatusStages`, reject entries not in `FalconSpotlightLaneFilter.SupportedStatuses` — either fall back to the default (consistent with the "ignore unparseable, keep default" convention the comment claims) or fail fast at build time with a clear message. Returning the default on any invalid entry is the smaller, safer change. Add a builder test for an invalid stage value. Local patch.

---

### 3. [Minor] Spurious volume yield possible on the true-final data page when the lane's terminal fetch returns an empty page

**File:** `Flows/Findings/FalconFindingsFlow.cs:BuildOnPagePublished` (`isFinalPageOfFinalLane` derivation, ~L683) + `FalconSpotlightVulnerabilitiesRunner.cs:245-248`.

**Problem.** `isFinalPageOfFinalLane` is `lane.IsFinalLane && string.IsNullOrWhiteSpace(page.AfterToken)`. The runner only invokes `onPagePublished` for pages with emitted records; if the last record-bearing page carries a non-empty `after` token and the *next* fetch returns zero records, the runner returns early (L245-248) without a callback. The final data page therefore had a non-empty `after` token, so `isFinalPageOfFinalLane` is `false`, and if the volume threshold was crossed on that page the flow yields 5m even though the run is effectively done.

**Impact.** One unnecessary 5-minute scheduled wait at the very end of a large run; on resume the empty page is observed and the run completes. No data loss, no budget consumption (unbudgeted). Low operational cost; worth a comment or a tightening if cheap.

**Recommended fix.** Optional. If you want to suppress it, treat "final lane + threshold crossed" more conservatively, or accept it and document that the terminal yield is harmless. Not merge-blocking.

---

### 4. [Minor] Volume yield and status-stage yield can both be "due" at a lane boundary, producing two consecutive waits

**File:** `Flows/Findings/FalconFindingsFlow.cs` (volume yield in `BuildOnPagePublished`, stage yield after `CompleteLane`).

**Problem.** If the last page of a non-final lane crosses the volume threshold, the volume yield throws from inside the callback first. On resume the lane completes and the status-stage yield fires. The two waits stack (5m then 10m) across two extra invocations for the same boundary.

**Impact.** Extra latency only; correct and unbudgeted. Acceptable tradeoff given the staged design. Noting for awareness, not action.

---

### 5. [Observation] Planned-yield exception is correctly isolated from the cursor-expired / 5xx recovery paths

**File:** `Flows/Findings/FalconSpotlightVulnerabilitiesRunner.cs:315` vs the `try/catch` at L179-207.

The `onPagePublished` callback (which can throw `FalconPlannedYieldException`) is invoked **outside** the `try` that catches `FalconCursorExpiredException` / 5xx-with-after. So a planned yield cannot be swallowed into a watermark-reset `continue`. Combined with `FalconFlowRetryPolicy` excluding the type from blind retry, and the resilience factory + classifier routing it to an unbudgeted `RequestDeferredRecovery`, and `RecoverFresh/ResumeFindingsAsync` short-circuiting to `Continue` (honoring the pre-written cursorless checkpoint), the control-flow-via-exception is cleanly separated from real failures. This is the highest-risk part of the change and it is implemented correctly. Confirmed by reading the call sites and `AdapterFailureDecisionExecutor` (unbudgeted path `Clear`s the budget, never `Write`s).

---

### 6. [Observation] Checkpoint round-trip parity for the five new fields is correct

**Files:** `Recovery/FalconCheckpointHelper.cs` (save L190-200 / load L502-547), `Recovery/FalconCheckpointState.cs`.

Persist and restore are symmetric for `spotlightStatus` (empty-string ⇄ null), `spotlightLaneIndex`, `spotlightCompletedLanes` (JSON, with a best-effort `JsonException` fallback to empty), `spotlightNextVolumeYieldThreshold`, `spotlightCumulativeFindingsCount`. Legacy checkpoints lacking the keys default cleanly (lane 0, empty completed set, zero counters), and `ResolveLane` clamps a stale/out-of-range index, so a pre-staged checkpoint resumes at lane 0 rather than throwing. Negative parsed values are rejected. Good defensive parsing.

---

### 7. [Observation] Cursor-TTL sanitizer and floor re-anchoring are sound

**Files:** `Recovery/FalconCursorSanitizer.cs`, `FalconCheckpointHelper.ApplyCursorTtlForResume`, `FalconResumeRunner.SanitizeResumeCursors`, `FalconRecoveryContinuationBuilder`.

The 120s gate is correctly independent of the ~23h whole-checkpoint stale threshold; `fromScheduledWait` forces the drop; missing `CheckpointCreatedUtc` is treated as stale. `ApplyCursorTtlForResume` re-anchors `MonthSegmentFloorUtc` from `LastWatermark` only when a cursor was actually dropped (`ReferenceEquals` guard), and leaves watermark/floor intact otherwise so the after-less scroll falls back to month-segment floor / base date — lossless given the boundary-ID dedup. The cursor-expired continuation preserves the existing byte-for-byte re-anchor and additively sanitizes the other two cursors. Pure, well-tested helpers.

---

### 8. [Observation] `EvaluateVolumeYield` threshold arithmetic is correct including multi-multiple crossings

**File:** `Flows/Findings/FalconSpotlightStageSequencer.cs:120-148`.

`yieldEvery <= 0` disables; zero `nextThreshold` seeds to `yieldEvery`; a single page crossing N multiples advances `effectiveThreshold + crossings*yieldEvery` so the next yield only fires on a fresh crossing; final-page-of-final-lane never yields but still reports the advanced threshold for a faithful terminal checkpoint. Cross-checked the `crossings` formula at the boundary (`cumulative == effectiveThreshold` → crossings 1) and mid-multiple. Correct.

---

### 9. [Nit] `FindingsFlowRunConfig` uses a fully-qualified type reference for the default

**File:** `Dtos/FindingsDtos/FindingsFlowRunConfig.cs` — `SpotlightStatusStages` default is written as `Cymulate.Integration.Adapters.Collectors.FalconCollector.Processing.Configuration.FalconCollectorConfiguration.DefaultSpotlightStatusStages`. A `using` would read better, but `FindingsFlowRunConfig` is a config DTO and the FQN avoids a namespace cycle ambiguity; harmless. No action.

---

## Test Quality

Strong, behavior-driven coverage — not trivia:

- **`FalconStagedSpotlightFlowTests`** drives real `ProcessAsync`/`ResumeAsync` across three invocations with a fake HTTP factory, asserting: `PartialWaitRequired` + exact wait reasons/delays, no failure/no completion on yield, the published crossing page (A3), per-lane `status:'...'` clauses in the actual request URL, cursorless lane-advanced checkpoints, and that no completed lane is replayed. This is the right altitude for a distributed-resume feature.
- **Sequencer / lane-filter / sanitizer / resilience** unit tests cover boundaries (empty stages, clamp, idempotent completion, disabled yield, TTL edges at ±121s and scheduled-wait-within-TTL, unbudgeted decision shape, wait-reason prefix lock).
- New tests are **additive**; no existing assertions were weakened or over-mocked.

**Gap (ties to Findings #1 and #2):** every flow test uses the no-user-filter path and only valid stage values. The two Major findings live precisely in the untested user-filter resume path and in unvalidated config. Add: (a) a multi-lane resume test with a user FQL filter asserting non-empty AID scoping on `reopen`/`closed`; (b) a builder test for an invalid `spotlightStatusStages` entry.

---

## Overall Assessment

The hard part of this change — using an exception for a planned control-flow yield without letting it be caught, retried, or published as a failure, and without consuming the recovery budget — is implemented correctly and is well isolated (Findings #5). Checkpoint round-trip, cursor-TTL sanitization, floor re-anchoring, and the volume-threshold arithmetic are all sound and properly unit-tested (#6-#8). The pure helpers are clean, immutable, and idiomatic.

Two Major issues must be addressed before merge:

1. **Cross-lane resume on the user-filter path silently collects nothing for lanes after the first** (#1) — a data-loss regression on a supported configuration, hidden by the test suite's exclusive use of the no-filter path.
2. **Invalid stage config is retried as transient and fails the run** (#2) — a deterministic input error mishandled as a transient fault.

Neither requires an architectural refactor; both are local patches plus the missing tests. The remaining items are minor latency tradeoffs or confirmations. Recommendation: **fix #1 and #2 (with the two missing tests) before merge**; #3 and #4 are accept-or-tidy at the author's discretion.
