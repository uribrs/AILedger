# Code Review — Falcon correlated findings redesign (adapters + parsers)

Reviewer: independent code review, 2026-07-03.
Scope: uncommitted working-tree changes in `cymulate-integration-adapters` (FalconCollector findings flow rewrite, Egress content hash, tests) and `cymulate-integration-parsers` (crowdstrike correlated single-lane parser path).

Calibration: **High risk** — pagination state, checkpoint/resume semantics, watermark replay, shared egress library, cross-repo data contract. Reviewed accordingly (failure semantics, recovery/idempotency, scale behavior first).

Verification performed: Falcon test project builds clean and passes (99/99). Parser suite `tests/test_crowdstrike_assets_findings.py` passes (20/20). Findings below are from reading the code, not from test failures.

Overall: the rewrite is a large net simplification (−8.3k lines) with a coherent watermark-only resume model, good streaming discipline, and unusually good test coverage for re-anchor/dedupe paths. The findings below are concentrated where watermark replay meets the vendor's opaque cursor and where the collector's output contract meets the parser's assumptions.

---

## 1. MAJOR (likely risk) — Spotlight scroll mutates its filter while paging with a live `after` token

**File:** `src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/Findings/Correlated/FalconSpotlightBatchScroller.cs:81` (filter rebuilt per iteration) together with `:160-165` (`updatedFloor` advances after **every** page, not only on re-anchor).

**Defect:** each continuation request combines the *previous* page's `after` token with a *new* filter (`updated_timestamp:>=` advanced to the last page's max), i.e. the token is applied to a different query than the one that minted it.

**Why this is wrong by the change's own model:** `FalconFindingsFlow.cs:118-119` states "The after-token continues the search context of the query that created it, so the scroll filter stays fixed for the lifetime of a forward scroll" — and the Discover loop implements exactly that (`scrollFilter` is only rebuilt on re-anchor). The Spotlight loop violates it. CrowdStrike documents `after` as an opaque continuation of the *same* request; behavior with a changed filter is undefined.

**Failure scenario:** batch scroll of a large host group, page 1 returns floor F1→F2 and token A1. Page 2 request = `filter >= F2 & after=A1`. If the token is offset-like against the (now smaller) re-executed result set, `limit` records of the true page 2 are silently skipped — findings lost for the run with no error, no orphan count, nothing observable. The unit test for this path (`FalconCorrelatedFindingsTests` test 8) routes the mock purely on `after=cursor-B` and never validates the filter, so the tests cannot catch it.

**Fix (local patch):** compute the filter once per scroll anchor — rebuild it only when `after` is reset to null (the re-anchor path already does the right thing). `updatedFloor` should keep advancing as the re-anchor bookmark, but must not leak into forward-scroll continuation requests.

---

## 2. MAJOR (cross-repo contract) — parser assumes "every host appears exactly once at chunk == 0"; the collector cannot guarantee it, and the parser has no aid dedupe

**Files:**
- `cymulate-integration-parsers/libs/packages/parsers/deprecated/crowdstrike/CrowdstrikeAssetsFindingsCorrelated.py:16-17` (documented invariant), `:155-165` (`_build_asset_spine` — no dedupe on `aid`), `:106` (`correlate(asset_spine, findings_df, on aid)`).
- Collector sources of duplicate aids: `FalconFindingsFlow.cs:133-141` (Discover re-anchor) and the resume path.

**Defect:** a host legitimately re-appears in the Discover scroll after a re-anchor or resume whenever its `last_seen_timestamp` advances past the watermark mid-run (host checked in during a multi-hour collection — routine, not exotic). The collector then emits a **second** chunk-0 record for that aid, complete with its full findings set. The correlated parser builds the spine from *all* chunk-0 records with no dedupe, then `correlate` embeds the aid's findings onto **each** spine row.

**Failure scenario:** host H with n findings, published in batch 3; a Discover cursor expires in batch 7; H checked in meanwhile, so its `last_seen` is now above the watermark; the re-anchored scroll re-emits H. Final lane contains 2 chunk-0 records and 2n finding rows for H. Parser: spine has 2 rows for H's aid; the findings side explodes 2n rows; the aid join gives each spine row the full 2n array → **4n finding rows and 2 asset rows** downstream for that host. Silent count inflation; the e2e test's ground truth (which counts raw records) would also over-count, so it wouldn't flag it either.

**Fix (local patch, parser side):** dedupe the spine — one row per aid (e.g. `row_number() over (partition by aid)` keep first, or `dropDuplicates(["aid"])` on the chunk-0 projection) and dedupe exploded findings on the finding `id` before correlate. That makes the parser robust to *all* collector replay semantics (which are inherent to watermark re-anchor) instead of depending on an invariant the collector cannot fully deliver. Collector-side dedupe alone cannot close this (see finding 3 as an independent duplicate source).

---

## 3. MAJOR (latent) — boundary dedupe compares full-tick watermark equality, but the re-anchor filter truncates to whole seconds

**Files:** `FalconFindingsFlow.cs:208-212` (`IsBoundaryDuplicate` — exact `DateTime` equality), `:241-249` (`AdvanceWatermark` — boundary aids collected only at exact tick equality), vs `Flows/SharedFlows/FalconHostFilters.cs:7,21` (filter format `yyyy-MM-ddTHH:mm:ssZ` — truncates fractional seconds).

**Defect:** the re-anchor/resume Discover query floors at `truncate_to_second(watermark)` (`>=`), but the dedupe set only covers hosts whose `last_seen` equals the watermark **to the tick**. Any published host whose `last_seen` lies in `[floor, watermark)` within the same second is re-fetched and fails `IsBoundaryDuplicate` → re-emitted.

**Failure scenario:** if Discover ever returns sub-second precision (CrowdStrike emits fractional-second timestamps on several fields; `FalconJson.TryReadUtcDateTime` faithfully preserves them), hosts at `…T10:00:00.100Z` are re-emitted after every re-anchor whose watermark is `…T10:00:00.700Z` — duplicate host envelopes with full duplicate findings, feeding directly into finding 2's inflation. Today Discover `last_seen_timestamp` appears second-granular, so this is latent — but the code has a granularity mismatch between the value it persists/compares and the value the vendor filter can express.

**Fix (local patch):** truncate the watermark to seconds at the single point it is advanced (or compare truncated values in `IsBoundaryDuplicate`/`AdvanceWatermark`), so the dedupe set's equality domain matches the filter's expressible floor. Same pattern the Spotlight side already gets right by construction (seen-id set, not timestamp equality).

---

## 4. MINOR — `DiscoverWatermarkAids` accumulation is unbounded; the checkpoint can bloat

**File:** `FalconFindingsFlow.cs:241-249` (boundary aids accumulate for as long as the watermark does not advance) → serialized whole into the checkpoint at `FalconCheckpointSerializer.cs` (`DiscoverWatermarkAids` JSON list, every batch).

**Defect:** the boundary set is bounded only by "hosts sharing the exact watermark timestamp". A bulk import/migration that stamps thousands of hosts with an identical `last_seen_timestamp` (second granularity makes collisions easy at fleet scale) grows the list across consecutive batches without bound and rewrites it into `AdapterState` on every batch.

**Failure scenario:** 50k hosts sharing one timestamp → ~1.8 MB of aid JSON per checkpoint write; checkpoint persistence may exceed platform state-size limits and the run loses resumability precisely on the tenants that need it. The deleted deserializer carried an explicit 1 MB guard against exactly this class of payload (legacy `collectedAids`) — the lesson exists in this repo's history; the new code reintroduces the exposure without a cap.

**Fix (local patch):** cap the persisted boundary list (e.g. N thousand aids); when exceeded, fall back to persisting `watermark + 1 tick`-style floor advance or accept bounded boundary re-emission (which finding 2's parser dedupe would absorb). At minimum log the set size.

---

## 5. MINOR — Discover prefetch task is orphaned on any failure in the batch loop

**File:** `FalconFindingsFlow.cs:144-146` (prefetch started), `:157-193` (batch loop can throw — publish failure, checkpoint failure, cancellation at `:159` — with `nextPageTask` still in flight; no `try/finally` observes it).

**Defect:** when the loop exits via exception, the in-flight prefetch task is abandoned. Its own failure (an *expected* one: cursor-expired 404, transport fault) becomes an `UnobservedTaskException`.

**Failure scenario:** a publish failure escalates to the resilience layer while the prefetch 404s; seconds later the finalizer thread surfaces an unobserved `FalconCursorExpiredException` in host telemetry, unattributable to any run context — noise at best, process-policy dependent at worst. Repeats on every deferred-recovery cycle.

**Fix (local patch):** wrap the loop in `try/finally` that awaits/observes `nextPageTask` (swallowing its exception with a debug log) before propagating.

---

## 6. MINOR — batch scroll early-exit compares a stale first-page `total` against a set that includes orphans

**File:** `FalconSpotlightBatchScroller.cs:156` (`reportedTotal ??=` first page only), `:166` (`seenFindingIds.Count >= reportedTotal` breaks the scroll while `after` is still live). Note `seenFindingIds` is populated at `:124` *before* the aid/orphan check, so orphans count toward the total.

**Defect:** `total` is snapshotted from the first response and never refreshed; the seen-set counts records that were not emitted (orphans, and ids seen across re-anchor whose records changed). If findings are updated *into* the window mid-scroll (their `updated_timestamp` bumps into range), the vendor's live total grows past the snapshot and the break fires with a live `after`, dropping the tail of the batch for this run.

**Failure scenario:** batch of 250 busy hosts during patch Tuesday; 300 findings get re-scored mid-scroll; the scroll terminates at the stale total and the newest findings are silently missing until the next scheduled collection.

**Fix (local patch):** drop the early-exit entirely — `after == null` already terminates the scroll, and the guard buys nothing except this failure mode. If it exists to bound a pathological live-edge loop, gate it on `>=` with a generous multiplier of the total instead of equality-order.

---

## 7. MINOR — the findings Discover scroll dropped the deep-scroll depth cap; `MaxPagesPerCursorScroll` is now a dead knob with a stale doc

**File:** `Dtos/FindingsDtos/FindingsFlowRunConfig.cs:31-35` (documented as "caps server-side scroll depth to avoid the deep-scroll 500"; carried through `FalconFlowRunPreparer.cs:66`) — no consumer anywhere in the correlated flow. The assets flow still enforces it (`FalconAssetsScrollRunner.cs:375-380`).

**Defect/scenario:** a tenant with >500 Discover pages in the window (500k+ hosts at page size 1000) will ride one `after` scroll past CrowdStrike's documented deep-scroll instability and take reactive 500s; each becomes a budgeted scheduled-recovery wait (5m/15m/30m) instead of the assets flow's free in-process proactive re-anchor. Self-heals, but slowly and while consuming recovery budget.

**Fix (local patch):** either wire the existing knob into the flow loop (count pages per anchor; when exceeded, re-anchor from the watermark exactly like the cursor-expired path — the mechanism is already there) or delete the property from `FindingsFlowRunConfig` and fix the doc. Half-carried config is worse than either.

---

## 8. MINOR (parsers) — `detect_correlated_shape` pays a full schema-inference read of the first shard just to list columns

**File:** `CrowdstrikeAssetsFindingsCorrelated.py:76` (`spark.read...json(paths[0]).columns`).

**Defect/scenario:** Spark JSON schema inference scans the entire file. Correlated shards routinely reach tens of MB (multipart logical objects larger); on Glue this is a wasted full pass over shard 1 — then `_load_records_df` immediately re-reads everything with inference again. Detection needs one line, not a schema.

**Fix (local patch):** read the first non-empty line via `spark.read.text(paths[0]).limit(...)` (or plain storage ranged-read through the existing helpers) and check keys with `json.loads`; or at least pass `samplingRatio`. Keep the same fall-back-to-split exception discipline.

---

## 9. NIT — checkpoint field doc claims validation that does not exist; ignored config knob accepted silently

- `Recovery/FalconCheckpointState.cs` — `AidBatchSize` doc says "Validates continuity across resume", but nothing compares the checkpoint value to the resumed config anywhere; it is parsed, required (`FalconCheckpointDeserializer.cs:178-183` rejects when absent) and then unused. Either validate or soften the doc; requiring an unused field also means a hand-migrated checkpoint fails for no behavioral reason.
- `FalconCollectorConfigurationBuilder.cs:221-223` still parses `sortDescending` into `SpotlightSortDescending`, which the correlated flow ignores (documented in the record, invisible to the operator who set it). Consider logging a one-line "ignored knob" warning at build time.

---

## Observations (no action required)

- **Egress content hash** (`NdjsonContentHasher.cs`, both batch sessions, `ResultsBatchPublisher.cs`): clean. Hash is appended exactly once per flushed payload before publish; a failed flush tears the session down (no double-append path); `IncrementalHash.GetCurrentHash()` is non-resetting so repeated `ContentHashHex` reads are safe; `TryGetBuffer` + `stream.Length` is the correct no-copy read. The single/multipart-equivalence and determinism tests are exactly the right assertions. Only note: the digest's sole consumer is a log line, and it costs one full SHA-256 pass over every uploaded byte across all collectors — fine at current volumes, worth remembering if anyone questions egress CPU later.
- **Held-open vendor stream during chunk flushes** (`FalconSpotlightBatchScroller.cs:147-152`): a chunk-cap `yield` suspends inside the open Spotlight response stream; a memory-pressure multipart flush in the consumer can stall the read past the transport timeout. Mid-stream failures are not caught by the cursor-expired handler (request-time only) and abort the batch to the resilience layer — correct, just be aware the "re-anchor absorbs slow joins" story covers request-time expiry only.
- **Per-batch memory**: `seenFindingIds` is unbounded within a batch (250 aids × all findings); bounded by batch scope, acceptable. `FalconCorrelatedRecord.Build`'s `ToJsonString()`→`GetBytes` double-materializes each record (~2× a chunk's size transiently); the 2000-finding cap bounds it.
- **Duplicate-host replay is inherent** to watermark re-anchor semantics and existed in the split-lane assets flow too; finding 2 is about the *parser's* new hard assumption, not about eliminating replay.
- **Parser giant-row aggregation**: `correlate(..., embed_as="vulnerabilities")` re-assembles all of a host's chunks into one Spark row, deliberately undoing the collector's chunking; inherited shape from the split handler, fine until a pathological host (hundreds of thousands of findings) hits row-size limits.
- **Test quality**: the correlated adapter tests (re-anchor + boundary dedupe, seen-id overlap dedupe, chunk semantics, zero-finding hosts, checkpoint v2 round-trip and pre-v2 rejection) and the parser tests (multi-chunk, absent assets lane, detection routing both ways, status normalization) are well targeted. Missing cases worth adding alongside the fixes above: duplicate-aid input to the parser (finding 2), and a Spotlight mock that *asserts* the continuation filter is unchanged (finding 1).
- Untracked `.claude/` directory sits in the adapters working tree; confirm it is not intended for commit.

---

## Summary

| Severity | Count |
|---|---|
| Blocker | 0 |
| Major | 3 (findings 1, 2, 3) |
| Minor | 5 (findings 4–8) |
| Nit | 1 (finding 9, two items) |

All three Majors have small, local fixes; none requires reworking the architecture, which is sound.
