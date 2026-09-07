# Code Review 1 — AgentService Falcon correlated findings collector

Scope: uncommitted working-tree changes in `/Users/user/Dev/AgentService` (branch `feature/falcon-correlated-findings`).
Files: `Source/CybiCollectors/FalconCollector/FalconFindingsCollector.cs` (rewrite), `FalconCollector.cs`, `FalconUrls.cs`, new `Tests/CybiCollectors/FalconCollector.Tests/FalconCorrelatedFindingsCollectorTests.cs`.

Calibration: production collector feature logic; **High risk** (pagination state, retry interplay, large data, output-contract change). Reviewed at high depth, including the transport stack it now leans on (`ApiClientBase.makeApiCallAsync`, `RestClient.SendRequestAsync`, `FalconApi.tryHandleApiCallError`).

Verified: `FalconCollector.csproj` builds clean; `FalconCollector.Tests` passes 7/7 (all 7 are the new tests; the legacy facts in `FalconCollectorTests.cs` were already commented out). I also confirmed the 404-body cursor-expiry detection is viable end-to-end: `RestClient` sends with `HttpCompletionOption.ResponseHeadersRead`, but `HttpContent.ReadAsStringAsync` buffers, so the marker read in `FetchJsonAsync` still works after the retry loop has logged the body.

The watermark/boundary-aid algebra itself checks out: batch max is monotonic under the asc sort, truncation is downward (over-inclusive `>=` refetch), and boundary aids cover every emitted host in the watermark second across batch boundaries. No defect found in that core mechanism.

---

## Major

### 1. Emission path allocates a throwaway multi-MB string per record and buffers ~48 MB of serialized-equivalent JObject DOM
- **Where:** `Source/CybiCollectors/FalconCollector/FalconFindingsCollector.cs:631-650` (`CorrelatedRecordEmitter.EmitAsync`), `:33` (`cDefaultBatchFlushBytes`).
- **Problem:** In batch mode, every record is fully serialized (`record.ToString(Formatting.None)`) solely to measure its length, then the string is discarded and the *JObject* is buffered; the uploader later re-serializes everything a second time. A full chunk record (2000 findings × 2–10 KB with cve/remediation/evaluation_logic facets) is a 4–20 MB string — a Large Object Heap allocation per emitted chunk, churned continuously. Separately, the flush target is 48 MB of *serialized* size, but a Json.NET DOM occupies roughly 3–6× its JSON text in managed heap, so the buffer alone can hold ~150–300 MB.
- **Failure scenario:** Large tenant, hosts with thousands of findings each: sustained multi-MB LOH allocations drive gen2/LOH fragmentation and GC pauses, and the agent process (customer machine, not a cloud pod) peaks at several hundred MB for the buffer on top of the accumulators — OOM or paging on constrained endpoints.
- **Fix (local patch):** Measure size without materializing a string (serialize once into a counting/`Stream.Null` `JsonTextWriter`), and size the flush target for DOM expansion (e.g., 8–12 MB serialized target, or additionally cap by record count). Keeping the single up-front serialization *and using it* is only possible if the uploader API accepted strings — it takes `IEnumerable<JObject>`, so the cheap win is eliminating the throwaway string and lowering the target.

### 2. Cursor-expiry 404 — an expected control-flow signal — burns the blind transport retry budget before the re-anchor logic ever sees it
- **Where:** `Source/CybiCollectors/FalconCollector/FalconFindingsCollector.cs:741-750` (`FetchJsonAsync` classification) interacting with `Source/Application/Cymulate.Agent.Application.Common/Abstractions/ApiClientBase.cs:188-276` (retry loop, `cMaxApiCallsTries = 3`) and `FalconApi.tryHandleApiCallError` (`FalconApi.cs:234`, sleeps the API interval and returns retry for any non-400/401/429 status).
- **Problem:** `ValidateResponse` throws on the 404, so `makeApiCallAsync` re-sends the *already-dead* cursor two more times, with `Task.Delay(interval)` before each try plus a handler sleep per failure — ~5 interval waits total (5 s at minimum interval, up to ~2.5 min at the 30 s cap), the handler sleeps being thread-blocking `CancellableSleep`s. Only after that does the last 404 response reach `FetchJsonAsync` and become `FalconCursorExpiredException`. The new design makes token expiry a *routine* event on slow pages (the class doc says so), so this is a recurring stall, not an edge case.
- **Failure scenario:** A tenant whose per-page join work exceeds 120 s expires the Discover prefetch token on every page: each page then pays 2 wasted requests + up to minutes of sleeping before re-anchoring, multiplying total collection time and burning CrowdStrike rate-limit budget on requests that can never succeed. A Spotlight mid-batch expiry stalls the whole pipeline the same way (nothing else runs while that scroll is blocked).
- **Fix (local patch in `FalconApi`):** Short-circuit retry for the expiry signature — in `FalconApi.tryHandleApiCallError`, return `false` for 404 (or 404 + body marker) so the response returns to the caller on the first attempt. 404 on these endpoints is never retryable anyway. Do not use `throwOnFirstError: true`; that throws before the body can be classified.

### 3. Live-edge hosts can be emitted twice as two complete `chunk 0 / isLastChunk=true` envelope sets for the same aid
- **Where:** `Source/CybiCollectors/FalconCollector/FalconFindingsCollector.cs:130-132` (`freshHosts` filter), `:255-259` (`IsBoundaryDuplicate`).
- **Problem (likely risk on long scans, by design of timestamp re-anchoring):** Dedupe only covers hosts whose `last_seen_timestamp` still equals the watermark second. `last_seen_timestamp` is live — an online host processed at scan hour 1 that heartbeats at hour 3 moves *ahead* of the cursor/watermark and is re-served, both by a normal forward scroll over a live index and by any re-anchor. It passes `IsBoundaryDuplicate` (its second ≠ watermark second) and is fully re-processed: a second Spotlight scroll and a second full envelope set (`chunk: 0, isLastChunk: true`) for the same aid in the same deliverable. There is no run-level aid dedupe.
- **Failure scenario:** 6-hour scan of a 200k-host tenant; thousands of online hosts refresh `last_seen` mid-scan and appear twice. If the downstream chunk-reassembly keys on `(aid, chunk)` or asserts one `isLastChunk` per aid, it double-counts findings or corrupts reassembly; even with last-writer-wins upserts it doubles Spotlight query volume for those hosts.
- **Fix:** Decide explicitly. If downstream upserts idempotently per aid (the platform-port contract may already guarantee this), document that at the emitter and accept the duplicate work. If not, a run-level `HashSet<string>` of emitted RecordAids (~32 bytes × host count — bounded and cheap relative to the rest) turned into a skip filter in the page loop is a local patch. Requires a decision now, not deferrable: it changes the output contract.

---

## Minor

### 4. Final flush runs on the (possibly canceled) run token and swallows upload failures while stats still count the dropped records
- **Where:** `FalconFindingsCollector.cs:169` (`finally` → `emitter.FlushAsync(cancellationToken)`), `:652-701` (`FlushAsync` catch-all + `finally { _buffer.Clear(); }`).
- **Problem:** On cancellation, `SaveUploadAndDeleteBatchAsync` throws `OperationCanceledException` immediately, the catch logs it as `[BATCH ERROR]`, and the tail buffer is cleared — contradicting the comment "whatever is buffered when the run ends … is uploaded". More generally, any upload failure (thrown or `success == false`) discards the batch silently while `totalFindings`/`totalEnvelopes` were already counted at emit time, so a *successful* run can over-report delivered data. The swallow behavior is parity with the old `uploadBatchAsync`, but the byte-based batching makes the tail batch systematically large now.
- **Fix (local patch):** Pass `CancellationToken.None` (or a short timeout token) for the terminal flush, and either propagate a final-flush failure or at least subtract dropped-record counts from the returned stats.

### 5. Discover scroll treats an empty page as terminal even when the vendor returned a live `after` cursor
- **Where:** `FalconFindingsCollector.cs:233` (`hosts.Count == 0 ? null : nextAfter`), `:140` (`page.Hosts.Count == 0 … break`).
- **Problem:** The Spotlight loop was deliberately hardened to "terminate only on an empty after token", but the Discover side nulls the cursor whenever a page yields zero usable hosts. A mid-scroll empty `resources` page with a live cursor (index churn under concurrent mutation, or a page whose entries all fail the JObject/id guards) silently truncates the host inventory with no error and a "completed" log line.
- **Fix (local patch):** Trust the cursor symmetrically: keep paging while `after` is non-empty, and break on empty `resources` only when `after` is also empty (optionally with a consecutive-empty-page safety cap).

### 6. The prefetch introduces the first-ever concurrent calls through token-refresh machinery written single-flight
- **Where:** `FalconFindingsCollector.cs:136-138` (prefetch task overlapping `ProcessAidBatchAsync`'s Spotlight calls), `FalconCollector.cs:239-262` (`MakeApiCallWithTokenRefreshAsync`).
- **Problem (confirmed hazard, self-healing impact):** The Discover prefetch and Spotlight requests now run concurrently through a wrapper doing an unsynchronized check-then-act on `rTokenExpiresAt` (a multi-word `DateTimeOffset` field — torn read/write is theoretically possible) and a shared-mutable `falcon.AccessToken`. Worst realistic outcome is duplicate simultaneous OAuth token fetches and a stale-token 401 that the transport's 401 handler repairs, plus two flows blocking thread-pool threads in `CancellableSleep` at once. The static `_headers` is a `ReadOnlyDictionary` (safe) and `RestClient` per-call clients look concurrency-tolerant, so no corruption path found — but nothing in that stack was reviewed/designed for concurrency before this change.
- **Fix (local patch):** Serialize refresh with a `SemaphoreSlim(1,1)` around the expiry-check-and-refresh block in `MakeApiCallWithTokenRefreshAsync`; document that at most two calls are ever in flight (one prefetch + one scroll).

### 7. Findings without an `id` bypass the re-anchor dedupe set
- **Where:** `FalconFindingsCollector.cs:459-464`.
- **Problem (possible concern):** `if (!string.IsNullOrWhiteSpace(findingId) && !seenFindingIds.Add(findingId))` means an id-less finding is *always* accepted; after a Spotlight cursor expiry, the `>=` replay emits it twice. Spotlight findings should always carry `id`, so this is defense-in-depth only.
- **Fix (local patch):** Skip (or key by a content hash for) findings with no `id` during a re-anchored scroll, or at least count them in the skip log.

### 8. Test coverage misses the highest-traffic paths
- **Where:** `Tests/CybiCollectors/FalconCollector.Tests/FalconCorrelatedFindingsCollectorTests.cs`.
- **Problem:** The suite covers the contract, chunk cap, shaping, both re-anchor paths, byte flush, and dry-run — good. But the *normal* hot paths are untested: multi-page Discover chaining (the prefetch happy path — the only place `page.After` feeds a follow-up request), a multi-page Spotlight scroll without expiry (floor advancement across pages), orphan-finding skip, empty-`after`-string vs missing-`after` normalization, cancellation mid-run (finally-flush behavior), and the exact-cap host (chunk N emitted with 0 findings, `isLastChunk: true`).
- **Fix:** Add the multi-page happy-path tests at minimum; they are cheap with the existing `FakeFalconApi`.

---

## Nit

### 9. `SortRemediationEntities` deep-clones every entity though the array is replaced wholesale
- **Where:** `FalconFindingsCollector.cs:537-548`. Json.NET clones parented tokens on add anyway; sorting in place (or building the new array from the *detached* tokens) avoids one full clone of `remediation.entities` per finding on the hot path. Local patch, optional.

### 10. Small per-page allocation/CPU waste
- **Where:** `FalconFindingsCollector.cs:308-309` (array overload of `AdvanceWatermark` materializes a tuple `List` per batch), `:212-215` and `:433-436` (draining `JArray` via `RemoveAt(0)` is O(n²) element shifts per page — pointer moves only, but iterating backward or detaching from the end is free). Cosmetic at current page sizes.

---

## Observations (no action required)

- **Status filter behavior change:** `cSpotlightBaseFilter` now pins `status:['open','reopen','closed']`, excluding `expired` findings; the old filter was suppression-only (all statuses). Presumably platform-port parity — worth a one-line confirmation that dropping `expired` is intended.
- **Cursor-expiry marker is a body substring** (`'after' key no longer valid`, case-insensitive). If CrowdStrike rewords, degradation is fail-safe (run failure, not silent corruption) — acceptable posture.
- **`TryReadUtcDateTime`** applies `ToUniversalTime()` to `JTokenType.Date`; a non-`Z` timestamp parsed as `Unspecified`/`Local` would shift by machine timezone and skew the watermark. Falcon emits `Z`-suffixed timestamps, so this is latent only.
- **Pathological same-second cohorts:** if very many hosts share one `last_seen` second (bulk import), every re-anchor re-scrolls the whole cohort before reaching fresh hosts — bounded, dedupe-correct, but O(n²) requests in the worst case.
- **Partial-chunk records on failed runs:** if a run dies mid-batch, already-flushed `isLastChunk: false` records exist without their terminal chunk (the finally-flush ships them). Downstream must treat a failed run's file set as incomplete — worth stating in the downstream contract.
- **Intentional tradeoffs that read as sound:** the prefetch-then-observe (`ObservePendingPrefetchAsync` in `finally`) correctly prevents `UnobservedTaskException` from a racing prefetch fault; refusing to use the first-page `meta.total` as an early exit is the right call for a live index; the epoch fallback keeping Discover requests never-filter-less is correct.

---

## Summary

| Severity | Count |
|---|---|
| Blocker | 0 |
| Major | 3 |
| Minor | 5 |
| Nit | 2 |
| Observation | 6 |

No blocker: the core watermark/boundary-dedupe algebra, cursor lifecycle handling, and emission contract are internally consistent and test-verified. The Majors are operational: memory profile of the emitter on customer agents (#1), routine cursor expiry paying the blind transport retry tax (#2), and duplicate same-aid envelope sets at the live edge whose downstream tolerance is unverified (#3). #3 needs an explicit decision; #1 and #2 are local patches.
