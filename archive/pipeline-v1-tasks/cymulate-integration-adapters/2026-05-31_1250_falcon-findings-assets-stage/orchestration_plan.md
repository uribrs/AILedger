# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: One cohesive subsystem (FalconCollector Flows/Findings + Recovery + one test file)
  with a strict dependency chain — checkpoint state → checkpoint helper serialization → fetcher
  host-row capture → new assets-stage helper → checkpoint writer → flow orchestration → tests.
  Worker boundaries would be fuzzy and coordination overhead would exceed any parallelism gain.

## Research Decisions
- None needed. All external Falcon behavior (flow dispatch, single-checkpoint-slot keying,
  AssetIdsFetcher host pager, CollectorNdjsonPublisher egress) was validated during planning and
  is recorded VALIDATED in assumptions.md. Remaining OPEN items (A7 stage order, A8 no
  segmentation, A9 persist watermark boundary IDs) are internal execution-level choices with
  recommended defaults — not external-system uncertainty.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Verification Obligations
- Cross-check against prompt_contract.md Success Criteria:
  - Solution builds.
  - FalconCollector unit tests pass, incl. new tests: filter run emits both assets_*.json and
    findings_*.json with unchanged findings bytes; no-filter emits all hosts; resume Stage="findings"
    and legacy (no Stage) skip assets; resume Stage="assets" continues assets then findings.
- Constraint adherence:
  - FalconAssetsFlow / standalone CollectAssets untouched.
  - Findings vuln stage, AID pre-pass, segmentation, existing findings checkpoint fields untouched;
    findings_*.json byte-for-byte identical.
  - AssetIdsFetcher change additive & default-off (AID pre-pass path identical when capture off).
  - Resume cannot re-publish a completed assets stage; checkpoint changes backward-compatible.
  - Assets stage runs as its own sequential pass (no publish/checkpoint inside the background AID producer).
  - New logic in helper class(es), confined to Flows/Findings + Recovery + test project.
- Execution must resolve A7/A8/A9 and record the outcomes in execution_notes.md.
