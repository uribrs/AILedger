# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: three bounded, well-located fixes in a handful of files; tight iteration (build+test) beats
  handoff; decomposition would only add coordination overhead.

## Research Decisions
- None needed. Findings are pre-located by the audit; all source is local.

## Worker Plan
Not applicable — direct path.

## Execution order
1. R3 — search for any reader of RunContext.Signal / FetchSignal; if none, delete the vocabulary + writes; fix comments.
2. R1a — tolerant next-token extraction in the 3 pagination strategies.
3. R1b — clamp effective page_size + batch_size to >= 1.
4. R2 — extract one shared parse+validate routine for Preflight + RunAsync (behavior-preserving).
5. Build + test (+ add 1a/1b tests if feasible).

## Verification Obligations
- Cross-check the 6 Success Criteria in prompt_contract.md.
- Confirm conformance not regressed (interfaces, bus routing, resume, per-target page counter, ingress) — full test suite.
- Verifier subagent (full context) then isolated code-reviewer subagent (changed files only).
