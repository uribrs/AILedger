# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: every workstream (sink decorator, checkpoint/resume, chaining, collect mode)
  converges on WorkflowRunner + the new sink — no clean file partition exists; the refactor's
  subtle invariants (cursor-before-advance, decorator stacking, fast-path gate) make tight
  single-context iteration worth more than parallelism. Predecessor knowledge is already
  loaded in the main thread.

## Research Decisions
- None needed. B2/B3/B5 are code facts the executor verifies in-line (sink-ful stage path,
  paginator restorability, collect-mode source-key uniformity); no external-system uncertainty.

## Worker Plan
Not applicable — direct path.

## Verification Obligations
- Every Success Criterion in prompt_contract.md (esp. SC2 resume-boundary no-loss/no-dup test).
- Held-replay code fully removed (grep for heldByStage/held remnants).
- Stream-through byte-identity (pre-existing non-merge workflow tests unmodified and green).
- B2: CanStreamIngest gate unaffected for sink-less non-merge stages.
- Grammar backward-compat: single-string `on:` yamls behave identically (regression tests).
- Schema diff minimal; synthetic chaining+collect yaml loads through schema-validating loader.
- Full engine suite green; adapter project builds.
