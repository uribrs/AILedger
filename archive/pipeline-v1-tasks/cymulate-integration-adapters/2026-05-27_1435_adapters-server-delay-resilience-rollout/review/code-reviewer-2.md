# Code Review — TASK-20260527-1435 (run 2)

**Diff range:** `492cf97..HEAD` (commits `448fa11` + `625b03a`, 23 files changed, +314/-47).
**Risk classification:** High — shared resilience infrastructure, retry/idempotency semantics, distributed-coordination boundary; the one-line `RecoveryHandler` swap × 15 collectors changes the recovery-hook contract for every non-Falcon collector at once.
**Stack:** C# / .NET 8, Polly via `Cymulate.Http.Package.DefensiveToolkit`, xUnit + FluentAssertions + Moq.

The two commits separate cleanly: `448fa11` is the behavioral change (policy `MaxRetries`, 15 collector recovery handlers, one new test, four assertion bumps). `625b03a` is documentation only — XML doc comments on two `.cs` files plus a README rewrite, zero non-comment line changes on the .cs files (verified).

## Major

### M1 — Zero-delay externalization will throw `ArgumentOutOfRangeException` from `AdapterResult.PartialResult`

`ServerSuggestedRetryDelayPolicy.cs:24-31` builds the `BackoffPlan` using `match.Delay` verbatim:

```csharp
var backoff = new AdapterBackoffPlan
{
    MaxRetries = 3,
    InitialDelay = match.Delay,
    DelaySequence = new[] { match.Delay },
    UseJitter = false,
    MaxDelay = match.Delay
};
```

No guard against `match.Delay <= TimeSpan.Zero`. The SDK's `AdapterResult.PartialResult(TimeSpan resumeAfter, ...)` (Sdk/Models/AdapterResult.cs) throws `ArgumentOutOfRangeException` when `resumeAfter <= TimeSpan.Zero`. After commit `448fa11` flips the 15 non-Falcon `RecoveryHandler`s to Continue, the executor's `BuildPartialWaitResult` calls `AdapterResult.PartialResult(schedule.Delay, ...)`. When `schedule.Delay` resolves from a zero-delay externalization, the call throws.

Whether this fires in production depends on the toolkit's externalizer behavior on zero-valued headers. The earlier Qualys probe log (`/Users/user/Dev/Uri/localprojects/IntegrationProbes/Integrations/Qualys/log.log`) contains lines like:

```
QualysRateLimitProbe[0] [RetryPolicy] Externalized server-suggested retry delay 0ms. ... OriginalDelayMs=0; Clamped=False.
```

— so the toolkit can and does produce `Delay = 0ms` for at least one real vendor header (`X-RateLimit-ToWait-Sec: 0`). Before `448fa11` the `Decline` recovery hook short-circuited to `PublishFailure` and the zero-delay path never reached `PartialResult`. After `448fa11` it does.

**Impact.** Unhandled `ArgumentOutOfRangeException` bubbles out of `AdapterFailureDecisionExecutor.ExecuteAsync` into the calling `AdapterBusStrategyFlowExecutor`. The exception likely lands in the outer catch and surfaces as a generic processing error — masking the underlying server-suggested-delay semantics with a stacktrace-shaped failure mode.

**Recommended fix** (local patch, ~5 lines in the policy). Either:
- **(a) Drop to null** — treat a non-positive externalized delay as "no actionable wait":
  ```csharp
  if (match.Delay <= TimeSpan.Zero)
      return new ValueTask<AdapterFailureDecision?>((AdapterFailureDecision?)null);
  ```
  The exception falls through to subsequent policies (`RetryableTransportFailurePolicy` → `FallbackFailurePolicy`).
- **(b) Clamp to a positive minimum** — e.g. `TimeSpan.FromSeconds(1)` — and document the floor on the policy class. Trades a hard crash for a guaranteed-positive minimum wait.

Option (a) is the safer default: zero means "retry immediately," and that's what Polly's in-process retry already does. Externalizing only adds value when the delay is meaningful (≥ a second).

Either way: add a test case to `AdapterFailureDecisionExecutorTests` covering `Delay = TimeSpan.Zero` and asserting whichever behavior is chosen.

## Minor

### m1 — `MaxRetries = 3` × `MaxServerSuggestedDelay = TimeSpan.FromHours(24)` = 72h worst-case wait

Two settings that landed in separate commits (`448fa11` here, `492cf97` previously) compose multiplicatively. A pathological vendor returning `Retry-After: 24h` on every attempt produces three days of `ScheduledWait` before the collection finally fails. Most realistic cases (Retry-After ≤ 1h) yield ≤ 3h total, which is fine.

The bump to `MaxRetries = 3` was deliberate per the commit message ("give sustained-quota cases more patience"). The 24h cap is operator-set. The interaction is not called out anywhere; an operator alarmed by a stuck collection won't know the system is designed to wait three days.

**Recommended fix.** One-line note in the policy's XML doc (already touched in `625b03a`) stating the worst-case total wait is `MaxRetries × MaxServerSuggestedDelay`. Alternatively, telemetry / alerting on `ScheduledWait` rows older than (say) 26h would catch the pathological case without needing the cap change.

### m2 — 15× duplication of the no-op `Continue` recovery handler

The exact same 5-line lambda block appears verbatim in 15 collector entrypoint definitions:

```csharp
RecoveryHandler = static ctx => new ValueTask<AdapterRecoveryResult<TRequest>>(
    new AdapterRecoveryResult<TRequest>.Continue(
        new AdapterRecoveryContinuation<TRequest>(
            "no vendor-specific state to rewire; honor scheduled wait",
            ctx.WorkItem,
            static (req, progressCtx, ct) => Task.FromResult(0))))
```

Only `TRequest` varies. The previous round did the same with the `Decline` shape across all 15. Both rounds touched 15 collectors uniformly. A future round changing this default (e.g. updating the description string for telemetry, or moving to a different `AdapterRecoveryResult` shape) requires editing 15 files in lockstep.

A single helper in `Shared/Resilience/` would reduce 75 lines to 1 + 15 × 1-line invocations and prevent drift:

```csharp
internal static class RecoveryHandlers
{
    public static Func<AdapterRecoveryContext<TRequest>, ValueTask<AdapterRecoveryResult<TRequest>>>
        NoVendorSpecificStateContinue<TRequest>() =>
            static ctx => new ValueTask<AdapterRecoveryResult<TRequest>>(
                new AdapterRecoveryResult<TRequest>.Continue(
                    new AdapterRecoveryContinuation<TRequest>(
                        "no vendor-specific state to rewire; honor scheduled wait",
                        ctx.WorkItem,
                        static (req, progressCtx, ct) => Task.FromResult(0))));
}
```

Each collector then writes `RecoveryHandler = RecoveryHandlers.NoVendorSpecificStateContinue<XxxCollectorTriggerRequest>()`.

The codebase has an explicit "no speculative abstractions" preference. This isn't speculative — it's a concrete, repeated shape with a name. Recommend a local patch in a follow-up; not a merge blocker.

### m3 — README documentation drift on "per-vendor classifiers stay trivial"

`Shared/Resilience/README.md` after `625b03a` claims:

> Per-vendor classifiers stay trivial (`return null`) except where vendor mechanics require state rewriting. Falcon's cursor-expired + 401 handling is the only such case in this codebase.

That is not accurate for the current state. Several non-Falcon collectors wire their entrypoint `FlowExceptionClassifier` to a non-trivial `Processing/Validation/<X>FlowExceptionClassifier.cs` via fully-qualified namespace (e.g. CortexXdr, DefenderForCloud, DefenderVm, MicrosoftEntraId, ServiceNowCmdb — per the prior verifier-1 report). The `Processing/Resilience/<X>FlowExceptionClassifier.cs` files are trivial; the `Processing/Validation/<X>FlowExceptionClassifier.cs` files are not, and several are still wired.

The README's claim should either be qualified (e.g. "the `Processing/Resilience/` classifiers stay trivial; per-vendor validation classifiers in `Processing/Validation/` remain wired where the collector author kept them") or the codebase should be reconciled to match the doc.

**Recommended fix.** Adjust the README text to reflect the actual state. Five-line clarification.

### m4 — Commit SHA `e11ade6` referenced in `README.md`

`Shared/Resilience/README.md` after `625b03a` includes:

> sits ahead of transient transport so a `Retry-After` from a `429` does not get reclassified as generic retryable (added in `e11ade6`)

Commit SHAs are unstable — amend, rebase, or squash will invalidate the reference. After PR merge into the long-lived branch the SHA may differ. Documentation tied to a specific git-history shape rots quickly.

**Recommended fix.** Drop the SHA reference. The sentence stands on its own without it.

## Observations

### O1 — Test doesn't cover zero-delay or pathological externalization paths

`AdapterFailureDecisionExecutorTests.cs` adds one end-to-end test asserting `Delay = 42s` produces `PartialWaitRequired` with `ResumeAfter == 42s`. The test does not exercise:
- Zero delay (relates to M1).
- Inner-exception wrapping (the policy walks the `InnerException` chain; no test confirms the executor sees the right resolution after a wrap).
- `Decline` recovery hook returning to fallback (the executor end-to-end test only covers the success path; the fallback branch lacks a regression guard at the executor layer too).

The added test is the minimum-viable regression guard for B1 specifically. Adding two or three more cases (zero delay, inner-wrapped exception, Decline → fallback) would harden the executor against the full matrix of policy outputs.

### O2 — The continuation's `WorkItem` and `ExecuteAsync` are unused by the executor on the Continue path

Carry-over observation from the prior code-review run. `AdapterRecoveryContinuation<TWorkItem>` carries `(Description, WorkItem, ExecuteAsync)`. `CompleteRecoveryAttemptAsync` reads only `Description`. The new no-op `Task.FromResult(0)` Func is structurally correct but semantically dead. If a future executor revision adds in-process continuation logic, the no-op silently no-ops there too.

This is fine for the current ISB-scheduled-wait design. Flag for future maintainers: changing the executor to invoke `ExecuteAsync` requires reviewing all 15 no-op continuations.

### O3 — `AdvancePage(0, 0)` page-counter inflation per partial-wait cycle

`BuildPartialWaitResult` calls `progressContext.AdvancePage(0, 0)` to trigger SDK `OnCheckpoint`. Each attempt increments `CurrentPage` by 1 without advancing `ProcessedItems` / `ProcessedFindings`. With `MaxRetries = 3`, a collection that hits the policy three times before fallback will report three synthetic page advances. Today no downstream consumer reads `CurrentPage` as "pages of real data" — so this is operationally invisible. Carry-over from the prior code-review.

### O4 — `static ctx =>` outer + `static (req, ...) =>` inner closures

Both lambdas are `static`, so neither allocates a closure object. Reading `ctx.WorkItem` from the outer parameter is parameter access, not capture — compiles cleanly. The `Task.FromResult(0)` allocation is per-invocation but negligible.

## Summary

The change is structurally sound for what it intends: `Decline` → `Continue` swap unblocks the `PartialResult` path for 15 collectors, `MaxRetries = 3` gives sustained-quota patience, the new executor test catches the B1-shaped regression at the executor layer rather than just the policy layer.

The single Major (M1, zero-delay throw) is real and reachable via at least one observed vendor header (Qualys `X-RateLimit-ToWait-Sec: 0`). Recommend repairing M1 with a local patch before merge — drop or clamp non-positive delays in `ServerSuggestedRetryDelayPolicy.DecideAsync`.

The Minor findings are merge-tolerable. m1 (worst-case 72h wait) is a documentation gap; m2 (15× duplication) is a code-cleanliness gap; m3 (README inaccuracy) and m4 (commit SHA reference) are doc-hygiene gaps. None blocks the underlying fix from delivering its value.
