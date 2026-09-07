# Role

Act as the senior .NET architect and implementation owner for the YamlCollector execution-engine restructuring. Preserve existing behavior and domain language while converting orchestration into explicit, sequential, composable execution plans.

# Goal

Design and incrementally implement an internal block architecture for YamlCollector in which the unchanged YAML vocabulary compiles into an immutable execution plan and runs with strict global sequentiality. TPL Dataflow may be used as the internal substrate after its dependency is approved, but must not become the YAML language or public/domain contract.

# Context

The YAML bank at `/Users/user/Dev/cymulate-magic-integration/integrations` contains 279 surveyed definitions and 885 operations. Direct operations dominate; `defender-vm.yaml` and `tenable.io.yaml` are the current real workflows. The engine already supports authentication, request templating, pagination, mapping, hydration, error rules, workflows, streaming publication, checkpointing, and streaming merge enrichment. Their implementation is spread across mutable runners and conditional orchestration.

Two completed predecessor tasks record uncommitted baseline work on the current branch: declarative enrichment and streaming merge. Read their task directories and the live source before editing. Use `task.md` in this directory as the phased plan and `state.json` as the source of execution status.

# Constraints

- Do not change any YAML file, syntax, vocabulary, default, or intended behavior.
- Enforce no parallelism and no pipeline overlap within a collector execution.
- Preserve current adapter, Shared, ISB, output layout, progress, recovery, and done-event contracts.
- Keep the standalone engine independent from Cymulate/Shared references.
- Preserve all predecessor work and reconcile concurrent changes before editing overlapping files.
- Compile the existing validated model into immutable domain plan nodes; do not expose TPL/Dataflow types as domain or public contracts.
- Preserve per-page streaming merge semantics and checkpoint ordering.
- Carry retry, durable defer, cancellation, and failure as explicit domain outcomes across workflows.
- Do not add vendor conditionals, parallel/DAG vocabulary, speculative framework layers, or permanent dual executors.
- Characterize behavior before replacement; every phase must be buildable, reviewable, and verified.
- Do not begin product-code implementation until DR1–DR3 in `decisions.md` are resolved and the user authorizes execution.

# Success Criteria

- All 279 surveyed YAML definitions load and compile without modification.
- Existing YAML vocabulary has identical observable meaning; no consumer migration is required.
- A single execution never has overlapping block delegates, requests, mapping, publication, or checkpoint commits.
- Direct operations and workflows execute through immutable plans with behavior parity for auth, templating, pagination, mapping, hydration, error controls, streaming, publication, progress, and recovery.
- Defender VM and Tenable.io workflow behavior passes parity tests, including poll, capture, foreach, topics, resume, retry, and defer.
- CrowdStrike Falcon, Qualys, and InsightVM Cloud representative behavior passes collector/contract-level validation.
- Streaming merge keeps per-page enrichment, chaining, collect/embed modes, cursor resume, and no held replay.
- Resume tests prove no loss, duplication, output collision, or mixed host/engine accounting, including fingerprint mismatch.
- Expected defer/retry outcomes are no longer flattened into generic workflow failures.
- The legacy orchestration path and mutable runner result state are removed after verified cutover.
- The accepted implementation is reconciled with the second engine home according to an explicit ownership decision.
- Full relevant .NET test suites pass and verification evidence is recorded in this task directory.

# Execution Rules

1. Start with Phase 0 in `task.md`; read the current tree and predecessor records first.
2. Update `state.json` before and after each phase. Record assumptions as OPEN, VALIDATED, or REJECTED and append decisions rather than silently changing intent.
3. Resolve DR1–DR3 before implementation and DR4 before checkpoint cutover.
4. Create characterization tests before changing the behavior they describe.
5. Keep legacy and new executors mutually exclusive during migration; never dual-publish or compare by producing external duplicate effects.
6. Use domain interfaces at the orchestration boundary. Hide Dataflow construction, linking, completion, and fault handling inside the executor implementation.
7. Prove strict sequentiality with deterministic instrumentation tests, not configuration inspection alone.
8. Validate phase-by-phase against the representative matrix and the full YAML corpus.
9. Preserve dirty-tree changes. Do not use destructive git commands; stop and document any unresolved overlap.
10. Keep methods/classes small and feature-local, following existing .NET conventions without forced abstraction.
11. Run an independent verification and code review before production cutover.

# Output Format

Maintain this task directory as the durable handoff:

- `state.json`: current phase, step status, blockers, and verification truth;
- `execution_notes.md`: dated actions, commands, evidence, deviations, changed files, and next action;
- `assumptions.md`: status changes with evidence;
- `decisions.md`: accepted/rejected architecture decisions and rationale;
- phase-specific tests and code in their existing project locations;
- final summary linking verification results, corpus results, representative parity results, and any residual risks.

# Stop Conditions

- Stop before implementation if the user has not authorized execution or DR1–DR3 remain unresolved.
- Stop before checkpoint cutover if checkpoint compatibility/versioning (DR4) is unresolved.
- Stop on an unexplained baseline test failure, YAML corpus incompatibility, or observable contract drift.
- Stop if concurrent work overlaps the same behavior and cannot be safely reconciled from current source/history.
- Stop if strict no-overlap execution cannot be demonstrated.
- Stop if implementation would require changing YAML language, Shared/public contracts, output layout, or vendor-specific engine logic without a new approved contract.
- Do not mark complete while the legacy executor remains production-reachable, the second engine-home decision is unfulfilled, or required verification is missing.
