# Task: poll-and-drain collection capability (full feature, one task)

Add an async-job "poll-and-drain" capability to the generic CollectorExecutor engine, built generic
(survey of all 15 native collectors showed it recurs: TenableIo + CortexXdr use the async-job interleave;
per-item skip is near-universal). Build ALL four primitives together — no phases.

1. **`poll_and_drain` step** — fused poll + interleaved drain: each cycle poll status, classify it,
   compute new items = (declared drain-path list − already processed), run the inner per-item `step`
   immediately, exit when `until` holds AND the list is drained; short waits in-process, long waits
   externalize (like `poll_until`). Downloads items as they appear during PROCESSING — never idles to a
   terminal status. Modeled on native `TenableIoAssetsFlow.ProcessChunksProgressivelyAsync` + CortexXdr XQL.
2. **Shared item-failure tolerance policy** — skip + per-item retry budget + transient-vs-permanent +
   `max_failure_ratio` abort, used by BOTH `for_each` and `poll_and_drain` (lift today's inline for_each logic).
3. **Terminal-status classification** — `fail_states` set + resource-gone (404 → restart fresh).
4. **`best_effort` hydrate/fetch flag** — emit base records / skip batch on a sub-fetch failure (distinct
   from #2; parent survives a failed child).

Plus: checkpoint/resume of the processed-item set (idempotent re-entry), update `integrations/tenable-io.yaml`
findings to use `poll_and_drain`, and tests + a live Tenable verification.

## Motivation
A live Tenable findings run sat idle ~12 min polling `PROCESSING` with 36+ chunks ready, downloading
nothing, because the YAML model is `poll_until`→then→`for_each` (wait-for-FINISHED). Native interleaves.

## Out of scope
Other deferred audit findings (sparse-page truncation, string-only watermark, request.headers, arg-sprawl),
nested cross-stream join (design-excluded), time-window segmentation. Do not regress conformance/remediation.
