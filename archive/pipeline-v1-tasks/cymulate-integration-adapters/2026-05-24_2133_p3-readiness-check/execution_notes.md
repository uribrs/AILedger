# Execution Notes

## Context Usage Estimate (best-effort introspection)
- Conversation arc this session: status report → state refresh → P2
  contract draft → P2 execution (5 prod files + 6 test files) → P2
  verifier + code-reviewer → P2 repairs → this readiness check.
- Substantial reads: plan.md (~1000 lines, multiple sections),
  AdapterFlowRunner.cs (~excerpts ~150 lines), DummyCollectorTests.cs
  (~540 lines full), 5 production files Phase 2 (full reads), 6 test
  files Phase 2 (full reads), multiple state.json files, multiple
  review files (verifier-1.md ~265 lines, code-reviewer-1.md ~125
  lines).
- Subagent runs: 4 Explore in status-report W1-W4 + 1 verifier (status
  report) + 1 verifier (state refresh) + 1 verifier (P2 contract draft)
  + 1 verifier (P2 execution) + 1 code-reviewer (P2 execution) = 9
  subagent outputs totalling ~300-500 lines each.
- Artifacts written: 4 task directories with full pipeline files
  (task.md, prompt_contract.md, orchestration_plan.md,
  execution_notes.md, state.json, review/verifier-N.md) plus
  phase-2/{decisions,assumptions,constraints,task,prompt_contract,
  state,orchestration_plan,execution_notes}.md.

**Rough token estimate: 250K-450K of the 1M Opus 4.7 window.** No exact
introspection API. Significant headroom remains (~50-75% available).

## P3 Prereq Status

### Prereq 1 — `progressContext.SetState` durability
**STATUS: UNRESOLVED — needs SDK source or platform-team confirmation.**

- SDK xmldoc (Cymulate.Integration.Sdk 2.0.26) at
  `~/.nuget/packages/cymulate.integration.sdk/2.0.26/lib/net8.0/Cymulate.Integration.Sdk.xml`
  documents `SetState(string, string)` only as "Stores a key-value
  pair in the adapter state dictionary". `AdapterState` is described
  as "persisted across resume cycles" but the xmldoc does NOT say
  WHEN persistence happens.
- The plan's concern stands: does `SetState` alone trigger an
  immediate flush, or must the runner call `AdvancePage(0, 0)` (or
  equivalent) to commit the entry? The codebase's existing
  `*CheckpointHelper.cs` files write via `SetState(...)` and then
  call `AdvancePage(...)` — implying `AdvancePage` is what triggers
  persistence.
- **Cannot resolve locally.** Options:
  - (a) Ask platform team for `SetState` durability contract.
  - (b) Read SDK source (not in repo).
  - (c) Drop the strong-durability claim; spec the retry-budget as
    best-effort, weaken C6 (per plan's option 3).
  - (d) Always flush after `_retry.*` writes via
    `AdvancePage(0, 0)` (per plan's option 2).

### Prereq 2 — `_retry.*` key collision check
**STATUS: CLEAR — zero collisions detected locally.**

- Grep `'"_|retry'` over all `*CheckpointHelper.cs` / `*CheckpointWriter.cs`
  / `*CheckpointReader.cs` files under
  `src/Cymulate.Integration.Adapters/Collectors/` returned **no matching
  files** (out of 19 candidates).
- No existing collector key starts with `_` or contains `retry` in
  the checkpoint helper layer. The reserved `_retry.*` prefix is
  safe to introduce.

### Prereq 3 — Downstream consumers of `AdapterState`
**STATUS: UNRESOLVED — needs platform-team confirmation.**

- `IAdapterExecutionContext.PublishAsync` and the SDK's `AdapterState`
  dictionary are consumed by the platform side (outside this repo).
  Whether any downstream pipeline (analytics, dashboards, reports)
  iterates `AdapterState` without an underscore filter cannot be
  verified from inside the integration-adapters repo.
- **Cannot resolve locally.** Operator must flag this for platform
  team.

## Recommended Next

Two reasonable paths:

**(a) Pause and clear prereqs first.** Operator pings platform team
with two questions:
- "Does `AdapterProgressContext.SetState` persist immediately, or do
  we need to call `AdvancePage` to flush?"
- "Does any downstream consumer iterate `AdapterState` without
  filtering underscore-prefixed keys?"

Then P3 contract drafting proceeds with all three prereqs cleared.

**(b) Draft P3 with explicit best-effort durability.** Skip Prereq 1
investigation; spec the retry budget as best-effort (in-process
only — survives if the worker doesn't crash; doesn't fully satisfy
C6). Note the limitation in P3 decisions. Skip Prereq 3 by reserving
the `_retry.*` prefix and noting the assumption that downstream
consumers filter underscores.

**Recommend (a)** — the plan explicitly says "investigate before
drafting this phase's contract", and skipping the prereqs would bake
in a weaker C6 than the plan promises. Path (b) is the fallback if
the platform-team turnaround is slow.

## On "are you up for P3?"
- Context: yes, headroom for P3 (estimated 50-75% of window remains).
- Mechanics: yes, P3's code surface is smaller than P2 (~150-200 LoC
  per plan estimate). Direct-path executable.
- Blockers: the two unresolved prereqs above. Without them, P3
  contract is technically draftable (with explicit best-effort
  caveat) but the durability claim cannot be honoured rigorously.

Ready to proceed when operator picks (a) or (b).
