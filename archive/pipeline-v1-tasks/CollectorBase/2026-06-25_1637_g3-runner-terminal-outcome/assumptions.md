# Assumptions

- A1 — The externalized long-delay path (ExternalizeServerSuggestedDelays=true, delay > MaxServerSuggestedDelay)
  raises `ServerSuggestedRetryDelayException` → `Runner.cs:204` returns a PartialResult IMMEDIATELY (no in-process
  sleep), so a 120s-Retry-After test completes fast. STATUS: OPEN — confirm during execution (Shared honors the
  cap by externalizing, not sleeping then externalizing). If it would actually sleep, lower the test threshold so
  the test stays fast and document.

- A2 — A failing `IAdapterDataPublisher` test double (succeeds on page 1, fails on page 2) can be injected via the
  same service-provider seam `CreateMockContext` uses for `CapturingAdapterDataPublisher`, and the
  `CollectorNdjsonPublisher` surfaces a non-Success `publishResult`. STATUS: OPEN — confirm the publish seam +
  the page-2 failure reaches `Runner.cs:350`. A `FlakyStreamPublisher` existed before (removed); reintroduce a
  minimal one.

- A3 — A multi-page stream is needed for A-M2 (page 1 emits, page 2 publish fails). STATUS: OPEN — use an existing
  multi-page profile (e.g. the cursor scroll that yields ≥2 pages) so page 1 succeeds before page 2 fails.
