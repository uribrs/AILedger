# Orchestration Plan

## Complexity Decision
- Path: decompose (operator override of orchestrator's direct-path recommendation)
- Orchestrator's original read: tight coupling around `FalconFindingsCheckpointState` +
  `FalconFindingsFlow` argued for direct. Operator directed decomposition across coder
  subagents for a large change. Accepted.
- Collision-avoidance is the binding constraint. Achieved by **disjoint file ownership** +
  a sequenced spine: the two shared-spine files (`FalconFindingsFlow.cs`,
  `FalconSpotlightVulnerabilitiesRunner.cs`) are edited by exactly ONE worker (W6) last; all
  other new logic lands in NEW single-owner files; W1 freezes shared contracts first.

## Research Decisions
- None needed. A11–A14 resolve by reading this repo's own code. External vendor facts already
  established in existing code.

## Worker Plan

Phasing: W1 first (freezes contracts) → W2, W3, W5 in parallel (disjoint files) → W6 last (integration).

### W1 — Foundation: config + checkpoint schema + cursor sanitizer  [sequential, FIRST]
- Scope: 5 new config fields + builder parsing/validation; new Spotlight lane fields on
  `FalconFindingsCheckpointState`; a NEW pure `FalconCursorSanitizer` (clears
  AfterToken/AssetsStageAfterToken/AssetsAfterToken, re-anchors from watermark/floor, TTL-aware);
  unit tests for config defaults + sanitizer.
- Owns: `Processing/Configuration/FalconCollectorConfiguration.cs`,
  `Processing/Configuration/FalconCollectorConfigurationBuilder.cs`,
  `Recovery/FalconCheckpointState.cs`, NEW `Recovery/FalconCursorSanitizer.cs`,
  test `FalconCollectorConfigurationBuilderTests.cs`, NEW test `FalconCursorSanitizerTests.cs`.
- Output: frozen config + checkpoint shape + sanitizer API that W2/W3/W5/W6 consume.
- Resolves: A12 (validation idiom).
- Dependencies: none.

### W2 — Per-lane Spotlight status filter  [parallel after W1]
- Scope: NEW pure builder composing suppression + timestamp/month-segment + AID clause +
  `status:'open'|'reopen'|'closed'`; full unit tests. Does NOT touch the flow or the runner.
- Owns: NEW `Flows/Findings/FalconSpotlightLaneFilter.cs`, NEW test
  `FalconSpotlightLaneFilterTests.cs`. May READ `FalconUrls.cs`/runner for the existing filter
  shape but MUST NOT edit them.
- Output: `FalconSpotlightLaneFilter.Compose(baseFilter, status, ...)` for W6 to call.
- Resolves: A14 (composition point — as a pure helper).
- Dependencies: W1 (uses config stage strings; plain strings, no hard coupling).

### W3 — Lane sequencing + unbudgeted planned-yield decision  [parallel after W1]
- Scope: pure lane state machine (next lane, stage-complete, volume-threshold-crossed using the
  cumulative count + yield-every); NEW planned-yield exception; classifier mapping; resilience
  factory decision that maps to an UNBUDGETED `AdapterResult.PartialResult` with wait reasons
  `deferred-recovery:falcon-planned-yield:spotlight-volume` (5m) and `...:spotlight-status-stage`
  (10m). MUST resolve A11 first (read Shared Resilience budget-advance path) and STOP+report if
  unbudgeted cannot be done additively.
- Owns: NEW `Processing/Resilience/FalconPlannedYieldException.cs`, NEW
  `Flows/Findings/FalconSpotlightStageSequencer.cs` (pure), edits to
  `Processing/FalconFlowExceptionClassifier.cs` and
  `Processing/Resilience/FalconResilienceStrategyFactory.cs`, tests for sequencer +
  classifier/factory yield path (e.g. extend `FalconResilienceStrategyTests.cs` — see note).
- Output: exception type + sequencer + decision wiring for W6 to throw/consume.
- Resolves: A11 (unbudgeted), A13 (lane-boundary replay semantics, defined in sequencer).
- Dependencies: W1 (checkpoint lane fields).

### W5 — Cursor TTL safety in recovery + resume hooks  [parallel after W1]
- Scope: apply `FalconCursorSanitizer` (from W1) in fresh + resume recovery hooks for
  401/5xx/cursor-expired; ensure 401/5xx scheduled recovery (>120s) snapshots cursorless state;
  preserve existing 5xx budget/fallback + cursor-expired watermark re-anchor; load-time TTL
  sanitize on resume.
- Owns: `Recovery/FalconRecoveryContinuationBuilder.cs`, `Recovery/FalconResumeRunner.cs`,
  `Recovery/FalconCheckpointHelper.cs` (load-time sanitize hook), tests
  `FalconRecoveryContinuationTests.cs`, `FalconResumeRunnerTests.cs`.
- Output: cursor-safe recovery/resume.
- Dependencies: W1 (`FalconCursorSanitizer`).

### W6 — Integration: staged loop + runner wiring + e2e tests + version  [sequential, LAST]
- Scope: wire `FalconFindingsFlow` to run lanes open→reopen→closed after the assets stage using
  W3's sequencer; call W2's lane filter; count published findings cumulatively; throw W3's
  planned-yield exception at safe page boundaries (after publish+checkpoint, never final
  page/lane; 10m between stages, no delay after `closed`); write cursorless checkpoint (via W1
  sanitizer) before yielding; no fixed pre-Spotlight delay. End-to-end planned-yield + resume +
  lane-progression tests. Version bump per magnitude.
- Owns: `Flows/Findings/FalconFindingsFlow.cs`,
  `Flows/Findings/FalconSpotlightVulnerabilitiesRunner.cs`, NEW e2e/integration tests,
  per-adapter `.csproj` version. This is the ONLY worker that edits the shared-spine files.
- Dependencies: W2, W3, W5 (and W1).

## Synthesis Approach
Main thread integrates after W6 returns: confirm each worker produced its files, run the full
Falcon test project build (phase-bounded — not run concurrently by parallel workers), reconcile
any interface drift between W2/W3 helpers and W6's call sites. Then verifier, then code-reviewer.

## Verification Obligations
- 6 Success Criteria in prompt_contract.md.
- Planned yields UNBUDGETED (no 5xx-budget consumption) — explicit check.
- No volume yield after final page/final lane; no stage delay after `closed`.
- 120s cursor TTL is a NEW gate coexisting with the 23h staleness gate.
- Existing failure-behavior tests unchanged (else Stop Condition).
- Output + `FalconAssetsFlow` unchanged. Build on net8.0; Falcon test project green.
