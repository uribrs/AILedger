# Verifier Review 1 — AgentService Falcon correlated port

Reviewed: uncommitted changes on `feature/falcon-correlated-findings` in `/Users/user/Dev/AgentService`
(`FalconFindingsCollector.cs`, `FalconUrls.cs`, `FalconCollector.cs`, new
`Tests/CybiCollectors/FalconCollector.Tests/FalconCorrelatedFindingsCollectorTests.cs`).
Baselines: adapters `Collectors/FalconCollector/Flows/Findings/` (FalconFindingsFlow,
FalconDiscoverHostScroller, FalconSpotlightBatchScroller, FalconCorrelatedRecord, AidExtractor,
HostFindingsAccumulator, FalconHostFilters) and the prototype
`IntegrationProbes/Integrations/Falcon/FalconCorrelatedFindingsProbe.cs`; ancestor = `git show HEAD`
of the three changed files.

Verification performed: side-by-side code comparison against both references and the ancestor;
`dotnet build FalconCollector.csproj` (0 errors); `dotnet test FalconCollector.Tests.csproj`
(**7/7 pass**, 145 ms); empirical Newtonsoft round-trip experiment (net8.0 console, Newtonsoft 13.0.3);
scan of real vendor probe output (`ProbeResults/FalconCorrelatedFindings_20260702_132204_279Z`);
inspection of the downstream parser
(`cymulate-integration-parsers` branch `feature/crowdstrike-correlated-findings`,
`CrowdstrikeAssetsFindingsCorrelated.py`); inspection of `ApiClientBase.makeApiCallAsync` and
`FalconApi.tryHandleApiCallError` for the token-refresh path.

## Contract Constraints

| # | Constraint | Verdict | Evidence |
|---|-----------|---------|----------|
| C1 | Record contract identical: `{aid, chunk, isLastChunk, findingsInChunk, host, findings[]}`; cap 2000; zero-finding hosts empty findings; no host_info facet; strip apps+suppression_info; sort remediation.entities | **PASS** | `BuildCorrelatedRecord` (FalconFindingsCollector.cs:555-572) field-for-field matches `FalconCorrelatedRecord.Build`; `cChunkFindingsCap = 2000`; `ShapeFinding` strips `apps`/`suppression_info`/`host_info` and sorts entities by serialized form ordinal — identical to adapters. `BuildCorrelatedVulnerabilitiesBaseUrl` requests cve/remediation/evaluation_logic, no host_info (FalconUrls.cs:20-28), matching adapters `BuildCorrelatedSpotlightBaseUrl` (default path). Tests assert envelope fields, chunk split at 2001, strips, sort, and URL facets. |
| C2 | Traversal identical: Discover scroll (limit 1000, last_seen.asc, user FQL + last_seen>=baseDate, epoch fallback) with prefetch; aid batches 250; per batch one Spotlight scroll (limit 2500, updated.asc, suppression + status + updated>=baseDate + aid list); group per page by aid; flush at cap | **PASS** | Constants match (`cDiscoverPageLimit=1000`, `cSpotlightPageLimit=2500`, `cAidBatchSize=250`). Loop structure (prefetch before join work, `freshHosts.Chunk`, empty-page/terminal break condition) is line-for-line the adapters `FalconFindingsFlow.CollectAsync` shape. `BuildAssetsFilterForFindings` is byte-equivalent to adapters `FalconHostFilters.BuildLastSeenTimestampGateFilter` (incl. `Trim().TrimStart('+')`); epoch fallback via `windowStartUtc = baseDate > MinValue ? baseDate : UnixEpoch` guarantees the required Discover filter. Spotlight base filter string identical to adapters. |
| C3 | Cursor discipline: 120s after-tokens never trusted across stalls; in-memory re-anchor on 404 body marker; Discover from whole-second watermark + boundary-aid dedupe; Spotlight from updated floor + seen-id dedupe; Spotlight terminates only on empty after; no persisted checkpoints | **PASS** | `AdvanceWatermark`/`IsBoundaryDuplicate`/`TruncateToSecond` are verbatim ports of the adapters logic (incl. clear-on-advance, same-second repopulation, monotonic batch max). Spotlight re-anchor guard `when (!after.IsNullOrWhiteSpace())`, floor advance from every received finding (even skipped), seen-id dedupe, orphan-skip-before-seen-set — all mirror `FalconSpotlightBatchScroller`. Empty after normalized to null on both scrolls. Terminates only on empty after (meta total deliberately not used, comment preserved). No checkpoint state anywhere. Both re-anchor paths covered by tests (URL-level assertions on the re-anchored filter + dedupe). |
| C4 | Aid extraction keeps ancestor logic (`aid ?? device_id ?? TryExtractAidFromCombinedHostId`); sensorless hosts emit zero-finding envelope but are not sent to Spotlight | **PASS (code) / untested** | `ExtractSpotlightAid` + `TryExtractAidFromCombinedHostId` are verbatim from the ancestor (`FetchAssetIdsAsync`). `DiscoverHost(RecordAid, SpotlightAid, …)`: sensorless hosts get `RecordAid = raw Discover id`, `SpotlightAid = null`; only `SpotlightAid` values enter the aid filter (`spotlightAids` set in `ProcessAidBatchAsync`), and every accumulator emits a final envelope. **No test exercises a host without an AID-like id** — see Finding F2. |
| C5 | Findings flow drops the assets stage; standalone CollectAssetsAsync unchanged | **PASS** | Diff removes the entire assets-first stage from `CollectFindingsAsync` (no `assets_*.json`); `CollectAssetsAsync` and `FalconAssetsCollector.cs` untouched (git status confirms). `BuildAssetsUrl` untouched (shared by both, as in adapters). |
| C6 | Batch upload: ICybiBatchUploader + `findings_NNN.json`, flush by ~48MB serialized bytes, never unbounded | **PASS** | `CorrelatedRecordEmitter`: `cDefaultBatchFlushBytes = 48MB`, `findings_{n:D3}.json` naming identical to ancestor `uploadBatchAsync`, flush-in-finally + "No findings data collected" log preserved. Buffer bounded by flush target + one record (record itself bounded by the 2000-finding chunk cap). Yardstick is `ToString(Formatting.None).Length` (chars) — undercounts UTF-8 bytes for non-ASCII content, so flushes slightly late; acceptable. Byte-flush + naming verified by test with fake uploader. See F7 for the uploader-side peak-memory note. |
| C7 | Stats: TotalAssets = envelopes emitted; TotalVulnerabilities = findings emitted; AssetsWithoutVulnerabilities = zero-finding envelope count | **PASS** | `ProcessAidBatchAsync` returns `(findingsEmitted, accumulators.Count, zeroFindingEnvelopes)`; "envelope" = one per host (multi-chunk host counts once), which is exactly adapters' `stats.HostsEmitted` semantics. `CollectFindingsAsync` maps 1:1 (FalconCollector.cs:226-235), no subtraction. Asserted in 4 of 7 tests. |
| C8 | Keep public API surface, token-refresh call path, dry-run semantics (single probe, no publication), logging style, FQLParser usage | **PASS** | `IAsyncFindingsCollector.CollectFindingsAsync` signature unchanged; `MakeApiCallWithTokenRefreshAsync` is the sole transport (untouched); `ParseFqlOrThrow` retained; `tryLogWithProductName` style kept. Dry-run = one Discover probe (limit 1, gated filter), no emitter constructed, TotalAssets from `meta.pagination.total` (ancestor meaning). Note F4: the ancestor's dry-run also made a Spotlight request, so it implicitly validated Spotlight API scope; the port's does not. The contract pinned "single probe", so this is compliant — flagged as a behavioral regression vs ancestor. |
| C9 | No new dependencies; Newtonsoft idioms | **PASS** | No csproj changes (git status: only the new test .cs is untracked). `JObject.LoadAsync` + `RemoveAt(0)` detach is the prototype's exact pattern; `InternalsVisibleTo("FalconCollector.Tests")` pre-existing. |

## Success Criteria

| # | Criterion | Verdict | Evidence |
|---|-----------|---------|----------|
| SC1 | FalconCollector project builds (solution if buildable) | **PASS** | `dotnet build FalconCollector.csproj`: 0 errors (re-verified). Test project builds. Solution failure is the pre-existing net481 targeting-pack issue, out of scope per task instructions. |
| SC2 | CollectFindingsAsync emits only correlated batches; record contract verified in the repo's test harness | **PASS** | No assets stage remains; batch mode emits only `findings_NNN.json` of correlated records; non-batch mode emits correlated NDJSON lines. 7/7 new tests pass (re-run by verifier); contract fields, chunking, shaping, both re-anchors, byte flush, dry-run all asserted. Gap: sensorless path untested (F2). |
| SC3 | Traversal/re-anchor/chunking mirrors the adapters implementation observably | **PARTIAL** | Everything mirrors — loop structure, prefetch, watermark algebra, boundary dedupe, Spotlight scroll, chunk emission, prefetch observation in `finally` — **except** the adapters' `MaxPagesPerCursorScroll` proactive re-anchor (default 500) was dropped. See F1: the executor's justification for dropping it is factually wrong. Boundary-aid cap drop is correctly justified (checkpoint-payload bound only; no checkpoints here). |
| SC4 | Stats fields populated per mapping | **PASS** | See C7. |

## Scrutiny Items

### 1. Record-contract byte fidelity (Newtonsoft date/float handling) — REAL mechanism, LOW practical risk

Empirically verified (net8.0 + Newtonsoft 13.0.3, default `JsonTextReader` → `JObject.Load` →
`ToString(Formatting.None)`):

| Input string | Output | Mutated? |
|---|---|---|
| `2026-01-02T09:00:00Z` | `2026-01-02T09:00:00Z` | no |
| `2026-01-02T09:00:00.000Z` | `2026-01-02T09:00:00Z` | **yes** (trailing fractional zeros dropped) |
| `2026-01-02T09:00:00.950Z` | `2026-01-02T09:00:00.95Z` | **yes** |
| `2026-01-02T09:00:00.123456789Z` | unchanged (stays String; >7 digits doesn't parse as Date) | no |
| `…+02:00` offset | rewritten to machine-local offset (matched only by TZ coincidence in my run) | **machine-TZ-dependent** |
| `3.10` (number) | `3.1` (double parse) | semantically equal |

So yes: `JObject.LoadAsync` with default settings does mutate a subset of ISO timestamp strings on
re-serialization, and emitted records can differ byte-wise from the platform collector (which streams
raw UTF-8). **However**: (a) the ancestor's previous findings output used the identical load path
(ancestor lines 133-139), so this is not a regression for AgentService consumers; (b) the prototype —
whose output validated the record contract — used the same Newtonsoft pattern (no `DateParseHandling`
override); (c) a scan of the real vendor probe output (all `correlated_*.json` under
`ProbeResults/FalconCorrelatedFindings_20260702_132204_279Z`) found **zero** fractional-second or
non-UTC-offset timestamps — CrowdStrike emits whole-second `Z` timestamps, which round-trip verbatim;
(d) the parser reparses timestamps, so any residual difference is byte-level, not semantic. Internal
watermark logic is insulated: `TryReadUtcDateTime` handles both `JTokenType.Date` and string forms.

Verdict: risk is REAL as a mechanism but not observed in real data and identical to the validated
prototype's behavior. If byte-parity with the platform collector ever becomes a hard requirement,
setting `DateParseHandling = DateParseHandling.None` (and `FloatParseHandling.Decimal`) on the reader
in `FetchJsonAsync` is a two-line fix. Not required by the contract.

### 2a. Sensorless-host divergence — contract-mandated; parser handles it; no breakage

Confirmed the adapters scroller **drops** AID-less hosts (`FalconDiscoverHostScroller.cs:72-76`),
while the port emits them as zero-finding envelopes keyed by the raw Discover `id` — exactly as the
contract's C4 requires (this is a deliberate, contract-level divergence from the adapters reference,
not an executor invention). Parser check
(`CrowdstrikeAssetsFindingsCorrelated.py`, branch `feature/crowdstrike-correlated-findings`): the
asset spine is `chunk == 0` records deduped one row per **record-level** `aid` (host's own `aid`
field is dropped in favor of the record key); findings correlate by the same key. Raw Discover ids
(`<cid>_<hash>`, containing `_`) cannot collide with 32-hex AIDs, sensorless envelopes carry
`findings: []`, and every host appears once at `chunk == 0` — all parser invariants hold. The two id
vocabularies coexist without breaking dedupe or correlation.

Residual (by design): an agent-collected tenant will surface sensorless/unmanaged hosts as assets in
the findings feed where a platform-collected tenant will not. That is the contract's choice; flag it
so nobody later diagnoses the asset-count difference as a bug.

### 2b. Dropped caps — one safe, one not

- **Boundary-aid cap (5000) drop: safe.** In adapters it exists solely to bound the *persisted
  checkpoint payload* (`FalconFindingsFlow.MaxBoundaryAids` doc). Here the set is in-memory only and
  bounded by hosts sharing one whole second; even a pathological bulk import stamping 100k hosts with
  one timestamp costs a few MB of strings for the run's lifetime. Executor's justification correct.
- **`MaxPagesPerCursorScroll` drop: NOT justified by the executor's stated reason** — see F1. The
  depth cap is not a checkpoint concern; per the adapters code comments (`FalconFindingsFlow.cs:165-168`,
  `FalconAssetsScrollRunner.cs:375` "the deep-scroll 500"), CrowdStrike's server-side scroll context
  degrades with depth and eventually hard-500s. That vendor behavior applies identically in-process.
- Other memory: per-host accumulators bounded by the chunk cap; per-batch seen-finding-id set bounded
  by the batch's findings (parity with adapters); emitter buffer bounded by flush target + one record.
  No unbounded growth found.

### 3. Stats / dry-run / StreamWriter fallback

- Stats: exact per contract (C7).
- Dry-run: single Discover probe, no emitter, no upload/stream writes; `TotalAssets` keeps the
  ancestor's `meta.pagination.total` meaning; verified by test (single URL, `limit=1`). Regression
  note F4 (Spotlight scope no longer validated by dry-run).
- StreamWriter fallback: `EmitAsync` non-batch path writes the **correlated record** as one NDJSON
  line (`WriteLineAsync(record.ToString(Formatting.None))`) — not raw vulns. All five non-batch tests
  read their assertions back off the StreamWriter stream, so the fallback path is what the test
  suite exercises end-to-end. Correct.

### 4. Cursor-expiry detection vs token refresh — correct; 401 path intact

Traced the full transport stack. `ApiClientBase.makeApiCallAsync` (ApiClientBase.cs:175-283) throws
internally on any non-2xx (`ValidateResponse`), retries up to 3 tries via
`FalconApi.tryHandleApiCallError` — which is where **401 → `TryCheckConnectionAsync` → AccessToken
refresh → retry** lives (FalconApi.cs:246-263) — and, when retries exhaust, **returns the final
non-success response** rather than throwing. So by the time `FetchJsonAsync` sees a 401/404 response,
the refresh machinery has already run; reading the body on non-success cannot interfere with it. The
proactive pre-call refresh in `MakeApiCallWithTokenRefreshAsync` is also upstream of `FetchJsonAsync`.
The 404-body cursor-expiry classification is therefore correctly layered. Verified the body remains
readable (nothing in the retry loop consumes/disposes the returned response's content).

Latency note (F5): an expired-cursor 404 first burns makeApiCallAsync's full retry budget (3 attempts
of a permanently dead cursor, with pacing sleeps between) before the port sees the 404 and re-anchors.
Functionally harmless — re-anchoring is watermark-based, not time-sensitive — but each expiry costs
up to ~3 paced round-trips of dead work.

## Findings

- **F1 (Medium) — `MaxPagesPerCursorScroll` proactive re-anchor dropped on a wrong premise.**
  execution_notes.md claims "the cap exists only to bound persisted checkpoint payloads". That is true
  for the *boundary-aid cap* but false for the *depth cap*: adapters documents it as a defense against
  CrowdStrike deep-scroll context degradation ("eventually hard-500s"), a vendor behavior that is
  process-model-independent. In the port, a very large tenant (>~500 Discover pages ≈ 500k hosts on
  one cursor, or fewer pages ridden slowly) risks a mid-scroll 500 → `HttpRequestException` → run
  failure with no resume (partial batches flushed, stats lost). The port's watermark machinery already
  supports a proactive re-anchor — adding it is ~10 lines mirroring FalconFindingsFlow.cs:165-176/221-230.
  Not a contract-text violation (the constraint list omits it), but a gap against SC3's "mirrors the
  adapters implementation" and a real reliability regression for large tenants.
- **F2 (Low) — Sensorless-host path untested.** The contract-specified behavior (zero-finding envelope
  keyed by raw Discover id; excluded from the Spotlight aid filter) has no test; every test host
  carries an `aid`. One test with a host exposing only `id: "cid_<non-hex>"` would cover C4's most
  port-specific behavior.
- **F3 (Info) — Byte fidelity.** Real mechanism, empirically confirmed; zero exposure in real vendor
  data; identical to ancestor and prototype behavior. Optional hardening: `DateParseHandling.None` in
  `FetchJsonAsync`.
- **F4 (Low) — Dry-run no longer validates Spotlight access.** Ancestor dry-run issued one Spotlight
  request; the port probes Discover only (adapters relies on a separate access prober AgentService
  lacks). A key missing the Spotlight scope now passes dry-run and fails on the first real run.
  Compliant with the contract's "single probe" wording — surface to the operator as a contract-level
  trade-off.
- **F5 (Info) — Expired-cursor 404 burns the transport retry budget** (3 paced attempts of a dead
  cursor) before re-anchor. Latency only.
- **F6 (Info) — New concurrency on previously-sequential infra.** The Discover prefetch now races
  Spotlight calls through shared `FalconCollector`/`ApiClientBase` state (`rTokenExpiresAt`,
  `AccessToken` writes, `mNormalizedApiCallsInterval`). All observed races are benign (duplicate
  refresh, pacing-interval jitter), but this infra had never run concurrent calls before this change.
- **F7 (Info) — Uploader peak memory.** `CybiBatchUploader` materializes the full ~48MB batch as a
  `StringBuilder` NDJSON string (+ payload wrapper) on top of the buffered JObjects; peak per flush is
  a small multiple of 48MB on customer agent hosts. Already flagged in execution_notes; the target is
  one constant if it needs tuning.

## Overall Verdict

**PASS with reservations.** The port is a faithful, high-quality mirror of the adapters correlated
flow in AgentService idioms: every contract constraint is implemented and all stated success criteria
are met except SC3, which is **PARTIAL** due to the dropped `MaxPagesPerCursorScroll` proactive
re-anchor (F1 — dropped on an incorrect rationale, with a real large-tenant failure mode). Build
clean, 7/7 tests pass (independently re-run). Recommend: fix F1 (small, mechanical), add the F2 test,
and record F4 as an accepted trade-off before merge.
