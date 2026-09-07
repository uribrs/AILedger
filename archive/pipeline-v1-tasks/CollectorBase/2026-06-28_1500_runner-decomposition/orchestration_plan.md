# Orchestration Plan

## Complexity Decision
- Path: direct (incremental, S1→S5)
- Rationale: One critical file with tightly-coupled, sequential extractions — each new class must
  compile against the prior. Parallel workers would collide on the same file and lose the
  green-between-steps safety net. Tight iteration in one context beats handoff here.

## Research Decisions
- None needed. All facts are code-grounded and in-repo (the file, its callers, the test suite). The
  two OPEN assumptions (A1 test-coverage-implies-behavior-preserved, A2 RunContext-by-reference) are
  resolved by build/test + the code-reviewer, not by external research.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Omitted — direct path (no worker outputs to merge).

## Execution Sequence (enforced incremental — build + 88/88 between each)
- S1: StepExecutionScope record + StepRequestSender (collapse 4× HTTP build). Rewire callsites.
- S2: StepOutcomeFactory (partial-success-wins single home) + FailureResolutionRunner.
- S3: FetchStepExecutor (split into SendQuery/MapRecords/AdvanceWatermark/PublishSlices/HandleHttpFailure).
- S4: ItemToleranceRunner + ForEach/PollUntil/PollAndDrain executors.
- S5: ConnectionProbe; reduce Runner to conductor; final build + 88/88; responsibility-map note.
- STOP rule: any red build/suite that isn't trivial wiring, or a needed test edit → halt + report.

## Verification Obligations
- Cross-check against prompt_contract.md Success Criteria 1–6.
- Verifier MUST run the suite (88/88, no test edits) AND read moved code to confirm: checkpoint
  Write precedes AdvancePage; partial-success-wins is centralized + every former call site routes
  through it; poll_and_drain per-cycle (not per-item) checkpoint intact.
- Code-reviewer (isolated, minimal context): no behavior change in the move; RunContext carried by
  reference (not struct/clone); partial-success centralization faithful at every former call site.
