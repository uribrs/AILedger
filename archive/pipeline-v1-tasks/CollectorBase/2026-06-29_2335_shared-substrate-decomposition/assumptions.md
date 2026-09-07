# Assumptions

## A1 — Upstream propagation of the layout change [OPEN]
- This repo is an **extracted slice** of the Cymulate monorepo; `Shared` is the consumed substrate.
- Whether restructuring `Shared`'s project layout **here** is meant to propagate upstream (replayed in
  the monorepo) or is local-only is unconfirmed.
- **Working default (does not block):** treat moves as durable within this repo; keep the decomposition
  design portable — the design doc records the target layout + rationale so it can be replayed upstream.
- **Resolve by:** user confirmation. If "local-only/throwaway," demote code moves to a design-only
  deliverable; if "propagate upstream," ensure the carve maps cleanly to monorepo project boundaries.

## A2 — Kernel is a new project, not just a namespace [OPEN]
- Plan says "new Kernel project/namespace." A new **project** (`Kernel.csproj`) gives a real compile-time
  dependency boundary (the strongest enforcement of `Session ⊥ Resilience`); a namespace-only move inside
  Shared does not.
- **Working default:** new namespace **within Shared first** (cheapest, proves the carve), with a new
  project as a follow-up only if a hard compile boundary is wanted. Revisit during orchestration.
- **Resolve by:** orchestration planning + user preference on enforcement strength.

## A3 — Envelope shapes are genuinely cross-cutting [OPEN]
- `CollectorError` and partial-completion DTOs are assumed cross-cutting (Kernel-bound). Not yet
  grep-verified this session.
- **Resolve by:** orchestrator/executor confirms their actual dependents before moving.

## A4 — Orchestration types are pure governance classification [OPEN]
- `FlowExceptionHandling` + `AdapterFlowFailureHandling` assumed to be governance classification types
  mis-filed under Orchestration. Not yet grep-verified this session.
- **Resolve by:** confirm their dependencies/consumers before reclaiming into Resilience.
