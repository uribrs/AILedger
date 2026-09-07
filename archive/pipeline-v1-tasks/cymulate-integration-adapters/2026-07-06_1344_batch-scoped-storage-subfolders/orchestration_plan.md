# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: One coherent mechanism (scope helper + base restoration + dud-page rule) with tightly coupled touch points in Egress/Resilience and one test project. Worker boundaries would be fuzzy; tight iteration in one context wins.

## Research Decisions
- None needed. A1–A3 (ISB pass-through, never-purged bucket, upstream dedupe/trigger rules) were validated directly by the operator; A5/A6 are resolvable by in-repo search during execution.

## Worker Plan
Not applicable — direct path.

## Verification Obligations
- Cross-check against prompt_contract.md Success Criteria.
- Confirm A5 was actually verified by search (no other mid-run writer of Metadata["storageUrl"]) and A6 (page-number semantics match D6 file naming) — both must move out of OPEN.
- Confirm non-opted-in behavior is byte-identical (no default-path changes in NdjsonBatchSession/NdjsonUtf8BatchSession semantics).
- Confirm ordering-invariant test exists and fails if the invariant is broken.
- Confirm base restoration covers the AdapterFailureDecisionExecutor AdvancePage(0,0) snapshot path.
- Build + existing tests pass (respect hung-test-run guidance).
