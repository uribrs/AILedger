# Code Review — Tenable.io asset-centric split

Scope reviewed:
- .NET (`TenableIoCollector`): new `Flows/Assets/` (export client + chunk processor + flow), assets checkpoint state + helper + resume path, `IAssetsCollectorAdapter` impl, URL additions, findings-flow/chunk-processor changes, deletion of `TenableIoAssetEnricher`.
- Python (`cymulate-integration-parsers`): `preparation.py` dual-mode registration, `tenableAssetsAndFindings.py` mode-agnostic seam, new `tenableAssetsAndFindingsNotHydrated.py`.

Change type: feature logic + shared-ish collector/parser code. Risk: **Medium–High** (persistence/checkpoint boundary, paginated export resumability, large-data streaming, PySpark join correctness). Reviewed with proportional scrutiny on the hot/streaming/recovery paths.

Overall: the implementation is coherent, idiomatic, and faithfully mirrors the existing findings flow. Streaming, disposal, cancellation, and checkpoint mechanics are handled correctly. No blockers. Findings below are concentrated in dead code, retry amplification (inherited), and a couple of split-mode parser correctness assumptions worth confirming.

---

## Findings (ranked)

### 1. Dead configuration + dead stats now that enrichment/info-filtering is removed — Major (maintainability)
**Problem.** `TenableIoAssetEnricher` deletion and the findings chunk-processor change to "dumb passthrough" leave several members orphaned but still wired:
- `TenableIoCollectorConfiguration.EnableAssetEnrichment` and `MaxParallelAssetEnrichmentRequests` — still parsed in `TenableIoCollectorConfigurationBuilder` (lines 71-72) and round-tripped through the findings checkpoint (`TenableIoCheckpointHelper` ~95-96, 222-223; `TenableIoCheckpointState` 72/77) but no longer consumed anywhere.
- `TenableIoFindingsStats.IncrementFilteredInfo()` / `FilteredInfoVulnerabilities` — `IsInfoSeverity` filtering was removed from the processor, so the counter is never incremented; it's still a public surface.
- `TenableIoFindingsFlow.TotalAssetsCollected` is now hard-wired to `0` and `TotalUniqueVulnerabilities` is relabeled to "pairs seen" — comment says "retained for checkpoint contract."

**Impact.** Operators can still set `enableAssetEnrichment` / `maxParallelAssetEnrichmentRequests` and observe no effect — silent no-op config is a support trap. Dead stat methods invite future mis-wiring.

**Fix.** Decide explicitly: if the enrichment path is gone for good, drop the two config properties and the `FilteredInfo` stat (and their checkpoint keys, guarding load to tolerate their absence — the loaders already use `TryGet*`). If they are intentionally retained for forward/back-compat with in-flight checkpoints, leave them but mark `[Obsolete]`/comment at the config + builder site so they aren't re-plumbed. Local patch, not a refactor.

### 2. Nested retry amplification on chunk download — Major (runtime, but inherited / not new)
**Problem.** In `TenableIoAssetsFlow.ProcessChunksProgressivelyAsync` the per-chunk loop (`for attempt 1..MaxChunkRetryAttempts`) calls `GetChunkStreamWithRetryAsync`, which *itself* retries transient/transport/circuit-breaker failures up to `MaxChunkRetryAttempts`. A retryable failure is therefore retried up to `MaxChunkRetryAttempts × MaxChunkRetryAttempts` in-process per poll cycle, then re-queued for up to `MaxChunkRetryAttempts × 3` cumulative cross-cycle attempts (`transportRetryAttempts`). With the default and a long backoff this is a large blind in-process retry budget against a rate-limited vendor.

**Impact.** Under sustained 429/5xx this multiplies request volume and wall-clock against Tenable; the outer loop's `delay` and the inner loop's `delay` stack.

**Evidence/Severity caveat.** This is a **verbatim copy** of the existing `TenableIoFindingsFlow` behavior, not introduced by this change. So it is not a merge blocker for this PR. Flagging because the copy doubles the blast radius (now two flows) and is the right moment to decide whether the double layer is intentional. If the inner `GetChunkStreamWithRetryAsync` is meant to be the single retry authority, the outer `catch when (!isLastAttempt && IsRetryableStreamFailure)` branch is redundant for stream-acquisition failures (it would only meaningfully catch failures thrown *during* enumeration after a successful stream open).

### 3. Large-scale code duplication between assets and findings flows — Minor (maintainability)
**Problem.** `TenableIoAssetsFlow` is ~95% identical to `TenableIoFindingsFlow`: poll loop, status classification, `TrackServerSideFailures`, `FindNewChunkIds`, `ComputeChunkRetryDelay`, `AddJitter`, `IsRetryableStreamFailure`, `GetChunkStreamWithRetryAsync`, skipped-ratio gate, and the page/checkpoint pump differ only in log strings, the publisher call (`PublishAssetsUtf8PageAsync` vs `PublishFindings…`), and the checkpoint record type.

**Impact.** Two copies of intricate recovery logic will drift; a bug fixed in one (e.g. finding #2) must be fixed in both.

**Fix.** Defer for this PR — the repo convention is self-contained per-flow files, and extracting a shared `ExportChunkPump<TCheckpoint>` is a non-trivial refactor with its own risk. Recommend a follow-up: hoist the poll/retry/skip machinery into a shared base parameterized by (publisher delegate, checkpoint factory, log tag). Not required now; note it so the duplication is a conscious tradeoff, not drift.

### 4. Split-mode join correctness rests on an undocumented vendor-ID equality — Major to confirm (correctness)
**Problem.** `tenableAssetsAndFindingsNotHydrated.py` correlates findings→assets via `findings.asset.uuid == assets.id` (the `/assets/export` `id`). The reshape (`_build_asset_envelope_df`) hard-maps `id -> asset.uuid`. The whole split-mode linkage is correct **iff** Tenable's `/vulns/export` `asset.uuid` is the exact same value as `/assets/export` `id` for the same host.

**Impact.** If they ever diverge (different casing, or vulns uses a different asset identity than the export id), every finding LEFT-joins to null `asset_id` and findings silently orphan — no error, just broken correlation. This is the single highest-consequence behavior in the change.

**Fix.** This is asserted in the module docstring and `decisions.md` per the comments, so it's a recorded assumption rather than a code defect. Confirm it is backed by Tenable docs/operational evidence (not inference). If casing is a risk, consider normalizing both sides (the hydrated path already `F.lower(...)`s the value side); the split path does an exact equality with no normalization. Worth a one-line guard log counting unmatched findings after the join to make orphaning observable in prod.

### 5. `os_type` and `os_version` both resolve to the OS name — Minor (correctness, pre-existing)
**Problem.** Both `os_type` and `os_version` map `asset.operating_system` via `element_at(col, 1)` — so `os_version` is populated with the first operating-system string, not a version. The split reshape feeds `operating_systems` (array) into the same field, preserving this.

**Impact.** `os_version` carries the OS name, likely wrong semantically.

**Evidence.** This mapping is **unchanged** by the diff (present in the hydrated mapping already); the split path only keeps it consistent. Not introduced here, not a merge blocker — noting because the change touches these lines and now applies them to a second lane.

### 6. `df.count()` logging in split pre_process forces extra full passes — Minor (efficiency)
**Problem.** `pre_process` logs `assets_lane_df.count()` and `findings_lane_df.count()`. On Spark these are eager actions that materialize each lane once purely for a log line; the frames are not cached, so the data is read again downstream in `process()`.

**Impact.** Two extra full scans of the raw NDJSON inputs per run. On large Tenable exports this is non-trivial IO/compute for diagnostics only.

**Fix.** Drop the counts, or gate them behind debug logging, or `.cache()` the lanes if the count is genuinely wanted (they are read again in `process()` regardless). Local patch.

---

## Things checked and found correct (no action)
- **Stream lifecycle / disposal:** `await using` on chunk streams in both flows; `using (owned)` per record in both chunk processors; `JsonDocument` disposed; deleted enricher's `MemoryCache`/`SemaphoreSlim` disposal correctly removed with it.
- **Cancellation:** `ThrowIfCancellationRequested` at loop heads, `OperationCanceledException` rethrown ahead of retry filters in every catch ladder, `.WithCancellation` on the async-enumerable consumption.
- **Checkpoint round-trip:** `SaveAssetsState`/`TryLoadAssetsStateCore` are symmetric; `processedTenableChunkIds` parse tolerates malformed entries; stale-export guard (~24h) added for the assets flow; resume restores `pageCounter`, `_totalChunks`, `_totalAssetsPublished`, and processed-chunk set consistently. `_totalAssetsPublished` accounting (`+= effectiveRecordCount`, checkpoint `TotalAssets = base + effectiveRecordCount`) is internally consistent.
- **409 already-running handling** in `CreateExportAsync` reuses `active_job_id` and tolerates body-parse failure — sensible idempotency for a re-invoked export.
- **404-on-status → non-retryable `InvalidOperationException`** correctly forces a fresh collection rather than looping on an expired export.
- **PySpark mode-agnostic seam:** the `_asset_schema_df`/`_finding_schema_df` properties default to `full_df`, preserving hydrated behavior byte-for-byte; `_asset_has_field` extraction is a clean dedupe of the repeated `isinstance(... StructType) and name in names` guard. ACR `acr_score_v3`/`acr_score`/`acr_drivers` are always present in the reshaped struct, so the schema guards stay true and the `ACR_ASSET_ADDITIONAL_FIELDS` backfill loop won't double-emit. `_finding_asset_uuid` is captured from `asset.uuid` before `asset` is dropped, excluded from `additional_fields`, and dropped post-join — no column collision.
- **Split dedup:** `dropDuplicates(["_asset_correlation_id"])` is correct for the one-row-per-asset lane (no explode), and no-vuln assets survive because `assets_df` is built independently from the assets lane and the join is finding→asset LEFT (no phantom findings fabricated).
