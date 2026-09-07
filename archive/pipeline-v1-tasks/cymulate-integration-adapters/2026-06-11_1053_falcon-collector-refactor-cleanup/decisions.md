# Decisions

- Execution is phased; Phase A blocks Phase B. Within a phase, workers are file-disjoint.
- Phase A (one worker, foundational): create shared helpers + write the SharedFlows analysis.
- Phase B (parallel, file-disjoint): four workers, partition below.
- Param-farm fix is the primary lever for shrinking the findings trio; DTOs replace the bundles.
- `FalconCheckpointHelper` split keeps public statics as thin delegators — consumers untouched.
- Files left alone: FalconCheckpointState, FalconFindingsAssetsStage, FalconCollectorFlowRunner, FalconResumeRunner.

## Phase A scope (foundational)
- `FalconJson` helper: dedup JSON field readers (`ReadString`/`TryReadUtcDateTime`/
  `TryReadStringProperty`/`TryReadAnyId`/`TryReadUtcTimestamp`) — currently in
  SpotlightRunner:490-524, AssetIdsFetcher:455-479, FalconAssetsFlow:604-643.
- `FalconCollectorEvents` wrapper: the ~7 copies of the `_eventSink`
  ReportBatchProduced/ReportCheckpointAdvanced try/catch-LogDebug boilerplate
  (FalconFindingsCheckpointWriter ×4, FalconAssetsFlow ×3).
- Shared constants: checkpoint-metadata keys (`_checkpoint.*`) + `FalconUtcTimestampFormat`
  (dup in AssetIdsFetcher:19, SpotlightRunner:17, plus the writer/assets metadata keys).
- Reusable "apply checkpoint state dict to progress context" helper (promote
  FalconCollector's private `ApplyCheckpointState`; reused by the writer/flows ×6).
- Analyze + extract the common core of the 4× cursor-expiry→watermark-reset loop into
  a SharedFlows engine (subject to A2 risk — extract the safe common core, not a
  forced single engine).
- Deliverable: written SharedFlows analysis (what moved, what stayed, why).

## Phase B partition (file-disjoint)
- B1 — findings trio: introduce param-bundle DTOs (AID-scoped resume state:
  assetsAfterToken/assetsLastSeenWatermarkUtc/assetsFilterForFindings/
  assetsPaginationCompleted/pendingAids/pendingAidOffset; Spotlight lane/yield state)
  + named return record for `SpotlightVulnerabilitiesRunner.RunAsync` 5-tuple; refactor
  `FalconFindingsFlow.cs` + `FalconFindingsCheckpointWriter.cs` +
  `FalconSpotlightVulnerabilitiesRunner.cs`. Owns the new DTO files.
- B2 — `FalconCheckpointHelper.cs`: split into serializer / deserializer / key-schema /
  resume-policy; public statics delegate. Owns only this file + new split files.
- B3 — `FalconCollectorConfigurationBuilder.cs`: decompose 230-line Build (credential
  decrypt, options-override application, session-spec assembly). Standalone.
- B4 — `FalconAssetsFlow.cs`: decompose 410-line CollectAsync; adopt Phase A pagination
  engine. Owns only this file (+ may add a private/companion type in its own folder).

## Cross-worker file-ownership guard
- B1 owns FalconFindingsFlow/FalconFindingsCheckpointWriter/FalconSpotlightVulnerabilitiesRunner.
- B4 owns FalconAssetsFlow. Note: AssetIdsFetcher is read by both B1 (findings) and B4
  (assets) and was touched by Phase A (JSON readers / pagination core). Phase A finalizes
  AssetIdsFetcher; neither B1 nor B4 may modify it — flagged as a collision risk to enforce.
