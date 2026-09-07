# Constraints

- Do NOT modify `FalconAssetsFlow` or the standalone `CollectAssets` flow.
- Do NOT change the findings vulnerability stage, the AID pre-pass, month segmentation, or existing findings checkpoint fields.
- `findings_*.json` output must be byte-for-byte identical to today.
- `AssetIdsFetcher` change must be additive and default-off; the AID pre-pass behavior and per-page cost must not change when the new capture is off.
- Resume-safe: a mid-findings resume must NOT re-publish a completed assets stage (duplicate `assets_*.json` pages are the failure class — this is the `falcon-collector-dup-data-BUG` branch).
- Checkpoint changes must be backward-compatible: legacy checkpoints (no `Stage`, no assets-stage keys) must load and behave as `Stage="findings"` (skip assets).
- Assets stage publishes via `CollectorNdjsonPublisher.PublishAssetsUtf8PageAsync` → files named `assets_NNNNNN.json` (one host record per line, NDJSON).
- Assets-stage events use `AdapterTopics.Collector.AssetsFlow`; findings events stay on `FindingsFlow`.
- Keep the change bounded: new logic goes in helper class(es), not bulked into `FalconFindingsFlow.cs`.
- All new/changed code stays within `Collectors/FalconCollector/Flows/Findings` and `Collectors/FalconCollector/Recovery` (plus the test project).
- Flow's return value remains the findings (vulnerability) total, not assets.
- Respect existing concurrency model: the assets stage runs as its own sequential pass — do NOT publish/checkpoint from inside the background AID channel producer.
