# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: three small independent edits (CursorPaginationStrategy, ProbeAsync, a doc) + tests. No decomposition value.

## Research Decisions
- None needed. Code-grounded; the doc-drift dead-knob set is determined by grep at execution.

## Worker Plan
Not applicable — direct path.

## Verification Obligations
- SC1–SC4.
- B-M2: non-advancing cursor terminates (bounded) AND a normal advancing cursor still collects all pages (no regression).
- ProbeAsync: for_each-first profile probes the inner request path (assert probed URL).
- Doc removals grep-justified; hydrate retained.
- Verifier runs the suite + confirms the cursor guard doesn't break normal pagination.
