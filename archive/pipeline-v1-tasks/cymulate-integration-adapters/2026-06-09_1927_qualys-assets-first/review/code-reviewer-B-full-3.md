# Code Review B — Qualys agent-side collector: assets-first (`{HOST_DETAILS, DETECTION_LIST}`) + cursor-follow

**Scope:** `git diff` on branch `qualys-no-fixed`, files
`Source/CybiCollectors/QualysCollector/QualysCollector.cs` and
`Tests/CybiCollectors/QualysCollector.Tests/QualysCollector.cs`. Reviewed at final state, in
isolation, no external spec assumed. Stress suite NOT run (known harness hang); no build.

> Note on prior reviews: the `*-full-3.md` prior reviews appear to target the **platform/SDK-side**
> Qualys collector (PascalCase `GetHostIdsAsync`, `ParseHostListPageAsync`, `QualysFindingsFlow.cs`).
> This review is the **agent-side** collector (camelCase `getHostIdsAsync`, `processHostBatchAsync`,
> no flow file). Findings are assessed independently; where a concept overlaps a prior review I mark
> it `[overlaps prior]`, otherwise `[NEW]`.

---

## Overall verdict

**Ship-able with one MEDIUM worth addressing.** The core transformation is correct: every enumerated
host with spine details is emitted as `{HOST_DETAILS, DETECTION_LIST}`, findings-less hosts get
`DETECTION_LIST: []`, and a host whose detections straddle the truncation boundary is **merged** (not
overwritten, not truncated). Acquire/Release is balanced on every path. Disposal across the new
multi-page loop is correct. The two new tests cover the two headline behaviors well. Resilience
(1960/AIMD, server-requested delay) is intact and now applies uniformly to both the detection and
spine calls.

The one MEDIUM is an **asymmetry**: the detection call follows the truncation cursor, but the spine
call assumes a single complete response — so the exact failure this change set out to eliminate
(silent host loss at the truncation boundary) is still possible on the spine side. Everything else is
LOW/INFO.

---

## MEDIUM

### M1 [NEW] — Spine call does not follow the truncation cursor; spine-side truncation silently drops assets
`QualysCollector.cs:644-666`

The detection fetch was hardened to follow `<WARNING>/<URL>` across pages so no host is dropped at the
truncation boundary. The spine fetch was **not**:

```
$"{iApiInfo.URL}/{cApiPrefix}/asset/host/?action=list&details=All&show_asset_id=1&truncation_limit=0&ids={idsParam}"
```

It relies on `truncation_limit=0` to disable pagination and is parsed once via
`parseHostDetailsBatchAsync` with no cursor follow. If Qualys ever truncates this response anyway
(server-side hard caps can override `truncation_limit=0`; a very large `details=All` batch is the
realistic trigger), the omitted hosts are absent from `hostDetailsById`, and `buildHostRecords`
(`:142-145`) **silently `continue`s past them** — no log, no failed-batch entry. Net effect: assets
silently lost, which is precisely the misrepresentation this change was meant to prevent on the
detection side.

This is asymmetric risk: detection truncation is handled, spine truncation is not. Severity is MEDIUM
not HIGH because (a) batches are pre-sized by `calculateOptimalBatchSize` and (b) `truncation_limit=0`
usually holds. Cheapest mitigation: detect a `<WARNING>/<URL>` in the spine response and either follow
it (reuse the cursor pattern) or fail the batch loudly rather than dropping silently. At minimum, log
when `iHostIds.Count != iHostDetailsById.Count` in `buildHostRecords` so a spine shortfall is visible.

---

## LOW

### L1 [overlaps prior, agent-side confirmation] — No page-count guard / non-advancing-cursor detection on the detection loop
`QualysCollector.cs:600-631`

`while (nextDetectionPageUrl is not null)` terminates **only** when the server stops returning a
`<URL>`. A misbehaving/buggy Qualys that returns the same cursor URL, or never clears it, loops
indefinitely while the batch **holds its AIMD concurrency slot** (acquired at `:585`, released only in
the `finally` at `:691` once the whole loop exits) and `detectionsByHostId` grows unbounded.
Cancellation (`iCancellationToken`) does break the inner parse loop, so it is not a hard hang, but
there is no defensive max-page ceiling and no "cursor did not change since last page" check. The
pre-existing host-enumeration uses `truncation_limit=0` and does not cursor-follow, so this loop is the
first unbounded server-driven follow in this file. Low because real Qualys advances `id_min`
monotonically. Suggest a max-page cap (e.g. a few thousand) and/or rejecting a repeated cursor.

### L2 [NEW] — `_collectedAssets` is set from enumeration, not from emitted records — can over-count
`QualysCollector.cs:102` vs `buildHostRecords :140-163`

`_collectedAssets = hostIds.Count` is fixed up-front from the ID enumeration. But `buildHostRecords`
emits only hosts present in `hostDetailsById`; any host enumerated-but-spine-missing (deleted between
calls, or dropped by M1) is **not** emitted yet **is** still counted as a collected asset.
`AssetsWithoutVulnerabilities = Max(0, _collectedAssets - _collectedFindings)` (`:132`) then reports a
phantom. The stats become internally inconsistent with what was actually published. Tie this to M1;
counting emitted records (or decrementing on the `continue`) would make the stat truthful.

### L3 [NEW] — Identityless hosts are emitted (intentional), but the policy is worth a second look
`QualysCollector.cs:147-152, 174-176`

`buildHostRecords` deliberately emits a host with neither `NETBIOS` nor `IP`, logging at Info. The
docstring frames this as "surface rather than silently emit a broken asset." That is a reasonable
call, but it ships a record that downstream asset-identity resolution may not be able to key. This is a
product decision, not a bug — flagging so it is a conscious choice and not an accident. The per-host
Info log plus the per-batch summary log (`:166-168`) are fine; on a large all-identityless batch (e.g.
the stress fixtures, see T1) it is noisy but bounded.

### L4 [NEW] — Duplicate IDs in `iHostIds` produce duplicate emitted records
`buildHostRecords :340-162`

If `iHostIds` ever contains a repeated ID, the host is emitted once per occurrence (each iteration
re-finds it in `hostDetailsById` and `DeepClone`s its detections). Qualys host enumeration does not
repeat IDs in practice and `getHostIdsAsync` does not dedup, so this is theoretical. `DeepClone`
(`:155`) at least prevents JToken aliasing/double-parent corruption, so the duplicates are independent
objects. Low. A `HashSet<int> seen` guard would close it cheaply if desired.

### L5 [NEW] — Dead production code: `parseVulnerabilityDetectionsBatchAsync` shim is test-only
`QualysCollector.cs:1211-1217`

The shim now delegates to `parseDetectionPageAsync` and discards the cursor. No production caller
remains (production uses `parseDetectionsByHostIdAsync` / `parseDetectionPageAsync` directly). Its sole
consumer is the reflection-based test `ParseVulnerabilityDetectionsBatchAsync_MalformedXml_ThrowsException`
(test `:906`). Keeping a private method alive purely for a reflection test is mild cruft; the
malformed-XML behavior could instead be asserted against the real `parseDetectionPageAsync`. The shim
itself is correct. Low.

---

## INFO / confirmations

### Concurrency — slot held across the whole multi-page fetch (correct, but a behavior shift)
`QualysCollector.cs:585-692`. One `AcquireAsync` at `:585`, one `Release` in `finally` at `:691`.
Balanced on every path: success `return` (`:685`, inside the try → finally still runs), retryable
break (`:617`), spine-fail fall-through (`:657-659`), and exception (propagates through finally). No
acquire-without-release, no double-release (the limiter's `Release` also guards `rInFlight > 0`). The
slot is **held for the entire multi-page detection fetch + spine + enrichment**, not re-acquired per
page — this is intentional (comment `:583`) and matches the pre-change behavior of holding across
enrichment, but note the effective cost of one slot is now N sequential HTTP round-trips, so the AIMD
ceiling now bounds fewer in-flight *batches* during deep truncation. The limiter docstring still says
"only for the duration of a single call" (`AdaptiveConcurrencyLimiter.cs:11`) — stale, was already
stale pre-change. `ReduceOnThrottle`/`IncreaseOnSuccess` placement is correct: reduce happens after
slot release in the backoff path (`:712`), increase only on the clean-batch path (`:684`).

### Resilience — 1960/AIMD and server-requested delay intact and now uniform
`tryClassifyRetryableResponse` (`:742-789`) is a faithful extraction of the old inline switch and is
now applied to **both** the detection page failures (`:615`) and the spine failure (`:657`), so a 409
`1960`/concurrency or `X-RateLimit-ToWait-Sec` on the spine throttles the AIMD limiter and honors the
server wait identically to the detection call. `serverRequestedDelay` flows to the backoff at `:725`.
Auth 401/403 still throw terminally. Good.

### Resource management — per-page disposal is correct
Each loop iteration's `detectionResponse` is `using` (`:602`) and its stream `await using` (`:620`),
disposed per page — this is the fix the test factory-lambda change mirrors (see T2). The spine response
(`:644`) and its stream (`:664`) are likewise scoped. No single-use response is read twice.
Pre-existing note: `parseDetectionPageAsync` still does `ReadToEndAsync` into a string and then re-wraps
into a `MemoryStream` (`:1233-1243`) — a double-buffer of the whole page in memory; unchanged by this
diff but now happens once per truncation page. Not introduced here; flagging continuity only.

### Cursor extraction `insideHostList` guard — correct
`parseDetectionPageAsync :1259-1278`. The `<URL>` is only captured when `insideHostList is false`,
distinguishing the `<WARNING>/<URL>` next-page cursor (outside `</HOST_LIST>`) from any `<URL>` that
might appear inside a host/detection. `nextPageUrl` is a fresh local per call, so no stale cursor
carries between pages. Blank/whitespace `<URL>` → `null` → loop terminates. Correct.

> Minor pre-existing nit (not changed): the `SIMPLE_RETURN` check at `:1254` does not gate on
> `NodeType`, so it would also fire on the `EndElement`. Harmless because the element-content read on
> the start tag throws first. The new `parseHostDetailsBatchAsync` (`:37`) *does* gate NodeType
> correctly — inconsistent, but the new code is the better of the two.

### Per-host merge across pages — correct
`mergeDetectionsByHostId :460-476` reparents tokens (`detection.Remove()` then `existing.Add`) to avoid
"token already has a parent" — appends, never overwrites. `parseDetectionsByHostIdAsync` (`:431-453`)
only merges hosts with a non-empty `DETECTION_LIST`, so a page that lists a host with zero detections
does not create a spurious empty entry. Left-join in `buildHostRecords` `DeepClone`s the merged array
(`:155`) so the cached/accumulated array is not mutated by enrichment downstream. Correct.

### Left-join ID cases
- spine-present + detections-present → emitted with detections. ✓
- spine-present + no detections → emitted with `[]`. ✓ (the headline behavior)
- detections-present + spine-**absent** → **dropped** (`:142-145`), and over-counted by L2. This is
  the gap tied to M1/L2.
- spine-absent + detections-absent → never considered. ✓ (correct; nothing to emit)

---

## Tests

### T1 [NEW] — Stress fixtures now feed an identity-less spine; assertions still pass but for a degraded reason
The pre-existing stress tests (`:148-154` etc.) mock `Contains("/asset/host/")` → `CreateHostListResponse`,
which emits `<HOST><ID>i</ID></HOST>` with **no IP/NETBIOS**. After this change that same response is
what the **spine** call receives, so every host in those tests is now identity-less and logged via
L3's Info path. The assertions (`CollectedAssets == hostCount`, `CollectedFindings == hostCount*findingsPerHost`)
still hold because `_collectedAssets` comes from enumeration (L2) and findings come from the detection
endpoint — so the suite is green but no longer exercises a realistic spine. Not a production defect;
flagging that the stress suite's coverage of the spine path quietly degraded. The two **new** tests
(below) do supply real IP/NETBIOS and are the meaningful coverage.

### T2 [NEW] — Factory-lambda mock fix is correct and necessary
`.Returns(Task.FromResult(CreateHostListResponse(...)))` → `.Returns((args) => Task.FromResult(...))`
(`:154`, `:200`, etc., and `ReturnsAsync` → lambda at `:566`). The old form created **one**
`HttpResponseMessage` shared across every matching call; once the first call disposes it (the
production `using` now disposes per page) and consumes its stream, a second call to the same setup
would hand back a disposed response / drained stream. With the multi-page detection loop + a separate
spine call now hitting the mock multiple times per batch, a fresh response per invocation is required.
Correct fix, correctly applied to all setups. `_mockDecryptor` field/init added (`:17`,`:32`) to match
the ctor — fine.

### T3 [NEW] — The two new tests are good and target the right invariants
`CollectFindingsAsync_HostWithoutDetections_EmittedWithEmptyDetectionList` (`:633+`) asserts the
findings-less host (ID 2) is emitted with `DETECTION_LIST: []` while ID 1 keeps its detections, and
checks record shape on both. `CollectFindingsAsync_DetectionsAcrossTruncationPages_HostFullyRepresented`
(`:728+`) drives a real `<WARNING>/<URL>` cursor with CDATA, disjoint QID ranges across pages, asserts
the merged count (5) and that QIDs from **both** pages survive — this directly proves the merge-not-
overwrite and follow-the-cursor behaviors. `CreateDetectionPage` places the `<WARNING>/<URL>` outside
`</HOST_LIST>`, matching real Qualys and exercising the `insideHostList` guard. Good fidelity.

> Test gap (LOW): no test asserts the spine-missing-but-detections-present drop (M1/L2), and no test
> asserts loop termination on a repeated/non-advancing cursor (L1). Worth adding one negative test for
> the spine-shortfall path if M1 is addressed.

---

## Severity summary

| Sev | Item | File:line | New? |
|-----|------|-----------|------|
| MEDIUM | Spine call ignores truncation cursor → silent asset loss (asymmetric vs detection) | `:644-666`, `:142-145` | NEW |
| LOW | No page-cap / non-advancing-cursor guard on detection loop (holds slot) | `:600-631` | overlaps prior |
| LOW | `_collectedAssets` counts enumeration, not emission → phantom assets | `:102`, `buildHostRecords` | NEW |
| LOW | Identity-less hosts emitted (intentional; confirm it's the desired policy) | `:147-152,174-176` | NEW |
| LOW | Duplicate IDs → duplicate records (theoretical) | `:340-162` | NEW |
| LOW | Dead-but-for-reflection-test shim `parseVulnerabilityDetectionsBatchAsync` | `:1211-1217` | NEW |
| INFO | Slot held across whole multi-page fetch (intentional); limiter docstring stale | `:585-692` | — |
| INFO | Stress fixtures now feed identity-less spine; coverage degraded but green | tests `:148-154` | NEW |

No HIGH/critical findings. M1+L2 are one coupled issue (spine completeness + truthful counting) and
are the only thing I would gate a merge on for a production collector that explicitly set out to stop
silently dropping hosts.
