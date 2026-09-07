# Orchestration Plan — Phase 2

## Complexity Decision
- Path: **direct**
- Rationale: Scope is one coherent edit cluster — 5 production files +
  6 test files, all in `Shared/Failure/`, `Shared/Session/TransportErrorHandling/`,
  and the `DummyCollector.Test` project. Decomposition would split
  policy + planner + pipeline edits that must move in lockstep (the
  Polly pipelines' `DelayGenerator` calls depend on the new DelayPlanner
  ctor shape; the DefaultFailurePolicy's RetryInProcess branch depends
  on its planner field). Direct path mirrors P1's choice (D-P1-14).

## Research Decisions
- None needed. All inputs are local: the existing P1 stubs, the two
  Polly pipelines, the plan §6 Phase 2 directives, and the signed-off
  decisions D-P2-7 through D-P2-16. No external behavior to verify.

## Worker Plan
- Not applicable — direct path.

## Synthesis Approach
- Not applicable — direct path.

## Execution Sequence (executor follows in order)
1. Pre-flight checks (HEAD, SDK pin, P1 stub presence).
2. Edit `DelayPlanner.cs` — new ctor shape per D-P2-10; add
   `Default` static per D-P2-11; jitter math (relative, symmetric);
   per-instance delay arrays.
3. Edit `ProgrammerBugClassifier.cs` — `IsBug` body per D-P2-7.
4. Edit `DefaultFailurePolicy.cs` — ctor injection per D-P2-9;
   ProgrammerBug branch per D-P2-13; PROGRAMMER_BUG inline literal
   per D-P2-12; classified-retry branch now uses
   `_planner.PickClassified(ctx.AttemptNumber)`.
5. Edit `UnknownFlowRetryPolicy.cs` — optional `DelayPlanner? planner`
   param per D-P2-8; `DelayGenerator` calls `planner.PickUnknown(...)`.
6. Edit `ClassifiedRetryablePolicy.cs` — same shape.
7. Edit `AdapterFlowRunner.cs` if needed — `LogError` on PROGRAMMER_BUG
   per D-P2-14 (only if not already emitted by the runner's FailFast
   handler).
8. Edit test files in order: DelayPlannerTests, ProgrammerBugClassifierTests,
   DefaultFailurePolicyTests, UnknownFlowRetryPolicyDelaysTests,
   ClassifiedRetryablePolicyTests, DummyCollectorTests.
9. `dotnet build` (validate 0 errors / 0 new warnings).
10. `dotnet test` filtered to the DummyCollector.Test project (validate
    pass + sub-30-second runtime per SC2).
11. Update `phase-2/execution_notes.md`.
12. Update `phase-2/state.json` (`status: executed`).

## Verification Obligations
- Cross-check executed deltas against `prompt_contract.md` Success
  Criteria 1-13.
- Verifier subagent: full context — checks behaviour identity outside
  the new wiring, jitter determinism under seed=42, PROGRAMMER_BUG
  fail-fast path, C10 closure (sub-30-second test).
- Code-reviewer subagent: minimal context — reviews code quality of
  the produced diffs in isolation. Code-bearing work, so this is
  mandatory.
- LoC budget: stretch 250 / hard 600. Stop if exceeded.

## Stop Conditions
- Pre-flight check fails.
- LoC delta exceeds 600.
- `dotnet build` introduces a new warning.
- `dotnet test` produces a non-flaky failure.
- An out-of-scope file is touched (esp. marker-leak fixes,
  `Directory.Packages.props`, plan.md, phase-0a/0b/1).
