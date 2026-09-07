# Cross-artifact consistency review — Collector B (AgentService) vs Collector A + prototype

Anchor: Collector B (`/Users/user/Dev/AgentService` → `Source/CybiCollectors/QualysCollector/QualysCollector.cs`, Newtonsoft).
Comparators:
- Collector A (`/Users/user/Dev/cymulate-integration-adapters` → `src/.../Collectors/QualysCollector/Flows/Findings/*`, System.Text.Json).
- Prototype (`/Users/user/Dev/Uri/localprojects/IntegrationProbes/Integrations/Qualys/QualysAssetsXmlParser.cs`) + fixtures under `ProbeResults/QualysAssets_20260609_145715_071Z/` and `QualysExposurelessAsset_20260609_150258_039Z/`.

Fixture note: `asset_with_exposures.json` / `exposureless_asset.json` are the **SPARK-normalized** asset shape (a downstream mapping), NOT the collector emit. The collector-emit reference is `combined.ndjson` (`{HOST_DETAILS, DETECTION_LIST}` per line). The review uses `combined.ndjson` as the emit oracle and treats the normalized files only as the consumer-side shape.

---

## 1. Per-host `{HOST_DETAILS, DETECTION_LIST}` structure — MATCH (severity: none)

B emits exactly `{HOST_DETAILS, DETECTION_LIST}` per host:
- `QualysCollector.cs` `buildHostRecords` → `new JObject { ["HOST_DETAILS"] = hostDetails, ["DETECTION_LIST"] = detectionList }`.
- Identical to A's `QualysFindingsFlow.BuildHostRecords` and to the prototype's `combined.ndjson` top-level keys (`["DETECTION_LIST","HOST_DETAILS"]`; key order is irrelevant for object consumers).

Findings-less host → empty `DETECTION_LIST`:
- B: `iDetectionsByHostId.TryGetValue(id, ...) ? clone : new JArray()` — left-join over the requested IDs, empty array on miss.
- A: same logic, `new JsonArray()` on miss.
- Prototype `exposureless_asset.json` shows `"exposures": []` post-normalization; the pre-normalization equivalent is `DETECTION_LIST: []`, which B produces.
- B's own test `CollectFindingsAsync_HostWithoutDetections_EmittedWithEmptyDetectionList` asserts exactly this (host "2" emitted with empty `DETECTION_LIST`, host "1" with 3 detections, both records carry both keys).

A behavioral parity point worth noting (not a divergence, but a shared design change vs the old code): both A and B now drive the spine from the **host-list `/asset/host/?details=All` endpoint**, left-joining detections. The old B code emitted only hosts the detection endpoint returned. This is the intended "assets-first" change and both collectors implement it the same way (iterate requested `hostIds`, emit every host that has details, attach detections or empty array). Output ordering is stable in both (iterate `hostIds`, not dictionary order).

Conclusion: structurally identical to prototype and to A.

---

## 2. XML→JSON conversion (the highest-risk area) — MATCH on observed shapes, with documented edge differences

Three converters compared:

| Aspect | Prototype `ConvertElementValue` | A `ConvertElementValue` | B `convertElementValue` |
|---|---|---|---|
| Leaf (no children, no attrs) | `element.Value.Trim()` → string | `GetCleanElementValue` (`Value?.Trim() ?? ""`) → string | `GetCleanElementValue` (same) → string |
| Nested element | recurse → object | recurse → object | recurse → object |
| Repeated child name | 1st kept; 2nd promotes to `JArray` | same (`AppendValue`) | same (inline array-promote) |
| **Attributes** | **IGNORED entirely** (only `HasElements` checked) | `{VALUE?, attr...}` (VALUE only if text non-blank) | `{VALUE?, attr...}` (identical) |
| Empty leaf | `""` | `""` | `""` |
| Number/bool typing | none — all strings | none — all strings | none — all strings |

A vs B: **byte-for-byte equivalent semantics.** Both special-case attributes into a `VALUE` + per-attribute-key object, both suppress `VALUE` when the element text is blank, both promote repeated children to arrays, both emit all scalars as JSON strings (e.g. `SEVERITY:"2"`, `IS_DISABLED:"0"` in the fixture — confirmed all-string in `combined.ndjson`). `JObject` vs `JsonObject` is a library detail with no wire difference.

Prototype vs A/B — one real divergence, **dormant on current data:**
- The prototype **drops XML attributes** (it never checks `HasAttributes`; an element with attributes but no children is treated as a leaf and only its text is captured). A and B both **preserve** attributes as `{VALUE, <attr>...}`.
- Impact today: **none.** I scanned all 7 `combined.ndjson` records — there are zero attribute-bearing or `VALUE`-keyed objects anywhere (`HOST_DETAILS` and every `DETECTION` are flat string maps; the only nested object is `DNS_DATA` = `{HOSTNAME, DOMAIN, FQDN}`, all leaf strings). So on this tenant the three converters produce identical JSON.
- Where it would diverge: Qualys VM fields that legitimately carry attributes — e.g. `QDS severity="..."`, `CVSS`/`CVSS_V3` element-with-attribute forms, `DNS`/`NETBIOS` with a `name=` attribute on some API versions, `TAG` lists. On such input A and B emit `{ "VALUE": "...", "severity": "..." }` while the prototype emits just `"..."`. **A and B match each other; the prototype is the odd one out.** Since the prototype is a probe (explicitly "mirrors field-capture behaviour" but is not the production contract) and A+B agree, treat A/B as canonical. **Severity: low** — flag only so nobody "fixes" A/B to match the prototype and silently drops attributes.

`DNS_DATA` null case: one of the 7 fixture hosts has `DNS_DATA: null` (no element present). Both A and B simply omit the key when the element is absent (they only write keys for elements they encounter), which serializes/round-trips equivalently to the consumer. No divergence.

Empty-element handling, B-internal nuance: B's spine parser (`parseHostDetailsBatchAsync`) short-circuits `string.IsNullOrWhiteSpace(outerXml)` → `hostDetails[name] = string.Empty` **before** calling `convertElementValue`. A's `ReadElementValueAsync` does the same (`outerXml` blank → `JsonValue.Create("")`). Same result (`""`). Note this blank-`outerXml` branch is effectively unreachable for a real element (`ReadOuterXml` always returns the tags), so it's belt-and-suspenders in both; harmless.

Conclusion: no real-data divergence between A and B. The only structural difference in the trio is prototype-drops-attributes, which is dormant and where A/B are the more-correct pair.

---

## 3. Truncation fix divergence — **NOT behaviorally equivalent. Severity: MEDIUM–HIGH on the detection path.**

This is the one place A and B genuinely differ in design, and it is the riskiest finding.

**Host-detail spine (both endpoints `/asset/host/?details=All`):**
- B: `truncation_limit=0` (`QualysCollector.cs`, spine call in `processHostBatchAsync`), single response, no cursor follow.
- A: `truncation_limit={HostPaginationLimit}` (default **8000**) and **follows the `<WARNING>/<URL>` cursor** in a `while (nextPageUrl)` loop (`QualysFindingsApiClient.GetHostDetailsAsync` + `ParseHostDetailsPageAsync`).
- Both are bounded to the batch's IDs, and the batch ID count is capped (B `cMAX_BATCH_SIZE`; A's host-list paging). For the spine, one host = one `<HOST>`, so the row count ≤ batch size and `truncation_limit=0` is safe — B will not truncate. **Equivalent here.**

**Detection endpoint (`/asset/host/vm/detection/`) — the divergence:**
- B: `truncation_limit=0` (diff line adding `&truncation_limit=0&` to the detection URL), single response, no cursor follow. B's parser (`parseDetectionsByHostIdAsync` → `parseVulnerabilityDetectionsBatchAsync`) has **no `<WARNING>/<URL>` handling at all** — if a `<WARNING>` cursor came back, B would silently ignore it (exactly the bug B's comment says `truncation_limit=0` exists to avoid).
- A: keeps the endpoint's default paging and **follows the cursor**, merging per-host across pages via `MergeDetectionsByHostId` (so a host whose detections straddle the truncation boundary is reassembled).

Why this matters: the detection endpoint returns **one `<DETECTION>` row per (host, QID)**, not per host. A single batch of N hosts can therefore produce **far more than N detection rows** — easily thousands. `truncation_limit=0` asks Qualys to disable paging and return them all in one response.

The hazard with `truncation_limit=0`:
1. **Documented Qualys behavior:** `truncation_limit=0` means "no truncation." It is honored by the VM detection API, so in the normal case B is correct and equivalent to A's merged multi-page result.
2. **Server hard cap:** Qualys enforces a subscription-level output cap. For very large batches `truncation_limit=0` can still hit a server-side ceiling, in which case the API returns a `<WARNING>` cursor **even though the client asked for 0**. This is the realistic failure mode. When it happens:
   - **A recovers** (follows the cursor, merges remaining pages).
   - **B does NOT recover** — it ignores the `<WARNING>`, takes only the first response's detections, and every host whose detections fell past the cut is **left-joined to an empty `DETECTION_LIST` and emitted as falsely findings-less.** Silent data loss, no error, no failed batch.
3. B's own test `CollectFindingsAsync_DetectionsBeyondTruncationBoundary_HostFullyRepresented` only proves `truncation_limit=0` is *sent* and that the mock returns everything in one page when it is. It does **not** model the server-still-truncated-despite-0 case (the mock returns a full page whenever `truncation_limit=0` is present). So the test passing does **not** establish B is safe against the real hazard.

Net: behaviorally equivalent in the common case; **divergent and worse for B at the large-batch / server-hard-cap boundary**, which is precisely the case the truncation work was meant to harden. A's cursor-follow is strictly safer.

Mitigating factor (why MEDIUM, not HIGH): both collectors cap the batch ID count, so detection-row volume per request is bounded by `batchSize × avg_QIDs_per_host`. If `cMAX_BATCH_SIZE` is small enough that even worst-case row counts stay under Qualys's output cap, the hazard never fires and B is fine in practice. **Action: confirm `cMAX_BATCH_SIZE` (B) vs the Qualys per-request output ceiling.** If a max-size batch of high-finding hosts can exceed the cap, B should either (a) follow the cursor like A, or (b) shrink the batch. Without that confirmation, rate this MEDIUM–HIGH.

---

## 4. SPARK per-record shape (one JSON object per line / per host) — MATCH (severity: none)

- B's emit path: `enrichAndStoreDetectionsAsync` receives `List<JObject> hosts` (one object per host from `buildHostRecords`) and, with `cymulate_instanceId` set, routes them through `ICybiBatchUploader.SaveUploadAndDeleteBatchAsync(IEnumerable<JObject> ...)` — one `JObject` per host. B's test captures exactly this (`emitted` = flattened batch, one record per host, each with `HOST_DETAILS` + `DETECTION_LIST`).
- This matches `combined.ndjson` (one JSON object per line, one host per line) — the NDJSON-per-host shape SPARK expects.
- A produces the same per-host object set through its egress pipeline.

No divergence in record granularity or shape.

---

## Divergence summary (by severity)

| # | Area | Verdict | Severity |
|---|---|---|---|
| 3 | **Detection endpoint truncation: B uses `truncation_limit=0` + no cursor follow; A follows the `<WARNING>` cursor and merges.** Equivalent in the common case; B silently drops detections (→ falsely findings-less hosts) if the server still truncates at its hard cap. B's test doesn't cover that case. | **DIVERGENT, B weaker** | **MEDIUM–HIGH** — gate on `cMAX_BATCH_SIZE` vs Qualys output cap |
| 2 | Prototype `ConvertElementValue` drops XML attributes; A and B both preserve them as `{VALUE, attr...}`. Dormant on current fixtures (zero attribute-bearing elements). A and B are byte-equivalent. | A↔B match; prototype differs (and is less correct) | LOW (dormant; flag to prevent a regressive "fix") |
| 1 | `{HOST_DETAILS, DETECTION_LIST}` per host, empty `DETECTION_LIST` for findings-less hosts, stable ordering. | MATCH (A, B, prototype) | none |
| 4 | One JSON object per host on the emit/store path (SPARK NDJSON shape). | MATCH | none |

**Bottom line:** A and B emit structurally identical records and use semantically identical XML→JSON conversion on all real data. The single material divergence is the detection-endpoint truncation strategy (item 3): A's cursor-follow is strictly safer than B's `truncation_limit=0`-and-ignore-warnings; whether B's choice is actually safe depends entirely on B's batch-size cap staying under Qualys's per-request output ceiling — verify that before shipping.

**Certainty:** High on items 1, 2, 4 (read from emitted-shape tests, the diffs, and the fixture). Medium on item 3's severity — the *divergence* is certain (code + B's missing `<WARNING>` handling), but whether it ever fires depends on the unverified `cMAX_BATCH_SIZE`-vs-Qualys-output-cap relationship, which I did not confirm against Qualys's documented ceiling.
