# Orchestration Plan

## Complexity Decision
- Path: **direct**
- Rationale: One coherent concern carried verbatim — 14 interdependent files + 3 shared shapes, flatten +
  rewires + a mechanical D8 rename, same shape as the prior five carries. Completing Envelopes.Common and
  the wire-safety check are single checkpoints, not decomposition axes.

## Research Decisions
- None needed. All edges resolved by in-repo code-trace; A1–A5 VALIDATED. A-test is non-blocking. No
  external-system research.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Verification Obligations
- Cross-check against the 7 Success Criteria in prompt_contract.md.
- Behavior verbatim — Done/Progress shapes, the envelope builder, the status converter, the in-proc hub,
  diagnostics identical to source (modulo namespace flatten + rewires + D8 renames).
- Best-effort-forwarding invariant preserved; D8 renames consistent; JsonPropertyName + CollectorStatus
  string values unchanged; wire-safety re-confirmed.
- Common refs → Envelopes.Common; Envelopes.Common COMPLETED (2 shapes + converter); existing 3 shapes +
  Kernel/Emission/Job untouched.
- No residual `Cymulate.Integration.Adapters.Shared.*`; source not mutated.
- `dotnet build` clean; tests pass.
- Then (code-bearing): isolated code-reviewer pass, minimal context.
