# Assumptions

- A1 — `drain_path` ListAt (`Runner.cs:654`, poll_and_drain item-id list) left on non-flattening `ListAt`.
  STATUS: VALIDATED → left as-is. Shipped poll_and_drain drain paths are flat chunk-id arrays
  (`chunks_available`), not crossing a repeated parent; flattening it would need its own test + consumer.
  Out of this group's scope.

- A2 — the two nits DOCUMENTED, no behavior change. STATUS: VALIDATED.
  (i) `ListAtFlattened` exact-literal-key check at every object level is intentional for records-style paths
  (kept; no profile relies on the `At` root-only difference). (ii) `ResponseMapper.Map` array-of-arrays one-level
  flatten is a deliberate bound (already documented). No code change to the resolver.

- A3 — capture-SCALAR (`capture:`) and single-node nav (cursor/next_token_at/watermark/next_url) stay on
  `At`/`StringAt`, unchanged/non-flattening. STATUS: VALIDATED. `At`/`StringAt` textually untouched; locked by
  the existing `Flatten_IsRecordsOnly_SharedNavStillSingleNode` test + the full pagination/capture suite staying
  green (77/77).
