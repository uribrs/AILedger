# InsightVM Collector — Release Enriched Assets After Batch Add

## What

`InsightVmCollector` retains every enriched asset for the entire duration of a
findings collection run. Peak live set is `O(assets processed × ~1MB/asset)`
instead of `O(one batch)`. Release the master list's hold on each asset once the
batch accumulator has taken its row.

## Why it leaks

- `List<JObject> assets` (`InsightVmCollector.cs:125`) is a GC root until
  `CollectFindingsAsync` returns — `assets.Count` is read at `:131` and `:137`.
- Enrichment is written **into** an element of that list:
  `iAsset["vulnerabilityDetails"] = new JArray(enriched)` (`:410`).
- Each batch is `iAssets.Skip(i).Take(cBatchSize).ToList()` (`:185`) — the same
  object references, not copies.
- So `InsightVmBatchAccumulator.FlushAsync`'s `_buffer.Clear()`
  (`InsightVmBatchAccumulator.cs:112`) drops only the accumulator's reference.
  The master list still roots the whole enrichment subgraph, so nothing is
  reclaimed.

## Observed impact

Customer run `6a66f147c60e4f5de19a862e` (collector `2.1-6b765e21`, executor
`650.782`, host SWX66601, 2026-07-27 → 2026-07-28):

- 5,541 assets in scope; reached batch 46 of 111 (2,300 assets, 41%).
- 26 process-wide stalls — no API calls, no heartbeats, no log lines — totalling
  5.2h, growing monotonically 3min → 28min in lockstep with assets processed.
- Silent process termination at ~2,300 assets with no managed exception: the
  `catch` at `:141` never logged, and there is no `OutOfMemoryException` in the
  log. The Service found the process already gone via its orphan path
  (`InvalidSessionsManager.cs:182-202`); it did not kill it.
- Executor declares no GC configuration, so default Workstation GC applies —
  single-threaded collection. Pause magnitude implies GC plus paging rather than
  CPU-bound marking.

## Fix

In `processAndWriteAssetFindings`, batch-upload branch, after the
`while (outputQueue.TryDequeue(...))` drain loop (`:190-193`), null the master
list slots for that batch. Ownership transfers to the accumulator, whose
existing `_buffer.Clear()` then drops the last reference whenever its
byte-capped flush lands.

Flushes are byte-cap driven (`InsightVmBatchAccumulator.cs:64`, 50MB) and do
**not** align with batch iterations — real batch files held 7 to 148 rows, not
50. The caller therefore cannot release "after each flush"; it has no way to
know when one happened. Releasing at `AddAsync` time sidesteps that entirely.

## Not in scope

Deferred deliberately, not overlooked — see `constraints.md`: 404 retry policy,
silent-drop paths, per-vulnerability payload deduplication, GC configuration,
and the `O(assets × vulns)` API fan-out throughput problem.
