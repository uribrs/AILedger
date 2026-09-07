# Constraints

- Behavior preserved verbatim — namespace-only rewrite + the rewires + the D8 renames. No logic change.
- INVARIANT: the in-proc forwarding (CollectorInProcEventHub) is best-effort and MUST NOT affect publishing
  semantics — preserve verbatim.
- Source repo `/Users/user/Dev/Uri/localprojects/IntegrationsInfra` is reference-only — do not mutate.
- Kernel, Emission, Job, and the 3 EXISTING Envelopes.Common shapes are not modified — only the 2 new
  shapes + converter are added to Envelopes.Common. Reporting depends on these, never the reverse.
- D8 naming: neutralize the listed Collector*/ICollector* type names → Adapter*/IAdapter*. Wire-safe:
  no $type/JsonDerivedType; JsonPropertyName values AND CollectorStatus string values
  ("success"/"failed"/"partial") unchanged. Re-confirm $type-absence during execution.
- Co-locate AdapterStatus + AdapterStatusJsonConverter (the [JsonConverter] back-edge couples them).
- net8.0; build clean + tests pass; XML docs on every public member; no vendor identity introduced.
