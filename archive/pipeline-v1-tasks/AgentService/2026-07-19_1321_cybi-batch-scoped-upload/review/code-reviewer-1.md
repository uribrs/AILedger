# Code Review — cybi-batch-scoped-upload (pass 1)

Reviewer: isolated code-reviewer subagent (minimal context — diff only, no contract/verifier exposure).
Scope: uncommitted working tree vs HEAD; 36/36 CybiBatch tests run by reviewer; executor builds;
binary-compat of Infrastructure.Common additions checked (ICybiBatchUploader untouched, additive only).

## Blockers
None.

## Important — both REPAIRED
- I1 `CybiBatchUploader.cs` — armed entry points could throw `OperationCanceledException` from the
  gate wait, violating the never-throws contract collectors compiled against.
  **Fixed**: gate acquisition wrapped, cancellation returns `false` (both public entry points).
- I2 `CybiActionManager.cs:39` — new ctor param inserted mid-list against the parameter-ordering
  rule. **Fixed**: moved to end of real params, before the logger tail.

## Suggestions — dispositions
- S1 (gate released between final-close append/send/commit; race if uploads ran concurrently with
  completion): ACCEPTED RISK — unreachable today (single post-flows call site), documented in
  `BatchScopeState` doc comment.
- S2 (armed runs serialize uploads under one semaphore): ACCEPTED trade-off — correctness of
  counters/close decision over parallelism; opt-in per run; uploads are network-bound.
- S3 (scope entries never removed): documented — one action run per executor process; comment added.

## Nits
- N1 (doc comment referenced task artifact "A1"): **Fixed** — reworded to reference the
  CyAgentServer coordination directly.

## Open questions (forwarded to the server-half effort)
1. `itemCount` semantics: per-file on batch_file uploads vs folder-total on the completion close —
   backend must not double-count (already recorded in execution_notes.md).
2. Sequence ids are monotonic but not gap-free (consumed on failed sends) — consumer's guard is
   `$lt`, so gaps are safe; confirm no gap-free expectation server-side.
3. Opt-in key `batchScopedUpload` ships agent-first, dormant-safe; coordinate the CyAgentServer half.

Post-repair: build succeeded, 207/207 Infrastructure.Common tests pass.
