# Execution Notes

## What was implemented (direct path)

An independent, resumable **assets stage** was wired into the Falcon `CollectFindings` flow. The flow
now runs **assets → findings**; the findings (vulnerability) stage, AID pre-pass, segmentation and
existing findings checkpoint fields are unchanged, and `findings_*.json` output is byte-for-byte
identical.

Files:
- `Recovery/FalconCheckpointState.cs` — added to `FalconFindingsCheckpointState`: `Stage`,
  `AssetsStagePage`, `AssetsStageAfterToken`, `AssetsStageWatermarkUtc`, `AssetsStageWatermarkIds`,
  `AssetsStageCompleted`, `TotalAssetsCollected`. Existing AID-pre-pass `Assets*`/`PendingAids`
  fields untouched.
- `Recovery/FalconCheckpointHelper.cs` — serialize the new keys; load with safe defaults
  (stage absent → null; ints → 0; lists → empty; bool → false). Backward-compatible.
- `Flows/Findings/FalconFindingsStage.cs` (new) — `Assets`/`Findings` stage constants.
- `Flows/Findings/AssetIdsFetcher.cs` — additive, default-off `captureHostRows` + `startWatermarkIds`
  seed; `AssetIdPage` gained `HostRowsUtf8` and `MaxLastSeenIds`. AID-pre-pass path unchanged when
  capture is off (existing call site untouched; new params default).
- `Flows/Findings/FalconFindingsAssetsStage.cs` (new) — sequential host pager over
  `AssetIdsFetcher.FetchAssetIdPagesAsync` (capture on); publishes `assets_*.json` via
  `CollectorNdjsonPublisher.PublishAssetsUtf8PageAsync`; checkpoints + emits `AssetsFlow` events
  per page through the unified findings checkpoint.
- `Flows/Findings/FalconFindingsCheckpointWriter.cs` — new `OnAssetsStagePagePublished(...)`
  (Stage="assets", Page = assets page to satisfy the load `page >= 1` invariant, findings TotalItems
  kept at 0, assets tracked in `TotalAssetsCollected`); `OnPagePublished` now stamps
  Stage="findings", AssetsStageCompleted=true and carries `TotalAssetsCollected`.
- `Flows/Findings/FalconFindingsFlow.cs` — orchestration only: stage selection
  (`entryStage`, `shouldRunAssetsStage`), assets-stage invocation, and a derived
  `findingsResumeState` so the unchanged findings logic resumes only from a findings-stage checkpoint.
- Tests in `FalconCollectorTests.cs`.

## Stage / resume model

- Fresh run → `entryStage = "assets"`; run assets to completion, then findings fresh.
- Resume `Stage="assets"` & not completed → resume assets from its cursor, then findings fresh.
- Resume `Stage="assets"` & completed → skip assets, findings fresh.
- Resume `Stage="findings"` or legacy (no `Stage`) → skip assets, findings resume as today.
- Dry-run → assets stage skipped entirely (findings dry-run behavior unchanged).

`shouldRunAssetsStage = !IsDryRun && entryStage=="assets" && !AssetsStageCompleted` — this is the
dup-data guard: a completed/absent assets stage is never re-published on resume.

## OPEN assumptions resolved

- **A7 (stage order)** → assets-first (assets → findings); matches DefenderVm and the natural
  inventory→findings order.
- **A8 (no month-segmentation for the assets stage)** → confirmed; the assets stage uses
  `AssetIdsFetcher`'s after-cursor + `last_seen_timestamp` watermark fallback (no segmentation).
- **A9 (persist assets-stage watermark boundary IDs)** → implemented; `AssetIdsFetcher` exposes
  `MaxLastSeenIds` and accepts `startWatermarkIds`, persisted as `AssetsStageWatermarkIds` so a
  resume de-dups boundary host rows (parity with `FalconAssetsFlow.LastWatermarkIds`).

## Behavioral note discovered

Completion `ProcessedItems` now spans both stages (assets + findings) because each stage calls
`AdvancePage`; `ProcessedFindings` remains findings-only. The existing filter test was updated
accordingly (ProcessedItems 3 → 5, ProcessedFindings still 3). This is a progress/metric change
only — `findings_*.json` payloads are unchanged.

## Tests

`FalconCollectorTests` — 62 passing (`dotnet test`, ~5s):
- Updated `ProcessAsync_Findings_Emits...` (filter): now publishes `assets_000001.json` (2) +
  `findings_000001.json` (3); findings ids/shape unchanged; assets payload carries host rows.
- Updated `ProcessAsync_Findings_WhenAidPrepassCursorExpired404...`: assets stage given its own
  clean first host page so it doesn't consume the AID-pre-pass cursor-expiry simulation.
- New `ProcessAsync_Findings_NoFilter_PublishesAllHostsAsAssets_AndFindings`: unfiltered run emits
  all hosts as assets + findings; hosts endpoint hit once (assets stage only).
- New `FindingsCheckpoint_RoundTripsAssetsStageFields_AndLegacyDefaultsToFindings`: legacy/no-stage
  loads as null stage (skip assets); new assets-stage fields round-trip.
- New `ResumeAsync_Findings_WithFindingsStageCheckpoint_SkipsAssetsStage`: a findings-stage resume
  performs zero host paging and publishes no `assets_*.json` (dup-data guard).

## Build / test results
- `dotnet build` FalconCollector: success.
- `dotnet build` LocalAdapterRunner (references all collectors): success — no cross-project breakage
  from the additive `AssetIdsFetcher` signature change.
- `dotnet test` FalconCollector.Test: 62 passed, 0 failed.

## Review disposition

Verifier (verifier-1 → verifier-2): found criterion 5 (resume Stage="assets") lacked a behavioral
test. Repaired by adding `ResumeAsync_Findings_WithAssetsStageCheckpoint_ContinuesAssetsThenRunsFindings`.
Re-verified PASS (verifier-2.md).

Code reviewer (code-reviewer-1.md):
- **H1 (High) — FIXED.** Duplicate host rows possible on a cursor-expiry resume when a hosts page
  had no parseable `last_seen_timestamp` (watermark carried forward but boundary IDs persisted empty).
  Fixed in `FalconFindingsAssetsStage` by advancing `watermarkUtc` and `watermarkIds` together and
  carrying both forward unchanged when a page lacks a timestamp. New regression test
  `ResumeAsync_Findings_AssetsStage_WatermarkBoundaryIds_NotReEmittedOnResume` asserts the boundary
  host is not re-published on resume.
- **M3 (Medium) — FIXED.** `Page = Math.Max(1, assetsStagePage)` at the assets-stage checkpoint write
  site makes the `>= 1` load invariant explicit.
- **M2 (Medium) — ACCEPTED.** `ProcessedItems` sums assets + findings and is not resume-stable
  (findings resume restores findings-only). Consistent with the standalone `FalconAssetsFlow` (assets
  count as items) and CortexXdr (TotalItems = assets + findings). Progress/metric only; findings
  payload unaffected. Documented.
- **M1 (Medium) — ACCEPTED.** A fully-deduped non-final assets page `continue`s without persisting the
  advanced cursor, so a crash re-walks those pages. No data loss or duplication (re-walk is
  watermark-deduped); rare. Acceptable.
- **L1/L2/L3 (Low) — ACCEPTED.** Per-page list allocation when capture off is negligible; the noted
  dry-run/early-return and dedup-contract items are minor and not worth added complexity.

## Residual risks
- Double hosts fetch on the filtered path (assets stage + AID pre-pass hit the same endpoint) — an
  accepted cost of the operator-chosen independent-stage approach; redundant API calls only.
- Assets-stage boundary de-dup is keyed on AID (consistent with the AID pre-pass). Hosts lacking an
  AID exactly at the watermark boundary carry the same tiny residual duplicate risk on resume that
  the AID pre-pass already has.
- Manual end-to-end against a live Falcon tenant (filtered + unfiltered) and a real mid-run
  kill/resume were not executed in this environment; covered by unit tests + design. Recommended
  before release.
