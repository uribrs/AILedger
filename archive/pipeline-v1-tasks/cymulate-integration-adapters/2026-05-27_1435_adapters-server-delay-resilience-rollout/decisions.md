# Decisions

## Carried over from prior contract

- Target the SDK contract `AdapterResult.PartialResult(TimeSpan resumeAfter, string? waitReason, Dictionary<string, object>? data = null)`.
- ISB partial-wait host fix is a prerequisite, not work for this repo.
- `RecoverAndRetry` and `RequestDeferredRecovery` collapse to the same Shared executor flow: recovery decision accepted, recovery state and budget state written into `AdapterState`, `AdvancePage(0, 0)` snapshots the checkpoint, then `PartialResult` returns.
- `AdvancePage(0, 0)` is the adapter-side checkpoint trigger. Direct `OnCheckpoint` invocation is not part of the adapter contract.
- `CompletePartial` remains a separate terminal partial-success result. Distinct from `PartialWaitRequired`.
- `AdapterDeferredRecoveryRequest` and `AdapterDeferredRecoveryMetadata` are deleted, not deprecated.
- No replacement custom `IPublishRequest` introduced for deferred recovery.
- `AdapterRecoveryContinuation` stays as-is to avoid Falcon recovery-hook churn.
- Falcon production code is not modified.
- `AdapterResult.Data` is diagnostic. Resume correctness comes from checkpoint state.
- Only `Shared/Resilience/README.md` is rewritten for docs.

## New for expanded scope

- Slug renamed `falcon-resilience-partialresult-migration` → `adapters-server-delay-resilience-rollout`. Same task ID, same timestamp directory.
- Scope expanded from Falcon-only to all 12+ collectors plus the global externalization toggle.
- Phase ordering 1 → 2 → 3 → 4 is hard. Phase 1 lands before Phase 3 starts; Phase 4 is last.
- Phase 3 is decomposed into one parallel worker per collector. Strict file-touch partition: each worker is scoped to `Collectors/<X>Collector/**` plus its test project. No shared-file races.
- A worker that needs to touch a forbidden file stops and surfaces a blocker. The orchestrator does not let workers negotiate at runtime.
- Test cadence: phase boundaries only. Build may run mid-phase; `dotnet test` does not.
- C# style: small methods (~25 lines max before splitting), helper classes when warranted, mirror Falcon's `Processing/Resilience/` and `Recovery/` layout, no speculative abstractions.
- `ServerSuggestedRetryDelayPolicy` lives in `Shared/Resilience/Policies/` — a new shared policy, not a vendor-by-vendor classifier addition. Reusable across all collectors as they join the strategy.
- Policy ordering: `ServerSuggestedRetryDelayPolicy` is inserted between `ProgrammerBugPolicy.Definitive` and `RetryableTransportFailurePolicy` in `CreateDefault`. Rationale: server-validated explicit delay is more authoritative than generic transport-retry classification.
- The shared policy emits `RequestDeferredRecovery` with a single-element `DelaySequence = [ex.Delay]` and `MaxRetries = 1`. Plan shape is unchanged; the one-shot sequence carries the server delay verbatim.
- Inner-exception chain walk in the policy match — defensive against Polly wrapping.
- Phase 4 `MaxServerSuggestedDelay = TimeSpan.FromHours(24)`. Cap accepted as permissive; toolkit telemetry surfaces `Clamped=True` when a vendor exceeds it.
- Per-collector classifier and recovery handler are trivial baselines (return `null` and `Decline` respectively). Vendor-specific refinements are explicit follow-ups, not in this rollout.
- Qualys' `CreateRetryDefaults()` and TenableIo's `CreateTenableIoRetryOptions()` propagate the externalization knobs in Phase 4 — no Phase 3 changes to those builders.
- TenableSc inclusion is conditional on directory existence. Verify before spawning the worker.

## New for follow-on (PA/PB/PC)

- The Phase 3 "trivial baseline + Decline recovery handler" was a deliberate scope-deferral that produced two blockers (B1, B2) in the code review. Phase A flips the `RecoveryHandler` from `Decline` to `Continue` with a no-op continuation, restoring the `PartialResult` path that the executor's `BuildPartialWaitResult` builds for `RequestDeferredRecovery` decisions.
- Recovery handler `Continue`-default applies uniformly to all 15 non-Falcon collectors. No per-vendor variation in the no-op continuation; vendor-specific recovery logic belongs in the classifier and factory, not the handler.
- `ServerSuggestedRetryDelayPolicy.MaxRetries` becomes `3` (was `1`). Triples the patience for sustained-quota cases.
- Shared default transport backoff: `MaxRetries = 3`, `DelaySequence = [30s, 2m, 5m]`, `UseJitter = true`, `JitterRatio = 0.2`. Configurable via new `defaultTransportBackoff` parameter on `AdapterResilienceStrategy.CreateDefault`.
- Per-collector classifier ports the orphaned `Processing/Validation/<X>FlowExceptionClassifier.cs` where one exists. Where none exists, the worker adds minimal generic mappings (5xx retryable, non-429 4xx terminal, `ArgumentException` terminal) and documents the gap.
- Orphaned `Processing/Validation/<X>FlowExceptionClassifier.cs` files are deleted after porting. If a `*ResumeRunner.cs` imports them, the worker switches the import to the resilience-namespace version.
- Vendor-specific `retryableDecisionFactory` is optional. Attach only when vendor knowledge implies a backoff different from the shared default.
- Verifier should sample three collectors representing three vendor-knowledge buckets (rich legacy classifier, sparse, none) to confirm porting is grounded.
- Test cadence stays: phase boundaries only. No per-edit test runs.
- Each phase commits separately for clean reverts: PA → PB → PC.
- Hybrid retry boundary: Polly handles sub-30s transient retries in-process; ISB owns longer waits via `AdapterResult.PartialResult`. Vendor-supplied delays always externalize via header. Our-own delays externalize via the per-collector classifier's backoff plan.
