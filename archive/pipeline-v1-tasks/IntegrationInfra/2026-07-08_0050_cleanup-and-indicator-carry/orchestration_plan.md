# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: three bounded fixes in one repo on one branch (~10 files); tightly coupled by a shared build/test cycle; worker decomposition adds only coordination overhead.

## Research Decisions
- None needed. No OPEN assumption is external-behavior-dependent; all are verifiable in-repo by the executor (test-case parity diff, BaseFlowHandler dependency audit, usage sweeps).

## Worker Plan
Not applicable — direct path.

## Verification Obligations
- Cross-check against prompt_contract.md Success Criteria (build, full test suite, grep gates: zero Legacy identifiers, single trigger parser, 7 carried files byte-identical modulo rewires, IndicatorNames absent, new tests present).
- Verify the pre-delete re-checks actually ran (identity diff, usage sweep, test-case parity port).
- Verify zero behavior change: fixes 1–2 diff must be delete/rename-only; fix 3 logic identical to Shared source.
- Code-reviewer pass (code-bearing) with minimal context after verifier.
