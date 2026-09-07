# Task — Retire subset mapping (passthrough-only emit)

CollectorExecutor must care nothing about response content: the YAML decides shape via the REQUEST;
**what is gotten is published**. Retire the field-subset `mapped` emit mode entirely.

- The engine always emits the FULL record element. No field projection, no `sourceType` injection, no
  correlation-key injection.
- The only per-stream shaping knob is the **envelope**: `verbatim` (default, bare record),
  `typed_wrapper` (`{type,data}`), `source_type_prefix` (`{sourceType, …record}`). The envelope mechanism
  (incl. templated `envelope_fields`) is unchanged.
- Remove `MappingSpec.Fields`, the `mapped` emit_mode, and correlation-key injection. `mapping:` becomes
  `records_path` + envelope only.
- Convert all shipped profiles to passthrough (Cortex loses its field-picking — explicitly accepted).
- Update tests + yaml-contract.md.

Rationale: native collectors all do get-data→publish-data; subset mapping is a POC artifact that risks
data loss and contradicts the data-bits parity goal.
