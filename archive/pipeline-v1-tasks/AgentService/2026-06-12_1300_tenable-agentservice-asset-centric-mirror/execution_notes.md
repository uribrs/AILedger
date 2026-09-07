# Execution Notes

(Appended during execution by the orchestrator / workers.)

## Contract phase
- Full-tier contract created from the adapter spec. Endpoints/filters settled (A5 VALIDATED), co-located dual-lane requirement settled (A6 VALIDATED). Open items A1 (C1 mapping), A3 (engine/resume/egress mapping), A4 (run-mode dispatch) require reading the AgentService engine in the orchestration phase.

## W2

### Files touched
- `AgentService/Source/CybiCollectors/TenableCollector/TenableIoCollector.cs` — full rewrite of the collection logic (in place, single file; did NOT split into helper classes — minimal-diff path, since the engine's retry/rate-limit/circuit-breaker plumbing is bound to the `TenableIoApi` base class and the protected `eExportTypes` enum, which a helper class could not reach).
- `AgentService/Source/CybiCollectors/TenableCollector/Documentation/ASSET-CENTRIC.md` — new mirror doc (created).

### Decision: single-file vs split helpers
Change-map said either is acceptable, recommended split for Falcon parity. I kept it single-file because `executeApiCallAsync` / `rRetryPolicy` / `rCircuitBreaker` / `makeApiCallAsync` / `eExportTypes` all live on or in the base class; extracting helpers would have required re-plumbing the API-call closure (Falcon does this because its helpers receive a `MakeApiCallWithTokenRefreshAsync` delegate — Tenable has no token-refresh layer to delegate, so the indirection buys nothing). Lower-risk, style-consistent with the existing monolith.

### How (a)-(f) were implemented
- **(a) /assets/export lane** — new `collectAllAssetsAsync` → `createAssetsExportAsync` (POST `/assets/export`, body `{chunk_size:1000, filters:{last_assessed:<unix(baseDate-30d)>}}`) → `waitForExportCompletionAsync(..., eExportTypes.Assets)` (polls `/assets/export/{uuid}/status` to FINISHED) → `processAssetsChunksAsync` (sequential chunk download via shared `downloadExportChunkAsync`, emits each asset JObject verbatim to `assets_{n:D3}.json`). The `CollectAssetsAsync` stub (old `:114-127`) was replaced with a real assets-only implementation (dry-run guard + instanceId parse + `collectAllAssetsAsync`).
- **(b) info filter removed + state filter added** — deleted the per-record `severity=="info"` gate, the dead `trackInfoSeverityVulnerabilities` path, and the `rFilteredInfoVulnerabilities`/`rProcessedInfoVulnerabilities` counters. `createVulnerabilitiesExportAsync` body is now `{num_assets:500, filters:{since:<unix(baseDate)>, state:["OPEN","REOPENED"]}}`. No `severity` key anywhere.
- **(c) enrichment removed** — deleted the `Channel<VulnerabilityForEnrichment>` producer/consumer split, `processEnrichmentPipelineAsync`, `runEnrichmentWorkerAsync`, `enrichVulnerabilityWithAssetDataAsync`, `fetchAssetEnrichmentDataAsync`, `createAssetApiRetryPolicy`/`rAssetApiRetryPolicy`, `srAssetEnricherLock`, `srStreamWriterLock`, `rAssetMetadataCache`, and the `VulnerabilityForEnrichment`/`AssetEnrichmentData` records. Vulns now flow: download chunk → `tryAcceptVulnerabilityRecord` (structural drop guard) → buffer → flush at 1000 → `findings_{n:D3}.json`. No `/assets/{id}` calls remain.
- **(d) verbatim assets** — assets lane emits `assetRecord.ToString(Formatting.None)` with zero reshaping; no `acr_score`/`ratings.acr.score` access. Parser owns the mapping.
- **(e) co-location** — `CollectFindingsAsync` runs assets stage (best-effort try/catch, rethrows OperationCanceledException) THEN vulns stage, both with the same `instanceId`. Separate counters `rAssetsBatchCounter` / `rFindingsBatchCounter` → `assets_NNN` and `findings_NNN` number independently into `cybi/{instanceId}`. Standalone `CollectAssetsAsync` runs assets only. Stats: `TotalAssets` from the assets lane (`rTotalAssets`, incremented per emitted asset), `TotalVulnerabilities` from unique asset:plugin keys in the findings lane.
- **(f) C1 N/A + failure surfacing** — no checkpoint/resume invented. The legacy swallow in `collectAllVulnerabilitiesAsync` (old `:416-420`) was removed: the vulns lane now lets non-cancellation exceptions propagate (and `downloadExportChunkAsync` now throws on a hard chunk-fetch failure instead of returning 0), so a truncated run fails instead of reporting false success. The assets stage inside `CollectFindings` stays best-effort by design (Falcon parity).

### Mirrored from Falcon
- Assets-first-then-findings inside `CollectFindingsAsync`; best-effort assets stage (log-and-continue) — `FalconCollector.cs:203-250`.
- Per-lane independent batch counters + `assets_{n:D3}.json` / `findings_{n:D3}.json` naming, buffer-flush-at-limit, `SaveUploadAndDeleteBatchAsync` usage — `FalconAssetsCollector.cs` / `FalconFindingsCollector.cs`.
- Verbatim single-line emit, "[BATCH] No ... data collected - skipping batch upload" log, finally-flush of the residual buffer.

### Build
Ran: `dotnet build Source/CybiCollectors/TenableCollector/TenableIoCollector.csproj -c Debug` (SDK 9.0.301, project pinned net8.0). First pass surfaced one real error (CS1988: async method with a `ref` counter param in a shared upload helper) — fixed by incrementing the counter in the non-async wrappers and passing the value by value. **Second pass: Build succeeded, 0 errors** (469 pre-existing warnings across the Application project, none from TenableCollector). Restore worked; full Application/Infrastructure project graph compiled.

### Residual risks
1. **Parser must be DUAL_MODE split (cross-repo, highest impact).** Removing enrichment + emitting a separate assets feed breaks any consumer still expecting the old enriched single-findings feed. The Tenable parser must left-join `findings.asset.uuid == assets.id` and map `risk_score ← ratings.acr.score`. Verify before shipping. Outside AgentService.
2. **Backend re-run folder semantics for `cybi/{instanceId}`.** No checkpoint + monotonic in-process counters mean a retried run restarts numbering from 001; whether the backend clears stale `assets_*`/`findings_*` from a prior partial attempt is host behavior, unverified. Co-location/idempotency guarantees are bounded by it.
3. **All-severity volume (~3x) on a blocking, non-progressive 180-min poll.** Streaming per-chunk + flush-at-1000 bounds heap, but wall-clock/memory at platform scale unvalidated.
4. **EU/FedRAMP base URL still hard-coded** (`TenableIoApi.cs cBaseUrl`). Out of scope; noted.
5. **No unit-test project** exists for TenableCollector (none for Falcon either); changes verified by build + inspection + Falcon precedent only.

## W2-repair

Targeted repair of independent code review on `Source/CybiCollectors/TenableCollector/TenableIoCollector.cs` (branch `tenable-to-assets-first`). All line refs post-edit.

- **B1 (Blocker) — export poll terminal states.** `waitForExportCompletionAsync` now treats any terminal non-FINISHED status (FAILED/ERROR/CANCELLED, case-insensitive) as an immediate hard failure instead of polling to the 180-min timeout; FINISHED match also made case-insensitive. `TenableIoCollector.cs:646-665`.
- **M1 — upload failure no longer swallowed.** `uploadBatchAsync` now throws on exception OR uploader returning `false` (was catch-log-continue). `TenableIoCollector.cs:329-363`. Findings lane therefore aborts the run on a dropped batch; assets lane stays best-effort via the try/catch in `CollectFindingsAsync` (`TenableIoCollector.cs:166-180`) — intended asymmetry preserved.
- **M2 — batch counter increments only after successful upload.** `UploadFindingsBatchAsync`/`UploadAssetsBatchAsync` compute candidate `_NNN` as `current+1`, await upload, then `Interlocked.Increment` only on success (uploadBatchAsync throws otherwise). No filename-sequence gaps / overcount. `TenableIoCollector.cs:313-327`. Uploads are awaited serially, so read-then-increment is safe (no CAS loop).
- **M3 — fail-hard-on-chunk decision documented.** Added comment at the findings chunk-processing site explaining the deliberate choice: fail loud rather than ship a silently-truncated findings set; transient failures already absorbed by base-class retry/circuit-breaker (`executeApiCallAsync` -> `rRetryPolicy` + `rCircuitBreaker`) plus chunk-not-ready poll inside `downloadExportChunkAsync`; no resume, platform re-runs from scratch. `TenableIoCollector.cs:695-701`. Behavior unchanged; no resume/ratio-gate added.
- **Cleanups.** Removed dead `global using System.Threading.Channels;` (`_Usings.cs`); removed unused `rProcessedAssets` accumulator (field + its only write in `incrementCounters`); removed the dead double-parse null-guard in `incrementCounters` (fields already validated by `tryAcceptVulnerabilityRecord`). `TenableIoCollector.cs:847-866`.

**Build:** `dotnet build Source/CybiCollectors/TenableCollector/TenableIoCollector.csproj -c Debug` → **Build succeeded, 0 Error(s), 2 Warning(s)**. Both warnings are pre-existing NU1903 advisories on `Microsoft.Kiota.Abstractions` 1.13.1 (transitive, unrelated to this change).

**Deliberately NOT fixed:** no resume/ratio-gate mechanism added (out of scope per M3); assets-lane best-effort asymmetry kept by design.
