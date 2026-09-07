# Verifier-1 — Falcon Spotlight Staged Collection And Planned Yield

Independent verification against `prompt_contract.md` Success Criteria 1–6, constraints, and
assumptions A1–A14. Every verdict is grounded in the working-tree code and the Falcon test project,
not the worker reports. Paths are absolute; line refs are HEAD working-tree.

Note on git state: the implementation is **uncommitted** in the working tree on branch
`falcon-collection-strategy-refactor` (HEAD = `c3cb353`, a master merge). The 5xx→deferred-recovery
behavior and the 500-test rename were introduced by a PRIOR commit `c0ddcc7` ("Handle Falcon 5xx
failures with recovery backoff", 2026-06-08), **not** this task. THIS task's changes were isolated by
diffing the working tree against HEAD.

---

## Build / Test (observed)

- `dotnet test` on the Falcon test project (auto-pinned net8.0 via csproj):
  **Passed! Failed: 0, Passed: 153, Skipped: 0, Total: 153, Duration: 15s.**
- Only warnings were `NU1900` (CodeArtifact index unreachable offline) — benign auth noise, not code.
- Build succeeded on net8.0. **SC6 PASS.**

---

## Per-criterion verdicts

### SC1 — Config: PASS
- `FalconCollectorConfiguration.cs:114-151`: defaults are `open,reopen,closed` /
  `SpotlightStatusStageDelay=10m` / `SpotlightFindingsYieldEvery=1_000_000` /
  `SpotlightFindingsYieldDelay=5m`; `SpotlightCursorTtl = TimeSpan.FromMinutes(2)` is a `static
  readonly` internal constant (not parsed from user config). Matches contract exactly.
- Builder `FalconCollectorConfigurationBuilder.cs:166-186, 351-365`: mirrors the existing
  `cfg with {...}` + `TryGetInt/TryGetBool/TryGetLong` idiom; `TimeSpan` knobs parsed as
  `...DelaySeconds` ints; invalid/empty `spotlightStatusStages` → `null` → keeps typed default
  (A12 resolved as fall-back-to-default, matching the optional-knob convention). New `TryGetLong`
  added for the 1M threshold.
- Covered by `FalconCollectorConfigurationBuilderTests.cs` (+150 lines this task).
- Minor note (not a defect): the builder lowercases lane tokens but does **not** validate them
  against open/reopen/closed; an unsupported lane fails later in `FalconSpotlightLaneFilter.ApplyStatus`
  (ArgumentException → INVALID_FQL → terminal). Acceptable fail-fast, slightly later than config time.

### SC2 — Filters: PASS
- `FalconSpotlightLaneFilter.ApplyStatus` (`FalconSpotlightLaneFilter.cs:31-40`) appends exactly one
  `+status:'<value>'` clause; rejects blank base filter and unsupported status; **no `expired` lane**
  (`SupportedStatuses` = open/reopen/closed only, line 18).
- Composition in the runner (`FalconSpotlightVulnerabilitiesRunner.cs:50-68`): status folds into
  `baseFilterWithoutTimestamp` **after** the AID clause (line 51) and **before** the timestamp range
  (`ApplyUpdatedTimestampRange`, line 65). Suppression (`DefaultVulnerabilitiesFilter`) is the seed,
  so status composes WITH suppression + AID + timestamp, never replacing them.
- Watermark cursor reset re-applies timestamp to the **same** `baseFilterWithoutTimestamp` that
  already carries the status clause (`FalconSpotlightVulnerabilitiesRunner.cs:120-123`), so the status
  survives every cursor reset — preserved-behavior-safe.
- Covered by `FalconSpotlightLaneFilterTests.cs`.

### SC3 — Planned yields: PASS
- **Volume (5m), cumulative, post-publish, never final-page-of-final-lane:**
  `FalconFindingsFlow.cs:562-638` (`BuildOnPagePublished`). Order: `checkpointWriter.OnPagePublished`
  (page published + checkpointed) → `lane.CumulativePublished += page.PublishResult.RecordCount`
  (cumulative across lanes, A2) → `isFinalPageOfFinalLane = lane.IsFinalLane &&
  string.IsNullOrWhiteSpace(page.AfterToken)` (line 593) → `EvaluateVolumeYield(...)` →
  cursorless checkpoint (line 611) → throw `FalconPlannedYieldException(Volume, 5m)`. The
  final-page guard is honored inside the sequencer too (`FalconSpotlightStageSequencer.cs:146`:
  `yieldDue = !isFinalPageOfFinalLane`).
- **Status-stage (10m), after open & reopen, none after closed:**
  `FalconFindingsFlow.cs:269-311`. `CompleteLane` returns `StageYieldDue=false` for the final lane
  (`FalconSpotlightStageSequencer.cs:81-90`), and `AllLanesComplete` short-circuits before the yield
  block (line 275-278). Cursorless checkpoint written before throwing `StatusStage(10m)`.
- **Unbudgeted + no failure/completion:** the decision is `RequestDeferredRecovery(...,
  UseRecoveryBudget: false)` (`FalconResilienceStrategyFactory.cs:64-68`); see the dedicated
  unbudgeted trace below. The Shared path returns `AdapterResult.PartialResult` — never a
  `PublishFailure` or completion event.
- Covered by `FalconStagedSpotlightFlowTests.cs` (volume 5m no-failure/no-completion at lines
  202-209; status-stage 10m at 290-294; final-page-of-final-lane completes without yield at 247-250)
  and `FalconResilienceStrategyTests.cs:164-235`.

### SC4 — Resume / checkpoint: PASS
- **Cursorless yield checkpoint resumes same lane:** `FalconFindingsCheckpointWriter.cs:159-234`
  builds the yield state, runs it through `ApplyCursorTtlForResume(..., fromScheduledWait: true)`
  (line 216) which clears all 3 cursors, calls `progressContext.SetCursor(null,null)` (line 218),
  persists lane fields, and durably emits via `OnCheckpoint` (line 233) BEFORE the throw. Cross-resume
  test asserts `AfterToken/AssetsStageAfterToken/AssetsAfterToken` all null after each yield
  (`FalconStagedSpotlightFlowTests.cs:357-359, 393-394`).
- **Lane progression survives resume; completed lanes not repeated:** flow seeds `LaneRunState` from
  `findingsResumeState.SpotlightLaneIndex/SpotlightCompletedLanes/...` (`FalconFindingsFlow.cs:206-212`);
  the `alreadyCompleted` HashSet skips completed lanes (lines 230, 240-251). Cross-resume test asserts
  open→reopen→closed in order with `SpotlightCompletedLanes` accumulating and final run succeeding with
  no post-final yield (`FalconStagedSpotlightFlowTests.cs:309-424`). A13 bounded-replay documented in
  `FalconSpotlightStageSequencer.cs:17-30` and matches existing cursor-expiry watermark semantics.
- **120s gate drops cursors; <120s keeps unless scheduled wait:** `FalconCursorSanitizer.cs:26-46`
  (`fromScheduledWait` OR missing-time OR age>TTL).

### SC5 — Recovery (unchanged + extended): PASS
- **401/5xx scheduled recovery snapshots cursorless when delay >120s:**
  `FalconRecoveryContinuationBuilder.cs:60-70` — `Sanitize(context.WorkItem)` with
  `fromScheduledWait: true` clears all 3 cursors for the 401/5xx path.
- **Existing 5xx budget + fallback unchanged:** `FalconResilienceStrategyFactory.cs:80-93` keeps the
  scheduled-recovery `RecoverAndRetry` (5m/15m/30m, MaxRetries=3, PublishFailure fallback). Asserted
  intact in `FalconResilienceStrategyTests.cs:56-113` (MaxRetries=3, 5m start) and the 500-theory /
  401 ProcessAsync tests (`FalconCollectorTests.cs:1048-1112, 1114+`).
- **Cursor-expired still re-anchors from watermark:** `FalconRecoveryContinuationBuilder.cs:41-58` —
  the `AfterToken=null, MonthSegmentFloorUtc=LastWatermark` re-anchor is preserved byte-for-byte, and
  the other two Spotlight cursors are cleared **additively** via the sanitizer.
- The 120s gate is **independent** of the ~23h whole-checkpoint gate (`FalconCheckpointHelper.cs`:
  `CanResumeFrom` lines 35-64 still call `RecoveryParsingHelper.IsCheckpointStale`; the 120s gate lives
  in `ApplyCursorTtlForResume` lines 98-119 and runs only after a resumable decision). They coexist.

### SC6 — Builds net8.0 + Falcon project green: PASS
- 153/153 tests pass; build succeeded on net8.0 (observed above).

---

## Targeted high-risk checks

### Planned yields are genuinely UNBUDGETED (traced, not trusted)
PASS. `FalconResilienceStrategyFactory.cs:64-68` emits `RequestDeferredRecovery(...,
UseRecoveryBudget: false)`. Shared executor `AdapterFailureDecisionExecutor.cs`:
- Line 120: the `AttemptNumber >= MaxRetries` exhaustion gate is **skipped** when `UseRecoveryBudget`
  is false.
- Lines 194-235: when false, `currentAttemptNumber=0`, `nextAttemptNumber=0`, and it calls
  `AdapterRecoveryBudget.Clear(...)` (line 226) instead of `.Write(...)`. So `attemptCount` is never
  advanced AND the 5xx budget is reset — a long run can yield arbitrarily many times without ever
  exhausting `MaxRetries`.
- Precedent confirmed: `ServerSuggestedRetryDelayPolicy.cs:80` uses the identical `UseRecoveryBudget:
  false` shape; README (`Resilience/README.md:106-112`) documents the gate-skip. Backoff plan
  (`FalconResilienceStrategyFactory.cs:102-110`, MaxRetries=1) is only a delay carrier, not a budget,
  because the gate is never consulted for this decision.

### Volume yield: 1M cumulative, cross-lane, post-publish-and-checkpoint, never final page of final lane
PASS — see SC3. `BuildOnPagePublished` increments cumulative across lanes (A2), evaluates only after
`OnPagePublished` (A3), and guards `isFinalPageOfFinalLane` (line 593) which the sequencer also
enforces (line 146). The threshold advance handles a single page crossing multiple multiples
(`FalconSpotlightStageSequencer.cs:139-147`).

### 10m stage yield: after open & reopen, none after closed, none at final completion
PASS — `CompleteLane` returns `StageYieldDue=false` on the final lane and `AllLanesComplete` exits the
loop before any yield (`FalconFindingsFlow.cs:275-311`; `FalconSpotlightStageSequencer.cs:81-98`).

### No failure / completion event on a planned yield
PASS — the deferred path returns `AdapterResult.PartialResult(delay, waitReason, data)`
(`AdapterFailureDecisionExecutor.cs:282-295`); e2e asserts `capture.Errors.Should().BeEmpty()` and
`capture.Completions.Should().BeEmpty()` for both volume and stage yields
(`FalconStagedSpotlightFlowTests.cs:208-209, 293-294`).

### Cursorless checkpoint actually written before each yield (traced)
PASS — both yield sites call `checkpointWriter.WriteCursorlessYieldCheckpoint(...)` immediately before
`throw` (`FalconFindingsFlow.cs:286-302` stage; `611-637` volume). The writer drops cursors and emits
`OnCheckpoint` synchronously (`FalconFindingsCheckpointWriter.cs:215-233`).

### 120s cursor-TTL gate: stale / >120s / scheduled-wait drops all 3 cursors AND re-anchors floor; coexists with 23h gate
PASS — `FalconCursorSanitizer.SanitizeCursors` clears `AfterToken/AssetsStageAfterToken/
AssetsAfterToken` (`FalconCursorSanitizer.cs:79-84`); `ApplyCursorTtlForResume` re-anchors
`MonthSegmentFloorUtc = LastWatermark` when a watermark exists (`FalconCheckpointHelper.cs:116-118`).
Wired on the resume-load path (`FalconResumeRunner.cs:222-240`, `fromScheduledWait:false` defensive
age gate), the recovery-continuation path (`FalconRecoveryContinuationBuilder.cs:62-70`,
`fromScheduledWait:true`), and the cursorless-yield writer (`fromScheduledWait:true`). Independent of
the ~23h `RecoveryParsingHelper` gate in `CanResumeFrom`.

### No fixed delay between assets completion and first Spotlight lane
PASS — the lane loop starts immediately after the assets stage with no sleep/delay
(`FalconFindingsFlow.cs:235`); asserted by
`ProcessAsync_Findings_NoFixedDelayBetweenAssetsAndFirstLane` (`FalconStagedSpotlightFlowTests.cs:438-473`).

### Lane order open→reopen→closed; resume continues in-progress lane; completed lanes not repeated
PASS — `ResolveLane`/`CompleteLane` drive strict index order; `alreadyCompleted` skip prevents replay;
cross-resume e2e proves it (`FalconStagedSpotlightFlowTests.cs:356-423`).

### Status filter composes WITH suppression + timestamp/month-segment + AID; no `expired` lane
PASS — see SC2.

---

## Preserved behavior

- **5xx recovery (session→5m/15m/30m→fallback):** unchanged
  (`FalconResilienceStrategyFactory.cs:80-138`); asserted in `FalconResilienceStrategyTests.cs:56-113`
  and `FalconCollectorTests.cs:1048-1112`. (The 500→deferred-recovery shape predates this task,
  commit `c0ddcc7`.)
- **Cursor-404 / Spotlight-5xx-with-after watermark fallback:** preserved in the runner
  (`FalconSpotlightVulnerabilitiesRunner.cs:92-151`, `TryDropAfterAndRebuildFromWatermark`) and the
  cursor-expired re-anchor (`FalconRecoveryContinuationBuilder.cs:41-58`, byte-for-byte plus additive
  sanitize).
- **Output (findings_*.json / assets_*.json):** naming unchanged; assets-stage publish path and
  `FalconFindingsAssetsStage.cs` untouched by this task (`git diff HEAD` empty for that file).
- **Standalone `FalconAssetsFlow`:** untouched by this task (`git diff HEAD` empty for `Flows/Assets/`).
- **No failure-behavior test weakened by this task:** the only deletions this task made to
  `FalconCollectorTests.cs` are metadata-dict closing braces being expanded to add
  `["spotlightStatusStages"] = "open"` (12 single-lane additions); no assertion removed, no failure
  test renamed by this task. The 500-test rename is attributable to commit `c0ddcc7`, not here.

## Test integrity

PASS. The 10 modified happy-path tests were **legitimately scoped to a single lane**
(`spotlightStatusStages = "open"`) so their single-page / single-batch / single-checkpoint assertions
remain valid — running all 3 lanes would multiply page/batch counts. This is correct scoping, not
weakening: multi-lane behavior is covered separately and end-to-end by `FalconStagedSpotlightFlowTests`
(real `ProcessAsync`/`ResumeAsync`). Unbudgeted semantics asserted with an explicit
`UseRecoveryBudget.Should().BeFalse(...)` (`FalconResilienceStrategyTests.cs:188`).

## Assumptions A1–A14

No contradiction found. A1–A10 hold in code (cumulative cross-lane, post-publish gate, no fixed delay,
additive lane fields, `CheckpointCreatedUtc` reuse, the three cursor field names). A11 resolved
additively and traced above. A12 resolved as fall-back-to-default. A13 documented and matches existing
watermark-replay semantics. A14 resolved as a pure `FalconSpotlightLaneFilter` helper composing cleanly
after AID and before timestamp.

---

## Overall verdict: **PASS**

All six Success Criteria are met with code-level evidence; the unbudgeted, cursorless, never-on-final,
and TTL-gate guarantees are traced through both the collector and the Shared executor; preserved 5xx /
cursor / output / assets-flow behavior is intact; existing tests were scoped, not weakened; and the
Falcon test project is green (153/153) on net8.0.

### Unresolved gaps / notes (none blocking)
1. Invalid lane tokens are not rejected at config-build time; they fail later via
   `ApplyStatus` → INVALID_FQL terminal. Acceptable fail-fast, but slightly later than config-validation
   time and not unit-asserted as a config-level rejection. Minor.
2. The implementation is uncommitted in the working tree — verification was performed against the
   working tree, which is what the host would build. No action required for correctness; commit when
   the operator chooses.
