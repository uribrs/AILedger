# Code Review — Qualys collector "assets-first" left-join change (branch `qualys-no-fixed`, uncommitted)

Scope: `Source/CybiCollectors/QualysCollector/QualysCollector.cs` and `Tests/CybiCollectors/QualysCollector.Tests/QualysCollector.cs`. Reviewed the diff plus surrounding control flow, the batch retry loop, the AIMD limiter usage, both XML parsers, and the new test. No full build/suite run (per instructions; known stress-suite harness hang).

## Overall verdict

**Approve with one required fix and a few should-fix items.** The refactor is sound: the shared classifier is a clean dedup, the spine fetch is correctly placed inside the limiter slot, slot release is guaranteed by `finally`, and there is no double-read of a single response stream. The one genuinely material defect is **silent detection truncation** on the detection call, which directly undermines the feature's premise (a host with truncated detections is indistinguishable from a findings-less host). Everything else is severity Low/Info.

---

## Findings (severity-ranked, file:line)

### HIGH — Detection call has no `truncation_limit`; a host over Qualys's default truncation emits as falsely findings-less

`QualysCollector.cs:564` (detection URL) vs `:619` (spine URL).

The detection request is single-shot with **no `truncation_limit` and no next-page (`<WARNING>/<URL>`) follow**:

```
api/4.0/fo/asset/host/vm/detection/?action=list&show_asset_id=1&show_results=1&ids=...&status=New,Active,Re-Opened
```

The spine call (`:619`) and id-enumeration call (`:145`) both correctly pass `truncation_limit=0` (unlimited). The detection call does not. Qualys's host-detection API defaults to truncation at 1000 records when `truncation_limit` is omitted, and returns a `<WARNING><URL>` cursor for the next page. There is no pagination/`WARNING` handling anywhere in the file (confirmed by grep).

Pre-change, a truncated detection page just meant under-reported findings — bad but visible-ish. Post-change the impact is worse and more insidious: `buildHostRecords` (`:336`) left-joins by host ID, and any host whose detections were cut off mid-stream (or pushed onto an unfetched second page) now either (a) gets a **partial** `DETECTION_LIST`, or (b) if all its detections landed past the truncation boundary, is **absent from `detectionsByHostId`** and emitted with `DETECTION_LIST: []` — i.e. it looks exactly like a legitimately findings-less host. The whole point of this change is to faithfully emit findings-less hosts; truncation silently manufactures false ones and corrupts findings counts. The detection endpoint paginates over *detection rows across the whole batch*, so with a 1000-row default a batch of many hosts can be truncated even when each host has only a handful of detections.

Severity HIGH because it's a correctness/data-integrity bug in the feature's core invariant, and it's silent (no `WARNING`/`SIMPLE_RETURN` — truncation is a normal `<WARNING>` envelope on a 200, not an error). `parseVulnerabilityDetectionsBatchAsync` only throws on `SIMPLE_RETURN`, not on a truncation `WARNING`.

Fix: add `&truncation_limit=0` to the detection URL (matches the spine and enumeration calls and the existing intent), OR implement the `<WARNING>/<URL>` next-page follow. Given the batch sizes are already bounded by `calculateOptimalBatchSize`, `truncation_limit=0` is the simplest correct fix and is consistent with the two sibling calls. If there is a deliberate reason the detection call omits it (e.g. avoiding a single huge response), that reasoning is not in the code and the resulting silent-truncation behavior is not acceptable as-is.

> Certainty: High that the param is absent and no pagination exists (verified in source). Medium-High on the exact Qualys default magnitude (1000) — the magnitude is documented behavior, not verified against a live tenant here; the *direction* (omitted ⇒ truncates ⇒ silent false findings-less) is not in doubt.

---

### LOW — `_collectedAssets` is not derived from emitted records; the new test's asset assertion doesn't prove the left-join count

`QualysCollector.cs:102` sets `_collectedAssets = hostIds.Count` from the *enumeration*, independent of what `buildHostRecords` actually emits. The new test asserts `Assert.Equal(hostCount, collector.CollectedAssets)` (`Tests:...685`), but that assertion would pass even if `buildHostRecords` emitted zero records, because the counter is set upstream. The test *does* separately assert `emitted.Count == hostCount` against captured batches, which is the real proof — so the test is still valid — but the `CollectedAssets` assertion is misleading (it's testing enumeration, not emission). Not a code bug; a slightly redundant/misleading assertion. Leave or drop; no action required.

### LOW — `buildHostRecords` silently drops batch IDs present in detections but absent from the spine

`QualysCollector.cs:344-349`: the loop is keyed on `iHostIds` and `continue`s when an ID is missing from `iHostDetailsById`. That's the intended "spine is the source of truth" design and is reasonable. But the inverse asymmetry is worth a one-line log: if a host returned detections yet the spine omitted it (host deleted/purged between the two calls, or spine truncation — though spine has `truncation_limit=0`), its findings are dropped with no trace. Detections-without-spine should be rare, but currently it's completely silent. Consider a debug/info count of `detectionsByHostId` keys not present in `hostDetailsById`. Optional.

### LOW — Spine and detection responses are buffered fully; "memory bounded" comment is half-true

`QualysCollector.cs:614-615` comment claims per-batch fetch keeps memory bounded. True relative to fetching all hosts at once. But note `parseVulnerabilityDetectionsBatchAsync` (`:1170`) does `ReadToEndAsync()` into a string **and** re-encodes into a `MemoryStream` (`:1179`) — so the detection payload is held twice in memory per batch. Pre-existing, not introduced here, but the new "memory bounded" comment sits right next to it and overstates the property. The spine parser (`parseHostDetailsBatchAsync`, `:219`) correctly streams via `XmlReader` over the response stream — good, and a better pattern than the detection parser it sits beside. Info-level.

### LOW — `convertElementValue` attribute/child name collision can produce mixed-type values

`QualysCollector.cs:286-333`: if an element has both a child named `VALUE` and inner text on an attributed element, the inner text is written to `result["VALUE"]` (`:298`) and a child `<VALUE>` would then collide at `:308-318`, flipping it into a `JArray`. Also an attribute whose local name equals a child element's local name collides the same way. Extremely unlikely in Qualys `HOST_DETAILS` shape, but the function is a general recursive converter with no guard. Acceptable for the known schema; flagging for awareness only.

### INFO — Concurrency / limiter: correct

- Spine fetch is inside the `AcquireAsync`/`Release` slot (`:581`/`:665`), so a busy spine response throttles AIMD identically to the detection call — matches the stated intent and is correct.
- The spine-failure branch (`:628-633`) sets the out-params via the shared classifier and falls through the inner `else` without returning; control exits the inner `try`, the `finally` releases the slot (`:665`), and execution reaches the post-`try` throttle/backoff block (`:682+`) with `throttled`/`serverRequestedDelay`/`retryReason` correctly populated. `ReduceOnThrottle` (`:686`) only fires on `throttled`, `IncreaseOnSuccess` (`:658`) only on the full-success path. No slot is acquired-without-release on any path I traced (success, spine-fail, detection-fail, exception, cancellation). Good.
- No double-read of a single `HttpResponseMessage`: detection and spine are distinct messages, each read once (string on failure, stream on success), both `using`-disposed. The diff explicitly fixed the *test* single-use-stream problem with per-call factory lambdas; production was never affected.

### INFO — `tryClassifyRetryableResponse` extraction is a clean, faithful dedup

`QualysCollector.cs:716-763` is byte-for-byte the prior inline switch, now shared by both calls. 1960/concurrency/`X-RateLimit-ToWait-Sec` handling is unchanged (routed through `QualysBusyResponse.Classify`). The `_ =` discard of the return value at both call sites is fine since `retryReason != null` already drives the loop; the bool is informational. No regression to existing 1960/AIMD behavior. The method name promises "try...return bool" but callers ignore the bool and it *throws* for 401/403 rather than returning — mildly inconsistent naming vs. behavior, but it preserves the exact prior semantics, so leave it.

### INFO — Tests

- The new test (`Tests:636-690`) genuinely proves the findings-less path: host 2 is omitted from the detection response and asserted to emit with `DETECTION_LIST: []`, host 1 keeps its 3 detections, both have the `{HOST_DETAILS, DETECTION_LIST}` shape, and `capturedBatches` are deep-cloned at capture time (correctly avoiding mutation-after-capture). The URL disambiguation is correct: detection matches `/asset/host/vm/detection/`; spine matches `/asset/host/` AND NOT `/vm/detection/` (the v4 vs v2 prefixes make these disjoint). Good test.
- The factory-lambda conversion (`.Returns((HttpRequestMessage _, ...) => Task.FromResult(...))` replacing `Task.FromResult(...)`/`ReturnsAsync(...)`) is the right fix: `HttpResponseMessage`/`StringContent` are single-read, and the spine call now issues a *second* `/asset/host/` request per batch, so a cached single response instance would have a consumed stream on the second read. Per-call factories rebuild fresh content each invocation. Necessary and correct.
- `_mockDecryptor` initialization (`Tests:32`) fixes a pre-existing uninitialized field; sound.
- Caveat (not a defect): in the *existing stress tests* the broad `Contains("/asset/host/")` matcher now also serves the spine call, returning `CreateHostListResponse` (ID-only, no IP/NETBIOS). That parses fine, but every spine host will be `hasAssetIdentity == false`, so the new identityless Info log (`:344-347`) fires for every host under stress — log noise only, no assertion impact. If those tests are ever made strict on log output or record identity, they'd need the richer fixture. Worth a mental note.

---

## Summary of required vs optional

- **Required (HIGH):** add `truncation_limit=0` to the detection URL (`:564`) or implement `<WARNING>` next-page follow — otherwise the feature can emit false findings-less hosts and undercount findings, silently.
- **Optional (LOW):** log detections-without-spine drops; trim/clarify the misleading `CollectedAssets` test assertion and the "memory bounded" comment.
- No concurrency, slot-leak, disposal, or 1960/AIMD-regression defects found.
