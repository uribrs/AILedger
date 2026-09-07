# Code Review — TenableIo Correlated Findings Flow

**Stack:** C# / .NET 8, long-running K8s data-collection collector.
**Change type:** feature logic + shared-adjacent recovery/checkpoint code.
**Risk level:** High — persistence, resume/idempotency, pagination state, large-data streaming, retries.

Scope: the rewrite of the TenableIo `CollectFindings` flow to a three-phase correlated design
(asset spool → vuln-chunk correlation → zero-vuln sweep) plus its checkpoint/resume, config,
and tests. Reviewed on its own merits: correctness, failure semantics, memory, concurrency, JSON
fidelity, test quality.

Overall the code is clean, well-factored, and idiomatic. Streaming and JSON-ownership are done
right (no buffer aliasing, no full-DOM retention, remove-on-use spool). The findings below are
concentrated on the resume/recovery semantics and the overflow path, which is where the operational
risk lives.

---

## Majors

### M1 — Resume cost scales with prior progress; risk of non-termination under repeated restarts
`TenableIoVulnPhase.RebuildClaimedSetAsync` (TenableIoVulnPhase.cs:36-58) and
`TenableIoFindingsFlow.ResumeSetupAsync` (TenableIoFindingsFlow.cs:139-146).

**Problem.** On every resume the flow re-downloads *every already-processed vuln chunk* over the
network (UUID-only re-scan) to rebuild the claimed-asset set, then re-runs the full asset spool.
The further a run has progressed, the more chunks a resume must re-stream before it can make new
progress. A resume that lands in the sweep phase re-downloads the *entire* vulns export (all chunks
processed) purely to reconstruct UUIDs, since the vuln phase then no-ops.

**Impact.** For a large tenant a flapping pod can spend most of each invocation re-downloading past
work. Combined with Tenable's ~24h export expiry (enforced by `IsCheckpointStale`), a run that
restarts a few times late in its life may never reach FINISHED before the export expires — a
livelock that produces no forward progress. This is the highest-impact issue because it is a
scale-and-restart failure, not a rare edge.

**Fix.** Persist the claimed set (or a compact representation — e.g. a per-chunk "assets already
emitted" marker, or persist which chunks are spool-complete) so resume does not re-derive it by
re-downloading. If persisting the full UUID set is too large, at minimum short-circuit the
sweep-phase resume so it does not re-scan all vuln chunks. Requires a checkpoint-shape change, not
a local patch.

### M2 — Overflow host records are re-emitted unboundedly on resume and are non-idempotent under mid-chunk retry
`TenableIoAssetSpoolPhase.TrySpoolChunkAsync` (TenableIoAssetSpoolPhase.cs:145-189),
`AddOverflowRecord`/`FlushOverflowAsync` (191-210).

**Problem (resume).** Overflow assets are published as chunk-0 host records *during spooling* with
`buildCheckpoint: null` — i.e. no checkpoint records that they were emitted. On resume the spool is
rebuilt from scratch, and every asset that overflows again is published again. Overflow assets are
not in the `claimed` set (that set only covers assets referenced by *processed vuln chunks*), so the
re-spool re-emits the full overflow host set on each resume.

**Problem (in-run retry).** `spool.TryAdd` and overflow publishing happen *incrementally while the
chunk stream is being read*. If the stream drops mid-chunk, `TrySpoolChunkAsync` retries and
re-streams from the start; `spool.TryAdd` is idempotent (dictionary dedup), but `AddOverflowRecord`
is not — every asset that overflowed before the failure point is appended and published again. So a
single transient mid-chunk drop double-publishes overflow hosts. This asymmetry is notable because
the vuln phase (M-none) correctly buffers the whole chunk before any side effect, so it does not
have this problem.

**Impact.** Duplicate host records proportional to overflow volume (a 1 GiB compressed budget ≈ ~700K
assets before overflow begins). Tolerable only if downstream aggregation dedups per UUID; if it does
not, this is duplicated/again-counted asset data. Overflow is precisely the large-tenant path.

**Fix.** Make overflow emission idempotent under retry (accumulate overflow candidates for a chunk
and publish only after the chunk fully streams, mirroring the vuln phase), and give overflow assets
a durable "already emitted" marker in the checkpoint so resume does not re-emit them. Refactor of the
spool phase's side-effect ordering.

### M3 — Checkpoint's processed-chunk set always lags one chunk behind; last completed chunk re-emits on crash
`TenableIoVulnPhase.ProcessChunkWithRetryAsync` (TenableIoVulnPhase.cs:174-183).

**Problem.** `processedSnapshot` is captured *before* publishing the chunk, and
`processedChunkIds.Add(chunkId)` runs *after* `PublishAsync` returns. No checkpoint is written
between that Add and the next chunk's first page. So the persisted processed set never includes the
chunk that most recently completed. A crash in the window `[chunk C fully published]` →
`[chunk D first page published]` (which includes the poll delay and D's download) loses the fact
that C completed, and C is fully reprocessed and re-emitted on resume.

**Impact.** At-least-once re-emission of one chunk's envelopes on most crashes. Bounded (≤ one
chunk), so lower blast radius than M1/M2, but it is guaranteed rather than rare. Acceptable *only*
if downstream is idempotent per (uuid, plugin.id).

**Fix.** Either snapshot the processed set to *include* the current chunk for the final page's
checkpoint, or write a checkpoint immediately after `processedChunkIds.Add(chunkId)`. Local patch.
If at-least-once is an accepted contract, document it explicitly and confirm downstream dedup.

---

## Minors

### m1 — 404 re-create during spool keeps partial spool/overflow state
`TenableIoAssetSpoolPhase.RunAsync` (TenableIoAssetSpoolPhase.cs:65-76). On a mid-spool 404 the code
re-creates the export and clears `processedChunkIds`/`excludedChunkIds`/`totalChunks`, but does not
clear `spool`/`overflowMarkers` or reset `_overflowBuffer`. The new export re-streams all assets;
`spool.TryAdd` dedups by UUID, but any asset that already overflowed and was published gets
re-published when re-encountered. Low likelihood, but compounds M2. Consider resetting spool state
(or documenting why partial retention is safe) on re-create.

### m2 — Resume re-emission inflates the run counters
`TenableIoFindingsStats` is restored from the checkpoint on resume (`RestoreResumeState`), then
duplicate re-emissions from M2/M3 increment `AssetsEmitted`/`FindingsEmitted` again. Counters drift
above true distinct counts after any resume. Diagnostic-only per the code comments, so not
correctness — but the success payload reports these totals.

### m3 — `RebuildClaimedSetAsync` log line is inaccurate
TenableIoVulnPhase.cs:55-56: `claimed.Count == 0 ? 0 : processedChunkIds.Count()` logs "0 chunks"
whenever the claimed set is empty even though chunks were scanned, and calls `.Count()` (LINQ) on the
`IEnumerable<int>` a second time. Log-only; tidy to `processedChunkIds` being an `ICollection` count.

### m4 — Stale doc reference to a deleted type
`Shared/.../Session/docs/Streaming-via-Http-Package-Session.md:84` still references
`TenableIoFindingsChunkProcessor.BuildChunkRecordsAsync`, which was deleted in this change. Update or
remove the reference.

### m5 — Test gaps on the riskiest paths
The new tests are behavior-focused, deterministic, and free of network/time dependencies — good. But
the highest-risk paths have no coverage: resume with overflow assets (M2), the checkpoint-lag
reprocess window (M3), mid-chunk stream-failure retry during spooling, spool 404 re-create, and the
sweep-phase resume. `BuildRecordsForChunk`, the slicing cap, and checkpoint round-trip are well
covered; the resume/recovery seams are not.

---

## Observations (no action required)

- **O1 — Per-chunk peak memory ≈ 2× chunk.** `ProcessChunkWithRetryAsync` holds `buckets` (all
  findings decoded, one `byte[]` each) and the fully-built `records` list (each envelope copied via
  `WrittenSpan.ToArray()`) simultaneously before publish; each finding's bytes are copied twice
  (bucket, then envelope). Bounded by the num_assets=50 chunk size (measured 10–43 MB), so safe, but
  it is the memory ceiling of the flow — worth keeping in mind if `numAssetsPerChunk` is raised
  (bounds allow up to 5000).

- **O2 — Miss with empty embedded asset emits no host anywhere.** In `BuildRecordsForChunk`
  (TenableIoVulnPhase.cs:242-246), a spool miss whose finding has no/empty `asset` sub-object yields
  `host = null`, so `IsHostBearing` is false and no host record is produced for that UUID on any
  chunk. Findings still carry the UUID; whether a host-less asset is acceptable downstream is a
  contract question, not a code defect.

- **O3 — Overflow→miss reclassification is only unreachable under an unverified assumption.** If a
  single asset's findings can span more than one vuln export chunk, then an overflow asset appearing
  in both a processed and an unprocessed chunk would be `claimed` (skipped from re-spool) yet
  referenced by the unprocessed chunk, and would be reclassified as a *miss* on resume — producing a
  second (thin) chunk-0 host that collides with the original overflow chunk-0 host and restarts chunk
  numbering. I believe Tenable's vulns export keeps an asset's findings within one chunk
  (`num_assets` groups whole assets), which makes this unreachable — but the correctness of the
  claimed-skip logic rests entirely on that. Worth a confirming comment or test. (Low confidence that
  it is reachable; high confidence it would be a real bug if it were.)

- **O4 — Cancellation handling is correct.** `OperationCanceledException` is rethrown ahead of the
  retry filters in both phases, and `HttpTransportFailureClassifier.IsRetryableTransportFailure`
  explicitly excludes `OperationCanceledException`, so a genuine cancellation is not swallowed into a
  retry loop. Good.

---

## Overall assessment

This is solid, idiomatic .NET 8 with careful streaming and JSON handling; the hot path (per-chunk
bucketing and envelope emission) is correct, memory-bounded, and cancellation-safe, and the format-
version guard cleanly fences off old two-lane checkpoints. The risk is entirely in the recovery
plane. The design is at-least-once, which is defensible for this domain, but three things push beyond
"clean at-least-once": resume cost that grows with progress and can livelock a large tenant against
the 24h export expiry (M1), overflow host records that re-emit unboundedly on resume and duplicate
under mid-chunk retry (M2), and a checkpoint that always lags the last completed chunk (M3). None are
data-loss; all are duplicate-emission or non-termination risks whose acceptability hinges on
downstream per-UUID/per-finding idempotency, which should be confirmed before merge. M1 is the one I
would not ship without addressing, because its failure mode is a run that never completes rather than
a run that emits a bit extra.
