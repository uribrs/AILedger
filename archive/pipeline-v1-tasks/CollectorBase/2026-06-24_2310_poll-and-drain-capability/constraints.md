# Constraints

- Build all four primitives in THIS task; no phases/parts; execute to completion (impl + build + tests + live verify).
- Generic engine — NO vendor identity in the runner; all vendor specifics stay in YAML. Model on native
  (TenableIo ProcessChunksProgressivelyAsync; CortexXdr XQL) but hardcode nothing vendor-specific.
- `poll_and_drain` must DOWNLOAD INTERLEAVED — drain newly-available items each poll cycle during PROCESSING,
  not after a terminal status. Exit only when the success `until` holds AND no undrained items remain.
- Item-failure tolerance is ONE shared mechanism used by both `for_each` and `poll_and_drain` (skip +
  per-item retry budget + transient-vs-permanent + max_failure_ratio). Transient → propagate (resume re-runs);
  permanent → skip-and-count.
- `best_effort` (P4) is a DISTINCT concern from item tolerance (P2): parent record survives a failed child
  fetch. Keep it cleanly separable.
- Checkpoint ordering invariant preserved (state persisted BEFORE AdvancePage). Resume restores the
  processed-item set; no re-download of processed items, no skipped items. Inner fetches use the existing
  per-emit-target monotonic page counter (output stays contiguous).
- Do NOT regress conformance (full interface set, ProcessAsync→AdapterBusEntrypointRunner, ResumeAsync→
  CollectorResumeRunner, per-target page counter, RUN-envelope ingress) or the remediation fixes (tolerant
  token via JsonNav.StringAt, size clamps, single Prepare, no FetchSignal). Reuse Shared; async all the way
  (no .GetAwaiter().GetResult()/.Result/.Wait()/Thread.Sleep).
- net8.0; solution CollectorBase.slnx.
- Touch: Profile/Profile.cs, Execution/CollectorExecutorRunner.cs, Checkpointing/CheckpointState.cs,
  integrations/tenable-io.yaml, Tests/CollectorExecutor.Test. Strategy registry resolution unchanged.
