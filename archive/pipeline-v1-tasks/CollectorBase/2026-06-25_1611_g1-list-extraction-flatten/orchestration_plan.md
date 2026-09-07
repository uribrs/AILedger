# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: two call-site reroutes in CollectorExecutorStepHelpers.cs (capture_list :176, accumulate_list :191)
  to the existing `JsonNav.ListAtFlattened`, + tests. Tightly coupled, tiny surface.

## Research Decisions
- None needed. B1 is proven + located; the resolver already exists from the prior pass.

## Worker Plan
Not applicable — direct path.

## Verification Obligations
- Cross-check SC1–SC5.
- The NEW tests must use the capture→for_each shape (the structure the prior flatten test omitted — the reason B1 escaped).
- Guardrail audit: `At`/`StringAt` textually unchanged; only the two list-capture sites rerouted; drain_path left as-is.
- Verifier RUNS the suite and confirms the multi-host capture→for_each end-to-end count.
- Code-reviewer (isolated) confirms single-node nav not made to flatten + accumulate dedupe intact.
