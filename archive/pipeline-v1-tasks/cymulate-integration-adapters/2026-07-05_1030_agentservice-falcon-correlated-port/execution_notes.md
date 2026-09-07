# Execution Notes — AgentService Falcon correlated port

## Execution

### Files changed (AgentService, branch feature/falcon-correlated-findings — uncommitted)

- `Source/CybiCollectors/FalconCollector/FalconFindingsCollector.cs` — full rewrite to the correlated
  strategy. Discover scroll (limit 1000, last_seen_timestamp.asc, user FQL + last_seen>=baseDate with
  epoch fallback) with next-page prefetch before join work; aid batches of 250; one Spotlight scroll per
  batch (limit 2500, updated_timestamp.asc, suppression + status:['open','reopen','closed'] +
  updated_timestamp>=floor + aid list); per-page group-by-aid into per-host accumulators; chunk cap 2000
  (chunk/isLastChunk records); zero-finding envelopes at batch end (left-join); finding shaping strips
  apps/suppression_info/host_info and canonically sorts remediation.entities; in-memory re-anchor for
  both scrolls (Discover: whole-second last_seen watermark + boundary-aid dedupe; Spotlight:
  updated_timestamp floor + seen-finding-id dedupe); Spotlight terminates only on empty after; empty
  after normalized to null on both scrolls; pending prefetch always observed on exit. Emission via
  nested `CorrelatedRecordEmitter`: batch mode flushes `findings_NNN.json` by accumulated serialized
  bytes (~48MB target, `BatchFlushBytes` internal-settable for tests); StreamWriter NDJSON fallback
  preserved for non-batch mode. Returns `(totalVulnerabilities, totalAssets, assetsWithoutVulnerabilities)`
  = (findings emitted, envelopes emitted, zero-finding envelopes).
- `Source/CybiCollectors/FalconCollector/FalconUrls.cs` — `BuildVulnerabilitiesBaseUrl` replaced by
  `BuildCorrelatedVulnerabilitiesBaseUrl` (facets cve/remediation/evaluation_logic, NO host_info; sole
  caller was the rewritten collector). `BuildAssetsUrl` untouched (shared by CollectAssetsAsync and the
  correlated Discover traversal, as in the adapters reference).
- `Source/CybiCollectors/FalconCollector/FalconCollector.cs` — `CollectFindingsAsync`: assets stage
  removed entirely (no assets_*.json in the findings run); stats mapped per contract:
  TotalAssets = envelopes, TotalVulnerabilities = findings, AssetsWithoutVulnerabilities = zero-finding
  envelope count (exact, no longer a subtraction). `FalconAssetsCollector.cs` and `CollectAssetsAsync`
  untouched. `MakeApiCallWithTokenRefreshAsync` remains the sole transport.
- `Tests/CybiCollectors/FalconCollector.Tests/FalconCorrelatedFindingsCollectorTests.cs` — NEW. 7 tests:
  zero-finding envelope + stats; chunk-cap split (2001 → 2000/1, isLastChunk flags); finding shaping
  (strips + entities sort + URL facet/filter assertions); Spotlight cursor-expiry re-anchor
  (floor advance, no after, seen-id dedupe); Discover cursor-expiry re-anchor (watermark filter,
  boundary-aid dedupe, batch scoping); byte-aware flush with findings_NNN.json naming (fake uploader);
  dry-run single probe. Existing test project reused (all prior tests were already commented out);
  InternalsVisibleTo was already in place.

### Adaptations forced by ancestor idioms

- **Newtonsoft instead of System.Text.Json streaming**: pages load via `JObject.LoadAsync` (ancestor
  idiom) rather than the adapters' `TopLevelJsonArrayStreamReader`; hosts/findings are detached from the
  response `resources` array by `RemoveAt(0)` (prototype's pattern) so nodes re-parent into output
  records without cloning. Consequence: default Newtonsoft date handling applies (date-like strings
  round-trip through `JTokenType.Date`) — same fidelity as the ancestor's previous assets/findings output.
- **Cursor-expiry detection required restructuring error handling**: ancestor used
  `EnsureSuccessStatusCode()`, which discards the body carrying the only expiry signal. New
  `FetchJsonAsync` reads the body on failure, throws `FalconCursorExpiredException` on
  404 + "'after' key no longer valid", else `HttpRequestException` with status + truncated body.
  Token refresh stays entirely in `MakeApiCallWithTokenRefreshAsync` (untouched).
- **Sensorless hosts**: per contract (diverges from the adapters scroller, which drops AID-less hosts):
  ancestor extraction (`aid ?? device_id ?? TryExtractAidFromCombinedHostId`) keys the Spotlight batches;
  hosts without an AID-like id still emit a zero-finding envelope keyed by their raw Discover `id`, and
  are never included in a Spotlight aid filter.
- **Emitter instead of egress pipeline**: `ICybiBatchUploader` + `findings_NNN.json` retained; flush
  yardstick is `record.ToString(Formatting.None).Length` (chars≈bytes; matches the uploader's own
  serialization), replacing the old 5000-record count.
- **No checkpoints**: re-anchor state (watermark, boundary aids, seen ids) is purely in-memory —
  AgentService runs to completion in-process. The adapters' `MaxPagesPerCursorScroll` proactive
  re-anchor and the 5000 boundary-aid cap were NOT ported: both are absent from the contract's traversal
  constraints (the cap exists only to bound persisted checkpoint payloads, which don't exist here).
- **Dry-run**: single Discover probe (limit 1) with the gated filter, no publication; reads
  `meta.pagination.total` so TotalAssets keeps its ancestor dry-run meaning.

### Build / test status

- `FalconCollector.csproj`: builds clean (0 errors; CS8600 warnings at FalconCollector.cs:273–275 are
  pre-existing in `parseRawApiInfo`, untouched).
- `FalconCollector.Tests.csproj`: builds; all 7 new tests pass (164ms).
- `AgentService.sln`: fails only on `Cymulate.SmtpClient` (net481 targeting pack not installable on
  macOS) — pre-existing environment limitation, unrelated to this change; every project in the Falcon
  dependency graph builds.

### Risks

- Newtonsoft default date parsing can normalize vendor timestamp strings (fractional seconds/offsets) in
  the emitted "verbatim" host/finding JSON. This is unchanged from the ancestor's behavior, but it is a
  fidelity difference vs the adapters implementation (which streams raw UTF-8).
- Batch mode buffers up to ~48MB of serialized records as JObjects (in-memory expansion typically
  several-fold), and `CybiBatchUploader` then builds the NDJSON + payload strings — peak memory per flush
  is a multiple of 48MB. This mirrors the contract's chosen target; if agent hosts are memory-tight, the
  flush target is a single constant (`cDefaultBatchFlushBytes`).
- Dictionary enumeration order (envelope emission within a batch) is insertion order in practice but not
  contractually guaranteed by .NET; downstream must not (and does not) depend on record order.
- 48MB flush behavior is verified only via the test-scaled threshold (`BatchFlushBytes`), not at real scale.

## Repair round 1

Fixes for verifier-1.md (F1–F4) and code-reviewer-1.md (Majors 1–3, Minors/Nits triage). All changes
confined to `Source/CybiCollectors/FalconCollector/` + `Tests/CybiCollectors/FalconCollector.Tests/`.
Build clean, **14/14 tests pass** (7 pre-existing + 7 new/updated).

### Verifier F1 (Medium) — Discover depth-cap proactive re-anchor restored
- Change: `cMaxPagesPerCursorScroll = 500` (+ internal-settable `MaxPagesPerCursorScroll` for tests);
  `pagesSinceAnchor` counter, `reanchorAfterPage` gate on the prefetch, post-batches re-anchor block —
  mirrors adapters `FalconFindingsFlow.cs:165-176/221-230` exactly (incl. reset on cursor-expiry re-anchor
  and the fresh-watermark-bearing-host precondition so a re-anchored `>=` query cannot replay the page).
- Test: `CollectAsync_DiscoverDepthCap_ProactivelyReanchorsInsteadOfRidingCursor` (cap=1): the after-token
  is never ridden (no `after=` on any Discover request), the second scroll re-anchors from the watermark
  filter, boundary-aid dedupe scopes the follow-up Spotlight batch to fresh hosts only.

### Reviewer Major 1 — emission memory profile
- Change: `CorrelatedRecordEmitter` serializes each record **exactly once** at append and buffers the flat
  string wrapped in `JRaw` (a `JToken`, so the shared `CybiBatchUploader`'s `JToken.ToString(Formatting.None)`
  re-serialization writes it verbatim — no shared-infra change, byte-identical output). The record's JObject
  DOM is released at emit instead of being buffered; the throwaway multi-MB per-record sizing string is gone
  (the one serialization is now the retained payload).
- Final peak-memory profile (48MB flush target, worst case):
  - Standing between flushes: ~48M chars of buffered serialized records ≈ **~96MB** UTF-16 (was: JObject DOM
    at 3–6× serialized size ≈ 150–300MB, plus a discarded multi-MB LOH string per emitted chunk).
  - Transient during flush (inside the shared uploader, unchanged): NDJSON StringBuilder ~96MB + payload
    string ~96MB + UTF-8 `StringContent` ~48MB → **peak ≈ 340MB** for the flush call (was ≈ 400–550MB on top
    of continuous LOH churn). Per-record LOH allocations remain (records are multi-MB by design) but each is
    allocated once and used, never thrown away.
  - Flush target kept at the contract's ~48MB; it remains the single tuning constant (`cDefaultBatchFlushBytes`)
    if constrained agent hosts need a smaller standing buffer.
- Test: flush test re-pointed at `IEnumerable<JRaw>` and now asserts the uploaded serialized record parses
  back to the full envelope contract.

### Reviewer Major 2 — cursor-expiry 404 no longer burns the transport retry budget
- Fixed **within the collector's seam** — no shared infra touched: `FalconCollector` now overrides
  `tryHandleApiCallError` (virtual on `ApiClientBase`, previously overridden by `FalconApi`) and returns
  `false` for any 404, so `makeApiCallAsync` exits its retry loop on the first attempt and returns the
  response to `FetchJsonAsync` for body-marker classification. 404 on the collector's GET endpoints is
  deterministic (dead cursor or bad route) — blind re-sends can never succeed. All other statuses keep the
  FalconApi handling (429 backoff, 401 token refresh, paced retry). Routine expiry now costs one round-trip
  + immediate re-anchor instead of 2 wasted re-sends + up to minutes of paced sleeps.
- Test: `FalconCollectorRetrySeamTests` — 404 returns false (skip retries), 500 still returns true
  (delegates to base).

### Verifier F2 — sensorless-host test added
- `CollectAsync_SensorlessHost_EmitsZeroFindingEnvelopeAndIsExcludedFromSpotlight`: host with no
  aid/device_id and a non-AID combined id (`cid1_not-an-aid`) emits a zero-finding chunk-0/isLastChunk
  envelope keyed by the raw Discover id, never appears in the Spotlight aid filter, and counts in
  AssetsWithoutVulnerabilities.

### Verifier F3 — reader byte fidelity hardened
- Change: `FetchJsonAsync`'s `JsonTextReader` (the single reader path — every Discover/Spotlight/dry-run
  load goes through it) now sets `DateParseHandling.None` + `FloatParseHandling.Decimal`, so vendor values
  round-trip verbatim into emitted records (no fractional-zero dropping, no machine-local offset rewrite,
  no `3.10`→`3.1`). Internal watermark logic unaffected (`TryReadUtcDateTime` parses the string form).
- Test: `CollectAsync_VendorValues_RoundTripVerbatimIntoEmittedRecords` asserts the raw NDJSON line
  preserves `…08:00:00.000Z`, `…09:00:00.950Z`, a `+02:00` offset, and `3.10` byte-for-byte.

### Verifier F4 — dry-run Spotlight scope validation restored
- Change: `DryRunProbeAsync` issues a second `limit=1` probe against the correlated Spotlight URL (base
  suppression/status filter + updated floor, no aid clause) after the Discover probe — a key missing the
  Spotlight scope now fails at dry-run, matching ancestor behavior. Still no publication; `TotalAssets`
  keeps the Discover `meta.pagination.total` meaning.
- Test: dry-run test updated to assert both probes (Discover limit=1, Spotlight limit=1 + filter, no aid).

### Reviewer Major 3 — live-edge duplicate envelopes: documented, no code change
- Comment added at the `freshHosts` boundary-dedupe site: a host whose `last_seen` advances mid-run can be
  re-served ahead of the cursor and re-emitted as a second complete chunk-0/isLastChunk envelope set for the
  same aid. Accepted by design: the downstream correlated parser is **replay-idempotent per aid** (last-wins
  asset spine + finding-id dedupe), and the platform collector has the identical live-edge property. Cost is
  bounded duplicate Spotlight work for hosts that heartbeat mid-scan; no run-level aid dedupe added (would
  diverge from the mirrored implementation without changing the parsed result).

### Reviewer Minors/Nits triage
- **Minor 4 (terminal flush on canceled token): fixed** — the `finally` flush now runs on
  `CancellationToken.None` (uploader owns a 120s timeout, so bounded), so a canceled run no longer silently
  drops the tail batch. The stats-vs-dropped-batch accounting half is **skipped**: the swallow-and-count
  behavior is deliberate ancestor parity, and exact drop subtraction would thread per-record counters
  through the emitter for an informational stat.
- **Minor 5 (empty page with live after treated as terminal): skipped** — byte-for-byte parity with the
  adapters reference (`FalconDiscoverHostScroller.cs:90` has the identical `hosts.Count == 0 ? null : after`);
  diverging would violate the mirror mandate. The concern applies equally to the platform collector.
- **Minor 6 (concurrent token refresh): fixed** — `MakeApiCallWithTokenRefreshAsync` now single-flights the
  proactive expiry check-and-refresh behind a `SemaphoreSlim(1,1)` with a double-check, eliminating the
  duplicate-OAuth/torn-read race between the Discover prefetch and Spotlight calls.
- **Minor 7 (id-less findings bypass re-anchor dedupe): skipped** — identical guard in the adapters
  reference (`FalconSpotlightBatchScroller.cs:144-149`); defense-in-depth only (Spotlight findings always
  carry `id`), and parity wins.
- **Minor 8 (hot-path test coverage): partially fixed** — added the reviewer's stated minimum: multi-page
  Discover chaining through the prefetch (cursor + fixed scroll filter asserted) and multi-page Spotlight
  scroll (cursor chained, anchor filter fixed while the floor advances in memory). Cancellation-mid-run,
  orphan-skip, and exact-cap-host tests not added this round.
- **Nit 9 (entity sort deep-clones): fixed** — entities are detached (`Clear()`) then re-added in canonical
  order, clone-free; same array instance retained.
- **Nit 10 (RemoveAt(0) O(n²) / tuple list per batch): skipped** — cosmetic at current page sizes per the
  reviewer's own calibration; the detach-in-order idiom is the prototype's pattern.

### Build / test status (repair round 1)
- `FalconCollector.csproj`: builds, 0 errors (warnings pre-existing).
- `FalconCollector.Tests.csproj`: 14/14 pass (~1s).
- `AgentService.sln`: unchanged known net481 failure (out of scope).
