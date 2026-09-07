# Decisions

- One new step kind `poll_and_drain` (fuses poll + drain), not a flag on poll_until/for_each — clearer YAML and checkpoint shape.
- Item-failure tolerance extracted into ONE shared mechanism consumed by for_each AND poll_and_drain; add a per-item retry budget; keep transient→propagate / permanent→skip+count + max_failure_ratio abort.
- Terminal-status classification folded onto the poll step: `until` (success) + `fail_states` (terminal failure) + resource-gone (404 → restart fresh, reuse restart_if_stale semantics).
- `best_effort` is a separate flag on hydrate/fetch (parent survives a failed child) — implemented but kept independent of the item-tolerance policy.
- Processed-item set + captured handles + poll anchor persisted in CheckpointState; resume re-enters poll_and_drain and continues draining idempotently. Inner fetches reuse the per-emit-target page counter.
- tenable-io.yaml findings converted to poll_and_drain (the live driver of the feature); assets optional.
- Direct execution path expected (one coherent feature concentrated in the runner + profile model); orchestrator decides.
- Build the whole feature in one task (user directive: no phases/parts).
