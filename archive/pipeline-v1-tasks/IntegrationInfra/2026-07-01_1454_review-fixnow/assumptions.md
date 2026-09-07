# Assumptions

- A1 — Scrubbing the exception's stored `BodySnippet`/`Url` (not just `Message`) is desired, since both are logged/published. Status: OPEN — confirm no downstream consumer needs the raw body/URL; if one does, scrub only `Message` and document. (Lean: scrub all three — the exception is treated as loggable/publishable.)
- A2 — The `AdapterBusFailurePublisher` publish calls whose token changes to `None` are all terminal last-words (error + failure completion). Status: OPEN — confirm by reading before switching the token; do NOT change a legitimately-cancellable publish.
- A3 — `AdapterHttpClient.ThrowFailure` is reachable in a test at some seam (drive a failing `HttpResponseMessage`, or test the smallest composition seam that builds the exception message/properties). Status: OPEN — determine the cheapest non-brittle seam during execution.
- A4 — `CollectorResumeFailurePublisher` has the same callback-before-publish pattern as `AdapterBusFailurePublisher`. Status: OPEN — confirm by reading; guard only where the pattern actually exists.
- A5 — C4's `Unspecified` inputs should be treated as already-UTC (not thrown). Status: VALIDATED (operator decision D-C4) — matches the current effective behavior for Unspecified, only fixing the Local case.
