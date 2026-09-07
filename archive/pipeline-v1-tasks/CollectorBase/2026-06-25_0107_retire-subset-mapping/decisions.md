# Decisions

- Passthrough is the only emit behavior; `mapped` field-subset mode is retired entirely.
- The envelope (verbatim | typed_wrapper | source_type_prefix) is the sole per-stream shaping knob; default verbatim.
- `MappingSpec.Fields` and correlation-key injection are removed from the emit path and the model.
- `mapping:` reduces to `records_path` + envelope config.
- Cortex's field-picking is dropped (accepted data-shape change); Cortex keeps its native sourceType envelope.
- The IRecordMapper seam is kept; Map simplified to passthrough where clean.
- Runner stays thin orchestration; no vendor branches.
- yaml-contract.md updated to "always passthrough — the request shapes the data; response content is published as-is."
