# Assumptions

- A1 — VALIDATED: poll-and-drain + per-item skip recur across the native collectors (survey of all 15): async-job interleave in TenableIo + CortexXdr; per-item skip/continue in ~9. So a generic, decomposed primitive is warranted (not Tenable-specific).
- A2 — VALIDATED: the existing `poll_until` (RunPollUntilAsync) and `for_each` (RunForEachStepAsync) give the building blocks — poll loop + in-process/externalize wait + capture_list + per-item run + continue_on_item_failure/max_failure_ratio. poll_and_drain fuses them; the item-tolerance is lifted into a shared mechanism.
- A3 — VALIDATED: inner fetches publish via the per-emit-target monotonic page counter (AssetsPage/FindingsPage) already in RunContext/CheckpointState, so interleaved drains stay contiguous; resume restores counters.
- A4 — OPEN: exact persistence shape for the processed-item set on resume — likely a reserved CaptureLists key or a new CheckpointState field; resolve in execution (must round-trip + be idempotent on mid-drain re-entry).
- A5 — OPEN: whether `poll_and_drain` externalizes long waits the same way `poll_until` does (PartialResult, resume re-enters with the processed set restored) — confirm the externalize path carries the processed set + poll anchor through the checkpoint.
- A6 — VALIDATED: live Tenable creds work (used in prior tasks); a live findings run is feasible to verify interleave (note: creds are burned/in-transcript — rotate after).
