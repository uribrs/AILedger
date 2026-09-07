# Decisions (operator-fixed — inputs, not to be re-debated)

- **D1:** Reporting = outbound Done/Progress envelopes + CollectorEnvelopeBuilder + event-args/ICollectorEventSink
  + CollectorInProcEventHub (reclaimed) + Diagnostics (reclaimed). Carry verbatim on logic.
- **D2:** Charter — "emit progress, completion, and error signals as the run proceeds." Depends on Kernel + SDK.
  INVARIANT: best-effort in-proc forwarding must not affect publishing semantics.
- **D3:** Naming not bound by verbatim (D8). Neutralize the Collector*/ICollector* type names → Adapter*/IAdapter*.
  Wire-safe; CollectorStatus string values + JsonPropertyName values unchanged.
- **D4:** Completes Envelopes.Common: CollectorStatus→AdapterStatus, CollectorEventMetadata→AdapterEventMetadata,
  CollectorStatusJsonConverter→AdapterStatusJsonConverter (co-located with the enum). 
- **D5 (Events reckoning, resolved):** Envelopes.Common is the confirmed home for all 6 shared envelope shapes
  (used by Job + Reporting + FaultGovernance). No redistribution; the deferred Events-shapes question is closed.
- **D6:** Diagnostics + Telemetry are a clean reclaim from Orchestration (no internal drag).
- **D7 (standing — every carry):** SOLID separation, DRY, excellent XML docs, documentation, tests.
- **D8:** Verbatim relocation, namespace-only rewrite, source reference-only — same spirit as prior carries.
