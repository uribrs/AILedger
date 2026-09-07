# Orchestration Plan

## Complexity Decision
- Path: **direct**
- Rationale: One coherent concern carried verbatim — 10 interdependent files + 1 shared shape, flatten +
  rewires + a mechanical D8 rename. Same shape as the prior four carries. The wire-safety check and the
  Job→Emission edge are single checkpoints, not decomposition axes.

## Research Decisions
- None needed. All edges resolved by in-repo code-trace; A1–A5 VALIDATED. A-wire ($type check) and A-test
  are non-blocking, resolved during execution. No external-system research.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Verification Obligations
- Cross-check against the 7 Success Criteria in prompt_contract.md.
- Behavior verbatim — Run models, the two parsers, the hydrator (incl. exception-swallowing), trigger
  parsing, time defaults identical to source (modulo namespace flatten + rewires + D8 renames).
- D8 renames consistent; JsonPropertyName values unchanged; wire-safety ($type/JsonDerivedType) verified.
- Common→Envelopes.Common (AdapterRunMetadata); AdapterTimeDefaults→Emission.AdapterGlobalDefaults (flagged).
- No residual `Cymulate.Integration.Adapters.Shared.*`; Kernel + Emission unmodified; source not mutated.
- No parse→typed-job reshape; DefaultLookbackDays not moved.
- `dotnet build` clean; tests pass.
- Then (code-bearing): isolated code-reviewer pass, minimal context.
