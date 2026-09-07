# Tenable.io Asset-Centric Split — Feed-Shape Contract Research

READ-ONLY research. Three repos:
- Parsers: `/Users/user/Dev/cymulate-integration-parsers`
- Legacy collector: `/Users/user/Dev/AgentService/Source/CybiCollectors/TenableCollector`
- Adapters: `/Users/user/Dev/Uri/cymulate-integration-adapters/src/Cymulate.Integration.Adapters`

Validated facts taken as given (not re-investigated): v3 ACR on the assets feed at `ratings.acr.score`; correlation key `findings.asset.uuid == assets.id`; Qualys "assets-perspective" is the precedent pattern.

---

## A1 — Split data model / parser feed shape

### The two onboarding patterns (there are TWO, not one)

The parsers repo has **two distinct "split" mechanisms**. Pick the right one — they are wired differently.

**Pattern 1 — separate-file DUAL_MODE (input_resolver):** the parser consumes TWO physical lanes — `assets*.json` and `findings*.json` — at one base path, resolved at runtime.
- `preparation.py:16-20` — `DUAL_MODE_PARSERS` registry: only `defender-vm-assets-findings`, `cortex-assets-findings`, `crowdstrike-assets-findings` today. Onboarding is "add one line here — no preparer function needed" (`preparation.py:12-15`).
- `preparation.py:51-70` `prepare_parser_options` — if `parser_key` is in the registry, calls `_prepare_dual_mode_options`, else returns options untouched (legacy single-file).
- `preparation.py:73-108` `_prepare_dual_mode_options` → `resolve_assets_and_findings_contract` (`utilities/input_resolver.py:27`). This produces an `input_mode` of either `"split"` or `"hydrated"`:
  - `input_resolver.py:44-49` assets lane = `assets.json` (single) or `assets*.json` (glob).
  - `input_resolver.py:50-54` split findings lane = `findings*.json` (glob).
  - `input_resolver.py:55-60` hydrated lane = single `findings.json` (the combined file with assets embedded inside findings).
  - `input_resolver.py:77-91` **if BOTH `assets*` and `findings*` lanes exist → `input_mode="split"`** (split takes precedence over hydrated, `:79`).
  - `input_resolver.py:93-94` **assets present but no findings lane → hard `ValueError` ("split contract incomplete … missing findings lane")**. This is a guardrail: you cannot emit only assets.
  - `input_resolver.py:96-107` else fall back to `hydrated` (single combined `findings.json`).
- The façade parser then branches on `options.input_mode` (`defender_vm/defenderVmAssetsFindings.py:244-249`), loading from `assets_strategy`/`findings_strategy` via `helpers.fetch_df_from_strategy` (`:255-259`).

**Pattern 2 — single-file decoupled-in-parser (Qualys):** ONE combined file (`HOST_DETAILS` + nested `DETECTION_LIST`), lanes decoupled inside the parser. Qualys is **NOT** in `DUAL_MODE_PARSERS`; registry key `qualys-assets-and-findings` (`parsers/__init__.py:37`).

### How no-vuln assets are preserved (the SPARK-explode trap) — the key correctness answer

**Qualys (single-file, the cited precedent), `qualys/qualysAssetsAndFindings.py:226-274`:**
- Assets lane = host rows WITHOUT exploding: `self.full_df.selectExpr("HOST_DETAILS.*")` (`:244`). Every host → exactly one asset row. No-vuln hosts survive because the asset frame never touches `DETECTION_LIST`.
- Findings lane = **INNER** explode of `DETECTION_LIST` (`:260-266` `explode(DETECTION_LIST)`, NOT `explode_outer`). A host with empty/null detections emits **zero** finding rows — no phantom/empty findings.
- All-exposureless guard (`:276-290`): when EVERY host has empty `DETECTION_LIST`, Spark infers it as non-struct (`array<string>`/`array<void>`) and `DETECTION.*` would throw `AnalysisException`; `_detection_list_is_struct()` short-circuits the findings lane to an empty frame (`:267-272`).

**Defender VM (separate-file split), `defender_vm/DefenderVmAssetsFindingsNotHydrated.py:37-62`:**
- Assets loaded from their OWN lane (`assets_strategy`, `:39-40`), filtered to `machine` type, normalized.
- `correlate(machines_df, findings_df, _FINDINGS_CORRELATION)` with `JoinType.LEFT` + `embed_as="Vulnerabilities"` (`:21-26`, `:53`). LEFT join keeps every machine even with zero findings (`utilities/correlation.py:52-63`, `:101`, `fill_missing_array` `:263` backfills `[]`). The asset source frame `assets_source_df` is set to the hydrated (pre-explode) frame (`:55`) BEFORE `_explode_vulnerabilities` (`:59`), so explode only shapes the findings projection — no-vuln assets are never dropped.

**Current Tenable parser (the BROKEN baseline), `tenable/tenableAssetsAndFindings.py:332-402`:**
- Reads ONLY `self.options.findings_file_path` — a single combined file where each finding embeds an `asset` sub-object (`:341-344`).
- Derives assets by selecting from `full_df` (the findings rows) and dedup'ing on `aid`/(`type`,`value`) (`:402-420`). **An asset only appears if at least one finding row references it.** No-vuln assets from `/assets/export` are invisible — this is exactly the explode/derive trap the split is meant to fix.
- ACR today: read from `asset.acr_score_v3` per-finding (`:259-266` `risk_score` ← `asset.acr_score_v3`), carried in additional fields (`:125`, `:392-394`). v3 ACR lives INSIDE the embedded finding-asset, not on a true assets feed.

### EXACT input contract the Tenable parser must consume under the split model

To adopt **Pattern 1 (separate files)** — the asset-centric goal — the Tenable parser must:
1. Be added to `DUAL_MODE_PARSERS` in `preparation.py:16` → `"tenable-assets-findings": "Tenable IO"`. (Registry key confirmed `parsers/__init__.py:31`.)
2. Be rewritten as a façade that branches on `options.input_mode` (mirror `defenderVmAssetsFindings.py:244-249`):
   - `input_mode == "split"`: load assets from `options.assets_strategy` (file `assets.json` / glob `assets*.json` = `/assets/export` inventory, **incl. no-vuln assets**) and findings from `options.findings_strategy` (`findings*.json` = `/vulns/export`). Correlate `findings.asset.uuid == assets.id`, LEFT join so no-vuln assets survive.
   - `input_mode == "hydrated"`: keep the legacy combined-file path for backward compat (existing single `findings.json`).
3. Resolution is automatic from files-on-disk at the base path; no extra config. If the collector writes `assets*` but no `findings*`, the resolver throws (`input_resolver.py:94`) — both lanes must be emitted.

ACR placement: validated fact says v3 ACR sits on the **assets** feed at `ratings.acr.score`. The split assets parser must read ACR from the assets-lane record (`ratings.acr.score`) and map to `risk_score` — replacing today's per-finding `asset.acr_score_v3` read (`tenable/...py:259-266`).

---

## A2 — Legacy collector + batchful upload

Repo: `/Users/user/Dev/AgentService/Source/CybiCollectors/TenableCollector/TenableIoCollector.cs`.

### Asset vs finding shaping
- **Assets flow is NOT implemented.** `CollectAssetsAsync` logs "Asset collection is not implemented - using findings collection instead" and returns 0 (`:114-127`, esp. `:120`). There is no `/assets/export` call.
- **Findings only.** `CollectFindingsAsync` (`:130`) drives `/vulns/export` (`:443`), polls `/vulns/export/{uuid}/status` (`:478`), then downloads chunks. Each finding is **enriched** with per-asset metadata (`/assets/{uuid}`, 3-concurrent via `srAssetEnricherLock` `:15`) and the asset is embedded INSIDE the finding. Output is a single combined "finding-with-asset" stream — i.e. the `hydrated` shape the current parser consumes.

### Batchful upload — concrete mechanism
- `SupportsBatchUpload => true` (`:8`); batch size `cBatchUploadSize = 1000` (`:12`).
- `UploadFindingsBatchAsync` (`:279-308`): accumulates `List<JObject>` findings, names file `findings_{batchNumber:D3}.json` (`:284`, zero-padded sequence), uploads via `ICybiBatchUploader.SaveUploadAndDeleteBatchAsync(batch, fileName, instanceId, BaseDirectory, ct)` (`:288-293`) — writes the batch file, uploads, deletes locally. Counter `rFindingsBatchCounter` via `Interlocked.Increment` (`:283`).
- Pipeline (`:518-548`): Stage 1 chunk download (15-ish concurrent, batches of 6 `:559`), Stage 2 enrichment (3 concurrent) via a `Channel<VulnerabilityForEnrichment>` (`:530`); both stages `Task.WhenAll` (`:543`).
- Export creation (`:432-438`): POST `/vulns/export` with `{"filters":{"since":<unix base date>}}`. Status wait (`:465-516`): 180-min cap, 10s poll, on `FINISHED` reads `chunks_available`/`total_chunks`.

### Relevance to ~6.8M findings (~3× volume)
The *concept* (sequence-numbered NDJSON-ish JSON files, fixed batch size, upload-and-delete to bound disk) is directly relevant and is the same shape the new adapter egress already implements (page-numbered NDJSON, multipart). The legacy *code* is not reusable (Newtonsoft `JObject` in-memory batches of 1000, no resumable checkpoint, asset flow absent). Mirror the **pattern** (bounded batches, deterministic sequence names, stream-not-buffer), not the implementation.

---

## A3 — Adapter egress + resumability

Repo: `/Users/user/Dev/Uri/cymulate-integration-adapters/src/Cymulate.Integration.Adapters`.

### How a collector publishes a feed today
- `Shared/.../DataPipeline/Egress/CollectorNdjsonPublisher.cs` is the single entry point. It enforces mandatory naming: output name MUST be `findings` or `assets` (`:121-137`; constants `Glossary/CollectorGlobalDefaults.cs:19-20` = `"findings"`/`"assets"`). Anything else throws (`:131-133`).
- Per-feed APIs already exist for BOTH feeds: `PublishFindingsUtf8PageAsync`/`PublishFindingsPageAsync` and `PublishAssetsUtf8PageAsync`/`PublishAssetsPageAsync` (`:13-75`). They delegate to `ResultsBatchPublisher` which owns single-upload vs multipart and NDJSON normalization (Egress `README.md:12-35`).
- Output is page-based deterministic NDJSON: `CollectorOutputDefaults.BuildPageTargetPath(outputName, pageNumber)` (`CollectorNdjsonPublisher.cs:136`). Egress owns the four-tier heap defense (throttling → buffering → memory-pressure flush → multipart).

### Can a collector emit TWO distinct feeds? YES — already structurally supported.
- The Tenable adapter collector ALREADY has both flows wired through `AdapterBusEntrypointRunner` via `AdapterBusEntrypointDefinition`: `CollectAssetsAsync` and `CollectFindingsAsync` delegates, dispatched by `req.FlowName` (`Collectors/TenableIoCollector/TenableIoCollector.cs:309-345`, esp. `GetFlowName = r => r.FlowName` `:315`, `CollectAssetsAsync` `:322`, `CollectFindingsAsync` `:323-324`).
- Flow/topic names are glossary constants: `AdapterTopics.Collector.AssetsFlow` and `.FindingsFlow` (used at `TenableIoCollector.cs:106`, `:189`; flow publishes findings via `FindingsFlow` `Flows/Findings/TenableIoFindingsFlow.cs:492`).
- TODAY: `CollectAssetsInternalAsync` is a deliberate **no-op stub** mirroring legacy ("Asset collection is not implemented", `TenableIoCollector.cs:82-127`). The findings flow embeds the enriched asset INSIDE each finding record (`Flows/Findings/TenableIoFindingsChunkProcessor.cs:105-172` — `WriteFindingWithEnrichedAsset` substitutes the `asset` property and writes `acr_score`/`acr_score_v3`/`acr_drivers` into it, `:140-142`, `:170-172`).
- **Wiring two real feeds:** implement an assets flow (call `/assets/export`, page → `CollectorNdjsonPublisher.PublishAssetsUtf8PageAsync`) and slim the findings flow to emit raw `/vulns/export` rows (drop the per-asset enrichment/embedding; ACR moves to the assets feed). The dispatch, naming, and dual-target egress already exist — no Shared changes needed for two feeds.

### IResumableAdapter checkpoint shape to mirror
- Base: `Recovery/TenableIoCheckpointState.cs:7-49` — `Flow`, `LastPublishedPage`, `TotalFindings`, `TotalAssets`, `TotalUniqueVulnerabilities`, `CheckpointCreatedUtc`, `BaseDateUtc`, `IsDryRun`.
- Findings-specific: `TenableIoFindingsCheckpointState` (`:55-85`) — `ExportUuid`, `TotalChunks`, `EnableAssetEnrichment`, `MaxParallelAssetEnrichmentRequests`, `ProcessedTenableChunkIds[]` (resume skips completed chunks → no S3 dupes).
- Checkpoint lifecycle per page (`Flows/Findings/TenableIoFindingsFlow.cs:455-517` `PublishPageAndCheckpointAsync`): publish page → build checkpoint record → `progressContext.SetState(k,v)` for each field → `progressContext.AdvancePage(itemsInBatch, findingsInBatch)`. Resume restores from `resumeState` (`:57-93`): same `ExportUuid`/`TotalChunks`, restores `pageCounter = LastPublishedPage`, rehydrates `ProcessedTenableChunkIds`.
- **For a two-feed collector:** add a sibling `TenableIoAssetsCheckpointState` (export-job model: `ExportUuid`, `TotalChunks`, `ProcessedTenableChunkIds[]`, `LastPublishedPage`, `TotalAssets`) — `/assets/export` is the same create→poll-status→download-chunks export model as `/vulns/export`, so the findings checkpoint is a near-exact template. Each flow checkpoints independently under its own `Flow` discriminator; page numbering stays per-feed and deterministic.

---

## FEED-SHAPE CONTRACT (gates both downstream workers)

### Collector MUST emit (adapters repo)
- **TWO independent feeds**, both page-based NDJSON via `CollectorNdjsonPublisher`, both at the same instance base path:
  - **assets feed** → output name `assets` (`CollectorGlobalDefaults.AssetsFileName`), pages `assets/...page...`. Source = `/assets/export` (FULL inventory, incl. assets with zero vulnerabilities). One NDJSON record per asset.
  - **findings feed** → output name `findings` (`CollectorGlobalDefaults.FindingsFileName`), pages `findings/...page...`. Source = `/vulns/export` (ALL severities — drop the legacy info-severity filter if "all severities" is the goal; current processor filters by severity, confirm with parser worker). One NDJSON record per finding ("dumb" raw rows; do NOT embed/enrich asset).
- Each feed via its own resumable flow, dispatched by `req.FlowName` (`AssetsFlow` / `FindingsFlow`); no Shared changes required for dual emission.
- ACR: lives on the **asset** record at `ratings.acr.score` (raw `/assets/export` shape). Findings records carry NO ACR. Remove the current per-finding `WriteFindingWithEnrichedAsset` asset embedding.

### Per-record schema (raw passthrough — "dumb feeds")
- **assets record**: raw `/assets/export` asset object. Correlation id at `id` (string UUID). v3 ACR at `ratings.acr.score`. No-vuln assets included verbatim.
- **findings record**: raw `/vulns/export` finding object. Correlation key at `asset.uuid` (string). Severity, plugin, state etc. as Tenable emits.

### Parser MUST consume (parsers repo)
- Register `tenable-assets-findings` in `DUAL_MODE_PARSERS` (`preparation.py:16`).
- Rewrite `tenable/tenableAssetsAndFindings.py` as an `input_mode` façade (mirror `defenderVmAssetsFindings.py`):
  - `split`: assets lane `assets*.json` + findings lane `findings*.json`. **Both lanes required** (resolver throws if assets present without findings, `input_resolver.py:94`).
  - `hydrated`: keep legacy single-file combined path for back-compat.
- **No-vuln asset preservation (mandatory):** assets DF built from the assets lane independently (every asset → one row, no explode). Findings correlated to assets via LEFT join on `findings.asset.uuid == assets.id` — never inner-explode the asset frame. Use either the Defender `correlate(..., JoinType.LEFT, embed_as=...)` pattern or the Qualys decouple pattern; pick LEFT-join-from-assets-lane so zero-finding assets survive and zero phantom findings are fabricated.
- ACR mapping: read `ratings.acr.score` from the assets lane → `risk_score` (replace `asset.acr_score_v3` read at `tenable/...py:259-266`).

### Batchful-upload mechanism to mirror
- Use adapter egress page model (`CollectorNdjsonPublisher` + `ResultsBatchPublisher`): deterministic page-numbered NDJSON targets, single-vs-multipart auto-selected, four-tier heap defense. This IS the modern equivalent of legacy `findings_{NNN:D3}.json` batchful upload; do not port Newtonsoft 1000-row in-memory batching.

### Resumability mechanism to mirror
- Per-feed checkpoint records under distinct `Flow` discriminators. Findings: existing `TenableIoFindingsCheckpointState` (ExportUuid, TotalChunks, ProcessedTenableChunkIds[], LastPublishedPage, totals). Assets: new sibling with the same export-job fields. Lifecycle per page = publish → `SetState` → `AdvancePage`; resume restores page counter + processed-chunk set to avoid S3 duplicates.

---

## Could NOT determine / flag for workers
- **"All severities" vs current info-filter:** the current chunk processor filters findings (`TenableIoFindingsChunkProcessor` references info-severity filtering; legacy excluded info severity, `TenableIoCollector.cs:588`). I did not fully trace the adapter's severity filter logic. The findings worker must confirm whether "all severities" means removing this filter.
- **Exact `/assets/export` JSON field layout** (beyond `id` and `ratings.acr.score`): not inspected from a live Tenable response; treat the raw passthrough as authoritative and let the parser map fields. The validated facts (`id`, `ratings.acr.score`) are taken as given.
- **`correlate`/`fill_missing_array` exact null-array backfill semantics** confirmed by signature/docstring (`utilities/correlation.py:52-63,101,263`) but not by reading the full body — verify if the parser worker relies on embedded-array shape.
- **Whether the assets parser should produce `finding_ids` linkage** (Tenable currently defaults `finding_ids` to `[]`, `tenable/...py:221-225`): out of scope here; flag for parser worker.

---

## Confidence (one line each)
- A1 (split model / parser feed shape): **High** — read `preparation.py`, `input_resolver.py`, Qualys + Defender + current Tenable parsers in full; both split patterns and the no-vuln trap are directly evidenced.
- A2 (legacy collector + batchful): **High** — read the legacy collector's asset/findings flows, batchful upload, and export pipeline directly.
- A3 (egress + resumability): **High** — read `CollectorNdjsonPublisher`, the Tenable collector entrypoint definition, findings flow checkpoint lifecycle, and checkpoint state records directly; dual-feed structural support confirmed in code.
