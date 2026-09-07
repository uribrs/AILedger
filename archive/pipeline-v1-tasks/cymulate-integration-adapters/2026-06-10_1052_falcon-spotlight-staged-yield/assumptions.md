# Assumptions

## From the user-provided spec (treated as authoritative)
- A1 [VALIDATED] "Findings count" = published/emitted Spotlight finding records, not raw API
  resources discarded by parsing/filtering. (Author decision.)
- A2 [VALIDATED] Volume throttling (`spotlightFindingsYieldEvery`) is cumulative across all
  Spotlight status lanes within a single findings run.
- A3 [VALIDATED] If one page crosses the 1M threshold, yield *after* that page is published and
  checkpointed (no mid-page yield).
- A4 [VALIDATED] No fixed post-assets delay; auth/session safety already handled by OAuth token
  reuse + 401 refresh. The previously proposed Discover→Spotlight fixed delay is removed.
- A5 [VALIDATED] Standalone assets flow (`FalconAssetsFlow`) unchanged; this refactor only
  changes the findings flow's internal assets-plus-Spotlight sequence.

## Code-grounded mappings (terminology reconciliation — confirmed against repo)
- A6 [VALIDATED] "PartialWaitRequired" maps to the existing
  `AdapterResult.PartialResult(delay, waitReason, data)` mechanism, produced via a deferred-
  recovery decision (`RequestDeferredRecovery`). No new result type is introduced.
  Evidence: `FalconResilienceStrategyFactory.cs`, CLAUDE.md Resilience section.
- A7 [VALIDATED] The `deferred-recovery:falcon-planned-yield:*` wait-reason strings are NEW;
  existing Falcon reasons are bare (`falcon-server-error`, `falcon-cursor-expired`,
  `falcon-unauthorized`). The new prefix pattern is additive.
- A8 [VALIDATED] Checkpoint timestamp field is `CheckpointCreatedUtc` (required, on
  `FalconCheckpointState`). The 120s cursor-TTL gate reads this same field but is a separate,
  tighter gate from the existing ~23h `RecoveryParsingHelper.DefaultStaleThreshold`.
- A9 [VALIDATED] Cursor fields to sanitize exist by these names on
  `FalconFindingsCheckpointState`: `AfterToken`, `AssetsStageAfterToken`, `AssetsAfterToken`.
  Re-anchor sources: `LastWatermark`/`MonthSegmentFloorUtc` (findings), `AssetsStageWatermarkUtc`
  (assets stage), `AssetsLastSeenWatermarkUtc` (AID-scoped findings).
- A10 [VALIDATED] New Spotlight lane checkpoint fields are additive to
  `FalconFindingsCheckpointState`: current status, lane index, completed lanes, next volume-yield
  threshold, cumulative Spotlight findings count. They do NOT reuse `Stage`.

## Open / to confirm during execution
- A11 [OPEN] Exact "unbudgeted" wiring: confirm the planned-yield decision bypasses the
  `attemptCount`/`MaxRetries` budget tracked in checkpoint `AdapterState` (does not snapshot via
  the budget-advancing path used by recovery). Resolve by reading the budget-advance path in
  Shared `Resilience/` + `AdapterFailureDecisionExecutor`. Blocks correct planned-yield wiring.
- A12 [OPEN] Whether invalid/empty `spotlightStatusStages` should fall back to defaults vs hard
  validation failure — pick whichever the existing builder does for analogous fields (mirror,
  don't invent). Resolve by reading `FalconCollectorConfigurationBuilder` validation style.
- A13 [OPEN] Lane-boundary watermark replay: "does not repeat completed lanes except boundary-
  safe watermark replay" — confirm the acceptable replay window at a lane boundary mirrors the
  existing cursor-expiry watermark fallback semantics (small overlap acceptable, dedup downstream).
- A14 [OPEN] Where the status clause is injected in the Spotlight filter string
  (`FalconSpotlightVulnerabilitiesRunner` filter composition vs a dedicated filter builder) so it
  composes cleanly with `BuildAidListClause()` + `ApplyUpdatedTimestampRange()`.
