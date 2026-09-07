# Decisions

- Extension over modification for all engine work; opt-in flags gate every behavior
  change. (User-imposed after live-run gap analysis, 2026-07-21.)
- Implementation order: item 1 (floor bug fix) → item 2 (group merge) → item 3
  (first_of/regex_extract) → item 4 (prefetch) → item 5 (completeness guard).
  Smallest/least-risky first; parity rerun after all land.
- merge_into N:1 fan-in is a new opt-in mode (`group: true`), not a change to the
  match dictionary semantics — flag-off keeps single-slot last-wins including its log line.
- Prefetch is the fix for the stale-cursor truncation; reactive cursor_recovery cannot
  detect it (vendor returned HTTP 200 + short tail, not 404). (Live-verified 2026-07-21.)
- Silent truncation is converted to loud failure via the opt-in scroll-completeness
  assertion, independent of prefetch.
- Correlated-record envelope stays `{chunk, isLastChunk, aid, host, findings[]}`;
  `findingsInChunk` and byte-order parity remain out of scope (parser verified to not
  read `findingsInChunk`; detection = `isLastChunk` + `host` presence, spine = chunk==0,
  join by record-level aid).
- Falcon spine filter stays native-identical (`last_seen_timestamp:>=` floor only);
  aid coverage comes from item 3, not from an entity_type filter. (Reversed 2026-07-21
  after live filter verification.)
- Task artifacts live in the adapters repo (`ai/active/…`); the YAML + schema sync
  touches cymulate-magic-integration.
