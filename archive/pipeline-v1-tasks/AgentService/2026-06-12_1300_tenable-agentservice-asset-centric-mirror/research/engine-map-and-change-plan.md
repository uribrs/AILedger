# Tenable.io AgentService Collector — Engine Map & Asset-Centric Change Plan

Read-only mapping. Target: convert the legacy AgentService `TenableIoCollector` to the asset-inventory-centric design specified by the reference adapter docs (`src/.../TenableIoCollector/Documentation/01–04`). Another worker implements from this map.

**The single most important finding:** AgentService already ships an in-engine precedent for this exact mirror — `FalconCollector` (`Source/CybiCollectors/FalconCollector/`). Falcon's findings flow runs an assets stage first (`assets_*.json`) then a findings stage (`findings_*.json`), both via the same `ICybiBatchUploader` keyed by `instanceId`, with **no enrichment** and **two independent raw streams**. The Tenable conversion is "make Tenable's findings flow do what Falcon's findings flow already does." Mirror Falcon, not the SDK adapter line-for-line — the SDK adapter's shared-pipeline/checkpoint machinery does not exist in AgentService.

All file:line refs below are AgentService unless prefixed `[SDK]` (the reference repo `/Users/user/Dev/Uri/cymulate-integration-adapters`).

---

## 1. Engine map — how the legacy collector works today

### Entry point, interfaces, host contract
- `TenableIoCollector.cs:3` — `class TenableIoCollector : TenableIoApi, IConnectionChecker<string>, IAsyncAssetsCollector, IAsyncFindingsCollector`.
- `TenableIoCollector.cs:8` — `SupportsBatchUpload => true`. This flips the host onto the batch-upload path (`CybiIntegrationAction.cs:82-98`, `:698-702`): the host still hands the collector a `StreamWriter` over `findings.json`/`assets.json`, but a batch-upload collector ignores it and uploads directly via `ICybiBatchUploader`.
- Two entry methods:
  - `CollectAssetsAsync` (`:114-127`) — **a no-op stub.** Logs "Asset collection is not implemented - using findings collection instead", returns empty stats. Today there is no real assets lane.
  - `CollectFindingsAsync` (`:130-161`) — the real flow. Parses creds (`:138`), honors dry-run (`:140-148`), extracts `instanceId` from `cymulate_instanceId` (`:150`, `ParseInstanceId :266-277`), then `collectAllVulnerabilitiesAsync` (`:153`) → `finalizeCollectionAsync` (`:155`).

### Run-mode / topic dispatch (how a run is told assets vs findings)
- The host (`CybiIntegrationAction.runFlowsAsync :315-476`) iterates the backend-selected `eCybiIntegrationFlowTypes` (`Connection=0`, `CollectAssets=1`, `CollectFindings=2` — `eCybiIntegrationFlowTypes.cs:5-9`). Each flow is dispatched independently:
  - `CollectAssets` → `handleCollectAssetsFlowAsync :840` → writes/uploads to `assets.json` (`:857`).
  - `CollectFindings` → `handleCollectFindingsFlowAsync :674` → writes/uploads to `findings.json` (`:691`).
- **Crucial:** `CollectFindings` does **not** trigger the assets lane. Flows are only run if the backend selects them. So "CollectFindings also emits assets" must be implemented **inside** `CollectFindingsAsync` (Falcon does exactly this — `FalconCollector.cs:203-250`). There is no shared progress context; co-location is achieved purely by both lanes uploading to the same backend folder `cybi/{instanceId}` (see §2e).
- Stats from each flow are aggregated by the host (`aggregateCollectionStats :982-1028`).

### `/vulns/export` create / poll / chunk
- **Create:** `createVulnerabilitiesExportAsync :423-461`. POST `{cBaseUrl}/vulns/export` (`:443`) with body `{"filters":{"since":<unix(baseDate)>}}` (`:432-438`). **Note what is absent and present:** no `severity` filter (severity is dropped later, per-record), **no `state` filter at all** (so FIXED/closed vulns are currently included at the API level — see §2b), no `num_assets`.
- **Poll:** `waitForExportCompletionAsync :465-516`. Loops up to 180 min (`:470`), polls `GET /vulns/export/{uuid}/status` every 10s (`:471,:512`). On `FINISHED` reads `chunks_available`, `total_chunks` (`:494-504`); on `FAILED` throws (`:506-509`). **This is a blocking wait-for-FINISHED — it is NOT progressive** (unlike the SDK adapter which downloads chunks as they appear). It returns only after the whole export is done.
- **Chunk download (findings path actually used):** `processChunkWithFilteringAsync :634-721`. GET `/vulns/export/{uuid}/chunks/{n}` (`:645`), with a "chunk not ready" retry loop (5×/10s on a `400 ... "not ready for download"`, `:647-680`), streams the chunk JArray (`:690-695`), passes to `filterAndQueueVulnerabilitiesAsync`.
- (`processExportChunkAsync :956-1046` + `trackInfoSeverityVulnerabilities :1048-1072` are a second, non-pipeline chunk path that writes straight to the StreamWriter. It is **dead on the batch-upload path** — `collectAllVulnerabilitiesAsync` uses the channel pipeline, not these. Worth deleting in the rewrite but not load-bearing.)

### The chunk engine (concurrency, batching)
- `processExportChunksAsync :518-548` builds an unbounded `Channel<VulnerabilityForEnrichment>` (`:530`) and runs **two stages concurrently** via `Task.WhenAll` (`:543`):
  - **Stage 1 (producer):** `processChunksWithFilteringAsync :550-602`. Iterates chunks in **batches of 6** (`const int batchSize = 6` `:559`), each batch fanned out with `Task.WhenAll` (`:576-583`). So chunk-download concurrency is effectively 6, additionally throttled by a global `SemaphoreSlim srApiRateLimiter = new(6,6)` (`:14`, acquired in `executeApiCallAsync :215`). (The task brief's "~15 concurrent" refers to design intent / comments at `:536`; the actual cap is 6.) Producer completes the channel writer in `finally` (`:594`).
  - **Stage 2 (consumer):** `processEnrichmentPipelineAsync :604-632` → **3 concurrent enrichment workers** (`for i<3` `:618`, `runEnrichmentWorkerAsync :762-828`).
- **Batching / egress buffer:** each enrichment worker buffers enriched records into a `List<JObject>` and flushes at `cBatchUploadSize = 1000` (`:12`, `:785-789`), with a final flush in `finally` (`:816-819`).

### The enrichment lane (to be removed)
- `filterAndQueueVulnerabilitiesAsync :723-760` queues each non-info vuln (carrying `assetUuid`) into the channel (`:752-753`).
- `runEnrichmentWorkerAsync :762-828` pulls from the channel and calls `enrichVulnerabilityWithAssetDataAsync :906-954` for **every** record.
- `enrichVulnerabilityWithAssetDataAsync` → `fetchAssetEnrichmentDataAsync :830-904`: **GET `/assets/{uuid}`** (`:853`) per asset, gated by `srAssetEnricherLock = new(3,3)` (`:15`, `:844`) and memoized in `rAssetMetadataCache` (`:32`, `:839-851,:889`). It mutates the vuln's embedded `asset` object in place (`:928-944`), adding `first_seen`, `last_seen`, `tags`, `acr_score`, `acr_score_v3`, `acr_drivers` (`:931-944`). Uses a separate retry policy `rAssetApiRetryPolicy` (`:22`, `:339-366`, `:854`).
- This is the per-asset hunt the SDK design (D3) removed.

### Where the info-severity filter is applied
- `filterAndQueueVulnerabilitiesAsync :733-741` — `if (severity == "info") { Interlocked.Increment(ref rFilteredInfoVulnerabilities); incrementCounters(...); continue; }`. Info records are counted but **never queued, never uploaded.**
- Duplicated in the dead StreamWriter path `trackInfoSeverityVulnerabilities :1056-1063`.

### Upload / egress ("batchful upload") mechanism + output naming/location
- `UploadFindingsBatchAsync :279-308`. `batchNumber = Interlocked.Increment(ref rFindingsBatchCounter)` (`:283`), `fileName = $"findings_{batchNumber:D3}.json"` (`:284`), then `rBatchUploader.SaveUploadAndDeleteBatchAsync(batch, fileName, instanceId, BaseDirectory, ct)` (`:288`).
- `BaseDirectory` = `Path.Combine(Path.GetTempPath(), "tenable-io-collections")` (`:13`) — a local scratch dir; the file is written there, uploaded, then deleted.
- `ICybiBatchUploader.SaveUploadAndDeleteBatchAsync` (`CybiBatchUploader.cs:40-121`): writes NDJSON to `{baseDirectory}/{instanceId}/{fileName}` (`:53-65`), uploads via `UploadBatchAsync :127-206` → POSTs to relative URL **`cybi/{actionId}`** (`:184`) — here `actionId` == the `instanceId` the collector passes. The backend stores files under a folder keyed by that id, with `isBatchUpload`/`stage="batch_file"` semantics (`:171-178`). On success the local file is deleted (`:81-84`).
- **Output naming is the co-location mechanism:** every batch for one run lands in the same backend folder `cybi/{instanceId}`, distinguished only by `fileName`. Falcon exploits this: `assets_NNN.json` (`FalconAssetsCollector.cs:150`) and `findings_NNN.json` (`FalconFindingsCollector.cs:217`) from one run co-locate because they share `instanceId` but differ by prefix. There is no batch-directory abstraction and no `storageUrl` — the folder IS `instanceId`.
- Completion/progress (`SendProgressUpdate`/`SendCompletion`) is sent **once by the host** after all flows (`CybiIntegrationAction.cs:190-195,:240-258`), not by the collector. The collector only uploads data batches.

### Resume / checkpoint mechanism
- **There is none.** The legacy engine has no checkpoint, no `IResumableAdapter` equivalent, no progress persistence. `collectAllVulnerabilitiesAsync :387-421` even **swallows non-cancellation exceptions** (`:416-420` logs and returns — does not rethrow), so a mid-run failure silently truncates the collection and the run still reports success to the host. A failed/restarted run re-pulls from scratch (a brand-new `/vulns/export`). The SDK adapter's entire Recovery/checkpoint subsystem and the C1 concern have **no analog here** (see §2f).

### Auth, base URL, config
- Base URL is hard-coded: `TenableIoApi.cs:10` `cBaseUrl = "https://cloud.tenable.com"`. No EU/FedRAMP override path exists in the legacy engine.
- Auth header set in `TenableIoApi.setRequestAuthInfo :157-170`: `X-ApiKeys: accessKey=<UserName>; secretKey=<Password>` (`:167`). Creds parsed from raw JSON `accessKey`/`secretKey` in `parseRawApiInfo :245-264`.
- **Reusable base-class building blocks already present** (`TenableIoApi.cs`): `exportAssetsAsync :509-534` (POST `/assets/export`, currently `filters.updated_at` — NOT `last_assessed`), `exportVulnerabilitiesAsync :477-507` (POST `/vulns/export` with `severity`+`state`+`since`), `waitForExportDoneAsync :375-475` and `fetchReportDataAsync :237-320` which already switch on `eExportTypes.Assets` vs `.Vulnerabilities` for status/chunk URLs (`:262-263,:395-396`). These belong to the cache-report code path, but the assets export/poll/chunk URL shapes are proven here and can be copied.

---

## 2. Change-map (six changes), adapted to THIS engine

> Overall approach: **mirror FalconCollector.** Extract the vulns chunk pump into a `TenableIoFindingsCollector` helper (optional but recommended, matching Falcon) and add a new `TenableIoAssetsCollector` helper, OR keep everything inside `TenableIoCollector.cs` and add an assets method + an assets stage call inside `CollectFindingsAsync`. The minimal-diff path keeps it in one file; the Falcon-parity path splits helpers. Either is acceptable — Falcon split, so recommend split for parity.

### (a) Add an `/assets/export` lane (create/poll/chunk, `filters.last_assessed` 30d) emitting a full-inventory feed

**Where to add:** a new assets producer mirroring the existing vulns producer.

1. **Create:** new method `createAssetsExportAsync(apiInfo, baseDate, ct)` modeled on `createVulnerabilitiesExportAsync :423-461`, POSTing `{cBaseUrl}/assets/export` with body:
   ```jsonc
   { "chunk_size": 1000,
     "filters": { "last_assessed": <unix(baseDate − 30 days)> } }
   ```
   Per SDK spec `[SDK] Documentation/01-collection-strategy.md:42-48`. **Do NOT reuse the base-class `exportAssetsAsync :509`** as-is — it filters on `updated_at`, not `last_assessed`. Use `last_assessed` to match the spec (recently-assessed inventory incl. zero-vuln hosts). `((DateTimeOffset)baseDate.AddDays(-30)).ToUnixTimeSeconds()` mirrors the `since` math at `:431`.
2. **Poll:** `waitForAssetsExportCompletionAsync` modeled on `waitForExportCompletionAsync :465-516`, but hitting `/assets/export/{uuid}/status`. (The base `waitForExportDoneAsync :393-397` already shows the assets status URL — reuse the shape.)
3. **Chunk download:** `processAssetsChunkAsync` modeled on `processChunkWithFilteringAsync :634-721` but hitting `/assets/export/{uuid}/chunks/{n}`. **No filtering, no enrichment, no channel** — for each asset JObject, emit verbatim (single-line `ToString(Formatting.None)`) to the asset egress buffer.
4. **Egress:** buffer to `List<JObject>`, flush at 1000, upload via a dedicated counter producing `assets_{n:D3}.json` (see (e)). Mirror Falcon `FalconAssetsCollector.cs:142-174`.
5. **Concurrency:** reuse the batch-of-6 + `srApiRateLimiter` pattern from `processChunksWithFilteringAsync :550-602`, OR keep assets simpler/sequential (asset chunks are far fewer than vuln chunks). Sequential is fine and lower-risk; the spec does not require parallelism here.
6. **Replace the `CollectAssetsAsync` stub (`:114-127`)** with a real implementation that calls only the assets lane (for the standalone `CollectAssets` flow). This makes `IAsyncAssetsCollector` genuinely functional — mirror `FalconCollector.CollectAssetsAsync :119-171`.

### (b) Remove the info-severity filter (all severities; keep `state=[OPEN,REOPENED]`)

- **Delete the severity gate** at `filterAndQueueVulnerabilitiesAsync :733-741` (and the dead twin at `:1056-1063`). All severities — including `info` — must flow through. Drop `rFilteredInfoVulnerabilities` (`:27`), `rProcessedInfoVulnerabilities` (`:31`), and the info-branch of `incrementCounters` (`:1114-1117`).
- **Add the `state` filter to the create body.** The legacy create (`:432-438`) sends only `since` — it omits `state`, so FIXED vulns are currently pulled. Per SDK spec (D4, `[SDK] 01:52-58`) the create body must be:
  ```jsonc
  { "num_assets": 500,
    "filters": { "since": <unix(baseDate)>,
                 "state": ["OPEN","REOPENED"] } }
  ```
  i.e. **add `state=[OPEN,REOPENED]`, add `num_assets`, and never add `severity`.** (The base `exportVulnerabilitiesAsync :486-498` already shows the `state` array shape to copy.)
- Net: severity filtering is fully removed; closed vulns are excluded at the API via `state`, exactly as the SDK adapter does.

### (c) Remove the per-asset `/assets/{id}` enrichment lane entirely

Delete the entire Stage-2 enrichment apparatus:
- `processEnrichmentPipelineAsync :604-632`, `runEnrichmentWorkerAsync :762-828`, `enrichVulnerabilityWithAssetDataAsync :906-954`, `fetchAssetEnrichmentDataAsync :830-904`.
- The `Channel`/producer-consumer split in `processExportChunksAsync :518-548` (no channel needed once there is no enrichment consumer).
- Supporting state: `srAssetEnricherLock` (`:15`), `rAssetMetadataCache` (`:32`), `rAssetApiRetryPolicy` (`:22`,`:339-366`,`:854`), `createAssetApiRetryPolicy`, records `VulnerabilityForEnrichment` (`:44-47`) and `AssetEnrichmentData` (`:49-56`).
- **Replace with a direct write:** the vulns chunk processor should emit each raw vuln JObject straight to the findings egress buffer (verbatim, embedded `asset` sub-object untouched), exactly like Falcon's findings collector and the SDK adapter's "dumb lane". The structure becomes: download chunk → for each record, buffer → flush at 1000 → `findings_{n:D3}.json`. No second stage.
- Keep only the structural drop guard the SDK keeps (`[SDK] 01:70`): drop a record only if it is not a JObject or is missing `asset.uuid`/`plugin.id`. The legacy code already drops on missing `asset.uuid` (`:744-749`); extend to also require `plugin.id` and add a counter (addresses SDK C9).

### (d) Source v3 ACR from the assets feed `ratings.acr.score` — collector emits raw, parser maps

- **Confirm: no in-collector ACR mapping is needed.** The asset records from `/assets/export` already carry `ratings.acr.score` in the raw payload. The collector's job is to emit the asset object **verbatim** (single-line). The parser performs `risk_score ← ratings.acr.score` and the `findings.asset.uuid == assets.id` left-join (`[SDK] 02-decision-making.md:29-34`, `01:88-89`).
- Therefore the implementer should **not** read, reshape, or copy `ratings.acr.score` anywhere in the assets lane. The only reason the legacy code touched `acr_score`/`acr_score_v3` was the now-deleted enrichment (`:883-885,:942-944`) — that disappears with (c). The bulk assets feed supersedes it.
- `acr_drivers` are intentionally not collected (only `/assets/{id}` carries them; accepted per SDK D3). The dumb assets feed does not fetch them.

### (e) Co-locate both lanes for one combined run; assets-only run emits assets only

**This engine co-locates by `instanceId` folder + filename prefix — there is no batch-directory or storageUrl to thread.** Mirror Falcon exactly:

- **Combined run (`CollectFindings`):** inside `CollectFindingsAsync`, run the **assets lane first, then the findings lane**, both passing the same `instanceId`. Mirror `FalconCollector.CollectFindingsAsync :203-268`: the findings method instantiates an assets collector/stage (`:223-237`), runs it (uploads `assets_NNN.json`), then runs the findings stage (`:259-268`, uploads `findings_NNN.json`). Because both upload to `cybi/{instanceId}`, the parser receives `assets*` + `findings*` in one folder — satisfying the DUAL_MODE split parser (`[SDK] 02:47-53`).
  - **Use separate batch counters per lane** (mirror Falcon: `FalconAssetsCollector._batchCounter` and `FalconFindingsCollector._batchCounter` are distinct fields — `FalconAssetsCollector.cs:13`, `FalconFindingsCollector.cs:17`). In the legacy single-file design, `rFindingsBatchCounter` (`:24`) is one counter; add a second `rAssetsBatchCounter` so `assets_*` and `findings_*` number independently and never collide. If splitting into helper classes, each helper owns its own counter for free.
  - **Make the assets stage best-effort** like Falcon (`:243-249`): an assets-stage failure logs and continues into findings, so the findings deliverable is never aborted by an assets hiccup. (Trade-off: matches Falcon; revisit if inventory completeness must hard-gate.)
  - Skip the assets stage on dry-run (Falcon `:208`).
- **Assets-only run (`CollectAssets`):** the real `CollectAssetsAsync` (replacing the `:114-127` stub) runs **only** the assets lane → only `assets_NNN.json` in the folder. Mirror `FalconCollector.CollectAssetsAsync :119-171`.
- **Stats:** populate `CollectionStats.TotalAssets` from the assets lane count and `TotalVulnerabilities` from the findings count, as Falcon does (`:273-284`). The legacy `incrementCounters :1098-1137` "unique asset" logic derived assets from findings — that is the old vulns-centric count and should be replaced by the real assets-lane host count (mirror Falcon `:273`).

### (f) C1 (mid-chunk-resume duplication) — does it apply here?

**It does NOT apply to this engine.** Reasoning:

- C1 (`[SDK] 03-current-concerns.md:9-21`) is a **resume/checkpoint** hazard: a multi-page chunk persists `LastPublishedPage` per page, dies mid-chunk, and on **resume** re-streams the chunk under new page numbers → duplicates. It exists only because the SDK adapter has progressive per-page checkpointing and a host that re-invokes `ResumeAsync`.
- **This engine has no checkpoint and no resume** (see §1 Resume). A failed run does not resume mid-chunk; the host either fails the action or a future run starts a brand-new `/vulns/export` from scratch. There is no persisted `LastPublishedPage`, no `ProcessedTenableChunkIds`, no re-pagination-on-resume path. The exact mechanism C1 describes cannot occur.
- **The analogous (different) exposure that DOES exist, and how the SDK's *intent* maps:** the legacy uploader assigns filenames from a monotonic in-memory counter (`findings_{rFindingsBatchCounter:D3}` `:283-284`). It is **append-only within a single process**, so within one run there is no overwrite/duplicate. The real idempotency gap is at the **whole-run** level: because there is no checkpoint and exceptions are swallowed (`:416-420`), a partially-uploaded-then-restarted run can leave the backend folder holding `findings_001..050` from attempt 1 plus `findings_001..080` from attempt 2 — and since `SaveUploadAndDeleteBatchAsync` writes by filename, attempt 2 **overwrites** `001..050` and **adds** `051..080`. Whether that is clean depends entirely on backend folder semantics on re-run (does it clear the `cybi/{instanceId}` folder before a new attempt?). This is an existing property of the batch-upload host, **unchanged by this conversion**, and is out of scope for the collector. The SDK's C1 "idempotent overwrite via stable page numbers" intent is **already structurally satisfied within a run** (stable monotonic filenames, overwrite-by-name) and there is no mid-run resume to break it.
- **Net for the implementer:** do not port checkpoint/boundary logic — there is nothing to checkpoint against. Just (i) keep the per-lane monotonic counter naming, and (ii) consider **stop swallowing exceptions** in the vulns collection (`:416-420`) so a partial run surfaces as a failure rather than a silent truncation — that is the one C1-adjacent correctness improvement worth making, and it is a behavior fix, not a checkpoint feature. Flag to the operator before changing, since it alters success/failure semantics the host sees.

---

## 3. Risks / open questions

1. **Backend folder re-run semantics (blocking question for completeness, not for the diff).** Co-location and idempotency both rest on how the backend treats repeated uploads to `cybi/{instanceId}`. If a retried run does not clear the folder first, stale `findings_*`/`assets_*` from a prior partial attempt may linger. This is host/backend behavior, **outside the collector** — confirm with the backend team. Does not block the conversion but bounds its correctness guarantees.
2. **No `CollectAssets`+`CollectFindings` fused flow type.** `eCybiIntegrationFlowTypes` has a commented-out `CollectAssetsAndFindings = 4` (`eCybiIntegrationFlowTypes.cs:13`). The combined behavior is therefore achieved by `CollectFindings` running both lanes internally (Falcon's approach), which depends on the **backend selecting `CollectFindings`** for Tenable. Confirm the Tenable integration's selected-flows config includes `CollectFindings` (and that the parser expects the assets feed to ride along). If the backend selects `CollectAssets` standalone for Tenable, the assets-only path must also be correct (it is, per (e)).
3. **Parser contract is cross-repo.** The "dumb collector, smart parser" split (SDK D2) means the Tenable **parser** must already do the left-join + `ratings.acr.score` mapping + plural→singular remap. That parser lives outside AgentService. If the AgentService-side parser/SPARK for Tenable still expects the old enriched single findings feed, removing enrichment + emitting a separate assets feed will break it until the parser is updated. **Verify the consuming parser is the DUAL_MODE split parser before shipping.** This is the highest-impact external dependency.
4. **Severity volume (~3×) on a blocking, non-progressive poll.** This engine waits for the entire export to FINISH before downloading (`:465-516`), unlike the SDK adapter's progressive download. All-severity volume (SDK C8) on a non-progressive, 180-min-capped poll may increase wall-clock and memory footprint. Streaming per-chunk + flush-at-1000 mitigates heap, but validate at platform scale. No code outside the collector required.
5. **EU/FedRAMP base URL is hard-coded** (`TenableIoApi.cs:10`). Out of scope for this conversion (the SDK adapter supports `baseUrl` override; the legacy engine does not). Note only.
6. **No shared host contract changes needed.** `IAsyncAssetsCollector`/`IAsyncFindingsCollector`/`ICybiBatchUploader` already support everything required (Falcon proves it). The conversion is collector-local. The one optional behavior change (rethrow instead of swallow, §2f) is also collector-local.

---

## 4. Build / test

- **Solution:** `/Users/user/Dev/AgentService/AgentService.sln`. Collector project: `Source/CybiCollectors/TenableCollector/TenableIoCollector.csproj`.
- **Target framework:** `net8.0` (`TenableIoCollector.csproj:4`); `ImplicitUsings` + `Nullable` enabled; `EnableDynamicLoading=true` (collectors are dynamically loaded by the host, hence the `ProjectReference … <Private>false</Private>` to avoid shipping host assemblies — `:11-22`).
- **Deps:** `Microsoft.Extensions.Logging 8.0.1`, `Newtonsoft.Json 13.0.3`, `Polly 8.6.2` (`:26-28`). Project-references `Cymulate.Agent.Application.Actions/.Common` and `Infrastructure.Common` (where `TenableIoApi`, the collector interfaces, and `CybiBatchUploader` live).
- **Installed SDK:** `dotnet 9.0.301` is present, but the project pins `net8.0`. Build the project, not a forced `-f net9.0`.
- **Buildability in this environment: not verified — likely blocked.** The collector references the full AgentService Application/Infrastructure projects (`:11-22`); a clean build needs the whole solution's restore (and possibly internal feeds), which was not exercised here. Treat as "build via the AgentService solution from a proper dev environment." There is **no co-located unit-test project** for `TenableCollector` (the dir contains only the collector, `_Usings.cs`, `csproj`, `bin/`, `obj/`). Mirror however the existing AgentService collector tests are organized (Falcon's, if any) when adding coverage; none exists to extend in-place.

---

### Reference precedent index (read these when implementing)
- `Source/CybiCollectors/FalconCollector/FalconCollector.cs:119-285` — combined findings-runs-assets-first pattern + stats.
- `Source/CybiCollectors/FalconCollector/FalconAssetsCollector.cs` — dumb assets lane + `assets_{n:D3}.json` upload.
- `Source/CybiCollectors/FalconCollector/FalconFindingsCollector.cs:216-217` — `findings_{n:D3}.json` upload, separate counter.
- `Source/Infrastructure/Cymulate.Agent.Infrastructure.Common/Services/CybiBatchUploader.cs:40-206` — the upload/co-location mechanism.
- `Source/Application/.../Tenable/IO/TenableIoApi.cs:477-534` — base-class export/poll/chunk URL shapes (assets + vulns) to copy.
