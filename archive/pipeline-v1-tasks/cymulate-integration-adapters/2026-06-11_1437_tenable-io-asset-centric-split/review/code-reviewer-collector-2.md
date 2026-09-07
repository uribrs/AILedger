# Code Review — TenableIo Collector (assets + findings two-feed split)

Reviewer: independent senior-engineer pass. Stack: C# / .NET 8.
Risk: **High** — production collector, persistence (checkpoint/resume), retries, large
streamed data, distributed re-invocation. Reviewed the diff plus surrounding collector code
(entry point, Flows/Assets, Flows/Findings, Recovery, Processing, Urls, Config).

## Summary verdict

The change is **functionally sound and safe to ship**. The new assets feed is a faithful mirror
of the existing, production-proven findings flow (same create→poll→chunk→publish→checkpoint
loop, same retry/exclusion semantics, same resumability). The removal of the synchronous
per-asset enrichment (`TenableIoAssetEnricher`, ~356 lines incl. a hand-rolled concurrency
limiter, in-flight dedup, global 429 cooldown, custom `ScalarValue` JSON writer) is a clear
net reduction in risk and complexity — that subsystem was the most dangerous part of the old
collector and is now gone. No blockers.

The dominant issue is **duplication**, not correctness: `TenableIoAssetsFlow` is a ~95% copy of
`TenableIoFindingsFlow`, and the checkpoint loader/saver now has three near-identical bodies.
That is a maintainability liability on a high-churn, high-risk path, but it is a conscious
mirror-the-reference choice and does not block merge.

---

## Findings (ranked)

### 1. Major — `TenableIoAssetsFlow` is a near-verbatim duplicate of `TenableIoFindingsFlow`
**Problem.** `ProcessChunksProgressivelyAsync`, `ProcessSingleChunkAsync`,
`PublishPageAndCheckpointAsync`, `GetChunkStreamWithRetryAsync`, `ToAsyncEnumerable`,
`TrackServerSideFailures`, `FindNewChunkIds`, `ComputeChunkRetryDelay`, `IsRetryableStreamFailure`,
`AddJitter`, both `Emit*` methods, and the entire poll-status state machine are duplicated
between the two flows. The only material differences are: the export client type, the publisher
method (`PublishAssetsUtf8PageAsync` vs `PublishFindingsUtf8PageAsync`), `findingsInBatch:`
(`effectiveRecordCount` vs `0`) on `AdvancePage`, the stats sink, and the log prefix string.
**Impact.** This is the collector's hot path and its retry/checkpoint correctness core. Two copies
means every future fix (a retry-classification tweak, a checkpoint-shape change, a throttling fix)
must be applied twice, and they *will* drift — the duplicated `processedTenableChunkIds` parse
block already appears 3× in `TenableIoCheckpointHelper`. On a High-risk path, drift here is
silent data loss or duplicate publication.
**Fix.** Extract the shared progressive-chunk engine into a generic base/helper in `Flows/Shared`
parameterized over: export client (interface), chunk processor, publisher delegate, `findingsInBatch`
selector, checkpoint-state factory, and log tag. The per-flow files shrink to the create-export call,
the checkpoint mapping, and the `AdvancePage` semantics. **Refactor, deferrable** — not required for
this merge, but should be the next task before a third Tenable feed or any retry-logic change lands.

### 2. Major — checkpoint (de)serialization triplicated in `TenableIoCheckpointHelper`
**Problem.** `SaveFindingsState`/`SaveAssetsState` are identical except the `flow` value, and
`TryLoadFindingsStateCore`/`TryLoadAssetsStateCore` are identical except `IsFindingsFlow` vs
`IsAssetsFlow` and the legacy `chunkId` fallback (findings only). The CSV chunk-id parse appears
three times in the file.
**Impact.** Same drift risk as #1, on the persistence boundary. A future field added to one
state and not the other resumes silently wrong.
**Fix.** Single private `TryLoadCommonCore` returning the shared fields, plus a thin per-flow
wrapper that validates the flow tag and constructs the concrete record; one private
`ParseChunkIds(string)` helper. **Local refactor.**

### 3. Minor — `findingsInBatch` on the assets `AdvancePage` is correct; confirm the SDK contract
**Observation/likely-OK.** `TenableIoAssetsFlow.PublishPageAndCheckpointAsync` calls
`AdvancePage(itemsInBatch: effectiveRecordCount, findingsInBatch: 0)` while findings passes
`findingsInBatch: effectiveRecordCount`. This is the intended asset-vs-finding distinction and
matches the `TotalFindings = 0` / `TotalAssets = …` split in the assets checkpoint. No action,
noted only because it is the single most behavior-significant divergence between the two flows and
is easy to get wrong if #1 is later refactored into a shared engine — preserve it as an explicit
parameter, not a hardcoded constant.

### 4. Minor — two `TenableIoFlowExceptionClassifier` types with the same name in sibling namespaces
**Problem.** `Processing.TenableIoFlowExceptionClassifier` (real classifier) and
`Processing.Resilience.TenableIoFlowExceptionClassifier` (a stub that always returns `null`)
coexist. The Resilience one is dead-weight indirection — its own comment says the real mappings
live in the Processing one.
**Impact.** Name collision invites a wrong `using` and confusion about which classifier is
authoritative; the stub adds no value.
**Fix.** Delete the Resilience stub (and its `using` if unused) or rename it to something that
signals "no-op baseline". **Local patch.** (Pre-existing, but the rework touches this area.)

### 5. Minor — assets `IsTransientOrRateLimited` delegates cross-flow to the findings client
**Observation.** `TenableIoAssetsExportClient.IsTransientOrRateLimited` forwards to
`Findings.TenableIoVulnsExportClient.IsTransientOrRateLimited`. Functionally fine and avoids a
fourth copy, but it couples the assets client to the findings namespace for a 4-line status check.
If the shared engine in #1 is built, lift this predicate into `Flows/Shared` instead. **Defer.**

### 6. Observation — findings behavior changes are deliberate, flag for product sign-off
Two semantic changes ride along in the findings path: (a) `filters.state=[OPEN,REOPENED]` now
excludes FIXED/closed vulns at the API; (b) the `info`-severity client-side filter was removed, so
info findings are now emitted (the `_filteredInfoVulnerabilities` stat and `IsInfoSeverity` are
gone). Both look intentional given the "dumb passthrough, mapping is the parser's job" comment, and
the code is correct. No code risk — calling out only so the downstream mapper/parser is known to
handle info-severity rows and the closed-vuln drop is expected product behavior.

### 7. Observation — checkpoint backward-compat is handled correctly
The findings loader keeps the legacy `chunkId` → `lastPublishedPage` fallback and the empty-set
default for `processedTenableChunkIds`; the dropped `enableAssetEnrichment`/
`maxParallelAssetEnrichmentRequests` keys are simply no longer read, so in-flight old checkpoints
still load. `IsAssetsFlow`/`IsFindingsFlow` accept both the legacy literal and the canonical
topic name. Resume cannot pick up an assets checkpoint as findings (flow-tag validated in both
loaders). Good.

## Things checked and found clean
- **Stream/resource lifecycle:** chunk streams are `await using`; `JsonDocument`/owned elements
  disposed via `using (owned)`; the deleted enricher's `MemoryCache`/`SemaphoreSlim` disposal is
  moot now. The reusable `ArrayBufferWriter` is safe given sequential consumption (documented).
- **Cancellation:** `OperationCanceledException` is rethrown before the retryable catch in both the
  poll loop and chunk-retry loops; token threaded through publish/enumerate. No swallowed cancel.
- **Retry/backoff:** transient vs circuit-breaker vs non-retryable correctly separated; jittered
  backoff; cumulative per-chunk transport budget (`MaxChunkRetryAttempts * 3`) before permanent
  exclude; `Retry-After` honored; 404 → fresh-collection InvalidOperationException; >50% permanent
  (non-transport) chunk failure aborts. Sound.
- **Resume time math:** `ResolveBaseDateUtcOrDefault` floors to now-365d, so the assets
  `last_assessed = base − 30d` Unix conversion can never go negative/pre-epoch.
- **Data structures:** `HashSet<int>` for processed/excluded/server-side/data-failed sets and
  `Dictionary<int,int>` for retry counts are the right choices; no O(n²) scans; chunks streamed,
  not materialized; per-page byte cap respected before publish.
- **No `.Result`/`.Wait()`, consistent `ConfigureAwait(false)`, no unbounded `Task.WhenAll`** (the
  only `WhenAll`, in the enricher, was deleted).
