# Orchestration Plan

## Complexity Decision
- Path: **direct**
- Rationale: One coherent concern carried verbatim — 18 interdependent Egress files + the defaults type,
  consistent namespace rewrite + Kernel rewires + a mechanical D8 rename. Same shape as the prior three
  carries; decomposition would fragment interdependent files for no gain. Package resolution is a single
  build-driven checkpoint, not a decomposition axis.

## Research Decisions
- None needed. All edges resolved by in-repo code-trace; A1–A4 VALIDATED. A-pkg / A-json / A-test are
  non-blocking, resolved during execution by the build / scope judgment — no external-system research.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Verification Obligations
- Cross-check against the 7 Success Criteria in prompt_contract.md.
- Behavior verbatim — formatters, batch sessions, throttling, multipart planner, the publisher engine
  identical to source (modulo namespace + Kernel rewires + the D8 renames).
- Single authoritative JSON validator invariant preserved; no second validation path; no ISink reshape.
- D8 renames applied consistently; held values (content-type/suffix/byte-budget) unchanged.
- No residual `Cymulate.Integration.Adapters.Shared.*`; Kernel unmodified; source not mutated.
- `dotnet build` clean; tests pass; output in execution_notes.md.
- Then (code-bearing): isolated code-reviewer pass, minimal context.
