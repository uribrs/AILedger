# Execution Notes

## Phase 0 — Discovery (complete)

### E1 — Collector A (adapters) change surface
- Drop guard to remove: `QualysFindingsXmlParser.ParseDetectionHostsAsync` lines ~100-103 (`if DETECTION_LIST.Count > 0`).
- Host enumeration `QualysFindingsApiClient.GetHostIdsAsync` returns `List<int>` (all IDs up front); host-list parser extracts only IDs today.
- Publish path `QualysFindingsBatchPublisher` → `CollectorNdjsonPublisher.PublishFindingsUtf8PageAsync`; record shape confirmed `{HOST_DETAILS, DETECTION_LIST}` per host. No Shared changes needed.
- Checkpoint: `QualysFindingsCheckpointState` (CompletedBatchCount, PublishedPageCount, HostBatchSize, MaxConcurrency, totals); 1960 degrade via `PersistSerialDegradeCheckpoint` (MaxConcurrency=1). Parallel batches publish in-order via SortedDictionary.
- Tests: `QualysCollectorTests.cs` (Moq + FluentAssertions, FakeHttpClientFactory, XML builders). Findings-less test slots alongside `ProcessAsync_Findings_PublishesEnrichedHostRecord`.
- E1 proposed a full page-streaming rewrite + checkpoint-schema change (NextHostListPageUrl). ORCHESTRATOR NOTE: prefer a smaller change that preserves the batch/checkpoint/1960 machinery (see W1 spec) unless the API assumption fails.

### E2 — Collector B (agent service) change surface + downstream verdict
- Output shape VERDICT: same `{HOST_DETAILS, DETECTION_LIST}` compound, same parser feed. Stored per-host to S3 batch (`UploadBatchAsync`) or stream writer (`enrichAndStoreDetectionsAsync` ~line 602-627).
- Drop guard: `QualysCollector.cs` ~line 974-977 (`if detectionList.Count > 0`). THE key change point.
- Host enumeration `getHostIdsAsync`/`parseHostIdsAsync` (~140-211) drains ALL IDs up front (memory trap); no pagination.
- NO resume (returns CollectionStats; not IResumableAdapter).
- `AdaptiveConcurrencyLimiter` (AIMD) + `QualysBusyResponse` (1960) — long-lived per run; preserve as-is.
- Tests at `/Users/user/Dev/AgentService/Tests/CybiCollectors/QualysCollector.Tests/` (xUnit + Moq; stress tests; no empty-host cases).
- E2 over-scoped (suggested separate assets flow + resume state machine) — REJECTED; out of contract.

## Phase 1 — Implementation (complete)

### W1 — Collector A (adapters) [build OK, tests 8/8]
- Design: PRIMARY (per-batch host-list detail spine via `?action=list&ids=&details=All&show_asset_id=1`). Machinery (enumerate IDs, batch, parallel, in-order publish, checkpoint, 1960 degrade) UNCHANGED.
- Files: QualysFindingsApiClient.cs (GetHostDetailsAsync + GetDetectionsByHostIdAsync), QualysFindingsXmlParser.cs (ParseHostDetailsPageAsync; ParseDetectionsByHostIdAsync — DROP REMOVED), QualysHostDetailsPage.cs (new), QualysFindingsFlow.cs (ProcessBatchCoreAsync rewrite → BuildHostRecords left-join + identity surface), tests updated + new ProcessAsync_Findings_PublishesFindingslessHostWithEmptyDetectionList.
- Caveat: host-list `ids=` confirmed via Qualys docs, not a live ids= probe call (high certainty, one notch below ran-it).

### W3 — SPARK parser [pyspark ran: 3/3 new + 78 pass, 10 skipped]
- pre_process split: assets_source_df = HOST_DETAILS.* (one row/host, no explode); findings_source_df = inner explode(DETECTION_LIST). No explode_outer.
- additional_fields built from HOST_DETAILS only (detection cols no longer leak into asset additional_fields). FQDN: path DNS_DATA.FQDN with DNS fallback. Dedup + findings→assets join unchanged.
- Added tests/test_qualys_assets_first.py against combined.ndjson-shaped fixtures.
- PRE-EXISTING behavior flagged (unchanged): post_process drops findings with empty cve_ids — raw detections without CVE hydration won't surface as findings.

### W2 — Collector B (agent service) [builds standalone, new + malformed tests pass; stress suite hung in harness]
- Mirrors A: getHostDetailsForBatchAsync spine, parseHostDetailsBatchAsync + convertElementValue (JToken idiom), parseDetectionsByHostIdAsync (keyed by ID), buildHostRecords left-join + identity log/count. Drop at ~974-977 removed from emission path. Wired into processHostBatchAsync; spine fetch inside the same AdaptiveConcurrencyLimiter slot.
- Preserved: record shape, 1960/409 handling, AIMD limiter, UploadBatchAsync/stream store, KB enrichment. No resume added (out of scope).
- Fixed pre-existing latent NRE (_mockDecryptor uninitialized) + single-use HttpResponseMessage stream (per-call factory lambdas in mocks).
- Divergence (benign): A truncation-paginates the spine; B uses truncation_limit=0 single page — equivalent for batch-sized ID lists.
- Stress suite: VSTest harness hang (known env issue), not a failure; stopped polling per guidance.

## Synthesis
- A and B emit identical {HOST_DETAILS, DETECTION_LIST} shape, host-list-sourced spine for every host, detections keyed by ID, left-join over requested IDs in order, empty DETECTION_LIST for findings-less, identity-less surfaced. Parity holds.
- Parser consumes that shape: every host → asset (incl. empty DETECTION_LIST); findings only from real detections; bidirectional correlation + dedup intact; FQDN fixed.

## Repairs (post-verifier / post-review)
- B: spine fetch routed through shared `tryClassifyRetryableResponse` → 1960/AIMD parity restored (build OK, tests pass).
- A: detection fetch now FOLLOWS the <WARNING>/<URL> cursor + merges pages (QualysDetectionPage, MergeDetectionsByHostId). 9/9 tests.
- B: detection fetch adds `truncation_limit=0` (single page, no cursor). New boundary test passes; 3/3 fast tests.

## Round 1 code-review (isolated) — review/code-reviewer-{A,B,parser}-1.md
- A: solid; HIGH detection-truncation (FIXED); lower: duplicate-ID record aliasing (shared HOST_DETAILS ref), spine-absent detection IDs silently dropped.
- B: approve; HIGH detection-truncation (FIXED via truncation_limit=0); 1960/AIMD parity intact.
- parser: approve; latent all-exposureless page → DETECTION.* AnalysisException (add guard); dead curated additional_fields props (pre-existing); empty-NETBIOS+IP host → orphaned finding/dropped asset (pre-existing).

## Round 2 comparison (vs each other + prototype) — review/compare-{A,B,parser}-2.md
- Shape parity A vs B vs prototype combined.ndjson: MATCH. Per-host {HOST_DETAILS, DETECTION_LIST}; findings-less → []. STJ vs Newtonsoft cosmetic only (both leaves-as-strings, order preserved). No SPARK-consumability blocker.
- XML→JSON: A and B semantically equivalent on all real data. Prototype probe converter drops XML attributes (A/B handle them) — dormant (fixtures have none); A/B are the more-correct pair. Do NOT regress A/B to match the probe.
- TRUNCATION DIVERGENCE (MEDIUM–HIGH, OPEN): A follows cursor + merges → recovers from any truncation. B uses truncation_limit=0 with NO cursor follow and overwrites on duplicate host-id pages → if Qualys still truncates at its server hard-cap, B silently drops overflow and emits hosts as falsely findings-less. B's test doesn't model server-still-truncated. ACTION: port A's cursor-follow to B for true parity + safety, OR verify cMAX_BATCH_SIZE(200) worst-case rows stay under Qualys per-request cap.
- Parser correlation: value-based join (asset_value/asset_type ↔ value/type), NOT id-based; asset.finding_ids emitted empty. Correct for RAW collector input. The mapper's finding_ids<->exposure.id is a prototype/downstream artifact, not parser input.
- Parser input is combined.ndjson (raw). asset_with_exposures.json / exposureless_asset.json are the prototype MAPPER's OUTPUT, not parser input.
- Empty-cve_ids drop (base_parser): findings only materialize with KB VULNERABILITY_INFO enrichment (collectors do this in prod; fixtures lack it). Non-CVE QIDs (config checks) dropped as findings even in prod — pre-existing product decision. Assets unaffected (LOW for assets-first).

## Status: implementation + both review rounds complete. One OPEN decision: B detection-truncation cursor parity (see above).

## Repairs round 2 (post comparison)
- B: ported A's detection <WARNING>/<URL> cursor-follow + per-host merge (dropped truncation_limit=0). 3 fast tests pass incl. multi-page DetectionsAcrossTruncationPages.
- Parser: added _detection_list_is_struct schema guard — all-exposureless page short-circuits findings lane to empty (host-schema) frame, no throw; assets still 1/host. 79 passed, 10 skipped.

## Parity re-verify (review/verifier-2.md): PASS
- B detection fetch now identical to A: cursor under <WARNING> outside </HOST_LIST> (insideHostList guard), GET→parse→merge-by-id→follow until null, append-not-overwrite, inside limiter slot + 1960/AIMD path. Neither sets truncation_limit on detections.
- Residual (flagged): SPINE fetch differs — A paginates (HostPaginationLimit + cursor), B single-page truncation_limit=0.

## Full /code-reviewer scan (round 3) — review/code-reviewer-{A,B,parser}-full-3.md
- Parser: APPROVE, tests executed 4/4 + independent mixed-page probe. Guard robust. No new blockers. LOW: guard doesn't probe element nullability (safe direction); pre-existing orphaned-finding-when-NETBIOS+IP-null; dead curated additional_fields props.
- A: APPROVE w/ 1 fix. NEW MEDIUM (verified by running): duplicate host ID within a batch is a HARD CRASH — BuildHostRecords (QualysFindingsFlow.cs:258) attaches spine JsonObject by reference; repeated ID re-parents → InvalidOperationException "node already has a parent", aborts batch. Enumeration does no dedup. Fix: Distinct() IDs or DeepClone the host node. LOW: ID GetValue<string> throws on attributed element; detection parser missing CheckCharacters=false (RESULTS raw output); no max-page guard on cursor loops.
- B: SHIP-ABLE w/ 1 MEDIUM. NEW MEDIUM: SPINE fetch doesn't follow cursor (truncation_limit=0 single-page) — if Qualys truncates spine for large details=All batch, omitted hosts silently dropped (buildHostRecords continues past them) — same silent-loss the detection fix closed, moved to spine; A paginates its spine so this is also an A↔B parity gap. LOW: _collectedAssets over-counts (from enumeration, not emitted); no page-cap on detection loop; parseVulnerabilityDetectionsBatchAsync now dead-but-for-reflection-test shim.

## Live local run validation (logs/published-batches/20260610-071549/collector-run/findings_000001.json)
- Real Collector A output (qg2 lab tenant): 7 NDJSON records, all {HOST_DETAILS, DETECTION_LIST}, HOST_DETAILS keys match prototype exactly (ASSET_ID/NETBIOS/IP/OS/DNS/DNS_DATA/SERIAL_NUMBER/HARDWARE_UUID/...). 0 findings-less (tenant all-vulnerable), 0 duplicate IDs, all have NETBIOS/IP. Shape CONFIRMED against fixtures on real data. Findings-less + duplicate-ID paths NOT exercised by this tenant (only by unit tests).

## OPEN (post full-scan): 2 NEW MEDIUMs — A duplicate-ID crash (verified); B spine-fetch no cursor-follow (parity + silent-loss). Both bounded fixes; pending user decision.
