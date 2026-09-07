## Role

You are a senior .NET backend engineer with deep familiarity with the
post-Phase-1 Cymulate integration-adapters codebase. You know:

- The merged `AdapterFlowRunner` static class (post-P0b) and its
  `DecideAsync` call sites added in P1 (fresh-run +
  resume catch blocks).
- The `IFailurePolicy` / `FailureContext` / `FailureAction` hierarchy
  added in P1.
- The Phase-1 stubs `ProgrammerBugClassifier.IsBug` (always false) and
  `DelayPlanner.PickUnknown` / `PickClassified` (fixed delays, no
  jitter).
- The two Polly v8 pipelines (`UnknownFlowRetryPolicy`,
  `ClassifiedRetryablePolicy`) and their `CreatePipeline` factories.
- `DummyCollectorTests` and how it currently spends ~21 minutes of
  test time on real `UnknownFlowRetryPolicy` delays (C10).

## Goal

Replace P1's stubs with their working bodies, thread the planner into
both Polly pipelines with ±20% jitter, and wire
`ProgrammerBugClassifier` into `DefaultFailurePolicy.DecideAsync` as an
early branch that returns `FailFast` with `ErrorCode = "PROGRAMMER_BUG"`,
`IsRetryable = false`. Fix C10 by injecting a zero-delay planner in
`DummyCollectorTests`.

Behaviour outside the new wiring stays identical to P1. Every existing
test must still pass.

## Context

### Pre-flight verification

Run these and stop if anything is off:

- `git rev-parse HEAD` — must be `f51ed3d` or a descendant on
  `falcon-another-resume-layer`.
- `git status` — must show the P0a + P0b + P1 working-tree state
  (unstaged changes spanning Shared/Failure + collectors).
- `grep '"Cymulate.Integration.Sdk"' src/Cymulate.Integration.Adapters/Directory.Packages.props`
  — must show `Version="2.0.26"`.
- `grep -n "Phase 2" src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Failure/DelayPlanner.cs`
  — should still show the "Phase 2 wires" comment, confirming P1 stubs
  are in place.

### Scope summary (mapped to plan §6 Phase 2)

1. `ProgrammerBugClassifier.IsBug` body — exception-type list per
   A-P2-1 (recommend option (a): all `Argument*Exception` count as bug).
2. `DelayPlanner` jitter semantics — change from absolute
   `TimeSpan jitterRange` to relative `double jitterFraction` per
   A-P2-6. Range `[0.0, 1.0]`; default `0.0` (identity).
3. `DelayPlanner` test injection — add ctor overload that accepts
   `TimeSpan[] unknownDelays`, `TimeSpan[] classifiedDelays` per
   A-P2-7. Production callers keep using the parameterless ctor with
   `0.20` jitter.
4. Polly pipeline wiring — both `UnknownFlowRetryPolicy.CreatePipeline`
   and `ClassifiedRetryablePolicy.CreatePipeline` accept an optional
   `DelayPlanner? planner = null` parameter per A-P2-2. Their
   `DelayGenerator` calls `planner.PickUnknown(args.AttemptNumber)` /
   `planner.PickClassified(args.AttemptNumber)`. Default planner used
   when caller passes null.
5. `DefaultFailurePolicy` becomes ctor-injected with a `DelayPlanner`
   field per A-P2-3. `Instance` becomes `new DefaultFailurePolicy(new
   DelayPlanner(jitterFraction: 0.20))`. The decision-tree's
   classified-retry branch returns
   `RetryInProcess(_planner.PickClassified(ctx.AttemptNumber),
   "classified-retryable", handling)`.
6. ProgrammerBug branch in `DefaultFailurePolicy.DecideAsync` — between
   cancellation and transport check per A-P2-5. Returns
   `FailFast(new FlowExceptionHandling("Unhandled programmer error:
   {ex.GetType().Name}", "PROGRAMMER_BUG", ErrorSeverity.Error,
   IsRetryable: false))`.
7. PROGRAMMER_BUG `ErrorCode` constant — grep first; if a constants
   file exists, add it there; otherwise inline the literal (A-P2-4).
8. `DummyCollectorTests` — inject a zero-delay `DelayPlanner` via the
   new ctor overload. Fixes the ~21-minute test runtime (C10).
9. Tests: extend `UnknownFlowRetryPolicyDelaysTests`,
   `ClassifiedRetryablePolicyTests`, `DelayPlannerTests`,
   `ProgrammerBugClassifierTests`, `DefaultFailurePolicyTests` for
   the new wiring + jitter. Add a planted-NRE test asserting
   PROGRAMMER_BUG + non-retryable + no retry delay per plan §6.
10. Optional ProgrammerBug early-fail in
    `UnknownFlowRetryPolicy.IsUnknownRetryCandidate` (A-P2-13) —
    **DEFAULT: NOT DONE**, await operator decision before drafting
    this edit. Surface as accepted risk if not done.

### Files modified

- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Failure/ProgrammerBugClassifier.cs`
- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Failure/DelayPlanner.cs`
- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Failure/DefaultFailurePolicy.cs`
- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Session/TransportErrorHandling/UnknownFlowRetryPolicy.cs`
- `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Session/TransportErrorHandling/ClassifiedRetryablePolicy.cs`
- Possibly a constants file for PROGRAMMER_BUG (A-P2-4 — TBD by grep).

### Files added

None (Phase 1 already added every type Phase 2 needs).

### Tests modified

- `…/DummyCollector.Test/DummyCollectorTests.cs` — inject zero-delay planner.
- `…/DummyCollector.Test/UnknownFlowRetryPolicyDelaysTests.cs` —
  jitter assertions with seed = 42.
- `…/DummyCollector.Test/ClassifiedRetryablePolicyTests.cs` — same.
- `…/DummyCollector.Test/DelayPlannerTests.cs` — replace zero-jitter
  pick assertions with jitter math under seed = 42.
- `…/DummyCollector.Test/ProgrammerBugClassifierTests.cs` —
  per-exception-type assertions per A-P2-1 set.
- `…/DummyCollector.Test/DefaultFailurePolicyTests.cs` — new branch
  test for ProgrammerBug → FailFast + PROGRAMMER_BUG + non-retryable.

### Out of scope (do NOT touch)

- Marker-leak chain-walk fix in `ClassifiedRetryablePolicy.ShouldHandle`
  (P5).
- Marker exclude fix in `UnknownFlowRetryPolicy.IsUnknownRetryCandidate`
  (P4).
- `_retry.*` checkpoint persistence (P3).
- Enabling classified-retry on any collector (P4).
- `Directory.Packages.props` / `Directory.Build.props` (frozen at
  SDK 2.0.26).
- `phase-0a/`, `phase-0b/`, `phase-1/` artifacts.
- `plan.md`.

## Constraints

See `constraints.md`.

## Success Criteria

1. `dotnet build` clean: 0 errors, 0 new warnings vs P1 baseline.
2. `dotnet test` passes for the full `DummyCollector.Test` project.
   `DummyCollectorTests` completes in under 30 seconds total (C10 fix).
3. `ProgrammerBugClassifier.IsBug` returns true for `NullReferenceException`,
   `IndexOutOfRangeException`, `InvalidCastException`,
   `ArrayTypeMismatchException`, `ArgumentNullException`,
   `ArgumentOutOfRangeException`, `ArgumentException`. False for everything
   else (covered by `ProgrammerBugClassifierTests`).
4. `DelayPlanner` constructed with `jitterFraction = 0.20` and
   `new Random(42)` produces deterministic, asserted delays in the
   updated `DelayPlannerTests`.
5. `UnknownFlowRetryPolicy.CreatePipeline(logger, vendor, flow)` —
   no-planner overload — produces identical delays to P1 baseline
   (RetryDelays[0..2]). With a planner that has `jitterFraction=0.20`
   and seed=42, produces deterministic asserted delays in the updated
   `UnknownFlowRetryPolicyDelaysTests`.
6. `ClassifiedRetryablePolicy.CreatePipeline` — same shape as (5),
   asserted in `ClassifiedRetryablePolicyTests`.
7. `DefaultFailurePolicy.Instance.DecideAsync` for a planted
   `NullReferenceException` returns
   `FailureAction.FailFast { Handling.ErrorCode = "PROGRAMMER_BUG",
   Handling.IsRetryable = false }`. Asserted in
   `DefaultFailurePolicyTests`.
8. `DefaultFailurePolicy.Instance.DecideAsync` for a
   `OperationCanceledException` still returns
   `CancelWithoutPublish` (D7 preserved). Asserted.
9. Every other `DefaultFailurePolicyTests` branch passes unchanged.
10. `AdapterFlowRunnerFacadeTests` (14 tests) pass unchanged.
11. LoC budget: net add ≤ 250 LoC stretch / ≤ 600 LoC hard. Diff
    counted as `git diff --stat | tail -1`.
12. No edits to any out-of-scope file (constraints.md list).
13. `state.json` updated to status `executed`, with `verifierRun`,
    `codeReviewerRun` set after the orchestrator's review subagents
    run.

## Execution Rules

- Stop and surface if an A-P2-N OPEN assumption blocks an
  implementation choice. Do not silently pick.
- Use `Edit` (surgical) for existing files. `Write` only for new
  files (none expected).
- Run `dotnet build` after edits, `dotnet test` after build. If
  `dotnet test` hangs in this harness (per user-feedback memory),
  STOP — do not poll. Confirm specific test classes pass via
  filtered runs (`dotnet test --filter`).
- For any test that depends on jitter, use `seed = 42` and assert
  the EXACT computed delay (no tolerance windows).
- For any production-default planner, jitter range is 0.20 (= ±20%).
- For zero-jitter test planners, jitter is 0.0 — no `Random` calls
  during pick.

## Output Format

- Code changes on disk.
- `phase-2/execution_notes.md` capturing: files touched, LoC delta,
  test result summary, any A-P2-N assumptions resolved during work,
  any surprises.
- Updated `phase-2/state.json`.

## Stop Conditions

- A required A-P2-N OPEN assumption blocks progress.
- LoC delta exceeds 600 net.
- `dotnet test` produces a new failure that is not a pre-existing
  flaky/hang.
- Build introduces a new warning.
- An out-of-scope file is touched.
- The marker-leak fixes (either flavour) become tempting (D8 forbids).
