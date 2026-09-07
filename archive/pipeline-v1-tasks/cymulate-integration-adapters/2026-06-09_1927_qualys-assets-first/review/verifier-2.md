# Verifier-2: Detection-fetch parity (Collector A vs Collector B)

Scope: confirm B's detection fetch now behaves identically to A's after both adopted the Qualys
`<WARNING>/<URL>` truncation cursor.

- A: `cymulate-integration-adapters` (this repo), branch `qualys-to-assets-perspective`
  - `Collectors/QualysCollector/Flows/Findings/QualysFindingsApiClient.cs` (`GetDetectionsByHostIdAsync`)
  - `.../QualysFindingsXmlParser.cs` (`ParseDetectionPageAsync`, `MergeDetectionsByHostId`)
  - `.../QualysFindingsFlow.cs` (`BuildHostRecords`)
- B: `/Users/user/Dev/AgentService`
  - `Source/CybiCollectors/QualysCollector/QualysCollector.cs`
    (`processHostBatchAsync` detection while-loop, `parseDetectionsByHostIdAsync`,
    `parseDetectionPageAsync`, `mergeDetectionsByHostId`, `tryClassifyRetryableResponse`)

## Verdict: PASS

Detection-fetch behavior is equivalent point-for-point. One intentional, harmless divergence
(spine truncation strategy — out of scope for the detection fetch) is noted below; it does not
affect the detection cursor parity.

---

## Point-by-point

### 1. Cursor location (under `<WARNING>`, outside `</HOST_LIST>`) — PASS
Both parsers track `insideHostList` (set true on `<HOST_LIST>` element, false on its EndElement)
and only accept a `<URL>` as the next-page cursor when `insideHostList == false`.

- A: `QualysFindingsXmlParser.ParseDetectionPageAsync` — `if (!insideHostList && ... Name == "URL")`.
- B: `QualysCollector.parseDetectionPageAsync` line 1273 — `if (insideHostList is false && ... Name == "URL")`,
  with the explicit comment "Guarding on !insideHostList keeps it distinct from any URL within a host."

B's `insideHostList` guard is present and confirmed. Additionally, in BOTH implementations the
`<HOST>` subtree is fully consumed by the inner host loop (A: `ParseHostAsync`; B: inner
`while ... EndElement HOST` at lines 1289-1310), so an intra-host `<URL>` never even surfaces to the
outer loop. The guard is correct belt-and-suspenders, not the sole defense — same in both.

### 2. Loop (GET -> parse -> merge by host ID -> follow NextPageUrl until null) — PASS
- A: `GetDetectionsByHostIdAsync` — `while (!string.IsNullOrWhiteSpace(nextPageUrl))` → GET stream →
  `ParseDetectionPageAsync` → `foreach` merge via `MergeDetectionsByHostId` → `nextPageUrl = page.NextPageUrl`.
- B: `processHostBatchAsync` — `while (nextDetectionPageUrl is not null)` → `makeApiCallAsync` GET →
  `parseDetectionsByHostIdAsync` → `foreach` merge via `mergeDetectionsByHostId` →
  `nextDetectionPageUrl = cursor`.

Same loop shape, same termination (null/blank cursor). B normalizes blank URL to `null` in the parser
(`string.IsNullOrWhiteSpace(urlValue) ? null`), so its `is not null` guard is equivalent to A's
`!IsNullOrWhiteSpace`.

### 3. Merge semantics (APPEND, not overwrite) — PASS
Both `MergeDetectionsByHostId` / `mergeDetectionsByHostId` are byte-for-byte equivalent in behavior:
- First sighting of a host ID: store the array as-is.
- Subsequent pages: iterate `detectionList.ToArray()`, detach each node from its source array
  (A: `detectionList.Remove(detection)`; B: `detection.Remove()`), and `existing.Add(...)`.

Detach-then-add is required because both JSON libraries (System.Text.Json `JsonNode` and Newtonsoft
`JToken`) forbid a node having two parents. Both handle it identically. Result: a host spanning N
pages accumulates ALL detections, no overwrite.

### 4. Throttle integration (concurrency slot + 1960/AIMD classification) — PASS
- A: the cursor-follow loop runs inside `GetDetectionsByHostIdAsync`, invoked from the flow within
  the same execution path the resilience strategy wraps; non-success is routed through the shared
  `AdapterResilienceStrategy` (the repo's standard `MappedFailurePolicy`/1960 path) unchanged. The
  GET helper is the same `_http.GetStreamAsync`.
- B: the entire `while` cursor loop is INSIDE the existing concurrency-limiter slot in
  `processHostBatchAsync` (the `finally` that releases the slot still wraps the whole loop). On a
  non-success detection page, B calls the newly extracted `tryClassifyRetryableResponse`, sets
  `retryableFailure = true`, and `break`s out to the shared throttle/backoff path — the WHOLE batch
  retries from page 1. `tryClassifyRetryableResponse` preserves the prior switch verbatim: 409 →
  `QualysBusyResponse.Classify` (1960/concurrency/rate-limit → `throttled`/`serverRequestedDelay`),
  429 → throttled, 503/500 → retry, 401/403 → throw. AIMD reduce-on-throttle / increase-on-success
  is unchanged.

The cursor-follow did not break throttle integration in either. B's refactor is a pure extraction of
the pre-existing classification switch; no branch was dropped (verified against the removed inline
switch in the diff — all seven cases preserved).

### 5. truncation_limit on detections — PASS (neither sets it on the detection call)
- A detection URL: `...vm/detection/?action=list&show_asset_id=1&show_results=1&ids=...&status=...`
  — no `truncation_limit`. Cursor is the mechanism.
- B detection URL (`firstDetectionPageUrl`, line 568): same — no `truncation_limit`. Cursor is the
  mechanism.

Both correctly rely on Qualys's server hard-cap + `<WARNING>/<URL>` cursor for the detection
endpoint rather than pinning a limit. Parity holds.

Out-of-scope note (does NOT affect detection parity): the two collectors differ on the HOST-DETAILS
SPINE call, not the detection call.
- A spine: `truncation_limit={_configuration.HostPaginationLimit}` and the spine ALSO follows the
  cursor (`GetHostDetailsAsync` has its own `while` loop).
- B spine: `truncation_limit=0` (line 645) — i.e. B disables truncation on the spine and fetches it
  in a single response, no cursor loop on the spine.
This is a divergence on the spine fetch only. It is safe because the spine is bounded to one batch's
explicit `ids=` list (batch size is collector-controlled), so a single un-truncated page is bounded.
It has zero bearing on the detection-fetch cursor parity this task asked to verify.

### 6. Net behavior (same per-host DETECTION_LIST; findings-less host emits []) — PASS
Both left-join detections onto the host-detail spine in `BuildHostRecords` / `buildHostRecords`:
iterate the requested `hostIds` in order, emit every host that has details, attach
`detectionsByHostId[id]` (DeepClone) or `new JsonArray()/new JArray()` when absent. A host present in
the spine but omitted by the detection endpoint → `DETECTION_LIST: []`. A multi-page host → fully
merged list. Identical logic, identical record shape `{ HOST_DETAILS, DETECTION_LIST }`, identical
identity-warning behavior (`HasAssetIdentity`/`hasAssetIdentity` on NETBIOS||IP).

---

## Tests

### A — ran (net8.0), GREEN
`dotnet test ...QualysCollector.Test --filter FollowsDetectionTruncationCursor|PublishesFindingslessHost`
→ **Passed: 2, Failed: 0**.

`ProcessAsync_Findings_FollowsDetectionTruncationCursor_AndCapturesAllPages` genuinely exercises a
2-page cursor: page 1 returns host 101's QID 9001 plus a `<WARNING><URL>...id_min=2</URL></WARNING>`
OUTSIDE `</HOST_LIST>`; the `id_min=` page returns QID 9002. It asserts the `id_min=2` URL was
requested AND that host 101's `DETECTION_LIST` contains BOTH 9001 and 9002 (length 2), while
findings-less host 202 has length 0. This is a real cursor + merge assertion, not a shape check.

### B — read only (did NOT run the stress suite, per instruction)
`CollectFindingsAsync_DetectionsAcrossTruncationPages_HostFullyRepresented` exercises a 2-page cursor:
page 1 = 3 detections (QIDs 1001-1003) + a `<WARNING><URL><![CDATA[...id_min=999999]]></URL></WARNING>`
cursor placed OUTSIDE `</HOST_LIST>` (the `CreateDetectionPage` helper emits the WARNING block after
`</HOST_LIST>`, matching Qualys). The `id_min=999999` request returns page 2 = 2 detections (QIDs
2001-2002) with no further cursor. It asserts host 1 carries all 5 merged detections (contains 1001
from page 1 AND 2001 from page 2, count == 5) and host 2 (genuinely findings-less) emits an empty
`DETECTION_LIST`. Disjoint QIDs across pages mean the assertion would fail on overwrite — so it
truly proves MERGE, not replace. The companion `..._HostWithoutDetections_EmittedWithEmptyDetectionList`
covers the findings-less `[]` case.

Build sanity for B: the test references only helpers present in the test file
(`CreateDetectionPage`, `CreateHostDetailsResponse`, `CreateVulnerabilityResponse`,
`CreateVulnerabilityInfoResponse`) and the diff adds `_mockDecryptor` to the fixture setup; the
Moq `.Returns((HttpRequestMessage,...) => ...)` lambda overloads replace the prior
`Task.FromResult(...)` so the same mock can branch on URL (required for the cursor's second request).
This compiles against the existing `SendRequestAsync(HttpRequestMessage,bool,bool,CancellationToken)`
signature. I did not execute B (stress suite excluded by instruction); B's test is asserted GREEN by
construction only — see certainty note.

---

## Residual divergence
1. Spine truncation strategy differs (A paginates the spine with `HostPaginationLimit` + cursor;
   B uses `truncation_limit=0`, single page). Out of scope for detection-fetch parity; bounded and
   safe because the spine is keyed to one batch's explicit `ids=`. Flag only if you want the spine
   itself to be parity-identical.

## Certainty
HIGH on detection-fetch parity (points 1-6): both implementations read directly, structurally
mirror each other, and A's 2-page cursor test passed live. MEDIUM-HIGH on B's test being green —
verified by reading (logic + helpers + Moq overloads are consistent and the assertions are
sound), but not executed since B's stress suite was excluded.
