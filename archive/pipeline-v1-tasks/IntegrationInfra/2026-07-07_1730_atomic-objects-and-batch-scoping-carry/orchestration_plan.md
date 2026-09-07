# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: one coherent port with a strict internal order (Changeset B rewrites the session lifecycle Changeset A touches; both share the twin session files). Parallel workers would collide on the twins; sequential single-context execution is strictly better.

## Research Decisions
- None needed. All external assumptions VALIDATED by the three-part recon (counterparts, paradigm map, D7 record, SDK parity, house style). OPEN items A6/A8/A9 are internal inspections assigned to execution, ordered before any code change.

## Worker Plan
Not applicable — direct path (single contract-driven execution unit).

## Synthesis Approach
Single unit — main thread reviews the diff and the 1:1-parity checklist before verification.

## Verification Obligations
- Cross-check every Success Criterion in prompt_contract.md.
- The parity checklist is the load-bearing artifact: verifier must sample it against actual source hunks (git show 832754a; git diff 8948fba..7218abc in the source repo) and the target diff — every hunk mapped or justified N/A.
- Twins symmetry: the changed logic byte-for-byte equivalent between NdjsonBatchSession and NdjsonUtf8BatchSession.
- Grep-clean sweep re-run by verifier (no _multipartFinalized, no record-too-large/batch-boundary, storageUrl-literal only where it is a wire-contract JSON property).
- DAG check: no new Conducting→Emission using directives; FaultGovernance→Envelopes.Common edge documented as permissible.
- Test translation fidelity: ported assertions semantically identical to the FluentAssertions originals (no weakened checks).
