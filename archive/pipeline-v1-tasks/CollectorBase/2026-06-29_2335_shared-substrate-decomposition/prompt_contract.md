# Prompt Contract — Shared-substrate decomposition

## Role
You are a senior .NET architect performing a structure-only refactor of a shared integration substrate.

## Goal
Make the real dependency layering explicit: extract the transport-fault **floor** into a **Kernel**
unit, leaving **Session** (Conversation) and **Resilience** (FaultGovernance) as independent peers that
both depend only on Kernel — with zero behavior change and a continuously green build.

## Context
- Floor = `Session/TransportErrorHandling` (`HttpTransportFailureClassifier`,
  `AdapterHttpRequestFailedException`, `UnknownFlowRetryPolicy`, `IHttpFailureClassifier`). Verified leaf.
- `Session` → floor; never Resilience. `Resilience` → floor only; never Session root. (Both verified.)
- Runtime order: Session within-request retry → throws floor exception → `AdapterResilienceStrategy.DecideAsync`
  fixed-order chain → UnknownFlow/Fallback use floor's `UnknownFlowRetryPolicy`.
- No Kernel project exists. Solution: CollectorExecutor + Runner + Strategies + Shared + Tests.
- Repo is an extracted slice of the monorepo (see assumption A1).

## Constraints
- See `constraints.md`. Key: zero behavior change; build + tests green at each phase; floor stays a
  leaf; Session and Resilience depend only on Kernel, never each other; resilience policy order unchanged.
- Move order: floor → peers → reclaim Orchestration types.
- No transport/egress/resilience reinvention — relocation only.

## Success Criteria
- Solution builds (`dotnet build CollectorBase.slnx`).
- Full test suite green (`dotnet test CollectorBase.slnx`).
- Floor has zero internal dependencies after the move (grep-verified).
- `Session` and `Resilience` reference only Kernel for floor types; neither references the other.
- `CollectorError` + partial-completion DTOs relocated to Kernel (only if A3 confirms cross-cutting).
- `FlowExceptionHandling` + `AdapterFlowFailureHandling` reclaimed into Resilience (only if A4 confirms).
- No diff to runtime behavior (pure structural; reviewer confirms no logic changes).
- Design doc records target layout + rationale for upstream replay.

## Execution Rules
- Do not assume missing data — grep-confirm A3/A4 dependents before moving those types.
- Resolve A1 (propagation) with the user before treating code moves as the final deliverable, or
  proceed under the default and flag it.
- Respect constraints strictly; surface — do not work around — any forced behavior change.
- Phase-gate on green build; if a phase breaks more than it fixes, revert and report.

## Output Format
- Code moves as described, phase by phase, each leaving the solution green.
- `execution_notes.md` updated per phase (what moved, references rewired, build/test result).
- Design doc (target layout + rationale).

## Stop Conditions
- A required behavior change surfaces (carve cannot stay structure-only).
- Build/tests cannot be made green after a phase.
- A1 resolution flips the task to design-only.
- Grep shows A3/A4 types are NOT cleanly relocatable (hidden cross-deps).
