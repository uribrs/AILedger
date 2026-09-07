# Verifier-1: Qualys assets-first switch

Scope verified against real diffs in three repos:
- Collector A: `/Users/user/Dev/cymulate-integration-adapters` (branch `qualys-to-assets-perspective`)
- Collector B: `/Users/user/Dev/AgentService` (branch `qualys-no-fixed`)
- Parser: `/Users/user/Dev/cymulate-integration-parsers` (branch `defender-vm-handle-numbered-findings-files` — NOT the expected branch name, but diff present)

## Per-criterion verdict

### 1. Collector A emits every host; findings-less carry DETECTION_LIST: [] — PASS
- Drop guard removed/relocated: old `ParseDetectionHostsAsync` (emit-gated by `DETECTION_LIST.Count > 0`) replaced by `ParseDetectionsByHostIdAsync` where the `Count > 0` check now only governs **detection-map population**, not emission (`QualysFindingsXmlParser.cs:171-181`).
- Left-join over the host-list spine: `QualysFindingsFlow.BuildHostRecords` iterates the requested `hostIds`, attaches detections if present else `new JsonArray()` (`QualysFindingsFlow.cs:228-271`, esp. 252-254).
- New asset spine endpoint `?action=list&details=All&show_asset_id=1&ids=` (`QualysFindingsApiClient.cs:51-81`).
- Test `ProcessAsync_Findings_PublishesFindingslessHostWithEmptyDetectionList` asserts host 202 emitted with empty DETECTION_LIST + full HOST_DETAILS, host 101 keeps detection + VULNERABILITY_INFO (`QualysCollectorTests.cs:663-760`). **Ran: 8/8 passed (net8.0).**

### 2. Collector B behaviorally equivalent — PASS
- Mirror of A: `getHostDetailsForBatchAsync` spine, `parseHostDetailsBatchAsync` + `convertElementValue` (Newtonsoft idiom), `parseDetectionsByHostIdAsync` keyed by ID, `buildHostRecords` left-join (`QualysCollector.cs:213-409`).
- Wired into `processHostBatchAsync` inside the same limiter slot (`QualysCollector.cs:674-699`).
- Old drop guard `if (detectionList.Count > 0)` still present at `QualysCollector.cs:1209` but now only feeds the detection map (consumed by `parseDetectionsByHostIdAsync`), not emission — functionally equivalent to A.
- New test `CollectFindingsAsync_HostWithoutDetections_EmittedWithEmptyDetectionList` (`Tests/.../QualysCollector.cs:636-756`). **Ran the single test: 1/1 passed (12s), build OK.** Full stress suite not run (worker reported VSTest harness hang; not re-attempted per guidance).

### 3. {HOST_DETAILS, DETECTION_LIST} record shape preserved (both) — PASS
- A: `BuildHostRecords` builds exactly `{ HOST_DETAILS, DETECTION_LIST }` (`QualysFindingsFlow.cs:256-260`).
- B: `buildHostRecords` builds exactly `{ HOST_DETAILS, DETECTION_LIST }` (`QualysCollector.cs:399-403`); test asserts both keys non-null on every emitted host.

### 4. Bounded memory — PASS
- Only the **ID list** is subscription-wide (`GetHostIdsAsync` returns `List<int>`, A `QualysFindingsApiClient.cs:22-44`; B `getHostIdsAsync`). This is the same pre-existing structure; not a regression.
- HOST_DETAILS fetched **per-batch** (`GetHostDetailsAsync(hostIds,...)` bounded to the batch's `ids=`), never for the whole subscription. A paginates the spine; B uses `truncation_limit=0` single page — both bounded to batch IDs.
- No full-run materialization of details introduced.

### 5. Bidirectional correlation intact end-to-end — PASS
- Forward (exposure→asset): parser left-join `asset_type == _lk_type AND lower(asset_value) == _lk_value` gives finding its `asset_id` (`qualysAssetsAndFindings.py:336-345`). Findings frame retains host columns (NETBIOS/IP) because findings are built from `HOST_DETAILS.* + explode(DETECTION_LIST)`, so `asset_value` still resolves (`qualysAssetsAndFindings.py:248-254`).
- Reverse (asset.finding_ids ← exposure.id): `BaseParser.add_finding_ids_to_asset_df` groups findings by `asset_id`, collect_set of `finding.id` → asset.finding_ids (`common/base_parser.py:390-410`). Asset `finding_ids` default empty array, back-filled here (`qualysAssetsAndFindings.py:86-90`).
- Collector record carries both HOST_DETAILS (asset identity) and DETECTION_LIST (exposure rows) so the parser join keys exist.

### 6. Identity-less hosts surfaced, not silently malformed (both) — PASS
- A: `HasAssetIdentity` (NETBIOS||IP); per-host warning + batch-level count log; host still emitted (`QualysFindingsFlow.cs:244-268, 273-280`).
- B: `hasAssetIdentity` + Info-level per-host log + count log; host still emitted (`QualysCollector.cs:382-396, 405-409`).
- Parser side: `value = NETBIOS else IP`; identity-less → null value, then `post_process` filters `value IS NOT NULL` (`common/base_parser.py:168`) — so a truly identity-less host is dropped at the parser, but the collector surfaces it (logged/counted). Matches the contract ("surfaced, not silently malformed").

### 7. No regression: 1960 / retry / rate-limit / resume (A) — PASS
- A: batch planner, parallel `processingGate`, in-order `SortedDictionary` publish, `PersistSerialDegradeCheckpoint` (MaxConcurrency=1), `QualysConcurrencyLimit.IsConcurrencyLimit` catch — all untouched (`QualysFindingsFlow.cs:46-198`). The two new fetches run sequentially **inside** the existing `processingGate` slot, so concurrency bound unchanged.
- Resume: checkpoint schema unchanged (no new fields); resume test asserts first batch host-details are NOT re-fetched (`QualysCollectorTests.cs:571-590`). Spine URL keyed by `ids=` per batch — survives resume on any pod.
- B: `QualysBusyResponse.Classify`, AIMD `IncreaseOnSuccess()`/`Release()`, retry/backoff loop intact (`QualysCollector.cs:631-718`). B has no resume (confirmed; out of scope).

### 8. Parser: asset for empty DETECTION_LIST; zero junk findings; additional_fields from HOST_DETAILS; FQDN from DNS_DATA.FQDN — PASS (logic), TESTS NOT RE-RUN (no JRE)
- Decouple, not explode_outer: `assets_source_df = full_df.selectExpr("HOST_DETAILS.*")` (one row/host, no explode); `findings_source_df` uses **inner** `explode(DETECTION_LIST)` → empty list = zero finding rows, no null-junk (`qualysAssetsAndFindings.py:243-261`).
- Assets built from `assets_source_df`; additional_fields from `assets_source_df.columns` only (HOST_DETAILS, no detection leakage) (`qualysAssetsAndFindings.py:275-300`).
- FQDN: path `DNS_DATA.FQDN`, transformation coalesce → `DNS` fallback (`qualysAssetsAndFindings.py:108-120`). Dotted path resolves because Spark infers DNS_DATA as a struct (`get_spark_full_schema`, `common/base_parser.py:377-387`).
- New tests `tests/test_qualys_assets_first.py` cover: every-host-asset + zero findings for empty list, additional_fields populated/no detection leakage, FQDN DNS_DATA→DNS fallback. **Could NOT re-run here: no Java/JRE in this environment (pyspark 3.3.0 present in `.venv`, but `Java gateway process exited`).** Worker reported 3/3 new + 78 pass / 10 skipped; plausible but unverified by me.

### 9. A↔B parity / flagged divergence — PASS (divergence benign)
- A paginates the spine (`truncation_limit=HostPaginationLimit`, follows `<URL>`); B uses `truncation_limit=0` (single unbounded page). For a batch-sized `ids=` list, both return exactly the batch's hosts → identical output set. Benign. Memory is bounded to one batch either way.
- Fetch order differs (A: details then detections; B: detections then details) — no output impact; both inside the limiter slot.

## Material gaps (ranked)

1. **(MINOR, robustness/parity) B spine fetch bypasses 1960/AIMD classification.** `getHostDetailsForBatchAsync` calls `makeApiCallAsync` + `EnsureSuccessStatusCode()` directly (`QualysCollector.cs:223-233`). A 409/1960 on the **spine** call throws and is caught by the generic exponential-backoff retry, NOT `QualysBusyResponse.Classify` / limiter-decrease that the detection call enjoys. Not a regression (detection call still drives the limiter), but the new call is less adaptive under concurrency pressure. A does not have this asymmetry (its spine call goes through the same `AdapterHttpClient` retry/strategy as detections).

2. **(LOW for this contract) Pre-existing cve_ids drop can silently suppress real findings.** `post_process` drops `type=="vulnerability"` findings whose `cve_ids` is empty (`common/base_parser.py:226-235`). Detections whose QID has no CVE mapping in the knowledge base produce ZERO findings. This is **pre-existing and unchanged**, and orthogonal to the assets-first goal (assets are emitted regardless). But it means "real detection → finding" is not guaranteed when the QID lacks a CVE. Worth surfacing to product; not a blocker for this task.

3. **(LOW, unverifiable here) W1 caveat — host-list `ids=` confirmed by Qualys docs only, not a live `ids=` probe.** The spine endpoint shape was validated live for `details=All` but the `&ids=` filter param was confirmed from docs. Standard Qualys host-list parameter; low risk, but a live `ids=` call would close it. The collector tests mock this shape, so unit tests can't catch a wrong real-API contract here.

4. **(LOW, env) Parser tests not independently re-run** — no JRE in this verification environment. Logic reviewed and judged correct; relies on the worker's reported pyspark run.

5. **(INFORMATIONAL) Identity-less host dropped at parser.** Collector surfaces/logs it (meets "not silently malformed"), but the parser's `value IS NOT NULL` filter (`common/base_parser.py:168`) drops it from assets output. So an identity-less host is logged by the collector but absent from the final asset table. Consistent with "value = NETBIOS else IP" + no valid identity = no insertable asset. Acceptable, but the host does not become an asset.

## Edge cases checked
- Host in detection response but absent from host-list spine: `BuildHostRecords` iterates `hostIds` and skips IDs missing from `hostDetailsById` (`QualysFindingsFlow.cs:239-242`); a detection-only host with no spine entry is **not emitted** (correct — spine is the authority). Same in B.
- Duplicate IDs: parser dedups assets by `(type, value)` (`qualysAssetsAndFindings.py:309-311`); collector dictionaries key by int ID (last-write-wins on dup spine rows).
- Pagination boundary (A): `nextPageUrl` from `<URL>` under `<WARNING>` outside `<HOST_LIST>` handled (`QualysFindingsXmlParser.cs:108-115`).

## Tests re-run by verifier
- Collector A Qualys project: **8/8 passed** (net8.0, no -f override).
- Collector B single findings-less test: **1/1 passed** (12s). Stress suite not run (known harness hang; per guidance not polled).
- Parser pytest: **not runnable** (no JRE for pyspark in this env). Logic reviewed; relies on worker's reported 3/3 + 78 pass.

## Overall verdict: PASS (with minor follow-ups)

All nine success criteria are satisfied by the actual diffs. The assets-first switch is implemented correctly and consistently across both collectors and the parser: every host becomes an asset, findings-less hosts carry empty DETECTION_LIST, the record shape and bidirectional correlation are intact, memory stays bounded to per-batch fetches, and 1960/retry/resume paths are preserved. Re-ran the C# tests (A 8/8, B new test 1/1). Parser logic verified by reading; tests not re-runnable locally (no JRE).

Follow-ups (none blocking): route B's spine fetch through `QualysBusyResponse`/AIMD for full 1960 parity (gap 1); decide whether CVE-less detections should surface as findings (gap 2, pre-existing); a live `ids=` probe would close the W1 doc-only caveat (gap 3).
