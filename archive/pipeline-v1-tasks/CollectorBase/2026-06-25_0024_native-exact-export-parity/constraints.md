# Constraints

- Generic engine: NO vendor identity in the runner/mapper — all vendor shape stays in YAML.
- Envelope/passthrough is selected per emitting step in YAML (snake_case via YamlDotNet); new fields validated at load.
- Existing `fields`-mapped emit mode stays the DEFAULT and unchanged (backward-compat).
- `verbatim` mode injects NOTHING (no `sourceType`, no correlation keys) — native Tenable parity.
- `typed_wrapper` = `{type:<v>, data:<full record>}`; `source_type_prefix` = `{sourceType:<v>, ...full record}` (full record flattened after the discriminator), matching `DefenderVmRecordFormatter`.
- Optional envelope metadata fields are templated (e.g. `recommendation_reference: "{{rec_id}}"`); resolved from the step's token scope.
- Do NOT build a new Defender pagination strategy — `next_url` already replicates native OData nextLink + `$skip`/`$top` fallback.
- No regression: poll-and-drain, SDK conformance (bus routing, ResumeAsync, per-emit-target page counter, RUN-envelope ingress), remediation fixes, reserved `__`-prefix capture guard. Full suite stays green.
- net8.0; solution CollectorBase.slnx; reuse Shared egress (CollectorNdjsonPublisher, byte-sliced page counter).
- Model behavior on native source (read-only reference); hardcode no vendor specifics in the runner.
- Defer cortex-xdr, crowdstrike-falcon, qualys profiles — do not rework unless trivial.
