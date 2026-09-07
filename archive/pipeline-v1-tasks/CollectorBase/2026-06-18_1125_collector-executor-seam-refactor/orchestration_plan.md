# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: One coherent, tightly-coupled refactor (seams + registry + decision-vocabulary + runner
  co-evolve), gated by build/tests as the behavior-preservation oracle. Decomposition would create
  fuzzy worker boundaries and coordination overhead with no real parallelism gain.

## Research Decisions
- None needed. All OPEN assumptions (A1–A5, Q1) are resolvable by reading LOCAL source in CollectorBase
  (Shared `AdapterProgressContext` extensibility, `Collectors.Tests.Infrastructure` deps) — not
  external-system behavior. `technical-researcher` is not warranted.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path. Execution is a single coherent pass via contract-driven-execution.

## Verification Obligations
- Every prompt_contract.md Success Criterion, especially: behavior-preserving (build + copied unit
  tests GREEN: assets=5, findings=3, resume no-overlap), seam interfaces with today's behavior as
  defaults, registry + load-time fail-closed validation, decision-vocabulary defined, invariants vs
  opt-in separated, generic strategies in a shared Strategies library (no project cycle),
  open/closedness demonstrated (new strategy needs no orchestrator edit), NO Falcon hardened quirks.
- Confirm A3 (RunContext over AdapterProgressContext, or documented sidecar fallback) and A4 (test
  project + infra copied locally) were resolved in code, not assumed.
- Code-reviewer pass (code-bearing): implementation quality of the seam/registry abstractions.
