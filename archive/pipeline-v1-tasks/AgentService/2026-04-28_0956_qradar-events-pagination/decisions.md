* Page QRadar event results at the result endpoint instead of limiting the AQL query.
* Keep the page size local to `QRadarApi` as an implementation detail.
* Use `Content-Range` when available to determine completion, and fall back to short page detection when needed.
* Advance the next result request from `Content-Range.To + 1` when QRadar supplies `Content-Range`; otherwise advance by the configured page size.
* Treat later-page fetch failures as failed result retrievals by propagating exceptions to the caller's cache-error path.
* Preserve the QRadar connection-check search predicate (`UTF8(payload) ILIKE '%test%'`) but remove its query `LIMIT`.
* Do not deep-clone QRadar `events` pages after JSON parsing; use the parsed `JArray` directly.
* Parse `Content-Range` positions and totals as `long` and ignore malformed or oversized values by falling back to short-page detection.
* Reset QRadar's `mStillInProgressCounter` at the start of each search id result-fetch workflow.
