# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: One product file + three test files, tightly coupled to a single settled design; decomposition would only add handoff overhead.

## Research Decisions
- None needed. All external-behavior assumptions are VALIDATED from source; the single OPEN assumption (ISB key delivery) is explicitly non-blocking for this adapter-side change.

## Worker Plan
Not applicable — direct path.

## Verification Obligations
- Cross-check against prompt_contract.md Success Criteria.
- Confirm path logic is byte-identical (diff must not touch BuildBatchSegment/StripBatchSegment/ResolveBaseUrl/RestoreBase storage-url behavior).
- Confirm v5 implementation is RFC 4122-correct (version/variant bits, namespace byte order) — validate against a known-vector if practical.
- Confirm determinism pins exist and filtered test runs pass (4 suites; no suite sweeps per repo policy).
