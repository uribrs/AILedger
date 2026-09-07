# Verifier Report — Falcon findings assets stage

Branch: `falcon-collector-dup-data-BUG`. Verified against `prompt_contract.md` Success Criteria + `constraints.md`.

## Build / Test
- `dotnet test` FalconCollector.Test: **62 passed, 0 failed, ~3s**. PASS.

## Success Criteria

### 1. Solution builds + FalconCollector tests pass — PASS
62/62 green. Build succeeded (test build compiles the collector + test project).

### 2. Findings-with-filter publishes BOTH assets_*.json and findings_*.json, findings unchanged — PASS
`FalconCollectorTests.cs:523-585` (updated `ProcessAsync_Findings_Emits...`): now asserts 2 stream batches — `assets_000001.json` (RecordCount 2) then `findings_000001.json` (RecordCount 3). Findings batch content/ids/shape assertions unchanged; `ProcessedFindings` still 3. Findings output preserved. The `ProcessedItems` metric changed 3→5 (assets+findings) — a progress metric only, documented in execution_notes.md; `findings_*.json` payload is byte-identical.

### 3. No-filter publishes all hosts as assets — PASS
`FalconCollectorTests.cs:1545+` (`ProcessAsync_Findings_NoFilter_PublishesAllHostsAsAssets_AndFindings`): unfiltered run, hosts endpoint hit exactly once (assets stage only, no AID pre-pass), `assets_000001.json` carries both host rows (device_id h1/h2), findings published after. Meaningful, not tautological.

### 4. Resume Stage="findings" AND legacy (no Stage) SKIPS assets — PARTIAL PASS
- Data layer: PASS. `FindingsCheckpoint_RoundTripsAssetsStageFields_AndLegacyDefaultsToFindings` (`:1676+`) loads a legacy no-stage checkpoint → `Stage == null`, `AssetsStageCompleted == false`, counters 0/empty. Flow maps null→"findings" (`FalconFindingsFlow.cs:92-94`), skipping assets.
- Behavior: PARTIAL. `ResumeAsync_Findings_WithFindingsStageCheckpoint_SkipsAssetsStage` (`:1730+`) proves the dup-data guard behaviorally for an explicit `Stage="findings"` checkpoint: zero `limit=50` host calls, no `assets_*.json` published. **There is no dedicated behavioral test for the LEGACY (null-Stage) resume path** — only the data-layer round-trip. The flow treats them identically (`Stage ?? Findings`), so this is a coverage gap, not a correctness gap.

### 5. Resume Stage="assets" continues the assets stage from its cursor, then runs findings — FAIL (no behavioral test)
The only `Stage="assets"` coverage is the checkpoint **data round-trip** (`:1708-1722`): it proves `AssetsStagePage/AssetsStageAfterToken/AssetsStageWatermarkUtc/Ids/TotalAssetsCollected` persist and reload. **No test exercises an actual `ResumeAsync` with `Stage="assets"`** asserting that paging resumes from `AssetsStageAfterToken`, file numbering continues from `AssetsStagePage`, then findings run fresh. Reading `FalconFindingsAssetsStage.CollectAsync` (`:62-66, 74-81`) the resume cursor is wired (page/after/watermark/ids seeded into the fetcher) and `FalconFindingsFlow.cs:96-117` selects the assets entry stage on resume — so the path is plausibly correct, but it is **unverified by test**, and this is an explicit Success Criterion. This is the most material gap.

## Constraint Adherence — PASS
- FalconAssetsFlow / standalone CollectAssets NOT modified: confirmed — `git diff` shows zero changes outside `Flows/Findings` + `Recovery` within FalconCollector.
- AID pre-pass / segmentation / existing findings checkpoint fields unchanged: the AID pre-pass call site (`FalconFindingsFlow.cs:405-412`) calls `FetchAssetIdPagesAsync` WITHOUT `captureHostRows`/`startWatermarkIds` → both default off. `AssetIdsFetcher` additive change is default-off: `captureHostRows=false`, `startWatermarkIds=null` → `watermarkFloorIds` empty → `watermarkFallbackActive=false` (`:56-59`), identical to prior behavior. `BuildSegmentsForRun` unchanged.
- Assets stage runs as its own sequential pass, NOT inside the background AID channel producer: confirmed — `FalconFindingsAssetsStage` is invoked before the segment/AID loop (`FalconFindingsFlow.cs:106-117`); the channel producer `FillAidBatchesAsync` neither publishes nor checkpoints.
- New logic in helper classes, FalconFindingsFlow.cs orchestration-only: confirmed (new `FalconFindingsAssetsStage.cs`, `FalconFindingsStage.cs`; writer gained `OnAssetsStagePagePublished`).
- Backward-compatible checkpoints: confirmed (helper reads new keys with safe defaults; legacy loads).
- Flow return value stays findings total: confirmed (`CollectAsync` returns `totalFindings`).
- Assets events on `AssetsFlow`, findings on `FindingsFlow`: confirmed (writer + event tests).

## Out-of-scope note
`FalconSpotlightVulnerabilitiesRunner.cs` shows as modified in `git diff` (adds a dry-run early-return). File mtime is **11:52**, before this task's execution (~13:04+; task dir stamped 12:50), and the new `ProcessAsync_Findings_DryRun_...` test exercises it. Per the verifier prompt this predates the task and is NOT attributed here. Flagging only that it does touch the findings vuln stage; if that change were in-scope it would breach the "do not change the findings vulnerability stage" constraint — but it is not part of this work.

## Test quality
Tests are meaningful, not tautological: they assert call counts (host endpoint hit-count distinguishes assets-stage vs AID pre-pass), NDJSON payload contents, target paths, event flows, and the dup-data guard via host-call==0. The cursor-expiry test was correctly adjusted to give the assets stage its own clean first page so it no longer consumes the AID-pre-pass expiry simulation.

## Uncovered edge cases that matter
1. **Resume Stage="assets" behavior (Criterion 5)** — no behavioral test. Highest priority gap.
2. **Legacy (null-Stage) resume behavior** — only data-layer coverage; behavior inferred from `Stage ?? Findings`.
3. **Assets stage producing zero hosts** — no test. The empty-page path (`FalconFindingsAssetsStage.cs:97-106`: `rows.Count==0` → `continue`/`break`) is unexercised; an all-empty inventory would publish no `assets_*.json` and still run findings — plausible but untested.
4. **Resume Stage="assets" with AssetsStageCompleted=true** — `shouldRunAssetsStage` guard (`FalconFindingsFlow.cs:102-104`) skips assets when completed even if entryStage=="assets"; correct by inspection, untested.

## Overall Verdict: PASS WITH GAPS
The implementation satisfies the goal, all constraints, and 4 of 5 testable behaviors; build + tests are green. The dup-data guard (the branch's reason for being) is correctly implemented and behaviorally tested for the `Stage="findings"` path. However **Success Criterion 5 (resume `Stage="assets"` continues the assets stage) is not verified by any behavioral test** — only by a data round-trip. Given this is the resume/dup-data branch, the assets-stage resume path is exactly the logic most worth a behavioral test. Recommend adding a `ResumeAsync` test with `Stage="assets"` + a non-empty `AssetsStageAfterToken` asserting host paging continues from the cursor, file numbering continues from `AssetsStagePage`, and findings then run. Secondary: a legacy-no-Stage behavioral resume and a zero-host assets stage test.
