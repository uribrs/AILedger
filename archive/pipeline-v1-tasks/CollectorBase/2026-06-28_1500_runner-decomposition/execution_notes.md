# Execution Notes — Runner decomposition

## Outcome
Behavior-preserving structural refactor of `CollectorExecutorRunner.cs`. **88/88 green and build clean
after every step.** No test edits. No public adapter-surface change (`RunAsync`/`ProbeAsync`/`Preflight`/
`CanResume`/`Fingerprint` all intact).

**Runner: 811 → 193 lines** (now a conductor). 10 new files (~806 lines) under `Execution/Steps/`,
`Execution/Outcomes/`, and `Execution/ConnectionProbe.cs`.

## Before → after responsibility map
| Responsibility (was in Runner) | Now lives in |
|---|---|
| Run orchestration + step-loop dispatch + run-level catches + success mapping | `CollectorExecutorRunner` (conductor, 193 ln) |
| HTTP "build ctx→url→method→body→send" (was 4× copies) | `Steps/StepRequestSender.cs` |
| Fetch page-loop (`RunFetchStepAsync`, 195 ln) | `Steps/FetchStepExecutor.cs` — split into `SendQueryAsync` / `MapRecordsAsync` / `AdvanceWatermark` / `PublishSlicesAsync` / `HandleHttpFailureAsync` |
| `for_each` | `Steps/ForEachStepExecutor.cs` |
| `poll_until` | `Steps/PollUntilStepExecutor.cs` |
| `poll_and_drain` | `Steps/PollAndDrainStepExecutor.cs` |
| item-failure tolerance | `Steps/ItemToleranceRunner.cs` |
| ~15 loose params per method | `Steps/StepExecutionScope.cs` (run-scoped bundle; RunCtx by reference) |
| `PartialSuccess` / `TerminalResult` / the partial-success-wins ternary (8× inline) | `Outcomes/StepOutcomeFactory.cs` (single home, via `PartialOrElse`) |
| `ExecuteDecisionAsync` / `HandlingOf` | `Outcomes/FailureResolutionRunner.cs` (renamed off Shared's `AdapterFailureDecisionExecutor`) |
| `ProbeAsync` | `ConnectionProbe.cs` (Runner keeps a 1-line delegating method) |

## Steps + gates
- **S1** `StepRequestSender` — 4 HTTP-build copies → 1; dropped unused `using System.Text;`. Build + 88/88.
- **S2** `StepOutcomeFactory` + `FailureResolutionRunner` — centralized partial-success-wins (5 fetch ternaries via `PartialOrElse`) + 3 `ExecuteDecisionAsync` calls rewired; deleted the 4 private methods. One trivial wiring fix: `ErrorSeverity` resolves via `Cymulate.Integration.Sdk.Events` (not `Sdk.Enums`). Build + 88/88.
- **S3+S4** (merged — mutually dependent via scope + cross-calls) `StepExecutionScope` + all five executors; `RunAsync` builds the scope and dispatches; deleted the 5 moved methods. Build clean **first try** + 88/88.
- **S5** `ConnectionProbe` (done during S3+S4 wave) + Runner reduced to 193-line conductor. Build + 88/88, no new warnings.

## Invariants preserved (verbatim moves)
checkpoint-persisted-BEFORE-`AdvancePage` (FetchStepExecutor, after `CheckpointManager.Write`); partial-success-wins
(now single-sourced in `StepOutcomeFactory.PartialOrElse`); defer/wait-does-not-advance-page; per-target page
counters (`runCtx.AssetsPage/FindingsPage` via scope.RunCtx by reference); reset-to-watermark
(`HandleHttpFailureAsync` returns reset-and-continue; caller sets `cursor=null; ScrollDepth=0; continue`);
404→`EXPORT_GONE`; reserved `__drained_<step>` key; next_url non-advance guard (`usedUrl` returned from
`SendQueryAsync`); per-cycle (not per-item) poll_and_drain checkpoint.

## Notable implementation choices
- Folder-based concern segregation (`Steps/`, `Outcomes/`) but the **flat `...Execution` namespace is kept** —
  no `using` churn across the codebase; C# folder≠namespace is fine here (no analyzer enforces it).
- `async` rules out `ref`, so `PublishSlicesAsync` returns an updated-totals + optional-terminal record
  (`PublishOutcome`) instead of mutating `emitted`/`findingsEmitted` by ref — behavior identical.
- `RunContext` carried BY REFERENCE on the scope record (it is a class) — mutation semantics unchanged (A2 validated).

## Assumptions resolved
- A1 (tests cover behavior) — held: 88/88 green at every gate, no test edits. Verifier still to read invariant code.
- A2 (RunContext by reference) — VALIDATED: scope holds the `RunContext` class instance; executors mutate in place.
- A3 (probe/decision wiring) — VALIDATED at build: `_context`/`_logger`/`_httpClientFactory` flow via scope/ctor params.

## Commands
`dotnet build CollectorBase.slnx` → clean (no new warnings; pre-existing NU1507 + CS0067 events only).
`dotnet test CollectorBase.slnx` → 88/88.

## Residual risk
Runner is 193 ln, slightly above the ~150 target (the verbatim `RunAsync` prep-unpacking + xmldoc account for it).
Not worth compressing further — clarity over a line count.
