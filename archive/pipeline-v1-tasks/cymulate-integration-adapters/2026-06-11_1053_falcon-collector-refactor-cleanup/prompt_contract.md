# Prompt Contract — FalconCollector internal refactor / cleanup

## Role
You are a senior .NET refactoring engineer with deep familiarity with this repo's
adapter architecture (Shared Orchestration/Recovery/Resilience/DataPipeline boundaries
and the FalconCollector flow/recovery model).

## Goal
Reduce size and duplication in the FalconCollector through pure internal refactoring:
classes under ~400 lines (best-effort), parameter farms replaced by house-style DTOs,
cross-flow helpers extracted (and homed in `Flows/SharedFlows` where they belong),
with zero behavioral change and an unchanged public/SDK surface.

## Context
- Target: `src/Cymulate.Integration.Adapters/Collectors/FalconCollector`.
- Authoritative input findings (do NOT re-derive — act on them):
  - Worst param-farm methods: `FalconFindingsCheckpointWriter.WriteCursorlessYieldCheckpoint`
    (18 params, FalconFindingsCheckpointWriter.cs:159), `FalconFindingsFlow.RunSpotlightLaneAsync`
    (17, FalconFindingsFlow.cs:448), `FalconSpotlightVulnerabilitiesRunner.RunAsync`
    (16 + 5-tuple return, FalconSpotlightVulnerabilitiesRunner.cs:31),
    `FalconFindingsCheckpointWriter.OnPagePublished` (15, :27),
    `OnAssetsStagePagePublished` (10, :254),
    `FalconAssetsFlow.PersistTerminalAssetsSnapshotWithoutPublishedPage` (10, :462).
  - Two recurring field bundles to model as DTOs:
    - AID-scoped resume state (6): assetsAfterToken, assetsLastSeenWatermarkUtc,
      assetsFilterForFindings, assetsPaginationCompleted, pendingAids, pendingAidOffset
      (mutable fields in FalconFindingsFlow.cs:43-48; partly captured by private
      `AidBatchResult` :50-55).
    - Spotlight lane/yield state (5): spotlightStatus, spotlightLaneIndex,
      spotlightCompletedLanes, spotlightNextVolumeYieldThreshold,
      spotlightCumulativeFindingsCount (modeled by nested `LaneRunState` :408-441).
  - Oversized: FalconFindingsFlow.cs (961), SpotlightVulnerabilitiesRunner.cs (683),
    FalconAssetsFlow.cs (683; CollectAsync is a single ~410-line method),
    FalconCheckpointHelper.cs (609; Save×2, Load×2 ~80% duplicate, CanResumeFrom, ApplyCursorTtlForResume).
  - Duplication: JSON readers in SpotlightRunner:490-524 / AssetIdsFetcher:455-479 /
    FalconAssetsFlow:604-643; event-emit try/catch ×7 (FalconFindingsCheckpointWriter ×4,
    FalconAssetsFlow ×3); `Save…State` + `foreach SetState` loop ×6; `FalconUtcTimestampFormat`
    dup (AssetIdsFetcher:19, SpotlightRunner:17); `_checkpoint.*` metadata keys dup
    (FalconFindingsCheckpointWriter:13-16, FalconAssetsFlow:34-37); cursor-expiry→watermark-reset
    loop ×4 (SpotlightRunner.RunAsync, AssetIdsFetcher ×2, FalconAssetsFlow).
- House DTO style: `Dtos/FindingsDtos/FindingsFlowRunConfig.cs` (positional `sealed record`
  + XML doc per param + init-only extras); Shared `CollectorResumeExecutionContext`
  (17 positional params), `AdapterRecoveryContext` (init-only + `required`).
- Build: net8.0, pin `TargetFramework`; tests = `Cymulate.Integration.Adapters.Collectors.FalconCollector.Test`.

## Constraints
See `constraints.md`. Key: no behavioral change; public/SDK surface and
`FalconCheckpointHelper` public statics frozen (split hides behind delegators);
phased + file-disjoint execution; net8.0 pinned; update `ai/skills/collector-flow-patterns`
and `ai/skills/collector-recovery` if documented architecture changes.

## Execution plan (phased — see decisions.md for full partition)
- Phase A (one worker, foundational, blocks B): FalconJson, FalconCollectorEvents,
  shared constants, apply-checkpoint-state helper, and the SharedFlows pagination-engine
  analysis + safe-core extraction. Phase A finalizes `AssetIdsFetcher`; B1/B4 must not touch it.
- Phase B (parallel, file-disjoint): B1 findings trio + new DTOs; B2 FalconCheckpointHelper
  split (delegators); B3 FalconCollectorConfigurationBuilder; B4 FalconAssetsFlow decomposition.

## Execution Rules
- Do not assume missing data; respect constraints strictly.
- Preserve behavior exactly. When a duplicated loop's copies differ (assumption A2),
  extract only the safe common core; do not force a unification that changes behavior.
- Run the FalconCollector test project at phase boundaries (after A, after B) — not per file.
- No two workers in the same phase edit the same file.
- Mirror existing neighbor patterns; keep methods small.

## Output Format
- Refactored code under the FalconCollector directory (+ new DTO/helper files, possibly
  a `Models/` namespace and `Flows/SharedFlows` additions).
- `execution_notes.md` updated per worker (what moved where, line-count before/after,
  SharedFlows analysis outcome).
- Updated skill docs if architecture docs changed.

## Success Criteria
1. No class >400 lines where reasonably possible (documented exceptions allowed).
2. Param farms (15–18 param methods) replaced by house-style DTOs/records; the named
   return record replaces the 5-tuple.
3. Helper classes extracted; the listed duplications removed.
4. Written SharedFlows analysis exists; genuinely cross-flow helpers homed there.
5. Build green (net8.0); all existing FalconCollector.Test tests pass; no behavioral change.

## Stop Conditions
- Goal achieved and verified (tests green, criteria met).
- A required behavioral change surfaces (would violate "no behavioral change") — stop and surface.
- NuGet restore fails with 401/403 on `Cymulate.*` (auth, not code) — surface, do not work around.
- A phase-A/phase-B file-ownership collision is unavoidable — stop and surface.
