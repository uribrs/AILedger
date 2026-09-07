# Orchestration Plan

## Complexity Decision
- Path: **direct**
- Rationale: One coherent concern carried verbatim — ~21 interdependent files needing a consistent
  namespace rewrite + Kernel rewires, same shape as the FaultGovernance carry. Decomposition would
  fragment interdependent files for no parallelism gain. The only non-mechanical risk (package resolution)
  is a single build-driven checkpoint, not a decomposition axis.

## Research Decisions
- None needed. All edges resolved by in-repo code-trace; A1–A4 VALIDATED. A-pkg (package refs) and A-test
  (test scope) are non-blocking, resolved during execution by the build / scope judgment — no external-
  system research.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Verification Obligations
- Cross-check against the 7 Success Criteria in prompt_contract.md.
- Behavior verbatim — Session classes, SessionHandle dispose ordering, credential auth/cache, cert handler,
  lifecycle identical to source (modulo namespace + Kernel rewires + any naming neutralization).
- No residual `Cymulate.Integration.Adapters.Shared.*`; TransportErrorHandling/LogRedaction NOT re-carried.
- Kernel unmodified; Conversation depends on Kernel, never the reverse.
- Build resolves (package refs added if needed, central-managed); source repo not mutated.
- `dotnet build` clean; tests pass; output in execution_notes.md.
- Then (code-bearing): isolated code-reviewer pass, minimal context.
