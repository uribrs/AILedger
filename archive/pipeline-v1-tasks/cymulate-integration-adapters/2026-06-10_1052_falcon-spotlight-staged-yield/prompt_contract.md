# Prompt Contract — Falcon Spotlight Staged Collection And Planned Yield

## Role
You are a senior .NET engineer working on the Cymulate integration adapters repo, expert in the
Falcon collector's findings flow, checkpoint/resume model, and the Shared Resilience layer.

## Goal
Refactor the Falcon **findings** flow to (1) run Spotlight in value-ordered status lanes
(open→reopen→closed) after the internal assets stage, (2) apply unbudgeted host-scheduled
planned yields (10m between stages, 5m per 1,000,000 published findings, never at final
completion), and (3) enforce a 120s cursor-TTL safety gate that drops/re-anchors Falcon `after`
cursors on stale or scheduled-wait resumes — without changing existing failure behavior or output.

## Context
- .NET 8 (build pinned to net8.0; SDK is 9.x). Branch: `falcon-collection-strategy-refactor`.
- Builds on landed assets-stage work (task 2026-05-31_1250_falcon-findings-assets-stage):
  `FalconFindingsCheckpointState.Stage` already discriminates "assets"|"findings".
- Authoritative file map (verified):
  - Flow: `Collectors/FalconCollector/Flows/Findings/FalconFindingsFlow.cs`,
    `FalconFindingsAssetsStage.cs`, `FalconSpotlightVulnerabilitiesRunner.cs`
  - Checkpoint: `Collectors/FalconCollector/Recovery/FalconCheckpointState.cs`
    (`FalconFindingsCheckpointState`), `FalconCheckpointHelper.cs`, `FalconResumeRunner.cs`
  - Filters: `FalconSpotlightVulnerabilitiesRunner.cs` (`BuildAidListClause`,
    `ApplyUpdatedTimestampRange`); suppression const + `FalconUrls.BuildVulnerabilitiesBaseUrl`
  - Config: `Processing/Configuration/FalconCollectorConfiguration.cs` +
    `FalconCollectorConfigurationBuilder.cs`
  - Resilience: `Processing/FalconFlowExceptionClassifier.cs`,
    `Processing/Resilience/FalconResilienceStrategyFactory.cs`,
    `FalconRecoveryContinuationBuilder.cs`; Shared `Resilience/` (`AdapterResilienceStrategy`,
    `AdapterFailureDecisionExecutor`, budget/`attemptCount`)
  - Tests: `UnitTests/Collectors/...FalconCollector.Test/`
    (`FalconCollectorConfigurationBuilderTests`, `FalconUrlsTests`,
    `FalconRecoveryContinuationTests`, `FalconResilienceStrategyTests`, `FalconResumeRunnerTests`)
- Terminology reconciliation: spec "PartialWaitRequired" = `AdapterResult.PartialResult(...)`
  via deferred-recovery decision. Spec `spotlightCursorTtl` 120s gate is NEW and separate from
  the existing ~23h `DefaultStaleThreshold`.

## Constraints
See `constraints.md` (authoritative). Highlights:
- Additive only; `findings_*.json`/`assets_*.json` content and `FalconAssetsFlow` unchanged.
- Do not reuse `Stage` for lane state; add separate lane fields.
- Planned yields: unbudgeted, no failure/completion event, cursorless checkpoint before yielding,
  never after final page/final lane, no stage delay after `closed`.
- 120s cursor TTL: clear+re-anchor `AfterToken`/`AssetsStageAfterToken`/`AssetsAfterToken` on
  stale/scheduled-wait resume; applied in fresh+resume hooks for 401/5xx/cursor-expired.
- Preserve 5xx recovery budget (5m/15m/30m), watermark fallbacks, and no fixed post-assets delay.
- Config: 5 new fields with specified defaults; `spotlightCursorTtl` internal constant; mirror
  existing builder validation style.
- Mirror neighboring code; small methods; Glossary constants; no magic strings.

## Success Criteria
1. **Config**: defaults parse to `open,reopen,closed` / `10m` / `1,000,000` / `5m`;
   `spotlightCursorTtl`=120s internal. Invalid/empty stage config handled per existing builder
   style. Covered by tests mirroring `FalconCollectorConfigurationBuilderTests`.
2. **Filters**: generated Spotlight requests include correct `status:'open'|'reopen'|'closed'`,
   composed with suppression + timestamp/month-segment + AID clause. Covered by filter tests.
3. **Planned yields**:
   - After 1M cumulative published findings → `PartialResult` with 5m, unbudgeted, no failure
     publication; no volume yield after final page/final lane.
   - After `open` and after `reopen` complete → `PartialResult` with 10m; no delay after `closed`.
4. **Resume/checkpoint**:
   - Planned-yield checkpoint resumes at the same lane without `after` cursors.
   - Lane progression survives resume; completed lanes not repeated (except boundary-safe
     watermark replay).
   - Checkpoint older than 120s drops all Falcon cursors; immediate (<120s) resume keeps cursor
     only if not from a scheduled wait.
5. **Recovery (unchanged + extended)**:
   - 401/5xx scheduled recovery now snapshots cursorless state when delay >120s.
   - Existing 5xx budget + fallback unchanged; cursor-expired still re-anchors from watermark.
6. Solution builds on net8.0; full Falcon test project green.

## Execution Rules
- Do not assume missing data; resolve OPEN assumptions (A11–A14) by reading code, not guessing.
- Respect constraints strictly; additive changes only.
- If a fix causes more failures than it resolves, revert and surface.
- Resolve A11 (unbudgeted wiring) before implementing the planned-yield decision path.

## Output Format
- Code changes in the Falcon collector + Shared Resilience (only if a new decision/reason hook
  is genuinely required there) + Falcon test project.
- `execution_notes.md` updated with decisions made, OPEN assumptions resolved, residual risks.
- Version bump per repo convention reflecting magnitude.

## Stop Conditions
- A11 cannot be satisfied additively (planned yield cannot be made unbudgeted without invasive
  Shared changes) — surface before proceeding.
- Existing failure-behavior tests would have to change to pass — surface (constraint says
  unchanged).
- Any spec requirement contradicts verified code behavior in a way that needs an operator call.
