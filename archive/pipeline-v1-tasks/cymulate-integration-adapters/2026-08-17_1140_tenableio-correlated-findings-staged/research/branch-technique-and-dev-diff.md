# Prior branch technique + dev-side diff

Research carried in at contract time (2026-08-17) by direct read of both trees. Sources are named per
claim; nothing here is inferred from memory.

Refs:
- prior work: `origin/feature/tenableio-correlated-findings` @ `999289e0`
- its merge base with dev: `c3c09ca3`
- this task's base: `origin/dev` @ `1f7a2ba6`

---

## 1. What the prior branch does (the technique to re-implement)

`CollectFindings` stops publishing two independent passthrough lanes (`assets_*.json` +
`findings_*.json`) for the parser to join in Spark. It emits self-complete correlated NDJSON
envelopes to a single findings lane:

```
{ uuid, chunk, isLastChunk, findingsInChunk, host, findings[] }
```

- `host` = the full `/assets/export` record **verbatim**, on chunk 0 only; OMITTED (not null) on
  chunks 1..N, on overflow assets' findings, and on marker-set findings.
- `findings` = raw `/vulns/export` records, byte-identical passthrough.
- 2,000-findings cap per envelope; a densest-asset slice spans multiple chunks.
- Zero-vuln assets still emit one empty envelope (`isLastChunk: true`).
- Standalone `CollectAssets` is unchanged.
- Record grammar deliberately identical to Falcon's correlated collector (`aid` → `uuid`).

### Three phases, both exports created up front

Both exports are created before either is consumed, so Tenable prepares them concurrently
server-side (10 concurrent exports per container).

| Phase | File | What it does |
|---|---|---|
| 1 — Spool | `TenableIoAssetSpoolPhase.cs` (264 L) | Streams `/assets/export` (`chunk_size=1000`, `filters.last_assessed>=base`) into `InMemoryGzipAssetSpool`: `uuid → gzip-compressed raw UTF-8 bytes`, never a parsed DOM. Hard compressed budget (1 GiB default, `spoolBudgetBytes`, bounds 1 B – 8 GiB) behind the `IAssetSpool` seam. Overflow degrades deterministically: the asset's chunk-0 host record is written to a streaming sink and its uuid joins `overflowMarkers`. No checkpoint is written while chunks stream. |
| 2 — Emit | `TenableIoVulnPhase.cs` (357 L) | Streams `/vulns/export` (`since>=base`, `state=[OPEN,REOPENED]`, `num_assets` **500 → 50**). Per chunk: bucket findings by `asset.uuid` (`TenableIoVulnChunkBucketer`), resolve host, slice at 2,000, publish the whole chunk as ONE atomic object, then checkpoint. |
| 3 — Sweep | `TenableIoFindingsFlow.SweepZeroVulnAsync` | Lazily drains the spool residue (`DrainRemaining()`) into ONE atomic object of empty-findings envelopes. Runs only after every vuln chunk completes — asset-partitioning makes absence global. |

### Host resolution (the correlation core)

`TenableIoVulnPhase.BuildRecordsForChunk` — three-way, per uuid, per chunk:

1. **spool hit** → hydrated host, remove-on-use, `startChunk=0`, host-bearing.
2. **overflow marker** → host-less, `startChunk=1` (host already emitted during spooling).
3. **miss** → **thin host** synthesized from the finding's own embedded `asset` sub-object,
   `startChunk=0`, counted in the miss lane. Never dropped, never buffered.

Correlation is **chunk-local**, and that is only sound because an asset's findings live entirely
within one vulns-export chunk (A1 below). A straddle degrades gracefully to a second thin chunk-0
host on the later chunk, absorbed by parser per-uuid aggregation.

### Recovery — checkpoint format v2

- Persists BOTH export uuids (`exportUuid` + new `assetsExportUuid`), `processedTenableChunkIds`,
  and a batch counter, plus `findingsFormatVersion: "2"`.
- A chunk joins `processedTenableChunkIds` on its single object's checkpoint, written **after** the
  upload succeeds — so a chunk is durably processed only once its object exists. Crash replay is
  bounded to the in-flight chunk.
- Old two-lane checkpoints are **refused**: missing/mismatched `findingsFormatVersion`, and any
  `CombinedRun` assets checkpoint. Both fall through to a fresh collection.
- Removed on that branch: `LegacyChunkId` ("chunkId") back-compat, `AssetsPhaseComplete`,
  `LegacyPhase`/`FindingsInProgress`, the phase-transition checkpoint, and the combined-run chaining
  in `ResumeAssetsAsync`.
- **Resume re-spools the FULL assets export.** A claimed-set rebuild (uuid-only re-scan of processed
  vuln chunks) was implemented and then deleted in code review: no uuid projection exists, so it
  re-downloaded every processed chunk's full bytes, making resume cost grow with progress and risking
  livelock against the 24 h export expiry. Duplicates on resume (sweep empties, in-flight chunk
  replay, overflow re-publish) are pushed onto parser-side per-uuid replay-idempotent aggregation.

### Egress adjustment already made on the branch (`999289e0`)

The branch was brought onto the shared egress law after a dev merge:

- collector-side byte-budget pagination **deleted** — one vendor chunk = one object.
- the `isFinalPage` checkpoint seam removed (dead with one object per chunk).
- overflow streams into ONE announced object via `TenableIoOverflowChannelPublisher` (bounded
  `Channel<T>`, publish opened lazily on the first overflow record, completed at spool end).
- sweep streams lazily into one object; peak collector RAM = one record.

### Batch-scoped storage on the branch (`c8556f46`)

`BatchScopedStorage = true` capability constant on `TenableIoCollectorConfiguration`;
`TenableIoCorrelatedPagePublisher` calls `BatchScopedStorage.BeginPage` before each upload; the
standalone assets lane scopes up front guarded on `recordCount > 0`.

Left open on the branch: spool-phase overflow pages published without checkpoint/AdvancePage, so
their batch folders were never individually announced. The egress-law commit closed this by giving
overflow a single announced object with a checkpoint.

### Validation on the branch (largest tenant, run 20260706-102603, 3-day base date)

- 31,536 envelopes = 31,303 spooled (31,160 hydrated + 143 swept) + 233 thin-host misses
- 2,761,297 findings; **0 drops**; 2,168 pages / 17 GB
- spool peak **54.4 MB compressed** (5.1 % of the 1 GiB budget), ~1.74 KB/asset; process RSS ~215 MB
- overflow never engaged
- 41/41 tests green (vstest); solution build 0 warnings / 0 errors

### Known accepted behaviours on the branch

- at-least-once on resume: duplicate empty envelopes + at most one replayed chunk
- a zero-vuln asset that overflowed the budget never emits `isLastChunk: true` (chunk-0 host only)
- `totalUniqueVulnerabilities` wire key repurposed to carry the miss count (name kept)
- since-semantics: on daily-scanned tenants the vulns export returns the full open backlog regardless
  of base date (`last_found`-based) — pre-existing vendor behaviour, identical in the old flow

### Files added on the branch (all under `Flows/Findings/Correlated/`)

`CorrelatedRecord.cs` (14) · `IAssetSpool.cs` (41) · `InMemoryGzipAssetSpool.cs` (89) ·
`TenableIoAssetSpoolPhase.cs` (264) · `TenableIoAssetSpoolReader.cs` (59) ·
`TenableIoChunkRetry.cs` (28) · `TenableIoCorrelatedCheckpoint.cs` (40) ·
`TenableIoCorrelatedPagePublisher.cs` (183) · `TenableIoCorrelatedRecordWriter.cs` (61) ·
`TenableIoExportPollHelpers.cs` (38) · `TenableIoOverflowChannelPublisher.cs` (95) ·
`TenableIoVulnChunkBucketer.cs` (85) · `TenableIoVulnChunkBuckets.cs` (33) ·
`TenableIoVulnPhase.cs` (357). Plus `TenableIoFindingsFlow.cs` rewritten (783 L of diff),
`TenableIoFindingsChunkProcessor.cs` deleted (120), 4 test files (565 L), 6 doc files.

---

## 2. Where dev is now

**TenableIo on dev is functionally the branch's merge base.** Every dev commit touching the collector
since `c3c09ca3` is substrate migration, not behaviour:

`git log origin/dev -- Collectors/TenableIoCollector/` → `0e57f4a7` LocalAdapterRunner one-dir-per-run,
`a375d8c3` retire the SDK concept, `227a387e` adopt IntegrationInfra, `a1401e40` restore dry-run.
Diff `c3c09ca3..origin/dev` over that directory is +121/−90 across 19 files, all renames/rewires.

So dev still has: the two-lane combined run, `NumAssetsPerChunk = 500`, no `BatchScopedStorage`, and
a findings flow that still paginates each chunk by `MaxBytesPerBatch` (`TenableIoFindingsFlow.cs:419`).
On the last point the prior branch is **ahead** of dev.

---

## 3. The technical diff

### 3a. Mechanical (rename-level) — evidence: `git diff c3c09ca3 origin/dev` over the collector

| Prior branch | Current dev |
|---|---|
| `Cymulate.Integration.Adapters.Shared.DataPipeline.Json` | `Cymulate.IntegrationInfra.Kernel.Json` |
| `…Shared.DataPipeline.Egress` | `Cymulate.IntegrationInfra.Emission` |
| `…Shared.Session` | `Cymulate.IntegrationInfra.Conversation` |
| `…Shared.Session.TransportErrorHandling` | `…Kernel.Transport` / `…Kernel.Exceptions` / `…FaultGovernance` |
| `…Shared.Glossary` | `Cymulate.IntegrationInfra.Job` + `Cymulate.Integration.Adapters.Catalog` |
| `…Shared.Events` | `…Envelopes.Common` / `…Reporting` |
| `…Shared.Orchestration` | `…Conducting` (+ `…Conducting.Collectors.Recovery`) |
| `…Shared.Recovery` | `…FaultGovernance.Recovery` |
| `…Shared.Time` | `…Kernel.Time` |
| `Cymulate.Integration.Sdk.*` | `Cymulate.Integration.Client.*` |
| `CollectorNdjsonPublisher.PublishFindingsUtf8PageAsync` (static) | `NdjsonBatchEmitter.Create(ctx.Services).PublishFindingsUtf8PageAsync` (instance) |
| `ICollectorEventSink` | `IAdapterEventSink` |
| `IAssetsCollectorAdapter` / `IFindingsCollectorAdapter` | `IAssetsAdapter` / `IFindingsAdapter` |
| `CollectorOutputDefaults` | `AdapterOutputDefaults` |
| `CollectorInProcEventHub` | `AdapterInProcEventHub` |
| `CollectorTriggerParsing` / `CollectorRunActionParser` / `CollectorRunAction` | `AdapterTriggerParsing` / `AdapterRunActionParser` / `AdapterRunAction` |

Surviving unchanged and reusable: `TopLevelJsonStreamArrayReader`, `NormalizedUtf8Json`
(now with a reusable-buffer overload: `SerializeToSingleLine(item, buffer)`),
`HttpTransportFailureClassifier`, `AdapterHttpRequestFailedException`, `ThrottlingOptions`.

Pinned substrate: `Cymulate.IntegrationInfra` **1.2.0-preview.0**
(`src/Cymulate.Integration.Adapters/Directory.Packages.props:123`).

### 3b. Architectural — the branch's inventions now have substrate answers

**(i) `BatchScopedStorage` is emitter-owned.**
`IntegrationInfra/Emission/README.md`: the opt-in is "one boolean and zero publish call sites" —
`NdjsonBatchEmitter.Create(services, batchScopedStorage: true)`. The emitter owns scope → publish →
release-unless-earned → `AdvancePage`, and it now releases the scope on a **throwing** publish as
well as on a 0-record dud page (a deliberate divergence from the old `Shared` implementation, which
released only on duds). "A collector never names `BatchScopedStorage`."
Exemplar: `QualysCollector/Flows/Findings/QualysFindingsBatchPublisher.cs:33`.
Still hand-rolling it on dev (older style): Falcon (`FalconFindingsFlow.cs:283-298`) and
InsightVmCloud page publishers.
⇒ the branch's manual `BeginPage` in `TenableIoCorrelatedPagePublisher` is obsolete.

Two limits stay with the caller: page N+1 must not publish before page N's `AdvancePage`, and scoped
publishes on one `AdapterProgressContext` must be sequential.

**(ii) There is now a real staging façade — `IntegrationInfra.Ingestion`.**
Did not exist when the RAM spool was designed. Per `Ingestion/README.md`:

- `GuardedObjectStore` — the single funnel over the host-implemented `IAdapterObjectStore`.
  `Create(services)` mirrors `NdjsonBatchEmitter.Create`. One per run, shared (the concurrency limit
  lives on the instance).
- `Staging/FrozenKeyList` — `FreezeAsync` write-once, `ReadBatchesAsync` read in batches of N.
- `Staging/StagingManifest` — generic small-JSON manifest read/write.
- `Staging/PriorStateStore` — per-key `ReadAsync` / `WriteAsync` / `UpdateAsync` (RMW), one object
  per key. **This is the only keyed random-access primitive on offer.**
- `CappedNdjsonLineReader` — line framing with a cap.
- Guards: `MaxInMemoryObjectBytes` 8 MiB (above it a buffered read is refused, streaming forced),
  `MaxLineBytes` 16 MiB, `MaxConcurrentReads` 4, `MaxControlArtifactBytes` 1 MiB, optional
  `MaxListedObjects`. Memory pressure reuses Emission's `MemoryPressureOptions`.
- Explicitly NOT responsible for: the data lane (that is Emission — publishing through this façade
  is a defect), deletion (`IAdapterObjectPruner`, separate contract + separate IAM grant), or
  atomicity between two objects (no conditional write ⇒ last-writer-wins, single-writer-per-key is
  the caller's job).

Host side is real and deployed: `S3AdapterObjectStore` + `S3AdapterObjectPruner` in
`IntegrationServiceBus/…/Infrastructure.AWS`, registered at `DependencyInjection.cs:160`, gated by
`S3Options` delete permission. `LocalAdapterRunner/Publishing/S3ObjectStoreWiring.cs` wires it locally.

⇒ the 1 GiB RAM budget, the overflow degrade lane, the marker set, the host-less chunk-1 records,
`TenableIoOverflowChannelPublisher`, and the `IAssetSpool` "windowed-join escape hatch" are all
answers to a problem the substrate now owns.

**(iii) Falcon's correlated flow was rebuilt as two-phase staged** (task
`ai/active/2026-08-05_1937_s3-capability-falcon-two-phase-correlation`, code in
`FalconCollector/Flows/Findings/TwoPhase/`). The mechanism, per `FalconFindingsFlow.cs`:

- Phase 1 (`FalconHostSpooler`) pages the driving inventory into `{storageUrl}/_staging/{generation}/`
  and writes a **manifest last**. No enrichment inside that scroll.
- **The manifest's existence is the completion proof for Phase 1** — not the checkpoint. Present ⇒
  Phase 1 is never redone and Phase 2 resumes from the checkpoint position; absent ⇒ Phase 1
  restarts wholesale under a new generation. `ResolveFrozenKeyListAsync` tries the checkpoint's
  generation, then any completed generation, then re-spools.
- The resume position is a **coordinate** — (staged page, batch index) — and is deliberately never the
  object's name. The object name is `progressContext.CurrentPage`, a dense count of objects. v3 used
  one sparse ordinal for both and "every such comparison was a defect".
- The manifest's geometry wins over changed config mid-generation, so page numbering stays
  replay-stable.
- **Orphaned-leg guard:** a leg told to start fresh that finds objects already published under a
  completed generation **fails** rather than silently re-collecting. ISB only takes its resume path
  when `RetryCount > 0`, and re-dispatch as a new message loses the position (observed twice
  2026-08-11). The collector cannot read its own checkpoint, so any position derived locally would be
  a second authority.
- **Publish and record are one step** — nothing awaitable or cancellable sits between the publish and
  `checkpointWriter.OnBatchPublished`, so an object never exists without the position covering it.
- Cleanup is **garbage collection, never a cursor**: a staged page is deleted strictly after the
  checkpoint that retires it; the whole staging area after the last batch. A lost delete costs storage.
- `FalconResumeReach` writes three `_resume.*` keys so a co-operative stop states where it reached;
  absence of a statement means "nobody knows", which must not be reported as "redid nothing". The
  co-operative path persists via a state-snapshot `OnCheckpoint` with **no** `AdvancePage`.
- Mid-batch partial accumulators are **discarded, not flushed** — flushing would stamp
  `isLastChunk: true` on hosts still missing findings, i.e. silent loss.
- Staging is constructed through `GuardedObjectStore`; a missing `IAdapterObjectStore` is a **hard
  failure**, never a fallback to the old traversal.

⇒ the branch's resume model (full re-spool every leg, duplicates pushed onto the parser) is precisely
what this design was written to stop doing.

**(iv) Egress law.** `Emission/README.md`: one publish call = one atomic object, materialized on
multipart `Complete` or never; objects are **size-unbounded**; `MaxBytesPerBatch` (50 MiB default) is
in-flight part discipline only, not an object cap; a single record over `SoftRecordWarningBytes`
(24 MiB) is logged loudly, never rejected; the commit signal is produced once, only after Complete,
so a checkpoint never advances past an object that did not materialize; the publish line carries a
streaming `Hash=` for detecting byte-identical re-uploads. Scope note: the
`ThrottlingAdapterExecutionContext` decorator still hard-caps a direct
`PublishAsync(StreamBatchRequest)` — a different route.

---

## 4. The three open forks

### Fork A — spool backend (the load-bearing one)

Tenable's join needs **random access by uuid while vuln chunks stream**. Falcon's staging is
*sequential* page reads over a frozen key list, which does not provide that. Options:

1. **Keep the in-RAM gzip spool.** Measured peak 54.4 MB compressed on the largest tenant; budget
   never engaged. Deleting the overflow lane, the marker set and the host-less chunk-1 case as dead
   weight would remove most of the branch's complexity. Risk: unbounded tenants; the guard that
   justified 1 GiB was never exercised.
2. **Stage per-key (`PriorStateStore` shape).** True keyed access, spool survives the leg, enables
   Falcon's no-re-spool resume. Cost: ~108 K small S3 objects per run at reference scale, plus
   `MaxConcurrentReads = 4` on the read path in the hot vuln loop.
3. **Stage sequentially and invert the join.** Write the asset spine as staged pages; drive phase 2
   from the spine rather than from vuln-chunk order. Needs a way to reach an asset's findings without
   scanning every vuln chunk per page — Tenable offers no per-asset vulns query on the export API, so
   this likely requires a staged, uuid-partitioned rewrite of the vuln side.
4. **Hybrid:** RAM spool with a staged spill above a watermark, replacing the overflow *degrade* with
   an overflow *tier* (correctness identical to (1), bounded like (2)).

### Fork B — resume model

- Branch: full re-spool per leg; at-least-once; duplicates absorbed by the parser.
- Falcon-current: manifest = phase-1 proof; coordinate position; no duplicate spine; orphaned-leg
  guard; staged pages GC'd after the retiring checkpoint.
- Fork B is largely *determined* by Fork A: (1) forces re-spool, (2)/(4) enable the Falcon model.

### Fork C — parser ordering / scope

The branch's own deploy note: "Requires the tenable parser's correlated-shape support (per-UUID
replay-idempotent aggregation) to ship FIRST — not yet implemented; this branch must not reach
production before that parser change."

Unverified at contract time: the tenable parser is believed to have gone the *split two-lane* route
instead (validated on a real rfqa run in July per operator memory), which would make correlated a
second, different parser change rather than a pending one. **Must be checked in
`cymulate-integration-parsers` before any rollout claim.**

---

## 5. Vendor facts already validated on the prior branch (carry, do not re-derive)

From `ai/active/2026-07-05_1802_tenableio-correlated-findings/assumptions.md`:

- **A1 — vuln export chunks are asset-complete.** Official docs (`num_assets`: "The exported data of
  a chunk is the sum of all the vulnerabilities for each asset in that chunk"; allocate-then-populate
  job model, developer.tenable.com) + empirical probe on the largest tenant: 150 chunks / 7,486
  assets / 526,519 findings, **zero** assets in >1 chunk, assets/chunk min 48 max 50.
- **A2 — spool sizing.** 5,000-asset probe: avg 4,025 B raw/asset, p50 2,952 B, p95 6.9 KB, max
  59 KB; per-record gzip 2.9×; 108 K-asset projection 435 MB raw / 152 MB compressed. Fat fields:
  `network_interfaces` 20.6 %, `tags` 17.8 %, `installed_software` 6.4 %.
- **A3 — chunk sizes at `num_assets=50`.** 10–43 MB, p50 22 MB; densest asset 842 findings.
- **A4 — chunks stream during PROCESSING.** Docs + existing collector already downloads progressively.
- **A5 — export concurrency.** 10 concurrent exports/container; 429 + Retry-After on excess;
  duplicate-filter exports deduped server-side (409 `active_job_id` path already handled).
- **A6 — chunk retention.** Docs say 24 h in one place, 3 days in another; the collector's existing
  ~24 h staleness rule is the conservative bound.
- **A7 — phantom allocations are benign.** 14/7,500 slots (~0.2 %) allocated assets yielded zero
  rows; never >50/chunk ⇒ not a size cutoff. Absence from the vuln export == zero-vuln; handled by
  the sweep.
- **A8 — reference tenant scale.** 90-day window: 2,168 chunks ≈ 108 K assets, ~7.6 M findings.

Still OPEN there: A10 (assets-export re-creation on resume with the same filters is safe — new
snapshot deltas land in the miss lane), A11 (LocalAdapterRunner needs no structural change).
