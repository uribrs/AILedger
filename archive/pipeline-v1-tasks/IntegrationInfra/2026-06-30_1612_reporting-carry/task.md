# Task: Reporting carry (outbound envelopes + telemetry hub + diagnostics)

Carry the Reporting concern into IntegrationInfra, behavior verbatim (namespace-only rewrite + rewires +
D8 naming neutralization). Reporting = "emit progress, completion, and error signals as the run proceeds."
This carry also COMPLETES Envelopes.Common (the parked-shapes reckoning).

## In scope
- → src/IntegrationInfra/Reporting/ (flat ns Cymulate.IntegrationInfra.Reporting):
  - Outbound models (4): Done/{CollectorDoneEnvelope,CollectorDonePayload}, Progress/{CollectorProgressEnvelope,CollectorProgressPayload}
  - CollectorEnvelopeBuilder (BuildProgress/BuildDone)
  - Events root (3): BatchProducedEventArgs, CheckpointAdvancedEventArgs, ICollectorEventSink
  - CollectorInProcEventHub (in-proc event hub/forwarder, reclaimed from Orchestration/Collectors/Telemetry)
  - Diagnostics (5, reclaimed from Orchestration/Diagnostics; already Adapter*-named)
- → src/IntegrationInfra/Envelopes/Common/ (completes it): CollectorStatus→AdapterStatus,
  CollectorEventMetadata→AdapterEventMetadata, CollectorStatusJsonConverter→AdapterStatusJsonConverter
  (co-located with the status enum it serializes).

## D8 (neutralize → Adapter*/IAdapter*, wire-safe)
Done/Progress envelopes+payloads, EnvelopeBuilder, Status(+JsonConverter), EventMetadata, InProcEventHub,
ICollectorEventSink→IAdapterEventSink. Diagnostics already neutral. Do NOT change CollectorStatus string
values ("success"/"failed"/"partial") or JsonPropertyName values.

## Out of scope
- Already-carried shapes (AdapterError/PartialCompletion/RunMetadata in Envelopes.Common), inbound (Job).
- Any reshape; any change to the best-effort-forwarding semantics.

## Deliverables
1. 14 files → Reporting/ + 3 → Envelopes.Common; logic verbatim.
2. D8 renames; rewires (Common→Envelopes.Common); Diagnostics/Telemetry reclaimed clean.
3. XML docs; Reporting/README.md extended (incl. Envelopes.Common completion + the resolved Events reckoning).
4. Tests: EnvelopeBuilder, AdapterStatusJsonConverter, payload shapes, diagnostics.
5. Build clean; tests pass; Kernel+Emission+Job+existing Envelopes.Common shapes untouched.
