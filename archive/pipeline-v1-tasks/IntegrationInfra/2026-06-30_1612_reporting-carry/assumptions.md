# Assumptions

- **A1 (VALIDATED):** Reporting's source = outbound Done/Progress models (4) + CollectorEnvelopeBuilder +
  Events-root event-args/sink (3) + CollectorInProcEventHub + Orchestration/Diagnostics (5). Confirmed by
  survey + README.

- **A2 (VALIDATED):** The remaining Common shapes are CollectorStatus + CollectorEventMetadata (the other 3
  — Error/PartialCompletion/RunMetadata — are already in Envelopes.Common). CollectorStatusJsonConverter is
  coupled to CollectorStatus via a `[JsonConverter]` back-edge → co-locate both in Envelopes.Common. This
  COMPLETES Envelopes.Common.

- **A3 (VALIDATED — the Events reckoning):** After this carry, all 6 shared envelope shapes live in
  Envelopes.Common, used by Job (inbound) + Reporting (outbound) + FaultGovernance → genuinely cross-cutting
  → Envelopes.Common is the confirmed shared home. No redistribution. Resolves the deferred Events-shapes question.

- **A4 (VALIDATED):** Diagnostics + Telemetry are a clean reclaim — no Orchestration-internal `using`s
  (verified); Diagnostics already Adapter*-named.

- **A5 (VALIDATED):** Wire-safe — no $type/JsonDerivedType in the outbound shapes/converter/status. D8 rename
  changes C# type names only; JsonPropertyName + CollectorStatus string values stay. (Re-confirm at execution.)

- **A-test (OPEN — scope detail, non-blocking):** Target the testable units: CollectorEnvelopeBuilder
  (BuildProgress/BuildDone), AdapterStatusJsonConverter (string round-trip + invalid), the payload shapes,
  AdapterRunDiagnostics/snapshot if pure. The event-driven in-proc hub may resist clean unit tests — test
  the testable parts; don't force brittle event-wiring tests.

- **A-invariant (VALIDATED → constraint):** best-effort forwarding must not affect publishing — verbatim.
