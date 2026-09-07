# Constraints

## Repository Context

- Repo: `/Users/user/Dev/cymulate-integration-adapters`
- Branch: `codex/adapter-resilience-strategy`
- SDK reference: `Cymulate.Integration.Sdk` 3.1.1
- Toolkit reference: `Cymulate.Http.Package.DefensiveToolkit` — `ServerSuggestedRetryDelayException` at `/Users/user/Dev/Cymulate.Http.Package/DefensiveToolkit/Contracts/Exceptions/ServerSuggestedRetryDelayException.cs`
- ISB host fix landed: commits `3cf3381` + `5d7aaa2` on ISB branch `fix-partial-await-gap` — treat as available.

## Phase Ordering (Hard)

- Phase 1 must land before Phase 3 starts (collectors depend on the shared policy via `CreateDefault`).
- Phase 2 may land sequentially after Phase 1 — its file scope (Shared/) doesn't overlap with Phase 3's (Collectors/), but sequential commit boundaries keep reverts clean.
- Phase 4 is last. It flips externalization for every collector at once.

## Phase 1 — Files

- NEW: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/Policies/ServerSuggestedRetryDelayPolicy.cs`
- MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/Logic/AdapterResilienceStrategy.cs` — `CreateDefault` policy ordering only.
- MODIFY: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.DummyCollector.Test/AdapterResilienceStrategyTests.cs` — add two tests (direct + inner-exception cases).

## Phase 2 — Files (carryover)

- MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/Logic/AdapterFailureDecisionExecutor.cs`
- MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/Models/AdapterFailureDecisionExecutionContext.cs`
- MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Bus/Logic/AdapterBusStrategyFlowExecutor.cs`
- MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Collectors/Recovery/CollectorResumeStrategyExecutor.cs`
- MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/README.md`
- DELETE: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/Models/AdapterDeferredRecoveryRequest.cs`
- DELETE: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/Models/AdapterDeferredRecoveryMetadata.cs`
- TEST UPDATES (7 files):
  - `UnitTests/Collectors/.../FalconCollector.Test/FalconCollectorTests.cs`
  - `UnitTests/Collectors/.../FalconCollector.Test/FalconResumeRunnerTests.cs`
  - `UnitTests/Collectors/.../DummyCollector.Test/AdapterBusEntrypointRunnerPolicyTests.cs`
  - `UnitTests/Collectors/.../DummyCollector.Test/CollectorResumeRunnerPolicyTests.cs`
  - `UnitTests/Collectors/.../DummyCollector.Test/AdapterBackoffAndBudgetTests.cs`
  - `UnitTests/Collectors/.../DummyCollector.Test/AdapterFailureDecisionExecutorTests.cs`
  - `UnitTests/Collectors/.../DummyCollector.Test/AdapterResilienceStrategyTests.cs`

## Phase 3 — Per-Collector File Partition (PARALLEL WORKERS — HARD PARTITION)

Each worker's allowed file-touch set is **only**:

- NEW: `src/Cymulate.Integration.Adapters/Collectors/<X>Collector/Processing/Resilience/<X>FlowExceptionClassifier.cs`
- NEW: `src/Cymulate.Integration.Adapters/Collectors/<X>Collector/Processing/Resilience/<X>ResilienceStrategyFactory.cs`
- MODIFY: `src/Cymulate.Integration.Adapters/Collectors/<X>Collector/<X>Collector.cs` — entrypoint wiring only (the `FlowExceptionClassifier`, `ResilienceStrategy`, `RecoveryHandler` properties on the entrypoint definition build).
- MODIFY (where exists): `src/Cymulate.Integration.Adapters/Collectors/<X>Collector/Recovery/<X>ResumeRunner.cs` — pass strategy + recovery handler into `CollectorResumeStrategyExecutor.ExecuteAsync`.
- NEW (1 test): one smoke test in the collector's existing test project — `ServerSuggestedRetryDelayException` → `AdapterResult.Status == PartialWaitRequired` and `ResumeAfter == ex.Delay`.

Collectors (one worker per item):

- CloudGuard
- CortexXdr
- DefenderForCloud
- DefenderVm
- Guardicore
- InsightVmCloud
- IsbLoadTest
- MicrosoftEntraId
- Qualys
- SentinelOne
- ServiceNowCmdb
- Taegis
- TenableIo
- TenableSc (only if `Collectors/TenableScCollector/` exists; verify before spawning)

A Phase 3 worker that finds it must touch `Shared/`, the collector's `*Configuration` builder for status-code knobs, or another collector's files **must stop and surface as a blocker**.

## Phase 4 — Files

- MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Session/SessionRetryDefaults.cs` — `CreateTransient()` sets `ExternalizeServerSuggestedDelays = true`, `MaxServerSuggestedDelay = TimeSpan.FromHours(24)`.
- MODIFY: `src/Cymulate.Integration.Adapters/Collectors/QualysCollector/.../QualysCollectorConfiguration.cs` — `CreateRetryDefaults()` propagates same two settings.
- MODIFY: `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector/.../TenableIoCollectorConfigurationBuilder.cs` — `CreateTenableIoRetryOptions()` propagates same two settings.

## Files That Must Not Be Modified

- Falcon production files (the strategy migration in Phase 2 only touches Shared/ executors, not Falcon's own code):
  - `Collectors/FalconCollector/FalconCollector.cs`
  - `Collectors/FalconCollector/Recovery/FalconResumeRunner.cs`
  - `Collectors/FalconCollector/Processing/Resilience/FalconResilienceStrategyFactory.cs`
  - `Collectors/FalconCollector/Processing/FalconFlowExceptionClassifier.cs`
  - `Collectors/FalconCollector/Processing/Resilience/FalconRecoveryContinuationBuilder.cs`
  - `Collectors/FalconCollector/Processing/FalconPartialSuccessResultBuilder.cs`
  - `Collectors/FalconCollector/Recovery/FalconCheckpointState.cs`
  - `Collectors/FalconCollector/Recovery/FalconCheckpointHelper.cs`
- `Shared/Resilience/Policies/*` (the new `ServerSuggestedRetryDelayPolicy.cs` is added in Phase 1; existing policies are untouched).
- `Shared/Resilience/Logic/AdapterResilienceStrategy.cs` outside the policy-ordering edit in Phase 1.
- `Shared/Resilience/Logic/AdapterRecoveryBudget.cs` — read/write semantics unchanged.
- Indicators (`Indicators/.../*`).
- IntegrationServiceBus source.

If any phase needs to change a file outside its allowed scope, stop and surface — do not silently expand.

## Behavioral Contract (Phase 1 — `ServerSuggestedRetryDelayPolicy`)

- Matches `Cymulate.Http.Package.DefensiveToolkit.Contracts.Exceptions.ServerSuggestedRetryDelayException` directly OR via `Exception.InnerException` chain (Polly may wrap).
- Returns `AdapterFailureDecision.RequestDeferredRecovery` with:
  - `Handling` = `new AdapterFailureHandling(message, "SERVER_SUGGESTED_DELAY", ErrorSeverity.Warning, IsRetryable: true)` — message includes `ex.HeaderName`, `ex.DelaySource`, `ex.StatusCode`.
  - `BackoffPlan` = `new AdapterBackoffPlan { MaxRetries = 1, InitialDelay = ex.Delay, DelaySequence = new[] { ex.Delay }, UseJitter = false, MaxDelay = ex.Delay }`.
  - `Reason` = `$"server-suggested-delay:{ex.DelaySource}"`.
- Returns `null` for any other exception type.
- Inserted in `CreateDefault` between `ProgrammerBugPolicy.Definitive` and `RetryableTransportFailurePolicy`. Rationale: server-validated explicit delay is more authoritative than generic transport-retry classification.

## Behavioral Contract (Phase 2 — Executor)

- `RecoverAndRetry` and `RequestDeferredRecovery` both end in the same scheduled-wait path when recovery is accepted.
- Recovery hook still runs and may rewrite `AdapterProgressContext.AdapterState`.
- `AdapterRecoveryBudget.Write(...)` still records attempt count, last error, last delay, last resume time in `AdapterState`.
- After recovery state and budget state are present, call `progressContext.AdvancePage(0, 0)` to trigger SDK checkpoint persistence.
- After the snapshot call, return `AdapterResult.PartialResult(schedule.Delay, waitReason, data)`.
- Do not publish a custom `IPublishRequest`.
- Do not call `progressContext.OnCheckpoint` directly from product code.
- Recovery hook decline, hook exception, and budget exhaustion still execute the existing fallback decision.
- `CancelWithoutPublish`, `PublishFailure`, `FailFast`, `CompletePartial`, `RethrowForUnknownRetry` behavior is unchanged.

## Behavioral Contract (Phase 3 — Per-Collector)

- `<X>FlowExceptionClassifier.TryClassify(Exception) -> FlowExceptionHandling?`: returns `null` for every exception. Trivial baseline; vendor-specific logic is follow-up.
- `<X>ResilienceStrategyFactory.Create()`: `static AdapterResilienceStrategy Create() => AdapterResilienceStrategy.CreateDefault(mappedFailurePolicy: new MappedFailurePolicy(classify: <X>FlowExceptionClassifier.TryClassify, name: "<X>MappedFailurePolicy"));`. Mirror Falcon's `FalconResilienceStrategyFactory` shape; do not copy its cursor/unauthorized vendor logic.
- Entrypoint wiring in `<X>Collector.cs` `ProcessAsync`: set `FlowExceptionClassifier`, `ResilienceStrategy`, `RecoveryHandler` on the entrypoint definition. `RecoveryHandler = ctx => new ValueTask<AdapterRecoveryResult<TRequest>>(new AdapterRecoveryResult<TRequest>.Decline("no vendor-specific recovery"))`. Do not set `PartialSuccessResultBuilder` (leave it null).
- Resume runner (where present): pass `resilienceStrategy` and `recoverAsync` parameters into `CollectorResumeStrategyExecutor.ExecuteAsync` mirroring how `FalconResumeRunner` already does it.

## Behavioral Contract (Phase 4)

- `SessionRetryDefaults.CreateTransient()` sets `ExternalizeServerSuggestedDelays = true` and `MaxServerSuggestedDelay = TimeSpan.FromHours(24)` on the returned `RetryOptions`.
- Qualys' `QualysCollectorConfiguration.CreateRetryDefaults()` and TenableIo's `TenableIoCollectorConfigurationBuilder.CreateTenableIoRetryOptions()` propagate the same two settings on the `RetryOptions` they return, in addition to their existing custom `RetryStatusCodes`.
- `DefaultSessionFactory` already falls through to `SessionRetryDefaults.CreateTransient()` — no change.

## `AdapterResult.PartialResult` Data Payload

`AdapterResult.Data` is diagnostic only. The host scheduling contract reads `Status`, `ResumeAfter`, and `WaitReason`. Keep or migrate where cheap:

- `partialCompletion`
- `attemptNumber`
- `maxRetries`
- `resumeAfterUtc`
- `vendor`
- `flow`
- `baseDateUtc`
- `watermarkUtc`
- `continuation`

Resume correctness must not depend on this data payload.

## Test Cadence

- **No `dotnet test` per individual edit.** Build mid-phase is allowed; tests run at phase boundaries only.
- Phase 1: build `Shared/Cymulate.Integration.Adapters.Shared` + `DummyCollector.Test`. Run filtered tests `--filter "FullyQualifiedName~ServerSuggested|FullyQualifiedName~AdapterResilienceStrategy"`.
- Phase 2: build affected Shared + Falcon + 7 enumerated test projects. Run all. `rg "AdapterDeferredRecoveryRequest|AdapterDeferredRecoveryMetadata" src` returns zero.
- Phase 3: build all 12+ collector projects + their test projects. Run filtered smoke tests `--filter "FullyQualifiedName~ServerSuggested"`.
- Phase 4: build the full solution. Run `Shared` + `DummyCollector.Test` unit suite.

## Style Constraints

- Small methods. Default to splitting before ~25 lines.
- Helper classes when a chunk has its own identity.
- No speculative abstractions. No "in case we need this later" interfaces.
- Mirror neighboring code's shape (Falcon's `Processing/Resilience/` and `Recovery/` directories are the reference for Phase 3).
- No emojis. Sparse comments — only for non-obvious checkpoint/decision semantics.

## Verification (End-to-End — initial rollout P1-P4)

- `dotnet build src/Cymulate.Integration.Adapters/Cymulate.Integration.Adapters.sln` clean.
- All affected unit tests pass.
- `rg "AdapterDeferredRecoveryRequest|AdapterDeferredRecoveryMetadata" src` returns zero.
- `Shared/Resilience/README.md` describes the new flow with no stale references to publishing a custom request.
- Per-collector smoke test green: `ServerSuggestedRetryDelayException` → `AdapterResult.Status == PartialWaitRequired` + `ResumeAfter == ex.Delay`.

---

## Follow-on (PA/PB/PC) — current scope

### Phase Ordering (Hard)

- Phase A must land before Phase B starts (Phase B's classifier work depends on the `defaultTransportBackoff` plumbing Phase A introduces).
- Phase B's 15 parallel workers can run concurrently — strict per-collector partition prevents collisions.
- Phase C is last — cleanup and docs after Phase B's classifiers and validation-classifier deletions land.

### Phase A — Files (single worker)

- MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/Policies/ServerSuggestedRetryDelayPolicy.cs` — `MaxRetries = 3` (was 1); message/handling unchanged.
- MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/Logic/AdapterResilienceStrategy.cs` — `CreateDefault` gains an optional `AdapterBackoffPlan? defaultTransportBackoff = null` parameter; when supplied it's threaded into the `MappedFailurePolicy` so vendor-classified retryables that don't carry their own backoff fall back to this default.
- MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/Policies/MappedFailurePolicy.cs` — read the threaded default; when `retryableDecisionFactory` is absent OR returns a decision without a backoff plan, apply the default.
- MODIFY (15 files): `src/Cymulate.Integration.Adapters/Collectors/<X>Collector/<X>Collector.cs` — change only the `RecoveryHandler` property on the entrypoint definition from the `Decline` lambda to a `Continue` lambda with a no-op continuation. No other wiring.
- MODIFY: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.DummyCollector.Test/AdapterFailureDecisionExecutorTests.cs` — new test exercising `AdapterFailureDecisionExecutor.ExecuteAsync` end-to-end against a `ServerSuggestedRetryDelayException` with a `Continue` recovery hook, asserting `AdapterFailureExecutionResult<TWorkItem>.Completed` carries `AdapterResult.Status == PartialWaitRequired` and `ResumeAfter == ex.Delay`.
- MODIFY: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.DummyCollector.Test/AdapterResilienceStrategyTests.cs` — adjust the two Phase 1 tests' assertions for the new `MaxRetries = 3` (previously asserted `1`).

### Phase B — Per-Collector File Partition (PARALLEL WORKERS — HARD PARTITION)

Each worker's allowed file-touch set is **only**:

- MODIFY: `src/Cymulate.Integration.Adapters/Collectors/<X>Collector/Processing/Resilience/<X>FlowExceptionClassifier.cs` — replace the trivial `return null` baseline with a ported + extended classifier. Source: `Processing/Validation/<X>FlowExceptionClassifier.cs` where it exists. Output: vendor exception → `FlowExceptionHandling?` with `IsRetryable=true` only where wait-and-retry semantics apply.
- MODIFY: `src/Cymulate.Integration.Adapters/Collectors/<X>Collector/Processing/Resilience/<X>ResilienceStrategyFactory.cs` — extend `Create()` to attach a `retryableDecisionFactory` parameter on `MappedFailurePolicy` when vendor knowledge implies a specific backoff (e.g. Qualys instance-busy = exponential 30s/2m/5m). When the classifier's `IsRetryable=true` decision should use the shared default, no factory override is needed — the shared default kicks in.
- DELETE: `src/Cymulate.Integration.Adapters/Collectors/<X>Collector/Processing/Validation/<X>FlowExceptionClassifier.cs` (orphaned after porting; superseded).
- MODIFY (where exists and still imports the deleted validation classifier): `src/Cymulate.Integration.Adapters/Collectors/<X>Collector/Recovery/<X>ResumeRunner.cs` — switch the import to the resilience-namespace classifier.
- NEW / MODIFY tests: in the collector's existing test project, add per-vendor classifier tests covering:
  - Each retryable exception → executor output is `PartialWaitRequired` with the expected delay shape.
  - Each terminal exception → executor output is `Failure`.
  - At least one unclassified case (classifier returns `null`) → falls through to existing behavior.

Collectors (one worker per item):

- CloudGuard, CortexXdr, DefenderForCloud, DefenderVm, Guardicore, InsightVm, InsightVmCloud, IsbLoadTest, MicrosoftEntraId, Qualys, SentinelOne, ServiceNowCmdb, Taegis, TenableIo, TenableSc

A Phase B worker that must touch `Shared/`, `Processing/Resilience/<X>ResilienceStrategyFactory.cs` shared structure beyond its scope, or another collector's files **must stop and surface as a blocker**.

### Phase C — Files (single worker)

- MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/README.md` — rewrite the retry-boundary section to describe hybrid Polly+ISB:
  - Polly handles sub-30s transient retries in-process.
  - ISB owns longer waits via `AdapterResult.PartialResult`.
  - Vendor-supplied delays always externalize.
  - Per-collector classifier's `retryableDecisionFactory` controls the backoff for our-own delays; absent that, `AdapterResilienceStrategy.CreateDefault`'s `defaultTransportBackoff` parameter applies.
- MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/Policies/ServerSuggestedRetryDelayPolicy.cs` — docstring only (no behavior change). Mention `MaxRetries = 3` rationale.
- MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/Logic/AdapterFailureDecisionExecutor.cs` — docstring only (no behavior change). Describe the Continue/Decline branch in `CompleteRecoveryAttemptAsync`.

### Behavioral Contract (Phase A — RecoveryHandler swap)

For each of 15 non-Falcon `*Collector.cs`:

```csharp
RecoveryHandler = static ctx => new ValueTask<AdapterRecoveryResult<<TRequest>>>(
    new AdapterRecoveryResult<<TRequest>>.Continue(
        new AdapterRecoveryContinuation<<TRequest>>(
            "no vendor-specific state to rewire; honor scheduled wait",
            ctx.WorkItem,
            static (req, progressCtx, ct) => Task.FromResult(0))))
```

Where `<TRequest>` is the collector's trigger-request type (look at how the prior Phase 3 worker wired its `Decline` lambda for the exact type name).

Falcon's `RecoverFreshAsync` is not touched.

### Behavioral Contract (Phase A — ServerSuggestedRetryDelayPolicy)

- `BackoffPlan.MaxRetries` becomes `3` (was `1`).
- `BackoffPlan.DelaySequence` stays `new[] { ex.Delay }` — single-element. The executor's `GetDelay(attemptNumber)` returns `DelaySequence[Math.Min(attemptNumber, DelaySequence.Count - 1)]`, so all three attempts honor the same vendor-supplied delay.
- Everything else (`Handling`, `Reason`, message format) unchanged.

### Behavioral Contract (Phase A — defaultTransportBackoff)

- `AdapterResilienceStrategy.CreateDefault` signature gains `AdapterBackoffPlan? defaultTransportBackoff = null`. Default value when called with `null`: a plan with `MaxRetries = 3`, `InitialDelay = TimeSpan.FromSeconds(30)`, `DelaySequence = [30s, 2m, 5m]`, `UseJitter = true`, `JitterRatio = 0.2`.
- `MappedFailurePolicy` receives the default through its constructor (extend signature) and uses it inside `DecideAsync` when the classifier returns a retryable `FlowExceptionHandling` that doesn't carry its own backoff (e.g. via the existing `retryableDecisionFactory`).
- Phase B workers' `retryableDecisionFactory` overrides this default when vendor knowledge implies a tighter or looser plan.

### Behavioral Contract (Phase B — per-collector classifier)

- The new classifier returns `FlowExceptionHandling?` (same signature as the trivial baseline it replaces).
- For exceptions that should retry-with-delay, return `FlowExceptionHandling(message, errorCode, ErrorSeverity, IsRetryable: true)`. The Shared default backoff applies unless overridden by the factory.
- For exceptions that should fail terminally, return `FlowExceptionHandling(message, errorCode, ErrorSeverity, IsRetryable: false)`. Strategy emits `PublishFailure`.
- For unmapped exceptions, return `null`. Strategy proceeds to other policies in the chain.
- When the orphaned `Processing/Validation/<X>FlowExceptionClassifier.cs` exists, port its mappings verbatim. Extend with `IsRetryable` flags appropriate to each (the validation classifier may not have used the flag consistently).
- When no orphaned classifier exists, add minimal generic mappings: `HttpRequestException` with 5xx status → retryable, `HttpRequestException` with 4xx (non-429) → terminal (`PERMANENT_HTTP_4XX`), `ArgumentException` → terminal (`INVALID_INPUT`). Document in the file's docstring that vendor-specific extension is a follow-up.

### Behavioral Contract (Phase B — retryableDecisionFactory)

- Optional. Attach only when vendor knowledge implies a specific backoff different from the shared default.
- Signature mirrors Falcon's `FalconResilienceStrategyFactory.cs:50-76`:

```csharp
private static AdapterFailureDecision CreateRetryableDecision(
    AdapterFailureContext context,
    AdapterFailureHandling handling,
    AdapterBackoffPlan backoffPlan)
{
    // vendor-specific: return RecoverAndRetry with a tighter plan for X, RequestDeferredRecovery for Y, etc.
}
```

- The returned `AdapterBackoffPlan` should have at least one retry; values should be vendor-grounded (rate-limit cycles for that API typically resolve in ~N minutes).

### Test Cadence (carried forward)

- **No `dotnet test` per individual edit.** Build mid-phase is allowed; tests run at phase boundaries only.
- Phase A: build `Shared/Cymulate.Integration.Adapters.Shared` + 15 collector projects + DummyCollector.Test + 15 collector test projects. Run filtered tests in DummyCollector.Test (`FullyQualifiedName~ServerSuggested|FullyQualifiedName~AdapterResilienceStrategy|FullyQualifiedName~AdapterFailureDecisionExecutor`). Run filtered smoke tests across collector test projects (`FullyQualifiedName~ServerSuggested`). New executor end-to-end test passes.
- Phase B: build all 15 collector projects + their test projects. Run per-collector filtered tests (`FullyQualifiedName~FlowExceptionClassifier|FullyQualifiedName~ServerSuggested`).
- Phase C: build full solution. Run DummyCollector.Test + FalconCollector.Test + 15 collector test suites in full. Grep gate: `rg "Processing/Validation/[A-Z][a-zA-Z]+FlowExceptionClassifier" src` returns zero hits.

### Files That Must Not Be Modified (PA/PB/PC carryover)

All prior not-modify constraints carry forward, plus:

- Falcon's `RecoverFreshAsync` (in `FalconCollector.cs`) — not touched in Phase A.1; Falcon already returns Continue.
- `Shared/Resilience/Logic/AdapterFailureDecisionExecutor.cs` behavior — Phase C may update docstrings only, no behavioral changes.
- ISB source.
- Indicators.

If a Phase B worker needs to edit outside its allowed file set, stop and surface as a blocker.

### Verification (End-to-End — PA/PB/PC)

- All Phase A/B/C build + test gates green.
- **B1 closed:** 15 collectors' `RecoveryHandler` returns `Continue`; new executor end-to-end test in `AdapterFailureDecisionExecutorTests` passes and proves `AdapterResult.Status == PartialWaitRequired` actually fires for `ServerSuggestedRetryDelayException`.
- **M1 closed:** executor end-to-end test exists in `AdapterFailureDecisionExecutorTests`.
- **M2 closed:** `ServerSuggestedRetryDelayPolicy.MaxRetries = 3`; assertions in `AdapterResilienceStrategyTests` updated.
- **Vendor classifier work:** 15 collectors' `Processing/Resilience/<X>FlowExceptionClassifier.cs` carry real vendor mappings (ported from orphaned validation classifiers where present, generic mappings where not); orphaned `Processing/Validation/<X>FlowExceptionClassifier.cs` files deleted.
- `Shared/Resilience/README.md` documents the hybrid retry boundary (Polly < ~30s, ISB ≥ ~30s).
- `rg "Processing/Validation/[A-Z][a-zA-Z]+FlowExceptionClassifier" src` returns zero hits.
- Verifier (run 2) and code-reviewer (run 2) pass with no blockers.
