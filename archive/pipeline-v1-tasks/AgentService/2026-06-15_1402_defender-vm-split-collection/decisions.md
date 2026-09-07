# Decisions

- Keep the fix local to Defender VM collection + parser wiring; do not touch the shared uploader or Cortex.
- NDJSON byte cap is 50MB (not Cortex's 30MB); reuse Cortex only as a structural pattern reference.
- Byte accounting uses UTF-8 byte count + 1 newline, never string length.
- Findings collection must emit BOTH assets and findings lanes (split mode requires both).
- Parsers: Endpoint parser shares raw split INPUT handling with Defender VM, but keeps its own downstream OUTPUT mapping — no output aliasing.
- Hydration services may stay in the repo but must be removed from the findings execution path.
