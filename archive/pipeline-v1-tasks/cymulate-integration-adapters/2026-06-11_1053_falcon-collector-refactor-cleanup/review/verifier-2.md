# Verifier-2 — targeted re-check after Criterion-2 repair

Scope: confirm only the gap verifier-1 raised (two unreduced param farms). All other
verifier-1 findings stand unchanged.

## Result: gap CLOSED

- `FalconSpotlightVulnerabilitiesRunner.RunAsync`: 15 input params → **4**
  `(AdapterProgressContext, SpotlightVulnerabilitiesRunRequest, CancellationToken,
  Action<...>? onPagePublished = null)`. New DTO `Dtos/FindingsDtos/SpotlightVulnerabilitiesRunRequest.cs`
  (positional sealed record) bundles the scroll/resume scalars; reuses FindingsFlowRunConfig.
- `FalconFindingsFlow.RunSpotlightLaneAsync`: 17 params → **7**
  `(SpotlightLaneRunContext ctx, LaneRunState, string? laneStatus, int pagesDone,
  int totalFindings, FalconFindingsCheckpointState? laneResumeState, CancellationToken)`.
  Loop-invariant collaborators bundled into a nested `SpotlightLaneRunContext` record,
  built once in CollectAsync and passed to both call sites.

## Behavior preservation
- Both refactored methods use a top-of-method alias block (ctx.X / request.X → original
  local names); method bodies below are unchanged. Verified the alias mapping and that all
  call sites pass identical values in identical roles.
- Build green (0 warnings), `FalconCollector.Test` = 179 passed / 0 failed after the repair.

## Note
- FalconFindingsFlow.cs 860 → 899 lines (the nested context record + alias blocks). Accepted:
  removing the param farm was the higher-priority Criterion-2 goal; line count is the soft
  best-effort goal (A4). The file remains on the >400 best-effort-exception list.

## Overall: Criterion 2 now PASS. Request satisfied (remaining >400 files are documented best-effort exceptions).
