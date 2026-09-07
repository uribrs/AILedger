# Orchestration Plan

## Complexity Decision
- Path: **direct**
- Rationale: One coherent concern carried verbatim. The ~32 files are tightly interdependent (shared
  namespaces, the policy chain references its own models/policies), so consistent namespace rewriting must
  happen in one context. Decomposition would fragment interdependent files and add coordination overhead
  for no parallelism gain. Larger than the transport carry but the same mechanical shape.

## Research Decisions
- None needed. All edges resolved by in-repo code-trace; A1–A3 VALIDATED. A4/A5/A6 are non-blocking
  execution details (DTO drag-check during the carry, shared-shapes namespace, test-project choice).

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path. `contract-driven-execution` produces the artifacts; orchestrator verifies
and code-reviews.

## Verification Obligations
- Cross-check against the 7 Success Criteria in prompt_contract.md.
- Behavior verbatim — policy classes, chain order, decision records, recovery budget, checkpoint helpers
  identical to source.
- No residual `Cymulate.Integration.Adapters.Shared.*` references; transport rewired to `Kernel.Transport`
  (incl. `UnknownFlowRetryClassification` rename).
- Envelope DTOs NOT in Kernel and NOT inside FaultGovernance; Kernel unmodified.
- Source repo not mutated.
- `dotnet build` clean; tests pass; output in execution_notes.md.
- Then (code-bearing): isolated code-reviewer pass, minimal context.
