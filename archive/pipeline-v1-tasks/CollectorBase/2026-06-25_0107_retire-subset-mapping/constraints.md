# Constraints

- Passthrough is the ONLY emit behavior: ResponseMapper.Map returns the full record element, always.
- No field projection, no sourceType injection, no correlation-key injection in the emit path.
- emit_mode values: verbatim (DEFAULT) | typed_wrapper | source_type_prefix. `mapped` removed.
- Remove MappingSpec.Fields and CorrelationKeys (model + synthetic-step builder + validation). YAML loader
  IgnoreUnmatchedProperties tolerates leftover keys in profiles; strip them from shipped profiles for cleanliness.
- Envelope mechanism (typed_wrapper/source_type_prefix + templated envelope_fields, discriminator-wins-on-collision) stays exactly as-is.
- Keep the IRecordMapper seam + registry; Map may be simplified to passthrough (drop unused params) if clean.
- ValidateMapping: records_path required for emitting steps; type_value required for wrapper/prefix; envelope_fields only for source_type_prefix; type_value rejected on verbatim; no correlation-keys requirement.
- Runner stays thin orchestration (per Execution/README.md) — no vendor branches; vendor shape lives only in YAML.
- No regression: poll-and-drain, SDK conformance (bus routing/ResumeAsync/per-emit-target page counter/RUN-envelope ingress), the envelope feature, reserved `__`-prefix capture guard. Full suite green.
- net8.0; CollectorBase.slnx; reuse Shared egress; async-only.
- All 5 shipped profiles must validate and emit full records.
