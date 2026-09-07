# Verifier-3 — Final Parity: Qualys Collector A vs B

Scope: behavioral equivalence of the two Qualys collectors at final state.

- **A** = cymulate-integration-adapters (System.Text.Json), `Collectors/QualysCollector/Flows/Findings/*` (uncommitted diff on `qualys-to-assets-perspective`).
- **B** = AgentService (Newtonsoft), `Source/CybiCollectors/QualysCollector/QualysCollector.cs` (uncommitted diff on `qualys-no-fixed`).

## Overall verdict

**DIVERGES — two real behavioral divergences (one MEDIUM, one LOW-MEDIUM), plus minor observability gaps.**

The core data-plane mechanics (cursor following, per-host merge, spine left-join, findings-less emission, record shape) are equivalent. The divergences are in (4) duplicate-ID emission and (5) asset-count semantics. Both are reachable in production, neither crashes, but they produce different output/telemetry for the same vendor data.

Both projects **build clean**. A's Qualys test project **passes 10/10** (585 ms). B not run (per instructions); B builds clean.

## Dimension-by-dimension

### 1. Detection fetch (cursor + per-host merge, inside limiter + 1960/AIMD) — PASS
- A: `GetDetectionsByHostIdAsync` (QualysFindingsApiClient.cs:137-170) follows `<WARNING>/<URL>` via `AdvanceCursor`, merges per host via `MergeDetectionsByHostId` (append, not overwrite). Concurrency/1960 handled by `QualysConcurrencyLimit` + resilience strategy (out-of-flow).
- B: detection cursor loop in `processHostBatchAsync` (QualysCollector.cs:628-662), merges via `mergeDetectionsByHostId` (QualysCollector.cs:1288-1304, append). Loop runs inside `iLimiter.AcquireAsync` slot (line 611) and routes failures through `tryClassifyRetryableResponse` → `QualysBusyResponse.Classify` (1960/AIMD).
- Both merge (append) per host; neither overwrites. Equivalent.

### 2. SPINE fetch (cursor + merge, no silent drop) — PASS
- A: `GetHostDetailsAsync` (QualysFindingsApiClient.cs:96-128) follows cursor, merges into `hostDetailsById` map (last-write-wins per unique ID).
- B: spine loop in `processHostBatchAsync` (QualysCollector.cs:684-718) via `parseHostDetailsBatchAsync`, same map merge (line 711, "last write wins is fine").
- Both follow the spine cursor across pages; both held in the batch's concurrency slot and 409/1960-classified.
- Neither silently drops a *cursor* page. (A host absent from the spine entirely is skipped by both `buildHostRecords` — see dim 5.)
- Equivalent.

### 3. Cursor termination guards (max-page cap + non-advancing/repeated-URL) — PASS (different mechanism, same guarantee)
- A: `AdvanceCursor` (QualysFindingsApiClient.cs:34-58) — cap `MaxPagesPerFetch = 100_000`; non-advancing detection compares `nextPageUrl == previousUrl` (immediate predecessor only).
- B: `shouldFollowCursor` (QualysCollector.cs:866-888) — cap `cMaxCursorPages = 1000`; repeat detection via a `HashSet<string>` of all seen URLs (catches *any* repeat, not just the immediate predecessor).
- Both guarantee termination. Two non-load-bearing differences: cap magnitude (100k vs 1k) and repeat-detection scope (predecessor-only vs full set). B's guard is strictly stronger; A's is sufficient for real Qualys cursors. Not a behavioral divergence for valid responses.
- Note (not in the asked dimensions): A's host-ID *enumeration* (`GetHostIdsAsync`, truncation_limit > 0) follows + guards a cursor; B's enumeration (`getHostIdsAsync`, `truncation_limit=0`) returns all IDs in one page and has no loop. Both retrieve the full ID set; just different strategies.

### 4. Duplicate host ID within a batch — DIVERGES (MEDIUM)
- **A emits ONE record. B emits TWO.**
- A: `BuildHostRecords` (QualysFindingsFlow.cs:235, 248) uses `var emitted = new HashSet<int>()` + `if (!emitted.Add(id)) continue;` → one record per duplicated ID. Explicitly motivated in-code: re-attaching the same spine `JsonObject` twice would throw "node already has a parent" (STJ). **Covered by test** `ProcessAsync_Findings_DuplicateHostIdInBatch_EmitsSingleRecordWithoutThrowing` (asserts exactly one record).
- B: `buildHostRecords` (QualysCollector.cs:362-393) has **no seen-set**. For a duplicated ID, `iHostDetailsById.TryGetValue` succeeds on both iterations and `(JArray)detections.DeepClone()` (line 384-386) is taken each time, so two records are emitted. No "already has a parent" throw because Newtonsoft tolerates re-parenting differently and the detection list is cloned each time — but the **same `hostDetails` `JObject` instance is attached to both records** (line 390), which is a shared-reference aliasing hazard on top of the duplicate. **No duplicate-ID test in B's suite.**
- Reachability: identical in both — neither dedupes the enumerated host-ID list (A: `GetHostIdsAsync` appends; B: `parseHostIdsAsync` appends), and batches are contiguous slices, so a server returning an overlapping/repeated ID lands the same duplicate into one batch in both.
- Severity **MEDIUM**: B over-counts the duplicated asset (one extra emitted record + the count it feeds, see dim 5) and shares a mutable `HOST_DETAILS` instance across two emitted records. Not a crash, but wrong output and a latent aliasing bug.
- Exact locations: A = `Flows/Findings/QualysFindingsFlow.cs:248` (`emitted.Add(id)` guard). B = `Source/CybiCollectors/QualysCollector/QualysCollector.cs:370-393` (no guard).

### 5. Asset-count semantics (TotalAssetsCollected vs _collectedAssets) — DIVERGES (LOW-MEDIUM)
- **A counts ENUMERATED host IDs. B counts EMITTED records.**
- A: `TotalAssetsCollected = hostIds.Count` set once at `QualysFindingsFlow.cs:55` (the enumerated ID count) and only read thereafter — never re-derived from emitted records. So a spine shortfall (fewer hosts returned than enumerated) does **not** reduce A's reported asset count, and duplicate IDs (collapsed to one record) do **not** reduce it either.
- B: `Interlocked.Add(ref _collectedAssets, hosts.Count)` at `QualysCollector.cs:749` — sums the **emitted** record counts per batch. In-code comment explicitly states this is intentional: "reflects what was actually emitted ... so a spine shortfall does not over-count assets." A duplicate ID inflates B by one (it emits two — compounds dim 4).
- Net: for a clean run with no shortfall and no duplicates, both equal the host count. They diverge whenever (a) the spine returns fewer hosts than enumerated, or (b) a duplicate ID appears. The prompt's premise ("both now reflect emitted records") is **not** met — A reflects enumeration, B reflects emission.
- Severity **LOW-MEDIUM**: telemetry/stats only (does not change emitted records). But the two collectors will report different asset totals for the same shortfall/duplicate scenario, which undermines the parity goal for reconciliation.
- Exact locations: A = `Flows/Findings/QualysFindingsFlow.cs:55`. B = `Source/CybiCollectors/QualysCollector/QualysCollector.cs:749`.

### 6. Identity-less host (no NETBIOS/IP) — PASS (with LOW log-level/observability difference)
- Both emit the host anyway and log. A: `BuildHostRecords` (QualysFindingsFlow.cs:253-259) logs **Warning** per host + an aggregate Warning (line 272-277). B: `buildHostRecords` (QualysCollector.cs:377-399) logs the same two messages (same wording) at **Info**.
- Behavior (emit-anyway) is equivalent; only the severity level differs (Warning vs Info). LOW — does not affect data, only log filtering/alerting.

### 7. Emitted record shape ({HOST_DETAILS, DETECTION_LIST}, findings-less → []) — PASS
- A: `BuildHostRecords` emits `{ ["HOST_DETAILS"]=hostDetails, ["DETECTION_LIST"]=detectionList }`; findings-less gets `new JsonArray()` (QualysFindingsFlow.cs:261-269). Findings-less hosts absent from the detection map (parser only merges when `detectionList.Count > 0`, QualysFindingsXmlParser).
- B: same two-key shape (QualysCollector.cs:388-392); findings-less gets `new JArray()` (line 386). Detection parser only adds when `detectionList.Count > 0` (QualysCollector.cs:1411).
- Identical shape; both left-join to empty `[]` for findings-less. PASS.

## Build / test evidence

- A collector build: **succeeded**, 0 warnings/0 errors.
- A test project (`Cymulate.Integration.Adapters.Collectors.QualysCollector.Test`): **Passed 10/10, 585 ms**. Covers findings-less emission, detection truncation cursor, spine truncation cursor, duplicate-ID single-record, malformed XML.
- B collector build: **succeeded** (2 unrelated NU1903 Kiota warnings, pre-existing). B tests not run per instructions. B test diff adds: findings-less host, detection-across-pages, spine-across-pages — but **no duplicate-ID test** (confirms the dim-4 gap is untested in B).

## Summary of residual divergences

| Dim | Divergence | Severity | A location | B location |
|-----|-----------|----------|-----------|-----------|
| 4 | Duplicate host ID: A emits ONE, B emits TWO (+ shared HOST_DETAILS ref) | MEDIUM | QualysFindingsFlow.cs:248 | QualysCollector.cs:370-393 |
| 5 | Asset count: A=enumerated IDs, B=emitted records | LOW-MEDIUM | QualysFindingsFlow.cs:55 | QualysCollector.cs:749 |
| 6 | Identity-less host log level: A=Warning, B=Info | LOW | QualysFindingsFlow.cs:256,274 | QualysCollector.cs:380,397 |
| — | Spine-shortfall log: B logs it, A does not | LOW (observability) | (absent) | QualysCollector.cs:728-732 |

## Certainty

High on dims 1, 2, 3, 6, 7 and on the dim-4 / dim-5 divergences (direct code + test reading; both built; A's tests ran green). Medium-high on the *production reachability* of the duplicate-ID case for B — it depends on Qualys actually returning a repeated ID across overlapping ranges, which neither collector dedupes; the divergence is structural regardless of frequency. B's runtime behavior is inferred from code, not executed (tests not run per instructions).
