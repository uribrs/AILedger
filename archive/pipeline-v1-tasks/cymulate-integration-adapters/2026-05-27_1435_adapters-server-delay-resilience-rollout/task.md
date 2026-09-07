# Adapter Resilience Strategy Rollout + Server-Suggested-Delay Externalization

## Summary

Roll out the new shared resilience strategy across every collector and wire the SDK 3.1.1 `AdapterResult.PartialResult(...)` contract so vendor-supplied Retry-After / X-RateLimit-* / Qualys `X-RateLimit-ToWait-Sec` / Discord reset-after / Zendesk delays flow end-to-end and ISB schedules the wait via Quartz.

## Initial rollout (P1-P4) — landed

Four phases shipped as commits `e11ade6`, `a23baf0`, `da2543e`, `492cf97`:

1. **Shared infra** — `ServerSuggestedRetryDelayPolicy` + `AdapterResilienceStrategy.CreateDefault` ordering + DummyCollector tests.
2. **Executor migration** — replaced the dead `AdapterDeferredRecoveryRequest` publish path with `AdapterResult.PartialResult(...)`. Snapshots checkpoint via `progressContext.AdvancePage(0, 0)` before returning.
3. **Per-collector rollout** — brought 15 non-Falcon collectors onto the strategy with trivial null-returning classifiers and `Decline` recovery handlers.
4. **Global externalization** — flipped `RetryOptions.ExternalizeServerSuggestedDelays = true` + `MaxServerSuggestedDelay = TimeSpan.FromHours(24)` in `SessionRetryDefaults` + Qualys/TenableIo custom factories.

Verifier (run 1): pass with accepted risks. Code-reviewer (run 1): 2 blockers + 3 major.

## Follow-on (PA/PB/PC) — current scope

Closes the blockers/major findings from the code review and completes the vendor-classifier rollout that the initial round deferred. User's chosen direction: hybrid Polly+ISB — Polly handles sub-30s transient retries in-process; anything that exhausts Polly OR carries a long vendor delay escalates via ISB scheduling.

Three phases:

- **Phase A** (single worker, sequential) — fix B1/B2/M1/M2: 15 × `*Collector.cs` `RecoveryHandler` returns `Continue` with no-op continuation; new executor end-to-end test in `AdapterFailureDecisionExecutorTests`; `ServerSuggestedRetryDelayPolicy.MaxRetries = 3`; shared `defaultTransportBackoff` parameter on `CreateDefault` so vendor-classified retryables that don't supply a backoff plan fall back to a sensible default (30s/2m/5m × 3, jittered).

- **Phase B** (15 parallel workers, one per collector) — port the orphaned `Processing/Validation/<X>FlowExceptionClassifier.cs` logic into the active `Processing/Resilience/<X>FlowExceptionClassifier.cs`, extend with `IsRetryable=true` where the wait-and-retry semantic applies, attach `retryableDecisionFactory` in `<X>ResilienceStrategyFactory.cs` for vendor-specific backoff plans (mirror Falcon's pattern), delete the orphaned validation classifier, add per-vendor classifier tests.

- **Phase C** (single worker, sequential) — verify no validation classifiers remain in any collector directory, rewrite `Shared/Resilience/README.md` to document the hybrid retry boundary, docstring updates on `ServerSuggestedRetryDelayPolicy` and `AdapterFailureDecisionExecutor`.

## Why this scope

- **B1**: 15 of 16 collectors' `RecoveryHandler = Decline("no vendor-specific recovery")` makes the executor's `RecoverOrFallbackAsync` fall through to `PublishFailure` instead of producing `AdapterResult.PartialResult`. The new policy chain emits the right decision; the executor never reaches `BuildPartialWaitResult`. The vendor's Retry-After is computed and then discarded.
- **B2**: Same root cause expressed at the session layer. With externalization on but B1 unfixed, every 429+Retry-After now triggers an immediate failure event for 15 collectors where Polly's in-process retry used to absorb it. Net regression.
- **M1**: Smoke tests assert on the policy's decision, not the executor's output. B1/B2 are invisible to the test suite — fix needs a regression guard.
- **M2**: `ServerSuggestedRetryDelayPolicy.BackoffPlan.MaxRetries = 1` allows exactly one retry; a second consecutive Retry-After (vendor still saturated) fails the collection.
- Per-vendor classifiers were trivial baselines in Phase 3 (return `null` for every exception). Phase 3 also surfaced orphaned `Processing/Validation/<X>FlowExceptionClassifier.cs` files holding real vendor logic that's no longer wired. Porting them restores the vendor mappings while keeping the structural separation introduced by Phase 3.

## Predecessor

Slug renamed from `falcon-resilience-partialresult-migration` — same task ID `TASK-20260527-1435`, same directory timestamp. The 4-phase rollout is the prior round; PA/PB/PC is the current round.

## Out of scope

- Falcon production code modifications beyond the Phase A.1 `RecoveryHandler` swap (Falcon's `RecoverFreshAsync` already returns `Continue` and is not touched).
- IntegrationServiceBus source changes (host fix shipped separately).
- New `IAdapterFailurePolicy` variants.
- Operator-override path for stuck `ScheduledWait` checkpoints.
- Observability dashboards for `ScheduledWait` depth/age.
- Migration of Indicators onto the strategy.
- Vendor-specific `PartialSuccessResultBuilder` work beyond Falcon's existing implementation.
