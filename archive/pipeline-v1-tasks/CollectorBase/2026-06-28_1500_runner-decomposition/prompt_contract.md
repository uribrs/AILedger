Role:
You are a senior .NET engineer performing a behavior-preserving structural refactor of the most
critical file in a YAML-operated collector engine.

Goal:
Decompose `CollectorExecutor/Execution/CollectorExecutorRunner.cs` (811 lines, 9 responsibilities)
into concern-based helper classes per the signed-off layout below. The runner becomes a ~150-line
conductor. Zero behavior change; 88/88 tests stay green throughout.

Context:
- Runner responsibilities today: orchestration (`RunAsync`), probe (`ProbeAsync`/`Preflight`),
  fetch page-loop (`RunFetchStepAsync`, ~195 lines), `for_each`, `poll_until`, `poll_and_drain`,
  item-failure tolerance (`RunToleratedItemAsync`), failure-decision mapping
  (`ExecuteDecisionAsync`/`TerminalResult`/`PartialSuccess`/`HandlingOf`), resume/fingerprint
  passthroughs.
- Smells driving the work: ~15-param signatures; the HTTP "build ctx → BuildUrl → method →
  body-content → send" block copy-pasted 4×; the partial-success-wins ternary duplicated ~8×; the
  195-line fetch loop packs ≥6 concerns with nested try/catch.
- Shared models already exist in `Execution/ExecutionModels.cs` (`PreparedRun`, `FetchSeed`,
  `ResumeSeed`, `StepResult`, `ItemOutcome`) — reuse them; do not duplicate.
- `Seams.RunContext` is the mutable run-state (cursor/offset/page/watermark/scrollDepth/captures/
  captureLists/page counters) and is mutated in place by the executors.

Target layout (signed off — implement exactly, do not redesign):
- `Execution/CollectorExecutorRunner.cs` → conductor only: `RunAsync` orchestration + step-loop
  switch dispatch + run-level catches (`ServerSuggestedRetryDelayException`,
  `OperationCanceledException`) + success mapping. ~150 lines.
- `Execution/Steps/StepExecutionScope.cs` → record bundling run-scoped collaborators: Http, BaseUrl,
  Inputs, Config, VendorName, Fingerprint, Registry, Mapper, PageSize, Failure, Resilience, RunCtx
  (the existing `Seams.RunContext`, by reference), IsResumeRun, Logger, Context
  (`IAdapterExecutionContext`). CancellationToken stays a separate method param.
- `Execution/Steps/StepRequestSender.cs` → one helper replacing the 4 copies; preserve the fetch
  `next_url` branch that uses `GetStringAsync` (vs `SendForStringAsync`).
- `Execution/Steps/FetchStepExecutor.cs` → the fetch loop, split into named privates: `SendQuery`,
  `MapRecords` (hydrate-vs-direct incl. `best_effort`), `AdvanceWatermark`, `PublishSlices` (with its
  own try/catch + partial-success), `HandleHttpFailure` (reset-to-watermark / execute-decision). The
  parse-error catch stays.
- `Execution/Steps/ForEachStepExecutor.cs` → injects FetchStepExecutor + ItemToleranceRunner.
- `Execution/Steps/PollUntilStepExecutor.cs`.
- `Execution/Steps/PollAndDrainStepExecutor.cs` → injects ItemToleranceRunner; preserve
  404→`EXPORT_GONE` and the per-cycle (not per-item) checkpoint.
- `Execution/Steps/ItemToleranceRunner.cs` → shared by for_each + poll_and_drain.
- `Execution/Outcomes/StepOutcomeFactory.cs` → `PartialSuccess` + `TerminalResult` + the single home
  for the partial-success-wins ternary.
- `Execution/Outcomes/FailureResolutionRunner.cs` → `ExecuteDecisionAsync` + `HandlingOf` (renamed to
  avoid clashing with Shared's `AdapterFailureDecisionExecutor`).
- `Execution/ConnectionProbe.cs` → `ProbeAsync`, using `StepRequestSender`. `Preflight`, `CanResume`,
  `Fingerprint` stay thin (keep on Runner or move with probe — document the choice).

Constraints:
- See constraints.md. Strictly behavior-preserving; every listed invariant preserved EXACTLY.
- No public adapter-surface signature change; `CollectorExecutorStepHelpers` out of scope; no
  polymorphic dispatch; RunContext by reference, unchanged mutation semantics.
- Incremental: build clean AND 88/88 green after EACH extraction; STOP on red.
- No test edits (a required test change = red flag → STOP).

Success Criteria:
1. `dotnet build CollectorBase.slnx` clean.
2. `dotnet test CollectorBase.slnx` → 88/88, no test edits.
3. `CollectorExecutorRunner.cs` is a ~150-line conductor; the executors/scope/request-sender/
   outcome-factory/failure-resolution/probe live under `Execution/Steps/` and `Execution/Outcomes/`.
4. The partial-success-wins invariant lives in ONE place (`StepOutcomeFactory`).
5. The 4× HTTP-build duplication is gone (one `StepRequestSender`).
6. `execution_notes.md` documents the before/after responsibility map + where each piece moved.

Execution Rules:
- Do not assume missing data; respect constraints strictly.
- Move logic verbatim; if a move tempts a behavior tweak, STOP and surface it.
- Work one class at a time, rebuild + retest between each.

Output Format:
- New/updated .cs files per the layout; updated `execution_notes.md` + `state.json`.

Stop Conditions:
- Goal achieved (criteria 1–6 met), OR
- Any extraction turns the build or suite red and the fix is not a trivial mechanical wiring error,
  OR a test would need editing to stay green, OR an invariant cannot be preserved by a pure move.
