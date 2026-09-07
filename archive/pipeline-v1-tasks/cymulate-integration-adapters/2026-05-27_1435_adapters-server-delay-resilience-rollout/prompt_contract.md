Role:
You are a senior .NET engineer working in `/Users/user/Dev/cymulate-integration-adapters` on branch `codex/adapter-resilience-strategy`. You are rolling out the new shared resilience strategy across every collector and wiring the SDK 3.1.1 `AdapterResult.PartialResult(...)` contract so vendor-supplied retry delays flow end-to-end as scheduled waits managed by ISB.

Goal:
Execute four ordered phases:

1. Add a shared `ServerSuggestedRetryDelayPolicy` that catches `Cymulate.Http.Package.DefensiveToolkit.Contracts.Exceptions.ServerSuggestedRetryDelayException` and emits `AdapterFailureDecision.RequestDeferredRecovery` with a one-shot `DelaySequence = [ex.Delay]`. Insert it in `AdapterResilienceStrategy.CreateDefault` ordering.
2. Migrate `AdapterFailureDecisionExecutor` from publishing the dead `AdapterDeferredRecoveryRequest` to returning `AdapterResult.PartialResult(...)` after snapshotting checkpoint via `progressContext.AdvancePage(0, 0)`. Delete the dead types.
3. Bring 12+ non-Falcon collectors onto the strategy. Parallel workers, one per collector, hard file-touch partition.
4. Flip `RetryOptions.ExternalizeServerSuggestedDelays = true` + `MaxServerSuggestedDelay = TimeSpan.FromHours(24)` in `SessionRetryDefaults.CreateTransient()`. Propagate through Qualys and TenableIo custom factories.

Context:
- SDK reference: `Cymulate.Integration.Sdk` 3.1.1. ISB host fix landed (commits `3cf3381` + `5d7aaa2` on ISB branch `fix-partial-await-gap`).
- Toolkit reference: `Cymulate.Http.Package.DefensiveToolkit`. `ServerSuggestedRetryDelayException` carries `Delay`, `OriginalDelay`, `DelaySource`, `HeaderName`, `StatusCode`, `Reason`.
- `AdapterResultStatus.PartialWaitRequired` is the SDK discriminator. `AdapterResult.PartialResult(TimeSpan, string?, Dictionary<string,object>?)` is the factory.
- `AdapterProgressContext.AdvancePage(int itemsInBatch, int findingsInBatch = 0)` is the SDK-blessed checkpoint trigger.
- Falcon is the only collector currently opted into the new strategy and is the reference layout for Phase 3 workers.
- The branch `codex/adapter-resilience-strategy` has the new strategy machinery; the executor still publishes a dead `AdapterDeferredRecoveryRequest`.

Phase Ordering (hard):
- Phase 1 must complete before Phase 3 starts.
- Phase 2 lands sequentially after Phase 1.
- Phase 3 workers run in parallel; file partition prevents collisions.
- Phase 4 is last.

Constraints:
- File scopes per phase are enumerated in `constraints.md`. Workers must not modify files outside their phase's scope.
- Phase 3 workers must not touch `Shared/`, other collectors, or `*Configuration*` builders. Conflicts are blockers.
- Falcon production files are not modified (the Phase 2 executor migration is in Shared/, not Falcon).
- IntegrationServiceBus source is not modified from this task.
- `Shared/Resilience/Policies/*` (except the new policy in Phase 1) and `AdapterRecoveryBudget.cs` semantics are untouched.
- Do not introduce a new custom `IPublishRequest` or compatibility shim.
- Do not call `progressContext.OnCheckpoint` directly. Use `AdvancePage(0, 0)`.
- Keep `AdapterRecoveryContinuation` as-is.
- Test cadence: phase boundaries only. Build may run mid-phase; `dotnet test` does not.
- C# style: small methods (~25 lines max before splitting), helper classes when warranted, mirror Falcon's existing structure, no speculative abstractions, sparse comments.

Required behavior by phase:

Phase 1:
- `ServerSuggestedRetryDelayPolicy` implements `IAdapterFailurePolicy`. Matches `ServerSuggestedRetryDelayException` directly OR via the `Exception.InnerException` chain.
- Returns `AdapterFailureDecision.RequestDeferredRecovery(Handling, BackoffPlan, Reason)`:
  - `Handling` = `new AdapterFailureHandling(message, "SERVER_SUGGESTED_DELAY", ErrorSeverity.Warning, IsRetryable: true)` — message describes `ex.HeaderName`, `ex.DelaySource`, `ex.StatusCode`.
  - `BackoffPlan` = `new AdapterBackoffPlan { MaxRetries = 1, InitialDelay = ex.Delay, DelaySequence = new[] { ex.Delay }, UseJitter = false, MaxDelay = ex.Delay }`.
  - `Reason` = `$"server-suggested-delay:{ex.DelaySource}"`.
- Returns `null` for any non-matching exception.
- Inserted in `AdapterResilienceStrategy.CreateDefault` between `ProgrammerBugPolicy.Definitive` and `RetryableTransportFailurePolicy`.
- Two unit tests in `AdapterResilienceStrategyTests`: direct exception and wrapped-as-inner-exception.

Phase 2 (carryover from prior contract):
- For `RecoverAndRetry` and `RequestDeferredRecovery`, keep existing classification, backoff, fallback, and budget decisions.
- When recovery is accepted: invoke recovery hook, `AdapterRecoveryBudget.Write(...)`, then `progressContext.AdvancePage(0, 0)`, then return `AdapterResult.PartialResult(schedule.Delay, waitReason, data)`.
- Drop `RequestDeferredRecoveryAsync` from `AdapterFailureDecisionExecutionContext`.
- Remove `PublishAsync(new AdapterDeferredRecoveryRequest(...))` from bus and resume executors.
- Delete `AdapterDeferredRecoveryRequest.cs` and `AdapterDeferredRecoveryMetadata.cs`.
- Update 7 enumerated test files: replace publish-request assertions with `result.Status == PartialWaitRequired`, `result.ResumeAfter`, `result.WaitReason`, and useful diagnostic-data assertions.
- Rewrite `Shared/Resilience/README.md` to describe the new flow.
- `CancelWithoutPublish`, `PublishFailure`, `FailFast`, `CompletePartial`, `RethrowForUnknownRetry` behavior unchanged.

Phase 3 (per collector, parallel):
- New `<X>FlowExceptionClassifier.TryClassify(Exception) -> FlowExceptionHandling?` returning `null` (trivial baseline).
- New `<X>ResilienceStrategyFactory.Create()` returning `AdapterResilienceStrategy.CreateDefault(mappedFailurePolicy: new MappedFailurePolicy(classify: <X>FlowExceptionClassifier.TryClassify, name: "<X>MappedFailurePolicy"))`.
- Wire into the collector's `ProcessAsync` entrypoint definition build: set `FlowExceptionClassifier`, `ResilienceStrategy`, `RecoveryHandler = ctx => new ValueTask<AdapterRecoveryResult<TRequest>>(new AdapterRecoveryResult<TRequest>.Decline("no vendor-specific recovery"))`. Leave `PartialSuccessResultBuilder` null.
- Where a resume runner exists, pass `resilienceStrategy` and `recoverAsync` into `CollectorResumeStrategyExecutor.ExecuteAsync` mirroring `FalconResumeRunner`.
- One smoke test in the collector's test project: scripted `ServerSuggestedRetryDelayException` → `AdapterResult.Status == PartialWaitRequired` + `ResumeAfter == ex.Delay`.

Phase 4:
- `SessionRetryDefaults.CreateTransient()` sets `ExternalizeServerSuggestedDelays = true` and `MaxServerSuggestedDelay = TimeSpan.FromHours(24)` on the returned `RetryOptions`.
- `QualysCollectorConfiguration.CreateRetryDefaults()` and `TenableIoCollectorConfigurationBuilder.CreateTenableIoRetryOptions()` propagate the same two settings on the `RetryOptions` they return.
- Verify `DefaultSessionFactory` already falls through to `SessionRetryDefaults.CreateTransient()` (no change there).

Test requirements:
- Phase 1: two policy tests (direct + inner-exception) asserting decision type, backoff plan delay equality, reason string shape, error code.
- Phase 2: replace publish-request assertions with `PartialWaitRequired` + `ResumeAfter` + `WaitReason` + diagnostic-data assertions. Also assert checkpoint snapshot occurred via the observable `AdvancePage(0, 0)` effect.
- Phase 3: one smoke test per collector — `ServerSuggestedRetryDelayException` → `AdapterResult.Status == PartialWaitRequired` and `ResumeAfter == ex.Delay`. Mirror the canonical pattern from `DummyCollector.Test/AdapterResilienceStrategyTests.cs`.
- Phase 4: build the full solution; no behavior tests beyond Phase 3's smoke tests.

Success Criteria:
- `dotnet build src/Cymulate.Integration.Adapters/Cymulate.Integration.Adapters.sln` clean.
- All affected unit tests pass under the new contract.
- `rg "AdapterDeferredRecoveryRequest|AdapterDeferredRecoveryMetadata" src` returns zero hits.
- `Shared/Resilience/README.md` describes the new `AdvancePage(0, 0) + PartialResult(...)` flow.
- Every collector (Falcon + 12-13 others) has a `<X>ResilienceStrategyFactory.Create()` and is wired in its `ProcessAsync` entrypoint.
- `SessionRetryDefaults.CreateTransient()` returns `RetryOptions` with `ExternalizeServerSuggestedDelays = true` and `MaxServerSuggestedDelay = TimeSpan.FromHours(24)`.
- Per-collector smoke test green.

Execution Rules:
- Read `state.json` first, then relevant markdown files.
- Update `state.json` as steps move (status: pending → in_progress → complete).
- Append implementation findings to `execution_notes.md`.
- If a current file conflicts with this contract, stop and report.
- Use existing repo conventions: small methods, local helpers, no forced abstraction.
- Phase 3 workers must not touch files outside their `Collectors/<X>Collector/**` partition plus their test project. Conflicts are blockers.
- Test cadence: phase boundaries only. Do not run `dotnet test` after each edit.

Output Format:
- Code changes per phase, committed at phase boundaries (separate commits make reverts clean).
- Updated `state.json`, `execution_notes.md` reflecting progress.
- A final report summarizing: which collectors got wired (and whether TenableSc was included), build/test results per phase, the grep gate result, and any blockers encountered (file conflicts surfaced by Phase 3 workers).

Stop Conditions:
- Stop when the success criteria are met and report build/test/grep results.
- Stop if Phase 1's policy match doesn't work against Polly's wrapping behavior (assumption A9) — surface and revisit before continuing.
- Stop if a Phase 3 worker discovers a collector's HTTP wiring forces edits outside its partition; surface as a blocker so the orchestrator can re-scope rather than letting the worker race.
- Stop if `AdvancePage(0, 0)` produces externally-visible page-count inflation (assumption A8) — re-evaluate the snapshot mechanism.
- Stop if the contract appears to require Falcon production-code changes; ask for approval before expanding scope.

---

## Follow-on (PA/PB/PC) — current scope

The initial 4-phase rollout landed but the code review (run 1) surfaced two blockers and three major findings. This section extends the contract with three follow-on phases that close those findings and complete the vendor-classifier rollout that Phase 3 deferred.

### Phase A — Foundation fixes (single worker, sequential)

Goal:
- Fix B1/B2 — change `RecoveryHandler` from `Decline` to `Continue` with a no-op continuation in 15 × `*Collector.cs`. Restores the `PartialResult` path that `BuildPartialWaitResult` builds for `RequestDeferredRecovery`.
- Fix M1 — add an executor end-to-end test in `AdapterFailureDecisionExecutorTests` (DummyCollector test project) that proves `AdapterResult.Status == PartialWaitRequired` actually fires for `ServerSuggestedRetryDelayException` after the policy chain + Continue recovery hook.
- Fix M2 — bump `ServerSuggestedRetryDelayPolicy.BackoffPlan.MaxRetries` from `1` to `3`. Update assertions in `AdapterResilienceStrategyTests`.
- Add `defaultTransportBackoff` parameter to `AdapterResilienceStrategy.CreateDefault` and use it in `MappedFailurePolicy` when a classifier's retryable decision doesn't carry its own backoff plan.

Constraints (Phase A — files only):
- MODIFY: `Shared/.../Resilience/Policies/ServerSuggestedRetryDelayPolicy.cs` (MaxRetries=3)
- MODIFY: `Shared/.../Resilience/Logic/AdapterResilienceStrategy.cs` (CreateDefault gains `defaultTransportBackoff` parameter)
- MODIFY: `Shared/.../Resilience/Policies/MappedFailurePolicy.cs` (use default when classifier doesn't supply backoff)
- MODIFY: 15 × `Collectors/<X>Collector/<X>Collector.cs` (RecoveryHandler swap — entrypoint property only, do not touch other wiring)
- MODIFY: `UnitTests/Collectors/.../DummyCollector.Test/AdapterFailureDecisionExecutorTests.cs` (new executor end-to-end test)
- MODIFY: `UnitTests/Collectors/.../DummyCollector.Test/AdapterResilienceStrategyTests.cs` (adjust assertions for MaxRetries=3)
- DO NOT touch: Falcon production files; ISB; Indicators; `AdapterRecoveryBudget.cs`; any other collector's files.

Phase A Success Criteria:
- Build clean across affected projects.
- All filtered tests pass in DummyCollector.Test (`ServerSuggested|AdapterResilienceStrategy|AdapterFailureDecisionExecutor`).
- All 15 collectors' smoke tests pass.
- The new executor end-to-end test proves `PartialResult` actually fires after the Continue hook returns.

### Phase B — Per-vendor classifier rollout (15 parallel workers)

Goal: replace the trivial null-returning `Processing/Resilience/<X>FlowExceptionClassifier.cs` with real vendor-knowledge mappings; attach `retryableDecisionFactory` for vendor-specific backoffs; delete the orphaned `Processing/Validation/<X>FlowExceptionClassifier.cs`.

Constraints (Phase B — per worker, hard partition):
- MODIFY: `Collectors/<X>Collector/Processing/Resilience/<X>FlowExceptionClassifier.cs` (port + extend)
- MODIFY: `Collectors/<X>Collector/Processing/Resilience/<X>ResilienceStrategyFactory.cs` (attach `retryableDecisionFactory` where vendor knowledge implies a specific backoff)
- DELETE: `Collectors/<X>Collector/Processing/Validation/<X>FlowExceptionClassifier.cs` (orphaned after porting)
- MODIFY (where it still imports the deleted classifier): `Collectors/<X>Collector/Recovery/<X>ResumeRunner.cs` — switch import to resilience namespace
- ADD/MODIFY: per-vendor classifier tests in the collector's existing test project (≥ 2 retryable, ≥ 1 terminal, ≥ 1 unclassified)
- DO NOT touch: `Shared/`, other collectors, `*Configuration*` builders, any cross-cutting types.

Phase B Success Criteria:
- Build clean across all 15 collector projects + their test projects.
- Per-collector classifier tests pass.
- No `Processing/Validation/<X>FlowExceptionClassifier.cs` files remain in collectors where the worker completed.

### Phase C — Cleanup + docs (single worker, sequential)

Goal: confirm Phase B left no orphans; rewrite the Shared resilience README for the hybrid retry boundary; docstring tidy on the two policy/executor files.

Constraints (Phase C — files only):
- MODIFY: `Shared/.../Resilience/README.md` (rewrite for hybrid Polly+ISB boundary: Polly < ~30s, ISB ≥ ~30s; vendor-supplied delays externalize via header; our-own delays externalize via per-collector classifier backoff plan)
- MODIFY: `Shared/.../Resilience/Policies/ServerSuggestedRetryDelayPolicy.cs` (docstring only — no behavior change)
- MODIFY: `Shared/.../Resilience/Logic/AdapterFailureDecisionExecutor.cs` (docstring only — describe Continue/Decline branch)
- DO NOT touch: any production behavior.

Phase C Success Criteria:
- Build full solution clean.
- Run DummyCollector.Test + FalconCollector.Test + 15 collector test suites in full.
- `rg "Processing/Validation/[A-Z][a-zA-Z]+FlowExceptionClassifier" src` returns zero hits.
- README accurately documents the hybrid retry boundary.

### Phase Ordering (PA/PB/PC — Hard)

- Phase A must land before Phase B starts (B's vendor `retryableDecisionFactory` work depends on the `defaultTransportBackoff` plumbing).
- Phase B's 15 workers run in parallel — strict per-collector partition prevents collisions.
- Phase C is last.

### Output Format (PA/PB/PC)

- Each phase commits separately. Commit messages per the plan file (`/Users/user/.claude/plans/staged-sauteeing-balloon.md`):
  - Phase A: `fix(resilience): honor PartialResult for hook-less collectors and add executor regression guard`
  - Phase B: `feat(collectors): real vendor classifiers with retryable backoff plans`
  - Phase C: `docs(resilience): document hybrid retry boundary and per-vendor classification`
- Do NOT push. Do NOT amend. Do NOT modify ISB or Indicators.
- Update `state.json` workflow block as workers complete; append phase summaries to `execution_notes.md`.

### Stop Conditions (PA/PB/PC)

- Stop if a Phase B worker discovers vendor knowledge requires a new shared policy or cross-cutting change; escalate as blocker.
- Stop if the orphaned validation classifier doesn't exist where expected for a Phase B worker; report and proceed with generic mappings per assumption A13.
- Stop if `MaxRetries = 3` breaks an existing test that wasn't enumerated in the constraints; do not silently rewrite that test — surface for orchestrator decision.
- Stop if Phase A's `Continue` change breaks any existing test in the 15 collector test projects; investigate before proceeding.
