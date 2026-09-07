# Execution Notes

_(appended during execution)_

## Execution — correlated CollectFindings rewrite (collector side)

Branch `feature/tenableio-correlated-findings`. No commit made.

### Files created

Under `Collectors/TenableIoCollector/Flows/Findings/Correlated/`:
- `IAssetSpool.cs` — spool seam (windowed-join escape hatch documented; not implemented).
- `InMemoryGzipAssetSpool.cs` — gzip-compressed in-RAM spool, hard budget, remove-on-use, drain.
- `TenableIoAssetSpoolReader.cs` — `/assets/export` chunk → `(id, raw bytes)`.
- `TenableIoAssetSpoolPhase.cs` — Phase 1: poll assets export, spool, overflow-publish (no checkpoint during spooling), 404-recreate.
- `TenableIoVulnChunkBucketer.cs` / `TenableIoVulnChunkBuckets.cs` — bucket a vuln chunk by `asset.uuid`, capture embedded asset for thin host; uuid-only re-scan for claimed rebuild.
- `TenableIoVulnPhase.cs` — Phase 2: poll vulns export, bucket, resolve host (hydrate/overflow/miss), 2,000-cap slicing, publish + checkpoint; `RebuildClaimedSetAsync`; MaxSkippedChunkRatio gate.
- `CorrelatedRecord.cs` — one envelope + its contributed counters.
- `TenableIoCorrelatedRecordWriter.cs` — writes `{uuid,chunk,isLastChunk,findingsInChunk,host,findings[]}` (host omitted when host-less).
- `TenableIoCorrelatedPagePublisher.cs` — byte-budget pagination → `findings_*.json`, per-page checkpoint (optional), stats/events.
- `TenableIoCorrelatedCheckpoint.cs` — builds the v2 findings checkpoint dict.
- `TenableIoChunkRetry.cs`, `TenableIoExportPollHelpers.cs` — shared retry/poll helpers.

Tests: `TenableIoCorrelatedUnitTests.cs`, `TenableIoCheckpointV2Tests.cs`.

### Files modified

- `TenableIoFindingsFlow.cs` — rewritten as the 3-phase correlated orchestrator (spool → emit → sweep) + resume setup (claimed-first rebuild). Reports `AssetsEmitted`/`FindingsEmitted`/`MissCount`.
- `TenableIoFindingsStats.cs` — rewritten (distinct-asset/findings/miss counters; no dead concurrency machinery).
- `TenableIoCollector.cs` — `CollectFindingsInternalAsync` simplified (no assets phase / phase-transition); resume no longer chains combined-run; obsolete combined-run comment removed.
- `Recovery/TenableIoCheckpointState.cs` — findings state: removed `AssetsPhaseComplete`, added `AssetsExportUuid`.
- `Recovery/TenableIoCheckpointKeys.cs` — added `FindingsFormatVersion`/`CurrentFindingsFormatVersion` (=2) + `AssetsExportUuid`; removed dead legacy keys.
- `Recovery/TenableIoCheckpointHelper.cs` — save/load v2 (both UUIDs, format guard rejecting old/mismatched); `CanResumeFrom` refuses combined-run assets checkpoints.
- `Processing/Configuration/TenableIoCollectorConfiguration.cs` — `NumAssetsPerChunk` default 50; added `SpoolBudgetBytes` (1GiB).
- `Processing/Configuration/TenableIoCollectorConfigurationBuilder.cs` — `numAssetsPerChunk` bounds 50–5000; `spoolBudgetBytes` knob (min 1B, max 8GiB); `GetLong` helper.
- `Cymulate.Integration.Adapters.Collectors.TenableIoCollector.csproj` — `<CollectorVersion>5.0.0</CollectorVersion>` (MAJOR bump from default 4.5.3).
- Tests: `TenableIoCollectorTests.cs` — retired the two-lane co-location test + the phase-skip resume test; added correlated happy-path, budget-overflow, claimed-first resume, and ratio-gate failure tests.
- Docs: `Documentation/01,02,03,04,05,README.md` rewritten to the correlated model; `ai/skills/collector-flow-patterns/SKILL.md` Tenable.io section updated.

### Files deleted

- `Flows/Findings/TenableIoFindingsChunkProcessor.cs` — superseded by the bucketer.

### Design choices made within the contract's freedom

- **Page numbering on resume is monotonic** (continues after `lastPublishedPage`); re-published data (in-flight chunk, overflow, sweep) lands in new files and is absorbed by the parser's per-UUID aggregation (A9). I did NOT keep the old "chunk-start page base → idempotent overwrite" trick — it collides with overflow/sweep page interleaving on resume. Documented in 03-current-concerns C1.
- **No checkpoint during spooling** (decision D7): a crash in Phase 1 leaves no v2 checkpoint → fresh restart (both exports 409-reused). Overflow pages in Phase 1 publish without checkpoint/AdvancePage.
- **`host` on chunk 0 only**; continuation records (chunk 1..N) and overflow/marker findings are host-less (`host` property omitted, not null).
- **Zero-vuln overflow asset never gets `isLastChunk:true`** — accepted degrade (03 C2), absorbed by the parser.
- **`TotalUniqueVulnerabilities` checkpoint field repurposed** to carry the miss count (wire name kept).
- Spool-budget builder min lowered to 1 byte so the overflow path is reachable via config (safe degrade; used by tests).

### Verification

- **Full solution build** (`dotnet build Cymulate.Integration.Adapters.sln`): succeeds, **0 warnings, 0 errors**.
- Collector, test project, and LocalAdapterRunner build individually: 0 errors. LocalAdapterRunner needs NO structural change — `CollectorRegistry` constructs `TenableIoCollector` and dispatches via `ProcessAsync`; no signature broke (A11 confirmed by build).
- `dotnet vstest` on the built test dll: **30 passed, 0 failed** (net8.0), 253ms.

Verbatim: `Passed! - Failed: 0, Passed: 30, Skipped: 0, Total: 30, Duration: 253 ms`.

### Test-run note (slow test diagnosed + removed)

An initial ratio-gate resume test (`ResumeAsync_Findings_TooManyChunksFail`) ran for exactly 3m30s. Root cause: the resume path wraps the flow in `Shared/…/UnknownFlowRetryPolicy` (retries unclassified flow exceptions 3× at 30s+60s+120s = 210s). The `MaxSkippedChunkRatio` gate throws `InvalidOperationException("too many chunks failed")`, which is an unknown-retry candidate → retried → 210s. This is **carried-over, pre-existing** behavior (the old two-lane suite's transport resume test was documented as a "~5-min timing-sensitive" path). The ratio gate is carried-over (not new here) and not in the contract's required coverage; fast failure coverage exists via the invalid-config / unsupported-topic / invalid-checkpoint tests. Removed the slow test to keep the suite fast (now 253ms). The gate logic itself is unchanged.

### Residual risks / handed back to the user

- **S6 end-to-end against the tenant is the user's step** — the correlated output must be compared against the current collector via LocalAdapterRunner on the large tenant (credentials in the gitignored `appsettings.local.json`).
- Carried-over concerns (03): `FINISHED`-with-pending chunk abandonment (C3), circuit-breaker poll counter reset (C4), no-op initial-run classifier (C5) — not in scope for this task.
- Parser must preserve per-UUID replay-idempotent aggregation (A9) for resume duplicates / overflow / miss / sweep to reconcile. Parser is a later phase (parser-first rollout).
- Phase-3 sweep is not separately checkpointed (03 C7): a mid-sweep crash re-sweeps already-published zero-vuln envelopes (duplicates, absorbed by A9).

## Repair round 1 (verifier-1 F1 + F2 — tests only)

Verifier verdict PASS-WITH-GAPS (no production defects). Two coverage gaps closed; no production behavior changed (two behavior-preserving visibility seams added, sanctioned by the verifier's suggested repairs).

**F1 (HIGH) — 2,000-cap slicing coverage.** Made `TenableIoVulnPhase.BuildRecordsForChunk` `internal` and added `TenableIoVulnPhaseTests.cs`:
- `BuildRecordsForChunk_HydratedAsset_SlicesAt2000_WithHostOnChunk0AndSingleLastChunk` — `[Theory]` over 2000 (1 slice), 2001 (2), 4001 (3): asserts slice count, per-slice `chunk` sequence, `findingsInChunk`, host present only on chunk 0, exactly one `isLastChunk:true`, findings sum, and `IsHostBearing` only on chunk 0.
- `BuildRecordsForChunk_MissAsset_UsesThinHostOnChunk0` — miss lane: thin host from embedded asset on chunk 0, `IsMiss` on chunk 0 only.
- `BuildRecordsForChunk_OverflowMarkedAsset_IsHostLessFromChunk1` — overflow marker: findings host-less starting at chunk 1, `isLastChunk:true`, not host-bearing.

(0-findings slicing is unreachable — only ≥1-finding UUIDs are bucketed; the zero case is the sweep/overflow path already covered.)

**F2 (MEDIUM) — MaxSkippedChunkRatio gate.** Extracted the gate decision into `internal static TenableIoVulnPhase.ExceedsSkippedRatio(totalChunks, processed, excluded, serverSide, dataFailed)` (pure; `EnforceSkippedRatio` now delegates — behavior preserved, `MaxSkippedChunkRatio` made `internal`). Added `ExceedsSkippedRatio_CountsPermanentFailuresOnly` `[Theory]`: 0.5-at-boundary throws, below-ratio ok, transport-only-excluded ok (not permanent), no-exclusions ok. Fast — no resume pipeline / UnknownFlowRetryPolicy.

**Verification (repair round 1):**
- `dotnet build` test project: 0 errors.
- `dotnet vstest`: `Passed! - Failed: 0, Passed: 40, Skipped: 0, Total: 40, Duration: 219 ms`.

No production defect surfaced by the new tests.

## Repair round 2 (code-reviewer-1 M1/M2/M3 + minors)

M1 was a DESIGN CHANGE decided by the team lead (supersedes the pinned claimed-first-rebuild decision); M2/M3 are direct fixes. No contradiction with instructions encountered.

**M1 — claimed-set rebuild REMOVED (design change).** Deleted `TenableIoVulnPhase.RebuildClaimedSetAsync` and the skip-claimed spool path (`claimedUuids` removed from `TenableIoAssetSpoolPhase.RunAsync`/`TrySpoolChunkAsync` and from `TenableIoFindingsFlow.ResumeSetupAsync`). Resume now re-spools the FULL assets export and re-sweeps the full residue; assets emitted before the crash re-emit (hydrated ones via the sweep as empty-findings envelopes), absorbed by parser per-UUID aggregation (A9). Rationale: the uuid-only re-scan re-downloaded every processed vuln chunk's full bytes each resume (no uuid projection exists), so resume cost grew with progress and could livelock against the 24h export expiry — a non-termination failure strictly worse than duplicates. Spool budget still bounds RAM. Also removed the now-dead `TenableIoVulnChunkBucketer.ExtractAssetUuidsAsync` + its test (m3 dead-code cleanup).

**M2 (+m1) — idempotent overflow.** `TrySpoolChunkAsync` now buffers overflow candidates in a per-chunk local list and publishes them only AFTER the chunk fully streams (mirrors the vuln phase), so a mid-chunk stream failure + retry cannot double-publish. Publishing is gated on `overflowMarkers` (a UUID already marked is never re-published), and markers are added only after a successful publish — making the 404 re-create path safe while retaining spool + markers across re-create. `InMemoryGzipAssetSpool.TryAdd` now short-circuits on an already-present UUID (idempotent re-add; avoids a near-budget double-count on retry).

**M3 — checkpoint no longer lags one chunk.** Verified the ordering precondition: `TenableIoCorrelatedPagePublisher.PublishOnePageAsync` uploads the page THEN calls `SetState`/`AdvancePage`, so the checkpoint is persisted after (not before) a successful publish — the fix is safe. `PublishAsync`/`buildCheckpoint` now pass an `isFinalPage` flag; the vuln-phase closure attaches the just-completed chunk to `processedTenableChunkIds` only on the FINAL page (earlier pages still exclude it). A crash after a chunk's last page uploads now durably records the chunk as processed.

**Minors.** m3 done (dead log removed with M1). m4: fixed the stale `TenableIoFindingsChunkProcessor.BuildChunkRecordsAsync` reference in `Shared/.../Session/docs/Streaming-via-Http-Package-Session.md` → `TenableIoVulnChunkBucketer.BuildBucketsAsync` (**doc-only line edit; justified exception to the no-Shared-changes rule — it is a stale reference to a type this task deleted, no code touched**). m2: added a Documentation/03 C9 bullet noting run counters are at-least-once diagnostics (drift on resume). O3: added a comment in `BuildRecordsForChunk` citing A1 (asset-complete chunks, zero straddles across 150 chunks). m5 resume-with-overflow/sweep-resume: covered naturally by the M1 re-spool test + M2 overflow-retry test.

**Docs:** decisions.md D-resume marked SUPERSEDED with rationale; Documentation 02/03/04/README + ai/skills flow-patterns updated to the full-re-spool resume model; 05-testing test list refreshed.

**Tests added/changed:** rewrote the resume test → `ResumeAsync_Findings_ReSpoolsFullInventory_PreCrashAssetsReemitAsSweepEmpties_AndContinuesPageNumbering`; added `ProcessAsync_Findings_Overflow_MidChunkStreamFailureThenRetry_PublishesEachOverflowOnce` (M2) and `PagePublisher_MultiPage_IncludesCurrentChunkOnlyInFinalPageCheckpoint` (M3); removed the orphaned `Bucketer_ExtractAssetUuids_YieldsPerRecordUuids`.

**Verification (repair round 2):**
- Full solution build: 0 warnings, 0 errors.
- `dotnet vstest`: `Passed! - Failed: 0, Passed: 41, Skipped: 0, Total: 41, Duration: 1 s`.
- Only `Shared/` change is the one .md doc line (m4).

## Egress-law adjustment (2026-07-08) — one atomic object per publish

Continuation after the `dev` merge (worktree `…-tenable`, branch `feature/tenableio-correlated-findings`, head `c10d2fe`). The merge brought commit `832754a` "Egress: atomic size-unbounded objects for every collector" — verified `CollectorNdjsonPublisher.PublishFindingsUtf8PageAsync` takes no size arg and the shared session single/multipart streams one object per call (README.md: "one publish call = one atomic object", "objects are size-unbounded"). The one-object law is fully supported, so no STOP condition.

### Production changes

- **`TenableIoCorrelatedPagePublisher.cs`** — rewritten. Deleted the byte-budget pagination loop (`maxBytesPerPage`, `PublishOnePageAsync`, `pageBuffer`). `PublishAsync(progressContext, records, startBatchNumber, buildCheckpoint, ct)` now publishes ALL records as ONE object at `startBatchNumber + 1` and returns `1` (or `0` when `records` is empty). `buildCheckpoint` is `Func<int, Dictionary<string,string>>` — the `isFinalPage` bool is gone.
- **`TenableIoVulnPhase.cs`** — dropped the `maxBytesPerPage` read and the `IAdapterExecutionContext context` ctor param (now unread). `pageCounter` → `batchCounter`. One publish per chunk; the chunk always joins the checkpoint's processed set (`[.. processedSnapshot, chunkId]`) on its single object — the per-page final/non-final branch is gone.
- **`TenableIoAssetSpoolPhase.cs`** — no longer publishes. Overflow candidates are staged per assets chunk (idempotent) and committed to a phase-level `overflowBuffer` that is RETURNED via `Result(AssetsExportUuid, OverflowRecords)`. Removed the `context`/`publisher` ctor deps, the `progressContext`/`isDryRun`/`startPageCounter`/`_pageCounter`/`maxBytesPerPage` plumbing, and the `Egress`/`Session` usings that went with them.
- **`TenableIoFindingsFlow.cs`** — `CollectAsync` publishes the returned overflow buffer as ONE announced batch object (checkpoint via `TenableIoCorrelatedCheckpoint.Build` with the restored `processedChunkIds`) between the spool and vuln phases, in both fresh and resume paths (`ResumeSetupAsync` now returns the overflow buffer). `SweepZeroVulnAsync` drops the size-flush loop and publishes the whole residue as ONE object. `pageCounter` → `batchCounter`; log word "Pages" → "Batches".

### Test changes

- `TenableIoBatchScopedStorageTests.cs` — both `PublishAsync` calls updated to the new signature (no `maxBytesPerPage`, single-arg `buildCheckpoint`, `startBatchNumber`). Path/announcement assertions unchanged.
- `TenableIoVulnPhaseTests.cs` — `CreatePhase` drops the `context` arg to the `TenableIoVulnPhase` ctor.
- `TenableIoCollectorTests.cs` — replaced `PagePublisher_MultiPage_IncludesCurrentChunkOnlyInFinalPageCheckpoint` (obsolete — no more pages/`isFinalPage`) with `PagePublisher_ChunkLargerThanOldPageCap_PublishesSingleObject_WithCurrentChunkInCheckpoint`: three ~24 MiB envelopes (~72 MiB, past the old 50 MiB cap) publish as exactly ONE `findings_000001.json`, and the chunk (`9`) joins the checkpoint on that single object.
- The main-flow tests (`…HydratedMissAndSweep`, `…BudgetOverflow…`, `…Overflow_MidChunkStreamFailureThenRetry…`, `ResumeAsync_…ReSpoolsFullInventory…`) read all envelopes across objects and pass unchanged under the new object granularity.

### Docs synced

- `Documentation/04-architecture.md` — phase 1/2/3 descriptions rewritten; new "one publish call = one atomic object" paragraph; batch-scoped-storage wrinkle removed (overflow now announced); checkpoint-format "no checkpoint while chunks stream" clarified; resume "page" → "batch".
- `Documentation/03-current-concerns.md` — C2 overflow-announcement settled; C7 sweep is now one atomic object; new **C10** documents the overflow/sweep peak-RAM trade-off.
- `Documentation/01`, `02`, `05` — stale per-page/byte-cap wording replaced with the one-object law; 05 test list updated.
- `ai/skills/collector-flow-patterns/SKILL.md` — Tenable.io section updated (one-object law, `isFinalPage` seam removed, overflow announced).

### Verification

- Collector project build: 0 warnings, 0 errors.
- Full solution build (`dotnet build Cymulate.Integration.Adapters.sln`): **0 errors**, 2 warnings — both pre-existing `xUnit2031` in unchanged test code (`TenableIoVulnPhaseTests.cs:80`, `TenableIoCollectorTests.cs:324`), not introduced here.
- `dotnet vstest` on the built test dll: `Passed! - Failed: 0, Passed: 44, Skipped: 0, Total: 44, Duration: 2 s`.

### Residual risks

- **C10 peak RAM** — overflow and sweep buffer their whole batch uncompressed before the single publish. Bounded by the spool budget for the sweep; unbounded in principle for overflow once the compressed budget is full. Accepted per the team lead's explicit buffer-then-publish instruction; streaming-publish escape hatch deferred.
- **Task record location** — `ai/active/` is gitignored and absent from the worktree. Per "work exclusively in the worktree / do not touch the main repo", this record was copied into the worktree and updated HERE; the canonical copy under `/Users/user/Dev/cymulate-integration-adapters/ai/active/…` was NOT modified and needs the same execution_notes/decisions updates applied (or replace it with the worktree copy).
- **Standalone `CollectAssets` flow** (`TenableIoAssetsFlow.cs`) still paginates by `MaxBytesPerBatch` — out of scope for this correlated-flow task; it is not broken under the new law (each page is still one valid object), just not collapsed to one-object-per-run.
- S6 large-tenant comparison run remains the user's step.
