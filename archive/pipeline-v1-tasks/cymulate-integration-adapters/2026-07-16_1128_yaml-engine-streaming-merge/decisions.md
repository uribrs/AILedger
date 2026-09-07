# Decisions

- Execution shape: merge is a SINK DECORATOR over the target stage's topic sink, applied while the target executes — not a sequential stage. The merge stage entry in `stages:` is the declarative attachment point (position still validated: after target).
- Target stage with attached merges runs WITH a sink (the decorator chain terminating in the topic sink) — uses the engine's existing sink-ful page loop, not the sink-less stage mode. This is what makes per-page cursor checkpointing available for free.
- WorkflowCheckpoint gains target-stage cursor state (cursor token / offset / next page, mirroring YamlCollectorCheckpointState fields). Resume: if the checkpoint's completed-stage marker sits INSIDE a merge-target stage, re-enter that stage with the paginator primed from the stored cursor.
- The v1 "discard resume for merge workflows" branch is REPLACED by cursor re-entry; forced-fresh remains only for non-paginated targets (documented) and fingerprint mismatch (existing rule).
- Chaining: decorator stacking in declaration order (first-declared merge innermost/first-applied). Loader lifts the one-merge-per-target rejection; keeps: target earlier, target topic'd, merge stages topicless, no forward refs.
- collect mode grammar: `mode: collect` + `on:` as a YAML list of "path = source_key" expressions (all sharing one source key — loader validates sameness); matches collected into ONE record-level array at `as:`, deduped by canonical source key, source-order-stable.
- embed mode grammar unchanged (`on:` single string). YamlDotNet polymorphic `on:` (string | list) handled at the model/loader layer.
- {{keys}} for a collect-mode page = union of the page's key-set across all listed paths, deduped, comma-joined, batched as before.
- Per-page enrichment failure = page-level failure through the existing error-handling path (no partial-page publish; the checkpoint still points at the failed page, so resume retries it).
- Tests: reshape MergeIntoTests to streaming semantics; every behavioral assertion (join correctness incl. numeric canonicalization, unmatched keep/drop, duplicate last-wins, cache hit/eviction, batch splitting, publish counts/page numbering, checkpoint content) must survive the reshape. New: resume-from-cursor boundary test (no loss, no duplication), chaining test, collect tests.
- Predecessor artifacts remain the record for what shipped in v1; this task's notes only describe the delta.
