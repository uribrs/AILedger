# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: The code changes are tightly coupled inside one collector and its tests. Decomposition would add coordination overhead around a small staged-flow refactor.

## Research Decisions
- Topic: cortex-xdr-segmentation — triggered by assumption: XQL and endpoint segmentation — status: complete

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Verification Obligations
- Cross-check against `prompt_contract.md` Success Criteria.
- Confirm findings flow publishes CVE rows to findings output and endpoint rows to assets output.
- Confirm no adapter-side join/hydration remains in Cortex XDR findings flow.
- Confirm staged checkpoint state round-trips and resume re-enters the real flow.
- Run targeted Cortex XDR tests and report broader build/test status.
