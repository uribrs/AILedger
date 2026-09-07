# Assumptions

- **A1 (VALIDATED):** Job's source = 4 Run models + 3 inbound Logic (2 parsers + hydrator) +
  CollectorTriggerParsing + AdapterTopics + AdapterTimeDefaults. Confirmed by survey + README.

- **A2 (VALIDATED):** The Run side uses exactly ONE Common type — `CollectorRunMetadata` (a plain record,
  no internal deps). NOT CollectorError/Status/EventMetadata (those are outbound/Reporting). Confirmed by
  grep. → carry CollectorRunMetadata to Envelopes.Common as AdapterRunMetadata.

- **A3 (VALIDATED):** Outbound shapes (CollectorEnvelopeBuilder, CollectorStatus + converter,
  CollectorEventMetadata, Done/Progress) are Reporting's, not Job's. Excluded.

- **A4 (VALIDATED):** `AdapterTimeDefaults.DefaultLookback` reads `CollectorGlobalDefaults.DefaultLookbackDays`,
  now `AdapterGlobalDefaults.DefaultLookbackDays` in the merged Emission. Rewire → Job→Emission namespace
  coupling (same assembly, compiles). FLAGGED: the lookback constant is arguably mis-filed in Emission
  (Job/time concern, not egress invariant) → reshape candidate. Carry verbatim with the rewire; do not move
  the constant.

- **A5 (VALIDATED):** AdapterTopics → Job per README. Code consumers: CollectorTriggerParsing (Job) +
  AdapterBusFlowDispatcher (Conducting, later). Placing in Job is consistent (Conducting depends on all).

- **A-wire (OPEN — executor verifies before renaming):** The Run envelope types are deserialized from the
  bus RUN message. Verify NO type-name-based deserialization (`$type` / `[JsonDerivedType]`) before D8
  renaming; JsonPropertyName values stay. If a type name is on the wire, leave it un-renamed + surface.

- **A-test (OPEN — scope detail, non-blocking):** Job is unit-test-friendly (pure parsing). Target the
  parsers, CollectorTriggerParsing, AdapterTimeDefaults, and characterization tests for the hydrator. Reuse
  a new Job test project.

- **A-hydrator (VALIDATED → constraint):** RunPayloadCredentialHydrator carried verbatim incl.
  exception-swallowing; parse→typed-job seam deferred (reshape).
