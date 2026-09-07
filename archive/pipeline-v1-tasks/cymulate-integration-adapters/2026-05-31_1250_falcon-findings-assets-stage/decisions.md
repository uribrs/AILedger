# Decisions

- Add an independent assets stage inside `FalconFindingsFlow`, sequenced assets→findings.
- Use one unified findings checkpoint with a new `Stage` discriminator ("assets" | "findings"); legacy/no-Stage = "findings" (skip assets).
- Reuse `AssetIdsFetcher` as the host pager via additive, default-off host-row capture — do not re-implement host pagination, do not touch `FalconAssetsFlow`.
- Assets stage runs as its own sequential pass; never publish/checkpoint from the background AID channel producer (concurrency safety on the dup-data branch).
- New helper `FalconFindingsAssetsStage.cs` owns the assets-stage loop + publish + checkpoint calls; `FalconFindingsFlow.cs` only orchestrates stage selection.
- Findings vulnerability stage, AID pre-pass, segmentation, and existing findings checkpoint fields remain untouched; `findings_*.json` identical to today.
- Flow return value stays the findings total; `TotalAssetsCollected` tracked/persisted separately for progress + resume.
- Collect all hosts when unfiltered (operator decision).
