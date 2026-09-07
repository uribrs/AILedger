# Assumptions

- A1 — VALIDATED: Falcon flows run independently, one per platform request, each with its own progress context and single checkpoint slot keyed by the `flow` field. (Confirmed via `AdapterBusEntrypointRunner` dispatch + `RecoveryParsingHelper`.)
- A2 — VALIDATED: CortexXdr/DefenderVm findings flows collect assets as an in-flow stage IN ADDITION to their standalone assets flow (assets get published by both). Falcon follows suit. (Confirmed via `CortexXdrFindingsFlow` / `DefenderVmFindingsFlow`.)
- A3 — VALIDATED: Calling `FalconAssetsFlow` inside the findings flow would write a `"CollectAssets"` checkpoint into the findings slot and corrupt findings resume; a unified findings checkpoint with a stage discriminator is required. (Confirmed via single-slot keying.)
- A4 — VALIDATED: `AssetIdsFetcher.FetchAssetIdPagesAsync` already reads each full host object (only to extract the AID) and has after-cursor + `last_seen_timestamp` watermark fallback; it is the right host pager to reuse. (Confirmed by reading the file.)
- A5 — VALIDATED (operator decision): When no FQL filter is present, the assets stage collects the full host inventory.
- A6 — VALIDATED (operator decision): Independent assets stage (separate host pagination), not piggybacking on the concurrent AID producer — accepting ~2× host fetch when a filter is set.
- A7 — VALIDATED: Stage order is assets-then-findings (matches DefenderVm; natural inventory→findings order). Implemented in `FalconFindingsFlow.CollectAsync`.
- A8 — VALIDATED: The assets stage is NOT month-segmented; it uses `AssetIdsFetcher`'s after-cursor + `last_seen_timestamp` watermark fallback. Sufficient for an inventory snapshot and avoids re-implementing segmentation.
- A9 — VALIDATED: Assets-stage watermark boundary IDs are persisted (`AssetsStageWatermarkIds`); `AssetIdsFetcher` now exposes `MaxLastSeenIds` and accepts `startWatermarkIds` so a resume de-dups boundary host rows (parity with `FalconAssetsFlow.LastWatermarkIds`).
