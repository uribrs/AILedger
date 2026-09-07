Role:
You are a senior .NET engineer working on the Cymulate integration adapters, expert in the
CrowdStrike Falcon collector, its flow/checkpoint/resume model, and the shared collector data
pipeline (NDJSON egress).

Goal:
Make the Falcon `CollectFindings` flow also publish hosts to `assets_*.json` via an independent,
resumable assets stage, while leaving the findings (vulnerability) output and logic byte-for-byte
unchanged.

Context:
- Authoritative spec: `/Users/user/.claude/plans/falcon-collector-needs-to-joyful-firefly.md` (read it).
- Base dir: `/Users/user/Dev/cymulate-integration-adapters`, branch `falcon-collector-dup-data-BUG`.
- Code root: `src/Cymulate.Integration.Adapters/Collectors/FalconCollector`.
- Templates (read-only references): `Collectors/CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs`
  (staged findings→assets with a unified checkpoint + `Stage`), and
  `Collectors/DefenderVmCollector/Flows/Findings` (assets-as-stage).
- Reusable primitives already present: `AssetIdsFetcher.FetchAssetIdPagesAsync` (host pager with
  after-cursor + last_seen watermark fallback), `CollectorNdjsonPublisher.PublishAssetsUtf8PageAsync`,
  `NormalizedUtf8Json.SerializeToSingleLine`, `AdapterTopics.Collector.AssetsFlow`,
  `FalconHostFilters.BuildLastSeenTimestampGateFilter`.
- Each Falcon flow has exactly ONE checkpoint slot keyed by the `flow` field — so the assets stage
  MUST checkpoint through the findings flow's own unified state (a `Stage` discriminator), NOT by
  calling `FalconAssetsFlow`.

Constraints:
- Do NOT modify `FalconAssetsFlow` or the standalone `CollectAssets` flow.
- Do NOT change the findings vulnerability stage, AID pre-pass, month segmentation, or existing
  findings checkpoint fields. `findings_*.json` output must be identical to today.
- `AssetIdsFetcher` change is additive and DEFAULT-OFF (host-row capture + optional
  `startWatermarkIds` seed). With capture off, behavior and per-page cost are unchanged.
- Resume-safe: a resume into the findings stage must NOT re-run/re-publish the assets stage.
- Backward-compatible checkpoints: legacy state (no `Stage`, no assets-stage keys) loads and behaves
  as `Stage="findings"` (skip assets).
- Assets stage runs as its own sequential pass; do NOT publish or checkpoint from inside the
  background AID channel producer.
- Keep it bounded — new logic in helper class(es), not bulked into `FalconFindingsFlow.cs`.
- All changes confined to `Flows/Findings`, `Recovery`, and the FalconCollector test project.

Implementation (per plan; file-by-file):
1. `Recovery/FalconCheckpointState.cs` — extend `FalconFindingsCheckpointState` with: `string? Stage`,
   `int AssetsStagePage`, `string? AssetsStageAfterToken`, `DateTime? AssetsStageWatermarkUtc`,
   `List<string> AssetsStageWatermarkIds`, `bool AssetsStageCompleted`, `int TotalAssetsCollected`.
   Leave existing `Assets*`/`PendingAids` fields (AID pre-pass state) untouched.
2. `Recovery/FalconCheckpointHelper.cs` — serialize the new keys in `SaveFindingsState`; read them in
   `TryLoadFindingsStateCore` with safe defaults (stage absent → "findings"; ints → 0; lists → empty;
   bool → false). Backward-compatible.
3. `Flows/Findings/AssetIdsFetcher.cs` — additive opt-in host-row capture: when enabled, `AssetIdPage`
   carries the page's hosts as normalized UTF-8 (`NormalizedUtf8Json.SerializeToSingleLine`), one
   record per resource object (independent of AID de-dup). Add a `startWatermarkIds` seed param so
   resume can avoid re-emitting watermark-boundary host rows. Default path unchanged.
4. `Flows/Findings/FalconFindingsAssetsStage.cs` (NEW) — sequential loop over
   `AssetIdsFetcher.FetchAssetIdPagesAsync` (capture on); publish each page to `assets_*.json` via
   `CollectorNdjsonPublisher.PublishAssetsUtf8PageAsync`; emit `BatchProducedEventArgs` /
   `CheckpointAdvancedEventArgs` on `AssetsFlow`; `progressContext.AdvancePage(items, findingsInBatch:0)`;
   persist the assets-stage checkpoint via the writer after each page; resume from
   `AssetsStageAfterToken`/`AssetsStageWatermarkUtc`/`AssetsStagePage`. Filter: all hosts when
   unfiltered, last-seen gate when filtered. Returns total hosts.
5. `Flows/Findings/FalconFindingsCheckpointWriter.cs` — add `OnAssetsPagePublished(...)` writing the
   unified findings checkpoint with `Stage="assets"` + assets-stage cursor/page/watermark + running
   `TotalAssetsCollected`. Thread `Stage="findings"` and `TotalAssetsCollected` through the existing
   `OnPagePublished` path.
6. `Flows/Findings/FalconFindingsFlow.cs` — at the top of `CollectAsync`, pick entry stage from
   `resumeState?.Stage` (default "findings"). If the assets stage is not complete, run
   `FalconFindingsAssetsStage` first (mark complete, set Stage="findings"), then fall through to the
   UNCHANGED findings logic. Track/restore `TotalAssetsCollected`; return value stays findings total.
7. Tests — `UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test/FalconCollectorTests.cs`.

Success Criteria:
- Solution builds.
- FalconCollector unit tests pass, including new tests:
  - Findings run WITH a filter publishes BOTH `assets_*.json` and `findings_*.json`; findings output
    is unchanged vs. a pre-change baseline.
  - Findings run with NO filter publishes all hosts to `assets_*.json`.
  - Resume with `Stage="findings"` AND legacy (no `Stage`) SKIPS the assets stage (no asset re-publish).
  - Resume with `Stage="assets"` continues the assets stage from its cursor, then runs findings.
- No modifications to `FalconAssetsFlow` / standalone `CollectAssets`.
- `AssetIdsFetcher` AID pre-pass path behaves identically when capture is off.

Execution Rules:
- Do not assume missing data; read the plan and the referenced source files before editing.
- Respect constraints strictly; if a constraint and the code conflict, STOP and surface it.
- Prefer reuse of existing primitives over new implementations.

Output Format:
- Code edits to the files listed above plus new `FalconFindingsAssetsStage.cs` and new tests.
- Build + test results.
- `execution_notes.md` appended with decisions made, assumptions resolved (A7/A8/A9), and residual risks.

Stop Conditions:
- Goal achieved (builds + tests green) and constraints satisfied.
- A required constraint cannot be met without violating another (surface the conflict).
- Findings output cannot be preserved byte-for-byte (surface before proceeding).
