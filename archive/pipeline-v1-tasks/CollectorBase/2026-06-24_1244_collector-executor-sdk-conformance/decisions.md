# Decisions

- Big-bang refactor (not staged) — approved by user; POC lab, contained risk.
- Conform to the native collector contract SHAPE exactly; CollectorExecutor becomes a peer of Falcon/Qualys/DefenderVm, differing only in delegate bodies (generic interpreter) and `TRequest`.
- Introduce a generic `CollectorExecutorRequest` as `TRequest` carrying the parsed profile + payload (yaml/inputs/config).
- Existing `CollectorExecutorRunner` step loop is re-housed as the body of `CollectAssetsAsync`/`CollectFindingsAsync`, driven by the bus-provided progress context — interpreter logic is preserved, not rewritten.
- Page numbering: per-emit-target monotonic counter persisted in `CheckpointState`, decoupled from pagination cursor; byte-sliced to the egress `MaxBytesPerBatch`. This is the contract-correct fix for the for_each collision (no special-case patch).
- Resume idempotency mirrors native: persist the page-counter base at chunk/step start so mid-item re-runs re-publish the same page numbers (deterministic overwrite, no gaps/dupes).
- Out of scope (explicitly deferred): production RUN-envelope ingress; local Runner passthrough (already done).
