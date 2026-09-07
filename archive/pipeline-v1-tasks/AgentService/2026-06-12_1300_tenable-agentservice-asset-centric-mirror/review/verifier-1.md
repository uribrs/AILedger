# Verifier-1 — Tenable.io AgentService asset-centric mirror

Artifact: `/Users/user/Dev/AgentService/Source/CybiCollectors/TenableCollector/TenableIoCollector.cs`
(working tree, branch `tenable-to-assets-first`, uncommitted `M`) + new
`Documentation/ASSET-CENTRIC.md` (untracked).
Verified by reading the code and the `git diff HEAD` (the change is NOT yet committed — the diff
was taken against the working tree, not a commit).

## Per-criterion verdict

### SC1 — `/assets/export` lane exists, real impl — **PASS**
- `createAssetsExportAsync` POSTs `{cBaseUrl}/assets/export` with body
  `{ "chunk_size": 1000, "filters": { "last_assessed": <unix(baseDate-30d)> } }`
  (TenableIoCollector.cs:438-445; window const `cAssetsLastAssessedWindowDays = 30` :15, math :434).
- Poll `waitForExportCompletionAsync(..., eExportTypes.Assets)` → `/assets/export/{uuid}/status` to FINISHED (:417, :620).
- Chunk download `processAssetsChunksAsync` → `downloadExportChunkAsync(..., eExportTypes.Assets)` → `/assets/export/{uuid}/chunks/{n}` (:488, :781), emits each asset to `assets_NNN.json` (:330, :510, :516).
- `CollectAssetsAsync` is a real implementation (dry-run guard :109-117, instanceId parse :119, `collectAllAssetsAsync` :123) — the old no-op stub is gone (diff confirms). NOT a stub.

### SC2 — info filter GONE, state=[OPEN,REOPENED] present — **PASS**
- No `severity` key in the vulns export body; body is `{ "num_assets":500, "filters": { "since":…, "state":["OPEN","REOPENED"] } }` (:572-580).
- The per-record `severity=="info"` gate, `trackInfoSeverityVulnerabilities`, `rFilteredInfoVulnerabilities`/`rProcessedInfoVulnerabilities` counters, and the 2-arg `incrementCounters(record, isInfo)` are all deleted (diff shows removals; `incrementCounters` is now 1-arg, :847, sole call :706). Only remaining "severity"/"info" hits are explanatory comments (:570, :747).
- `tryAcceptVulnerabilityRecord` drops only structurally-unusable records (not a JObject, or missing `asset.uuid`/`plugin.id`) — :749-768; no severity drop.

### SC3 — `/assets/{id}` enrichment fully removed — **PASS**
- No `GET /assets/{id}` call anywhere; grep for `assets/{`, `enrich`, `Channel<`, `acr_`, `VulnerabilityForEnrichment`, `AssetEnrichmentData`, `rAssetMetadataCache`, `srAssetEnricherLock` returns only comments. Diff confirms deletion of `processEnrichmentPipelineAsync`, `runEnrichmentWorkerAsync`, `enrichVulnerabilityWithAssetDataAsync`, `fetchAssetEnrichmentDataAsync`, the `Channel` producer/consumer split, the enrichment records, and `rAssetApiRetryPolicy`/`createAssetApiRetryPolicy`.
- Vulns now flow straight: download chunk → accept guard → buffer → flush at 1000 → `findings_NNN.json`. No second stage. No dangling references to any deleted method (grep clean).

### SC4 — assets emitted verbatim, no in-collector ACR mapping — **PASS**
- Assets lane adds `assetRecord` (the raw JObject) to the buffer / writes `assetRecord.ToString(Formatting.None)` with zero reshaping (:507, :516). No `ratings.acr.score`/`acr_score` access in the assets path (grep clean). Comment at :502 states the parser owns the mapping. Matches spec D2/D3 (dumb collector).

### SC5 — co-location for combined run; standalone assets-only — **PASS**
- `CollectFindingsAsync` runs assets stage THEN findings stage with the SAME `instanceId` (:168 then :182), both uploading via `ICybiBatchUploader.SaveUploadAndDeleteBatchAsync` into `cybi/{instanceId}`, differing only by filename prefix (`assets_`/`findings_`, :330). Separate counters `rAssetsBatchCounter` (:26, :322) and `rFindingsBatchCounter` (:25, :316) number independently.
- Assets stage is best-effort (try/catch :166-180) and rethrows `OperationCanceledException` (:172-175) before the general catch — slightly stricter than Falcon's bare catch, not a regression.
- Standalone `CollectAssetsAsync` runs the assets lane only (:123) → only `assets_NNN.json`. Matches Falcon dispatch intent.
- Stats: `TotalAssets` from `rTotalAssets` (incremented per emitted asset, :503); `TotalVulnerabilities` from unique `asset:plugin` keys (:861-867). Matches the notes.

### SC6 — C1 correctly N/A; swallow removed — **PASS**
- C1 N/A is genuine, not hand-waved: `IAsyncFindingsCollector` (interface file:5-20) has no resume/checkpoint method; the collector has zero checkpoint/resume references (grep clean). No `IResumableAdapter` analog exists. There is no mid-chunk-resume path to duplicate.
- The swallow was real and is removed. `git show HEAD:` of the old `collectAllVulnerabilitiesAsync` shows a `catch (Exception ex)` that logged "Error during vulnerabilities collection" + stack trace and did NOT rethrow → silent truncation reported success. The new `collectAllVulnerabilitiesAsync` (:537-558) has NO try/catch — non-cancellation exceptions propagate and fail the run. `downloadExportChunkAsync` now `throw`s on a hard chunk-fetch failure (:814, :819) instead of returning 0; it only catches+rethrows `OperationCanceledException` (:840-844).

### SC7 — resumability/upload not regressed; build plausibly green — **PASS (build not run)**
- Upload mechanism unchanged (`SaveUploadAndDeleteBatchAsync`, `BaseDirectory` scratch dir, `cybi/{instanceId}` folder). No resumability existed to regress (none in the engine). Per-lane buffer-flush-at-1000 + finally-flush of residual (:523-533, :729-739).
- Build plausibility: internally consistent — no dangling references to deleted methods, `incrementCounters` arity consistent, `eExportTypes` is `protected` on the `TenableIoApi` base and reachable from the subclass (corroborates the single-file decision), all required usings present in `_Usings.cs`. The CS1988 fix (increment counter in the non-async wrapper, pass by value to async `uploadBatchAsync`) is present (:316/:322 → :326). Did not run the build; no obvious compile breakage.

### SC8 — mirror documentation present and accurate — **PASS**
- `Documentation/ASSET-CENTRIC.md` exists and is accurate: correct create bodies, correct lane/flow descriptions, the 6 deliberate AgentService deviations (no resume, batchful upload, instanceId co-location, blocking poll, swallow-removal as the C1-adjacent fix, Falcon mirror), and the cross-repo residual risk. Matches the code.

## Gaps / drift
- None material. Minor (non-blocking): `System.Threading.Channels` is still globally imported in `_Usings.cs` but unused after enrichment removal — dead using, harmless.
- Notes claim "rethrows OperationCanceledException" for the assets stage — accurate, and it is in fact stricter than the Falcon template it mirrors (Falcon's assets-stage catch is bare). Not drift, just worth noting the mirror is not line-identical here (intentional, and an improvement).
- Cross-repo risks are correctly recorded, NOT silently ignored: the DUAL_MODE split-parser dependency and the backend `cybi/{instanceId}` re-run folder semantics are documented in both `execution_notes.md` (residual risks 1-2) and `ASSET-CENTRIC.md` (Residual risk section). These remain genuine ship-blockers to verify out-of-repo, but they are out of scope for this collector and properly surfaced.

## Build caveat
The change is uncommitted in the working tree. The implementer's "dotnet build … 0 errors"
claim could not be independently confirmed here (build not run, per instructions), but the code
is internally consistent with no detectable compile breakage. The build claim is plausible.

## Overall verdict: **SATISFIED-WITH-RISKS**

All 8 Success Criteria PASS from the code. The "with-risks" qualifier is solely for the two
correctly-recorded cross-repo dependencies that gate real-world correctness but are outside this
collector's scope: (1) the consuming Tenable parser MUST be the DUAL_MODE split parser (left-join
+ `ratings.acr.score` mapping) or this change breaks the old enriched-feed consumer; (2) backend
re-run folder semantics for `cybi/{instanceId}` bound idempotency on a retried partial run. Both
are flagged, not hidden. No checkpoint/dup-path was silently shipped.

Certainty: high on the in-repo code criteria (read every relevant line + the diff). Medium on the
build-green claim (not executed). The cross-repo risks are unverifiable from this repo by design.
