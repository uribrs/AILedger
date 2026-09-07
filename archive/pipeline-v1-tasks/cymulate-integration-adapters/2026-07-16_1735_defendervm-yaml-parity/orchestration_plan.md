# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: two small engine primitives share schema/loader/test files (worker
  partitioning would collide); yaml rewrite depends on both; single coherent context wins.

## Research Decisions
- None needed. A2/A3 validated from live capture; A1/A4 are executor-verifiable code facts.

## Worker Plan
Not applicable — direct path.

## Verification Obligations
- Contract Success Criteria 1-5 (round-trip gate is the load-bearing one).
- Verifier: full context. Code-reviewer: minimal context (no contract/verifier artifacts).
