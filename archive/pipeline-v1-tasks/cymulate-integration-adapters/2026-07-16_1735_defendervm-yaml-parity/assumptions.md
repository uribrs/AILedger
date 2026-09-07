- A1 [OPEN] Engine record emission preserves vendor property order and JSON scalar
  formatting such that label-first injection yields canonical parity with native
  NormalizedUtf8Json output. Verify via round-trip fixture gate.
- A2 [VALIDATED] Real nextLink shape is a full URL (?pagesize=10000&$skiptoken=...);
  body_cursor cursor_regex + follow_url can drive it (log-proven 2026-07-16 capture).
- A3 [VALIDATED] recommendationReference is a vendor field on inventory records
  (962,932/963,154 records carry it) — fan-out input for batch 2.
- A4 [OPEN] Streamed root-array passthrough path (dev PR #281) emits records that the
  annotate injection site can stamp without full parse (byte splice after '{').
  Verify against the streaming ingress code before implementing.
