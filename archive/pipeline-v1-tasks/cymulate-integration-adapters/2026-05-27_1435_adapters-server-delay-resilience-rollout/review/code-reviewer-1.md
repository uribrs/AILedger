# Code Review — TASK-20260527-1435 (run 1)

**Diff range:** `40c98cc..HEAD` (94 files, +1982/-558).
**Risk classification:** High — shared resilience infrastructure, retry/idempotency semantics, HTTP error-handling defaults for every collector, distributed-coordination boundary.
**Stack:** C# / .NET 8, Polly via `Cymulate.Http.Package.DefensiveToolkit`, xUnit + FluentAssertions + Moq.

## Blockers

### B1 — Non-Falcon collectors do NOT produce `PartialResult` for server-suggested delays. They publish `PublishFailure` instead.

The 15 non-Falcon collectors all wire:

```csharp
RecoveryHandler = _ => new ValueTask<AdapterRecoveryResult<TRequest>>(
    new AdapterRecoveryResult<TRequest>.Decline("no vendor-specific recovery"))
```

The executor's `RecoverOrFallbackAsync` invokes the hook unconditionally before building `PartialResult`. The control flow for any recoverable decision (including server-suggested-delay from `ServerSuggestedRetryDelayPolicy`) is:

1. `ScheduleRecoveryAttempt` writes the budget (`AdapterFailureDecisionExecutor.cs:138-166`).
2. `TryInvokeRecoveryHookAsync` invokes the hook → returns `Decline` (the trivial baseline).
3. `CompleteRecoveryAttemptAsync` sees `result is Decline` → falls through to `ExecuteFallbackAsync` (`AdapterFailureDecisionExecutor.cs:218-228`).
4. `ExecuteFallbackAsync` recurses with `Decision = PublishFailure`.
5. The executor returns `Completed(PublishFailure result)` — **never `PartialResult`**.

`BuildPartialWaitResult` is only reached from the `result is Continue next` branch (`AdapterFailureDecisionExecutor.cs:202-213`). The Decline baseline makes that branch unreachable. The new `ServerSuggestedRetryDelayPolicy` produces a decision that the executor turns into a hard failure for every non-Falcon collector.

**Impact:** Server-suggested-delay handling is functional only for Falcon (whose `RecoverFreshAsync` actually returns `Continue`). For the 15 other collectors, the vendor's Retry-After value is computed by the policy, the schedule is built, the budget is written, the hook declines, and a `PublishFailure` is emitted carrying the `Retry-After`-derived `AdapterFailureHandling`. The scheduled wait never happens. The Quartz partial-wait machinery is never engaged.

The smoke tests don't catch this because they assert on the *policy*'s decision (`RequestDeferredRecovery`) rather than the *executor*'s output. See M1.

**Recommended fix (smallest correct change):** for the trivial-baseline collectors, the recovery hook should return `Continue` with a no-op continuation, not `Decline`, so the server-suggested-delay path produces `PartialResult`:

```csharp
RecoveryHandler = ctx => new ValueTask<AdapterRecoveryResult<TRequest>>(
    new AdapterRecoveryResult<TRequest>.Continue(
        new AdapterRecoveryContinuation<TRequest>(
            "no vendor-specific state; honor server-suggested delay",
            ctx.WorkItem,
            (req, progressCtx, ct) => Task.FromResult(0))))
```

Touches 15 `*Collector.cs` files. Local patch — no architectural change.

Alternative (lower-touch, larger semantic change): teach `AdapterFailureDecisionExecutor` to skip the recovery hook entirely for `RequestDeferredRecovery` when there's no per-vendor state to rewire — i.e. directly emit `PartialResult` instead of routing through `Continue/Decline`. This makes "request a wait" a hook-less code path. Cleaner long-term, but changes shared executor semantics.

### B2 — Enabling `ExternalizeServerSuggestedDelays = true` regresses pre-existing transparent 429 retry for 15 collectors.

`SessionRetryDefaults.CreateTransient()` previously had Http.Package's Polly silently retry 429 + Retry-After in-process. With Phase 4's flip:

```csharp
ExternalizeServerSuggestedDelays = true
```

Polly now throws `ServerSuggestedRetryDelayException` instead of sleeping locally. For Falcon, the exception flows to a `PartialResult` and the wait is honored (B1 doesn't affect Falcon). For the 15 other collectors, B1 applies: the exception ends in `PublishFailure`.

**Net behavioral delta:** A vendor that previously returned 429+Retry-After and got transparent retry now causes immediate failure events for 15 collectors. This is a production regression for the most common rate-limit scenario.

B1 and B2 are the same root cause expressed at different layers. Fixing B1 fixes B2.

---

## Major

### M1 — Smoke tests assert on policy decisions, not executor output. The Blocker is invisible to the test suite.

Every per-collector smoke test follows this shape (sample from `CloudGuardServerSuggestedDelayTests.cs`):

```csharp
AdapterFailureDecision decision = await strategy.DecideAsync(context);
decision.Should().BeOfType<AdapterFailureDecision.RequestDeferredRecovery>();
```

These verify the policy chain produces `RequestDeferredRecovery`. They do not verify the executor's downstream handling. The Decline-falls-back-to-PublishFailure bug (B1) survives because the test doesn't execute `AdapterFailureDecisionExecutor.ExecuteAsync` with the collector's actual recovery hook.

**Recommended fix:** add at least one integration-shaped test per collector (or one parameterized test) that executes the full pipeline:

```csharp
AdapterFailureExecutionResult<TRequest> exec = await AdapterFailureDecisionExecutor.ExecuteAsync(executionContext, CancellationToken.None);
exec.Should().BeOfType<...Completed>().Which.Result.Status.Should().Be(AdapterResultStatus.PartialWaitRequired);
exec.Result.ResumeAfter.Should().Be(TimeSpan.FromSeconds(42));
```

This requires constructing an `AdapterFailureDecisionExecutionContext` with mock `PublishFailureAsync`/`CompletePartialAsync` callbacks and the collector's actual `RecoveryHandler`. Worth doing once in `Shared` test infrastructure (parametrized over collector strategy factories) rather than copy-pasting per collector.

### M2 — `ServerSuggestedRetryDelayPolicy.BackoffPlan.MaxRetries = 1` allows exactly one retry — the second hit on the same Retry-After fails.

`ServerSuggestedRetryDelayPolicy.cs:25-32` sets `MaxRetries = 1` with a single-element delay sequence. The executor's gate (`TryApplyRecoveryGateFallbackAsync`, `AdapterFailureDecisionExecutor.cs:120-133`) checks `AttemptNumber >= MaxRetries` and falls back.

On the first encounter: `AttemptNumber = 0 < 1` → wait + Continue (assuming B1 is fixed). After the wait, if the vendor returns the same Retry-After (e.g. quota still depleted), `AttemptNumber = 1 >= 1` → fallback to `PublishFailure`. Collection fails on the second 429 even though the vendor explicitly said to wait again.

For most rate-limit scenarios this is fine (vendor quotas refill after one cycle). For sustained-quota-exhaustion scenarios (Qualys daily quotas, vendor outages with hours-long Retry-After), the second consecutive 429 becomes a hard failure.

**Recommended fix:** `MaxRetries = 3` (or another small but non-degenerate value) so the policy survives one or two retried encounters before falling back. Document the choice in the policy class summary.

### M3 — Lifecycle: trust assumption on `OnCheckpoint` round-trip is silently load-bearing.

`AdapterFailureDecisionExecutor.BuildPartialWaitResult` (`AdapterFailureDecisionExecutor.cs:264-278`) calls `progressContext.AdvancePage(0, 0)` to trigger the SDK's `OnCheckpoint` callback. The host wires `OnCheckpoint` to a fire-and-forget `Task.Run` chain (per existing `AdapterExecutionContext.cs` patterns).

The executor returns `PartialResult` immediately after `AdvancePage(0, 0)`. The host's `HandlePartialWaitAsync` is expected to `await AwaitLastCheckpointAsync()` before writing the `ScheduledWait` status (this is the ISB-side fix shipped separately). If that drain is ever removed, regressed, or skipped (e.g., a new host code path that bypasses it), the persisted checkpoint will not contain the `AdapterState` the recovery hook wrote.

**Recommended action:** a contract assertion in `Shared` tests that verifies `AwaitLastCheckpointAsync` is invoked between the adapter's return and the row's `ScheduledWait` transition. The adapter-side codebase can't enforce host behavior, but an integration test mirroring the host's wiring (or a doc comment on `BuildPartialWaitResult` explaining the host obligation) reduces the drift risk.

---

## Minor

### m1 — `AdapterBackoffPlan.MaxDelay = match.Delay` is redundant with `DelaySequence = [match.Delay]`.

`ServerSuggestedRetryDelayPolicy.cs:30`. The clamp can never fire because the sequence value equals the clamp value. Cosmetic; drop `MaxDelay` for clarity or keep it as a belt-and-suspenders explicit cap.

### m2 — `BuildMessage` whitespace in the formatted message.

`ServerSuggestedRetryDelayPolicy.cs:60-68`. The format yields `"...source=X;{header}{status}"` where each piece starts with a leading space, producing `...source=X; header='...' status=200` (note the space after the semicolon). Readable but inconsistent with the trailing `;`. Local edit.

### m3 — Duplicated keys in `BuildPartialWaitData`.

`AdapterFailureDecisionExecutor.cs:280-329`. Keys `attemptNumber`, `maxRetries`, `resumeAfterUtc`, `continuation` appear both in `partialCompletion.AdditionalData` and in the outer `data` dict. Either keep one source of truth or document the intentional duplication for backward compatibility with telemetry consumers.

### m4 — `static _ =>` lambda inconsistency across collectors.

`SentinelOneCollector.cs` uses `static _ =>` for the recovery handler; `CloudGuardCollector.cs` uses `_ =>` (non-static). Trivial inconsistency; static is preferable (no closure allocation) but the lambdas don't capture anything either way. Cosmetic.

---

## Observations

### O1 — `AdvancePage(0, 0)` increments `_currentPage`.

`AdvancePage` mutates the page counter even when both args are zero (the comment in `BuildPartialWaitResult` acknowledges "without inflating externally-visible item counters", but page counter is visible via `OnProgress`). For Falcon, the recovery hook rewrites resume position so the stale `CurrentPage` doesn't matter. For non-Falcon collectors after B1 is fixed, this could shift `CurrentPage` by 1 per partial-wait cycle, surfacing in progress events. If any downstream consumer treats `CurrentPage` as the page-of-record for the collection, this is a latent off-by-one. Today no consumer does, so this is a latent risk, not a defect.

### O2 — Policy ordering placement is correct.

`ServerSuggestedRetryDelayPolicy` inserted between `ProgrammerBugPolicy.Definitive` and `RetryableTransportFailurePolicy` (`AdapterResilienceStrategy.cs:29`). A 429 wrapped in a generic transport failure now hits the new policy first, which is the intended priority — vendor-supplied delay beats generic transport classification.

### O3 — `MaxServerSuggestedDelay = TimeSpan.FromHours(24)` is permissive.

24h is reasonable for daily-quota resets. A misconfigured vendor returning days-long values gets clamped by Http.Package telemetry surfacing `Clamped=True`. Operator-set value; the cap itself is fine. Worth monitoring at runtime once deployed.

### O4 — `RequestDeferredRecovery` wrapping in `RecoverAndRetry`.

`AdapterFailureDecisionExecutor.cs:41-48`. The executor synthesizes a `RecoverAndRetry` from a `RequestDeferredRecovery` with `PublishFailure` as fallback. Both decision types collapse to the same execution path. The two-variant union remains useful for callers that want different fallback semantics (`RecoverAndRetry` carries an explicit `FallbackDecision`), but the current code does not exercise that difference. Could be simplified — though only if a caller surfaces in the future that needs the distinction.

### O5 — Vendor-specific behavior dropped on some Phase 3 collectors.

The entrypoint `FlowExceptionClassifier` for Qualys, SentinelOne, DefenderVm, CloudGuard, Guardicore, Taegis, InsightVmCloud, ServiceNowCmdb, IsbLoadTest now points at the trivial `Processing/Resilience/<X>FlowExceptionClassifier.cs` that returns `null`. The pre-existing `Processing/Validation/<X>FlowExceptionClassifier.cs` (with real vendor logic — e.g. Qualys' 409 → `QUALYS_INSTANCE_BUSY`, DefenderVm's `RateLimitExceededException` mapping, etc.) is no longer wired at the entrypoint, though some collectors still reference it via their `*ResumeRunner.cs` `ClassifyFlowException`. This is an intentional trivial-baseline rollout, but the asymmetry (ProcessAsync uses trivial, ResumeAsync uses vendor) is worth surfacing for documentation. Not a code-bearing defect on its own.

---

## Summary

The shape of the change is correct: a shared policy that catches `ServerSuggestedRetryDelayException`, an executor that transforms recoverable decisions into `AdapterResult.PartialResult`, externalization enabled at the session layer, and per-collector wiring across 15 non-Falcon collectors. The per-file workmanship is competent — small methods, idiomatic C#, sparse comments where useful, consistent factory shape mirroring Falcon.

The set is not safe to merge in its current state because of B1/B2: 15 of 16 collectors will publish failures instead of honoring server-suggested delays, and enabling externalization regresses pre-existing transparent 429 retry behavior for those same 15 collectors. The smoke tests added in Phase 3 do not detect this because they verify the policy's decision rather than the executor's output (M1).

B1 has a small fix (recovery handler returns `Continue` with a no-op continuation; touches 15 files). M1 fixes the test gap that masked it. Together those two changes make the rollout functionally complete; B2 dissolves once B1 is fixed.

Recommend B1 + M1 + M2 land before merge. m1-m4 and the observations can ride along or be deferred without risk.
