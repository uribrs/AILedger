# Task: Streaming Merge — resume forward progress, chaining, aggregate embed

Rework `merge_into` execution from held-replay to a streaming enrichment sink so merge
workflows make forward progress across resume (must-fix), and extend it with merge
chaining (multiple merges per target) and an aggregate embed mode (`mode: collect`).

Motivating consumers (never named in code): dual-enrichment VM vendors (host-details +
KB per detection) and aggregate-enrichment vendors (asset + collected definitions array).

## In scope
- `MergeEnrichmentSink` decorator over the target stage's topic sink: per fetched page —
  extract keys → batched keyed fetch → embed → publish → checkpoint with the target's
  vendor pagination cursor. Merge stage declaration = attachment point, not an execution step.
- Resume from the checkpointed target cursor (`WorkflowCheckpoint` extension).
- Chaining: N merges per target, stacked in declaration order; later merges see earlier enrichment.
- `mode: collect`: `on:` as a list of target-key-path expressions; all distinct matches
  collected into one record-level array field (`as:`), deduped by source key.
- Schema additions for `mode` / `on`-as-list; loader validation updates.
- Test reshape to streaming semantics preserving all behavioral assertions; new resume/
  chaining/collect tests.

## Out of scope
- Yaml file changes (follow-up), version bump, ISB/Shared/adapter-surface changes,
  durable spill of enrichment cache (rebuilds lazily by design).
