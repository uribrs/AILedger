# Decisions

- Build all four capabilities including chunk framing — the phase goal is byte-identical envelopes. (User "go", 2026-07-22; may veto.)
- Implementation order B -> C -> D -> A: easiest/highest-value first (B closes the only parser-visible gap), the moderate chunk-framing item last.
- `sort.order` first/only mode = `serialized_ordinal` (native-exact OrderBy on serialized element, ordinal ascending). Room for a `by_field` mode later without a new grammar family. (User "go"; may veto.)
- `require_key` is a `merge_into` field, NOT a generic mapping-level "drop row when field null" — contained, less surface, no spine-publish-path change. (User "go"; may veto.)
- All four are opt-in fields on the existing `merge_into` block; flag-off is byte-identical. No new YAML syntax family.
- Chunk framing owns chunk/isLastChunk/findingsInChunk; when it is configured the Falcon spine op stops annotating chunk/isLastChunk.
- Egress is not touched — research verdict placed chunk framing above egress; the "50 MiB fail-fast" is a myth (per-part flush threshold).
- Per-batch dedupe-by-id is out of scope (booked) — only relevant on cursor-recovery overlap; parser dedupes findings by id downstream.
- Task artifacts live in the adapters repo; the YAML + schema sync touches cymulate-magic-integration. Same falcon-strategy-parity branch in both.
