# Execution Notes

Decompose path, 5 workers, phased: W1 (foundation) → {W2,W3,W5} parallel → W6 (integration).
Collision strategy: disjoint file ownership; spine files (`FalconFindingsFlow.cs`,
`FalconSpotlightVulnerabilitiesRunner.cs`) edited only by W6; phase-bounded authoritative build.

## W1 — Foundation (config + checkpoint schema + cursor sanitizer)
- Added 5 config fields (defaults open,reopen,closed / 10m / 1M / 5m; SpotlightCursorTtl=2m static const).
- Invalid/empty stage config → fall back to defaults (mirrors builder's optional-knob idiom). [A12 resolved]
- TimeSpan config parsed as int seconds (`...DelaySeconds`) per existing builder idiom; added TryGetLong.
- Added 5 additive Spotlight lane fields to FalconFindingsCheckpointState (Stage untouched).
- NEW pure FalconCursorSanitizer.AreCursorsStale/SanitizeCursors (clears 3 cursors; preserves watermarks).
- 18 tests pass.

## W2 — Per-lane Spotlight status filter
- NEW pure FalconSpotlightLaneFilter.ApplyStatus(baseFilter, status) → appends `+status:'<v>'`.
- FQL `+` join order-independent; throws on unsupported/blank status. [A14 resolved as pure helper]

## W3 — Lane sequencer + unbudgeted planned-yield decision
- A11 resolved ADDITIVELY: RequestDeferredRecovery(UseRecoveryBudget:false) → unbudgeted PartialResult;
  Shared prepends `deferred-recovery:`; factory passes bare reason. Precedent: ServerSuggestedRetryDelayPolicy.
- NEW FalconPlannedYieldException (Volume|StatusStage), pure FalconSpotlightStageSequencer
  (ResolveLane/CompleteLane/EvaluateVolumeYield). Classifier + factory edits additive; signatures stable.
- A13: resume restores current lane only; completed lanes never replayed; bounded within-lane watermark overlap.
- Boundary: planned yield must not be raised inside a recovery continuation (W6 honored).

## W5 — Cursor TTL safety (recovery + resume hooks)
- fromScheduledWait: true by construction in recovery-continuation hook; resume-load uses 120s age gate.
- Re-anchor made explicit: on cursor drop sets MonthSegmentFloorUtc=LastWatermark (ResolveResumePosition was
  diagnostic-only; floor drives the filter). NEW FalconCheckpointHelper.ApplyCursorTtlForResume.
- 401/5xx scheduled recovery (>120s) now snapshots cursorless state. Existing 5xx budget/fallback +
  cursor-expired watermark fallback byte-for-byte. 120s gate independent of ~23h staleness gate.
- Hand-off: pre-scheduled-wait checkpoints must be written cursorless (W6 honored for planned yields).

## W6 — Integration (spine) + authoritative build
- FalconFindingsFlow: outer lane loop open→reopen→closed after assets stage, no fixed pre-lane delay;
  LaneRunState seeded from resume; RunSpotlightLaneAsync threads laneStatus; skips completed lanes.
- Status filter folded into baseFilterWithoutTimestamp (after AID, before timestamp range) so it survives
  watermark cursor resets.
- Volume yield: cumulative published count across lanes; isFinalPageOfFinalLane = IsFinalLane && AfterToken null;
  cursorless checkpoint then throw Volume(5m). Stage yield on non-final lane completion → cursorless checkpoint
  then throw StatusStage(10m); none after closed.
- Lane fields now persisted via SaveFindingsState/TryLoadFindingsStateCore (W1 gap); threaded through
  FindingsFlowRunConfig ← FalconFlowRunPreparer ← config.
- NEW FalconFindingsCheckpointWriter.WriteCursorlessYieldCheckpoint (uses ApplyCursorTtlForResume fromScheduledWait:true).
- Drift reconciled (additive, within frozen contracts): (1) ErrorSeverity.Information→Warning;
  (2) NEW FalconFlowRetryPolicy excludes FalconPlannedYieldException from in-proc unknown-retry;
  (3) resume recovery hook honors already-staged planned-yield progress (no rebuild/clobber).
- Version 4.0.9 → 4.1.0 (minor; architectural findings-flow change, additive).
- NEW FalconStagedSpotlightFlowTests.cs (5 e2e tests via real ProcessAsync/ResumeAsync).
- Adjusted 10 existing happy-path findings tests to single-lane config (preserve single-page assertions);
  NO failure-behavior test changed.
- Authoritative build: solution build succeeded 0 err/0 warn; Falcon test project 153/153 green.

## Open assumptions resolved: A11, A12, A13, A14 (all VALIDATED via code).
