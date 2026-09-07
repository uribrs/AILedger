# Task: Job carry (inbound RUN envelope + parse/hydrate + triggers)

Carry the Job concern into IntegrationInfra, behavior verbatim (namespace-only rewrite + rewires + D8
naming neutralization). Job = "resolve an inbound RUN message into a fully-formed job — which stream,
since when, whose credentials, where to deliver."

## In scope (→ src/IntegrationInfra/Job/, flat ns Cymulate.IntegrationInfra.Job)
- Run models (4): CollectorRunAction, CollectorRunEnvelope, CollectorRunStartEnvelope, CollectorRunStartPayload
- Inbound Logic (3): CollectorRunEnvelopeParser, CollectorRunActionParser, RunPayloadCredentialHydrator
- CollectorTriggerParsing (from Orchestration/Collectors/Triggers)
- AdapterTopics (routing vocabulary, from Glossary)
- AdapterTimeDefaults (base-date/lookback, from Time)
- +1 shared shape → Envelopes.Common: CollectorRunMetadata (the only Common type the Run side uses)

## D8 naming (neutralize → Adapter*, wire-safe pending $type check)
CollectorRun{Envelope,Action,StartEnvelope,StartPayload} · CollectorRun{Envelope,Action}Parser ·
CollectorTriggerParsing · CollectorRunMetadata → Adapter*

## Out of scope
- Outbound/Reporting: CollectorEnvelopeBuilder, CollectorStatus(+JsonConverter), CollectorEventMetadata, Done/Progress.
- The parse→typed-job reshape of the hydrator (leaky untyped-dict + exception-swallowing) — verbatim, flag only.
- Moving DefaultLookbackDays out of Emission (reshape — flag only).

## Deliverables
1. Verbatim relocation (logic) under Job/, flat ns; AdapterRunMetadata in Envelopes.Common.
2. D8 renames; rewires (Common→Envelopes.Common; AdapterTimeDefaults→Emission.AdapterGlobalDefaults).
3. XML docs; Job/README.md extended (incl. the flagged Job→Emission coupling + the hydrator seam).
4. Tests: parsers, CollectorTriggerParsing, AdapterTimeDefaults, hydrator characterization.
5. Build clean; tests pass; Kernel + Emission untouched.
