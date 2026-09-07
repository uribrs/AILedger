# Decisions (operator-fixed — inputs, not to be re-debated)

- **D1:** Job = inbound RUN envelope models + parsers + credential hydrator + CollectorTriggerParsing +
  AdapterTopics + AdapterTimeDefaults. Carry verbatim on logic.
- **D2:** Charter — "resolve an inbound RUN message into a fully-formed job." Depends on Kernel + SDK
  (+ a flagged Emission edge for the lookback constant).
- **D3:** Naming not bound by verbatim (precedent: D8). Neutralize the Collector* RUN-envelope/parser/
  trigger/metadata names → Adapter*. Wire-safe (JsonPropertyName values unchanged); verify no $type/
  JsonDerivedType before renaming a deserialized type.
- **D4:** RunPayloadCredentialHydrator (untyped dict + exception-swallowing) carried VERBATIM; the
  parse→typed-job seam is a deferred reshape candidate.
- **D5:** CollectorRunMetadata → Envelopes.Common as AdapterRunMetadata (alongside AdapterError) — the
  shared envelope-shapes area; final home settles in the Events carve.
- **D6:** AdapterTimeDefaults rewires to Emission.AdapterGlobalDefaults.DefaultLookbackDays — a Job→Emission
  coupling. FLAGGED: DefaultLookbackDays is arguably mis-filed in Emission (reshape candidate). Carry
  verbatim; do not move the constant (would change merged Emission).
- **D7 (standing — every carry):** SOLID separation, DRY, excellent XML docs, documentation, tests.
- **D8:** Verbatim relocation, namespace-only rewrite, source reference-only — same spirit as prior carries.
