# Decisions (operator-fixed — inputs, not to be re-debated)

- **D1:** Conversation = the Session layer + reclaimed `AdapterSessionLifecycle` + `RateLimiterHelper` +
  `HttpStatusExtractor`. Carry verbatim on logic/behavior.
- **D2:** Charter — "hold an authenticated, resilient HTTP conversation with a vendor API"; owns
  WITHIN-request resilience; depends on Kernel + http.package + SDK.
- **D3:** Naming is NOT bound by the verbatim rule (precedent: faultgovernance-carry D8). Neutralize any
  identity-bearing (Collector*/vendor-family) names; Session/* names are already mostly neutral.
- **D4:** The http.package/DefensiveToolkit/Authentication leak in the Session surface is EXPECTED and is
  NOT fixed here — the adapter-facing leak is a Conducting concern. No facade over the substrate.
- **D5:** `SessionHandle` dispose ordering is load-bearing — relocate VERBATIM, never re-author.
- **D6:** Already-Kernel items (`TransportErrorHandling/*`, `LogRedaction.cs`, `DataPipelineTelemetry`) are
  rewired, not re-carried.
- **D7:** `CreatePipeline` stays deferred (Conducting), out of scope.
- **D8 (standing — every carry):** SOLID separation, DRY, excellent XML docs, documentation, tests.
- **D9:** Verbatim relocation, namespace-only rewrite, source reference-only — same spirit as prior carries.
