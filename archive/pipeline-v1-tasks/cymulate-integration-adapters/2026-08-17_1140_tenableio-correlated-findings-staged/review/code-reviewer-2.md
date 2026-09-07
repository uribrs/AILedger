# Code review — TenableIo correlated findings, phases 2 and 3

**Scope reviewed:** `Flows/Findings/Correlated/` (vuln phase, zero-vuln sweep, flow composition, publisher,
record writer, checkpoint, stats, bucketer, buckets, ceiling, `CorrelatedRecord`), `Recovery/` checkpoint
format changes, `TenableIoCorrelatedFlowTests.cs`. Read for context: `TenableIoAssetSpine`,
`TenableIoVulnsExportClient`, `TenableIoChunkRetry`, `TenableIoExportPollHelpers`, both
`TenableIoFlowExceptionClassifier`s, `TenableIoCollectorConfiguration(+Builder)`, the passthrough
`TenableIoFindingsFlow`, and in IntegrationInfra: `Emission/NdjsonBatchEmitter`,
`Ingestion/GuardedObjectStore`, `Ingestion/IngestionOptions`, `FaultGovernance/Logic/*`,
`FaultGovernance/Policies/UnknownFlowFailurePolicy`, `Conducting/Bus/Logic/AdapterBus*FlowExecutor`.

**Classification:** feature logic on a persistence + distributed-recovery boundary, large data volumes.
**Risk: High.** Reviewed at high depth (failure semantics, idempotency, scale, concurrency).

**Verification method.** Findings 1, 2, 5 and 6 were **confirmed by execution** with throwaway probe tests
added to `TenableIoCorrelatedFlowTests.cs`, run via
`dotnet vstest artifacts/bin/ut/…TenableIoCollector.Test.dll --TestCaseFilter:"FullyQualifiedName~PROBE_"`.
All probes have been removed and the file is back to its original 1038 lines; the suite is green
(**28/28 `TenableIoCorrelatedFlowTests`**). The remaining findings are reasoned from source and are marked
as such.

Overall: the phase code is unusually well reasoned, and several defect classes this codebase has been
bitten by before are demonstrably fixed here (exception filters do not mutate or log; caller cancellation is
distinguished from `Parallel.ForEachAsync`'s internal cancellation; the ceiling counts never-offered chunks
and is raised as a *type*; publish→checkpoint has nothing awaitable between them). The findings below are
about the three places where a *degraded* outcome is silently converted into a *positive false statement*,
and about one production config value that contradicts the memory model the code documents.

---

## 1. Blocker — a lost vulnerability chunk is republished as "this asset has zero findings"

**Confirmed by execution.**

`TenableIoVulnPhase.TryProcessChunkAsync` may abandon a chunk (`return false` → `pass.Excluded.Add`), and
`TenableIoSkippedChunkCeiling` tolerates up to 50 % of chunks going missing
(`TenableIoSkippedChunkCeiling.cs:24`). An abandoned chunk's assets are therefore never added to
`pass.Claimed` (`TenableIoVulnPhase.cs:301` only runs on a successful publish). Phase 3 then treats
"unclaimed" as "has no findings" and emits each of those assets as a host-bearing envelope with
`findingsInChunk: 0, chunk: 0, isLastChunk: true` (`TenableIoZeroVulnSweep.cs:125-135`).

So the flow does not merely *lose* the chunk's findings — it publishes an affirmative statement that those
assets are clean. `TenableIoZeroVulnSweep`'s own doc comment ("Only when the export is finished is 'no chunk
claimed it' the same statement as 'it has no findings'") is false whenever any chunk was excluded.

Probe (4 chunks, chunk 2 answered `500` once, asset `b`'s two findings live only in chunk 2): the run
completed successfully, `TotalFindingsCollected == 3`, and asset `b` was published as a single envelope with
`findingsInChunk == 0`, `isLastChunk == true`, host present. One failed chunk out of four is under the
ceiling, so nothing failed the run.

**Impact.** At reference scale a single lost chunk is ~500 assets (see finding 3) declared vulnerability-free
in a lane the parser treats as authoritative per asset. This is worse than dropping the assets: an
empty-findings envelope is a positive assertion, so it can retire live findings downstream. It is also
invisible — one `LogWarning` inside a successful run.

**Recommended fix.** The sweep may only run when phase 2 accounted for *every* chunk. Either
(a) make any excluded/unaccounted chunk fail the flow for the correlated shape (i.e. `MaxSkippedChunkRatio`
is not a meaningful tolerance once an unclaimed asset means "clean"), or (b) keep the tolerance but skip the
sweep entirely — and say so in the log and in the result payload — whenever `Excluded.Count + unaccounted > 0`,
accepting that findings-less assets are missing rather than misreported that run. (a) is the smaller and
more honest change: the correlated flow's output is per-asset complete or it is wrong.
**Local patch**, in `TenableIoCorrelatedFindingsFlow.CollectAsync` / `EnforceSkippedChunkCeiling`.

Note the same hazard on the phase-1 side: an assets chunk lost during the spool leaves its assets out of the
spine, which the vuln phase turns into thin hosts (benign) but which also makes *findings-less* assets from
that chunk invisible. That direction only loses data, so it is a lesser problem.

---

## 2. Blocker/Major — a resumed leg re-emits every already-published asset as a second chunk-0 host envelope

**Confirmed by execution.**

`TenableIoCorrelatedFindingsFlow.cs:78-79` seeds `processedChunkIds` from the checkpoint but starts
`claimedAssetIds` empty. Chunks already published are skipped, so their assets are never claimed, so the
sweep emits all of them again — `chunk: 0`, `isLastChunk: true`, `findingsInChunk: 0`, host present.

Probe (resume with `ProcessedTenableChunkIds = [1]`, asset-a's findings in chunk 1): the resumed leg
published exactly one envelope for asset-a, and it was the zero-findings sweep envelope. `TotalAssetsCollected`
came out at `3 + 2` — asset-a counted a second time.

This collides with the object the earlier leg already published for asset-a, which used `chunk: 0` for its
*first* findings slice. Two records for one uuid now share `chunk == 0` and disagree on `isLastChunk` and
`findingsInChunk`. Whether this degrades or corrupts depends entirely on the parser's aggregation rule,
which I cannot see from this repo: a union-by-uuid parser absorbs it; a parser that keys on
`(uuid, chunk)`, or that trusts `isLastChunk`/`findingsInChunk` for completeness, drops the real findings.
`TenableIoZeroVulnSweep`'s doc comment describes the tradeoff as "duplicates carry no findings and the
parser aggregates per uuid" — that understates it, because the duplicate is not an extra record, it is a
*conflicting* one at the same key.

The same collision has two other roads to it, which is why I would not leave it to the parser:
- the vendor does not promise a chunk is asset-complete, and a straddling asset produces two chunk-0
  host-bearing envelopes with overlapping `chunk` numbers (`TenableIoVulnPhase.cs:30-36` acknowledges this);
- finding 4 below re-publishes page 1 onwards on an in-invocation flow retry.

**Impact.** Systematic on every resume, not a corner case: resuming at 50 % of a 100 K-asset tenant re-emits
~50 K conflicting envelopes and inflates the reported asset total by the same amount.

**Recommended fix.** Make claims durable and cheap: after building a chunk's buckets and *before* publishing
it, write one small staging object `…/claims/{chunkId}` listing that chunk's uuids; on resume, load the claim
objects whose chunk id is in the processed set. Writing before the publish is safe — a claim file for a chunk
that never published is ignored because its id is not in the processed set — and it does not insert anything
awaitable between publish and checkpoint. The claims die with the spine in `DeleteSpineAsync`. Cost is one
small write per chunk (hundreds, not millions).
Second, independently: get the parser's aggregation rule written down as a contract and asserted, because
three separate mechanisms here depend on it.
**Local patch** (spine + phase 2 + flow), no refactor needed.

---

## 3. Major — production requests 500 assets per chunk while the code is designed and documented for 50

**Reasoned from source (config + client + bucketer), high confidence.**

`TenableIoCollectorConfiguration.NumAssetsPerChunk` defaults to **500**
(`Processing/Configuration/TenableIoCollectorConfiguration.cs:36`) and
`TenableIoVulnsExportClient.CreateExportAsync` sends it verbatim. The new correlated code assumes 50
throughout:
- `TenableIoVulnChunkBuckets` — "at `num_assets=50` a chunk measured 10–43 MB (p50 22 MB)";
- `TenableIoVulnPhase.SpineLookupConcurrency` — "At `num_assets=50` this is ~50 lookups per chunk";
- the unit tests pin `NumAssetsPerChunk = 50` (`TenableIoCorrelatedFlowTests.cs:668`), so nothing exercises
  the shipped value.

That default was harmless for the passthrough flow, which streamed chunks. This flow buffers a whole chunk
**twice**: `TenableIoVulnChunkBuckets` holds every finding's bytes plus the first embedded asset per uuid,
and `BuildChunkRecordsAsync` then materializes `List<CorrelatedRecord>` where each envelope is a fresh
`ToArray()` copy of the same bytes (`TenableIoCorrelatedRecordWriter.cs:69`). Scaling the documented
measurement by 10 gives ~100–430 MB per chunk resident, ~200–860 MB across the two copies, before the
emitter's part buffers and the transient `JsonDocument` per record. `numAssetsPerChunk` is operator-settable
up to 5000, with no ceiling on chunk bytes anywhere in the new path.

**Impact.** Heap pressure of exactly the kind Emission's four-tier defense exists to survive, on the flow's
hot path, at a magnitude nobody has measured. Under memory pressure `MaxConcurrentReads` drops to 1 and the
buffered-read cap tightens, which also makes finding 7 live.

**Recommended fix.** Two independent steps, both small:
1. Pin the correlated flow's export to the size the design was measured at (50, or an explicit constant on
   the collector's configuration) rather than inheriting the passthrough default, and test at the shipped
   value.
2. Drop the second full copy: build envelopes lazily inside the `IAsyncEnumerable` handed to
   `PublishBatchAsync` instead of materializing `List<CorrelatedRecord>`. `ToAsyncEnumerable` then
   disappears, and the empty-chunk guard is unaffected — `records.Count == 0` is exactly
   `buckets.FindingsByUuid.Count == 0`, since every bucketed uuid has at least one finding and therefore at
   least one slice. The shared `_envelopeBuffer` is already safe for this because `Build` copies out.
**Local patch.**

---

## 4. Major — an unclassified mid-flow failure restarts page numbering at 1 and overwrites the abandoned attempt's batch folders

**Reasoned from source; the mechanism is spread over four files, so worth confirming with a probe before fixing.**

`TenableIoCollector` supplies `FlowRetryPipelineBuilder = UnknownFlowRetryPolicy.CreatePipeline`
(`TenableIoCollector.cs:428`). `AdapterBusFlowExecutor` wraps the whole flow delegate in that Polly pipeline,
and `UnknownFlowRetryClassification.IsUnknownRetryCandidate` returns true for any exception that is not
cancellation, `HttpRequestException`, `TimeoutException`, or transport-classified. Failures the vendor
classifier *does* map (the ceiling exception, the 404) are converted to a `PublishFailure` decision inside
`AdapterBusStrategyFlowExecutor` and never reach Polly. Everything else — export status `FAILED`/`CANCELLED`
(`TenableIoExportPollHelpers.ThrowIfExportFailed`), `"did not return export_uuid"`, a `DataPipelineException`
from a spine read, a `JsonException` escaping the bucketer, the spine's own `InvalidOperationException`s —
is rethrown by `RethrowForUnknownRetry` and retried up to **three more times, in the same invocation**.

Each retry calls `CollectFindingsInternalAsync` again, which constructs a fresh
`TenableIoCorrelatedFindingsFlow`: `resumeState` is null, so `RestoreCounters` returns 0 and
`processedChunkIds` is empty, so the retry publishes pages 1, 2, 3… again. The progress context is the
*same* instance across attempts and still carries attempt 1's checkpoint state, but nothing reads it. With
batch-scoped storage, page N of attempt 2 overwrites `batch_{N:D6}/findings_{N:D6}.json` from attempt 1 —
with a different chunk's content, since chunk ids are export-relative and the retry may create a new export.
Any higher-numbered folders attempt 1 reached survive as orphans and stay announced for incremental parsing.

**Impact.** Mixed output across two attempts under one set of page numbers, plus stale batch folders from a
discarded attempt that upstream may already have consumed. The pattern predates this work (the passthrough
flow restarts numbering too), but it matters more now: a page is one atomic per-chunk object, folders are
announced for incremental parse, and each envelope is a whole-asset assertion.

**Recommended fix.** Make the flow read its position from the progress context when `resumeState` is null:
if `progressContext` already carries this flow's format version and `LastPublishedPage`/processed-chunk
state, seed `batchNumber` and `processedChunkIds` from it. That makes an in-invocation retry behave like the
resume it actually is, with no new persistence and no change to the checkpoint contract. (It also needs
finding 2's claim ledger to avoid re-sweeping.)
**Local patch** in `TenableIoCorrelatedFindingsFlow.RestoreCounters`.

---

## 5. Major — every checkpoint's totals exclude the batch it was written for

**Confirmed by execution.**

`TenableIoCorrelatedBatchPublisher.PublishBatchAsync` builds the checkpoint at line 85 and folds the batch's
counts into `_stats` at line 93. `TenableIoCorrelatedCheckpoint.Build` reads `stats.FindingsEmitted` /
`AssetsEmitted` / `MissCount`, so the persisted totals always lag by exactly the batch whose object that
checkpoint covers. `RestoreCounters` seeds the next leg from those values, so each resume permanently drops
one batch from the run's reported totals; `CollectAsync` returns `_stats.FindingsEmitted`, which is what
lands in the DONE payload.

Probe: a run that emitted 2 assets (one chunk + one swept) persisted `totalAssets = "1"` while
`flow.TotalAssetsCollected == 2`.

**Impact.** Not data loss — reporting loss, compounding per resume, in exactly the numbers used for
adapter↔vendor parity checks. A resumed long run silently under-reports.

**Recommended fix.** Move `_stats.AddBatch(...)` above `buildCheckpoint(batchNumber)`. The counts are final
the moment the upload returns, `AddBatch` is not awaitable, and the "nothing between publish and checkpoint"
invariant is about awaitable/cancellable work, so this does not weaken it. **Local patch, one line moved.**

---

## 6. Major — a `500` on a chunk download is not retried by the flow; the chunk is discarded after one attempt

**Confirmed by execution.**

`TenableIoVulnsExportClient.IsTransientOrRateLimited` covers only `429`, `502`, `503`, `504`. A `500` (or
`408`) on `GET /vulns/export/{uuid}/chunks/{id}` matches neither that branch nor
`TenableIoChunkRetry.IsRetryableStreamFailure` (transport classifier: socket errors and message markers
only), so it lands in the terminal `catch (Exception ex)` at `TenableIoVulnPhase.cs:243` and the chunk is
abandoned on attempt 1 — then feeds finding 1.

Probe (chunk answered `500` once, no session pipeline in the harness): `VulnChunkAttempts[1] == 1`, and the
run failed only because a single-chunk export trips the ceiling. At 200 chunks it would have been silent.

In production the session pipeline does retry `500` — `TenableIoCollectorConfigurationBuilder` adds
`HttpStatusCode.InternalServerError` to `RetryStatusCodes` — so the residual gap is the asymmetry after
Polly exhausts: a `502` still gets `MaxChunkRetryAttempts` (5) further attempts with backoff, a `500` gets
none. The comment at `TenableIoVulnPhase.cs:230-232` gives the argument for why that is wrong ("without this
branch one rate-limit response would discard a whole chunk of findings while the run still reported
success"); it applies unchanged to a `500`.

**Recommended fix.** Add `InternalServerError` and `RequestTimeout` to `IsTransientOrRateLimited`. Note it is
shared with the assets export client and the passthrough flows, which is fine — the same argument holds
there — but it is a behavior change beyond this folder, so call it out.

---

## 7. Major — a spine read that throws discards the chunk's findings, with no retry and no fallback to the miss lane

**Reasoned from source.**

`TenableIoAssetSpine.TryReadAsync` passes `maxBytes: null`, so `GuardedObjectStore.ReadAllBytesAsync`
enforces `MaxInMemoryObjectBytes` (8 MiB) — tightened to the control-artifact size under memory pressure,
which the spine's own remarks document. An asset record over the effective cap throws
`DataPipelineException`; the exception escapes `Parallel.ForEachAsync` in `ResolveHostsAsync`, fails the
chunk, is not transport-classified, so it is not retried and the chunk is discarded (→ finding 1).

`TenableIoVulnPhase`'s doc states the principle "a finding is never lost to a missing host", and then
resolves this case by losing every finding in the chunk because of a host. Retrying cannot help either: an
oversized record fails identically on all 5 attempts, so the retry budget is spent on a deterministic
failure.

**Recommended fix.** Treat a *deterministic* read refusal (`DataPipelineException` — object too large) as a
miss: thin host, `IsMiss = true`, counted. Keep genuine store faults (unreachable, auth, throttled) failing
the chunk, which is the distinction the class comment already draws between "absence is a value" and "an
unreachable store is not". **Local patch** in `ResolveHostsAsync`.

---

## 8. Minor — the 404 mid-pass path still relies on substring classification

`TenableIoVulnPhase.cs:152` raises `InvalidOperationException("Tenable.io vulns export not found …")` and
depends on `TenableIoFlowExceptionClassifier` matching `"export not found"` to stay retryable. That is the
exact coupling `TenableIoExportIncompleteException` was introduced to eliminate, and the classifier's own
comment frames the substring rules as legacy kept for "a message-raising site that has not been converted
yet" — while new code deliberately depends on them. Rewording that sentence silently turns a
"schedule a fresh collection" into a fall-through. Give it a type (`TenableIoExportGoneException` or a
reason enum on the existing type), same as the ceiling. **Local patch.**

## 9. Minor — export status `FAILED`/`CANCELLED` is unclassified

`TenableIoExportPollHelpers.ThrowIfExportFailed` raises a bare `InvalidOperationException`. It does not
publish a terminal non-retryable failure (it rethrows via `RethrowForUnknownRetry`, so host redelivery
semantics survive), but it does trigger the whole-flow retry of finding 4 — three full re-passes at
30/60/120 s, each creating/reusing an export and re-publishing page 1 onwards — for a condition whose only
real recovery is a fresh collection later. Classify it like the others and let the strategy publish a
retryable failure instead.

## 10. Observation — the sweep can advance a page for an object that was never written

If every unclaimed asset vanishes between `ListStagedAssetIdsAsync` and its read
(`TenableIoZeroVulnSweep.cs:114-122`), the batch produces 0 records; the emitter releases the batch scope
from its `finally`, but the sweep still writes a checkpoint with `LastPublishedPage = batchNumber` and calls
`AdvancePage`, and logs "published as batch N". Vanishingly unlikely and harmless beyond a gap in page
numbering, but the log line is misleading when it matters most.

## 11. Observation — `TotalUniqueVulnerabilities` now carries the miss count

Documented in three places and deliberate, so no action. Worth knowing that the DONE payload field keeps its
old name with a new meaning; anything downstream that charted it will be reading misses.

---

## What is right, and worth not regressing

- **Publish → checkpoint ordering.** `PublishBatchAsync` has nothing awaitable between the upload returning
  and `SetState`/`AdvancePage`, and the chunk joins the processed set through the same write
  (`processedIncludingThisChunk` is computed before the publish and passed into the checkpoint builder). The
  test that injects a publish failure and asserts no checkpoint state is the right test.
- **Exception filters are clean.** `TryPollStatusAsync` counts and logs in the handler body, never in the
  filter, with the reason written down.
- **`OperationCanceledException` is filtered on the caller's token**, so `Parallel.ForEachAsync`'s internal
  cancellation is treated as the chunk failure it is.
- **The ceiling is a type, not a message**, and it counts never-offered chunks as well as failed ones —
  both defect classes closed, and asserted on the type in the tests.
- **Spine layout and fan-out sizing.** One object per asset keyed by asset id, `SpineLookupConcurrency`
  sized to `MaxConcurrentReads` rather than to chunk width, and the sweep drains `ListAsync` into a list of
  ids *before* reading any of them — so no read slot is ever held across a yield to caller code, which is
  the nesting hazard `GuardedObjectStore` warns about.
- **Batch scoping is left entirely to the emitter**; nothing in the collector touches `BatchScopedStorage`
  except the read-only base-URL resolve, and the spine captures that base once, before any page scope
  applies.
- **`MaxBytesPerBatch` is not used as an object cap** anywhere; one publish call is one object.
