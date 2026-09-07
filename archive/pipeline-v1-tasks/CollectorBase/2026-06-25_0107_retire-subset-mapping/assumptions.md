# Assumptions

- **A1** — Passthrough-only is the target; the field-subset `mapped` mode is retired (not kept as a legacy
  opt-in). Status: VALIDATED (explicit user directive: "what is gotten is published").

- **A2** — Cortex's field-picking is dropped. Cortex native (`CortexXdrRecordFormatter`) STAMPS a `sourceType`
  discriminator (values incl. `va_cves`, `endpoint`, + a third) onto the full record → maps to
  `source_type_prefix` per stream. Status: OPEN — confirm the exact discriminator values + per-stream mapping
  by glancing at CortexXdrRecordFormatter during execution; if a stream stamps nothing, use verbatim.

- **A3** — Falcon + Qualys native emit the full record verbatim (no envelope) — Falcon AssetsPageParser does
  `SerializeToSingleLine(item)`. Status: OPEN — confirm during execution; default verbatim, add envelope only
  if the native stamps/wraps.

- **A4** — Removing `CorrelationKeys` from the C# model will not break profile loading: `ProfileLoader` uses
  `IgnoreUnmatchedProperties`, so leftover `correlation_keys:` in any YAML is silently ignored. Status: OPEN —
  confirm the loader builder still ignores unmatched after the field is gone.

- **A5** — Simplifying `IRecordMapper.Map` to drop `correlationKeys`/`sourceType` params is clean (only
  `DotPathMapper` + the runner call sites consume it). Status: OPEN — confirm no other caller; if churny, leave
  the signature and just ignore the params.

- **A6** — Watermark/pagination are unaffected: watermark reads the record's top-level field; under passthrough
  the record is the full element, so the field is present. Status: VALIDATED (already true for the envelope work).

- **A7** — Tests asserting projected field subsets (vendor harness tests + Emit_Mapped_*) will be rewritten to
  assert full-record passthrough; the count may drop as redundant mapped-mode tests are removed. Status: OPEN —
  resolved during execution; target = build clean + full suite green, not a fixed test count.
