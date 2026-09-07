# Assumptions

## A1 — ISB Partial-Wait Host Fix Is Available

**Statement:** IntegrationServiceBus includes the partial-wait fix before this adapter change ships. Host behavior:

- Pending adapter checkpoint writes are drained before transitioning the row to `ScheduledWait`.
- The scheduled-wait transition preserves adapter checkpoint fields instead of overwriting them with an empty checkpoint envelope.
- Scheduled continuation routes through `IResumableAdapter.ResumeAsync` (RetryCount-bumped dispatch).

**Status:** VALIDATED

**Evidence:** ISB commits `3cf3381` (`fix(scheduled-continuation): preserve adapter checkpoint and route resume through IResumableAdapter`) + `5d7aaa2` (`chore(versioning): bump SDK to 3.1.1 and ISB to 1.0.87`) on ISB branch `fix-partial-await-gap`. The adapter-side obligation is to call `progressContext.AdvancePage(0, 0)` before returning `PartialResult`.

---

## A2 — Adapter Checkpoint Snapshot Uses `AdvancePage(0, 0)`

**Statement:** The adapter-side checkpoint snapshot before `AdapterResult.PartialResult` uses `AdapterProgressContext.AdvancePage(0, 0)`.

**Status:** VALIDATED

**Evidence:** `AdapterProgressContext.AdvancePage(...)` triggers the same progress/checkpoint path used by normal collection. User confirmed there is no ISB-side durability bug as long as the adapter uses `AdvancePage` before asking for continuation. Direct `OnCheckpoint` invocation is not part of the adapter contract.

---

## A3 — Recovery Continuation Type Stays As-Is

**Statement:** `AdapterRecoveryContinuation` remains in place even if the executor only needs its description in the scheduled-wait model.

**Status:** VALIDATED

**Reason:** Collapsing it into a smaller descriptor would touch Falcon recovery-hook signatures and create churn unrelated to the SDK migration.

---

## A4 — Falcon Is The Only Production Collector Currently Opted Into The Strategy

**Statement:** Before Phase 3, Falcon is the only production collector wired to `ResilienceStrategy` and partial-success strategy behavior. Phase 3 changes this — every other collector will be wired by the end of this task.

**Status:** VALIDATED (pre-Phase-3 state)

**Evidence:** Prior grep found one production `ResilienceStrategy =` hit and one `PartialSuccessResultBuilder =` hit, both in Falcon.

---

## A5 — `AdapterResult.Data` Is Diagnostic Only

**Statement:** ISB scheduling and resume correctness do not depend on `AdapterResult.Data`.

**Status:** VALIDATED

**Evidence:** SDK host handling for `PartialWaitRequired` consumes `Status`, `ResumeAfter`, and `WaitReason` from `ProcessEventCommandHandler.HandlePartialWaitAsync`. Durable resume state comes from checkpoint persistence.

---

## A6 — Existing Deferred-Recovery Publish Path Is Dead

**Statement:** The existing `AdapterDeferredRecoveryRequest` publish path is not useful in production and is deleted, not adapted.

**Status:** VALIDATED

**Evidence:** SDK publish handling does not recognize the custom request type, and the adapter call sites do not inspect the failed publish result.

---

## A7 — No-Durable-Progress Recovery Falls Back To Fresh Behavior

**Statement:** If a collector's recovery hook returns `Decline`, Shared resilience executes the embedded fallback decision (typically `PublishFailure`). For collectors with the trivial baseline `RecoveryHandler` (returns `Decline`), the host treats the resumed run as fresh.

**Status:** VALIDATED

**Reason:** Matches Falcon's current behavior for no-durable-progress paths and avoids inventing per-vendor flow state in Shared.

---

## A8 — Synthetic Zero-Item Page Is Acceptable

**Statement:** `AdvancePage(0, 0)` may increment SDK page counters. This is acceptable because it is the SDK-blessed checkpoint trigger and does not change collector flow semantics.

**Status:** OPEN

**Resolution path:** During implementation, tests should assert item/finding counts are not inflated and that resume uses `AdapterState` flow data rather than the synthetic SDK page count. If page-count effects become externally visible (e.g. partial-done envelope reports inflated `processedItems`), stop and reassess.

---

## A9 — `ServerSuggestedRetryDelayException` Can Surface As Inner Exception

**Statement:** Polly's `ResiliencePipeline` may wrap the toolkit's `ServerSuggestedRetryDelayException` inside `OperationCanceledException` or similar. The shared policy matches it via `Exception.InnerException` chain, not just the outer type.

**Status:** OPEN

**Resolution path:** Phase 1 worker checks Polly's rethrow behavior in `Cymulate.Http.Package.DefensiveToolkit/Policies/RetryPolicy.cs:70-74` (the catch block that re-throws the exception). If Polly preserves the outer type, the inner-exception walk is defensive but harmless; if it doesn't, the walk is load-bearing. Implementation must support both shapes either way.

---

## A10 — TenableSc Is A Collector (Or Isn't)

**Statement:** `Collectors/TenableScCollector/` may or may not exist as a collector in this branch. If it exists with a `*Collector.cs` entrypoint, it gets Phase 3 treatment; otherwise it's skipped.

**Status:** OPEN

**Resolution path:** Orchestrator verifies `ls src/Cymulate.Integration.Adapters/Collectors/TenableScCollector` before deciding whether to spawn the 13th Phase-3 worker.

---

## A11 — Phase 3 Workers Stay Within Their Collector Directory

**Statement:** A Phase 3 worker only touches files under `Collectors/<X>Collector/**` plus its test project. Shared/, other collectors, configuration builders for status-code knobs, etc. are out of scope.

**Status:** VALIDATED (operator hard constraint)

**Resolution:** If a worker discovers it must edit a forbidden file (e.g. a collector's HTTP wiring lives in Shared and not in its own directory), the worker stops and reports a blocker. The orchestrator escalates rather than letting workers race.

---

## A12 — `Continue` With A No-Op Continuation Is A Safe Default

**Statement:** For non-Falcon collectors with no vendor-specific state to rewire on recovery, returning `AdapterRecoveryResult<TRequest>.Continue` with a no-op continuation (`(req, ctx, ct) => Task.FromResult(0)`) is the correct default. The executor uses the continuation's `Description` only — its `ExecuteAsync` Func is not invoked in the `PartialResult` path. The no-op continuation differs from Falcon's state-mutating continuation only in the unused closure body.

**Status:** VALIDATED

**Evidence:** Prior code-review (run 1) traced the executor's `CompleteRecoveryAttemptAsync` (`Shared/Resilience/Logic/AdapterFailureDecisionExecutor.cs:198-228`). The Continue branch builds `PartialResult` from the schedule and the continuation's `Description`; nothing invokes the continuation's `ExecuteAsync` Func. Falcon's existing hook proves the pattern; Phase A simply propagates it to the 15 other collectors.

---

## A13 — Orphaned `Processing/Validation/<X>FlowExceptionClassifier.cs` Files Are The Authoritative Source For Porting

**Statement:** Phase B workers prefer to port mappings from the orphaned validation classifier where one exists rather than re-derive them from vendor documentation. For collectors without an orphaned validation classifier, the worker adds minimal generic mappings (5xx → retryable, non-429 4xx → terminal, `ArgumentException` → terminal) and notes the gap for follow-up vendor-knowledge work.

**Status:** OPEN

**Resolution path:** Each Phase B worker reports which collectors had a validation classifier to port vs needed generic mappings. The verifier samples three collectors representing three vendor-knowledge buckets (rich legacy classifier, sparse, none) to confirm porting is grounded.

---

## A14 — Shared `defaultTransportBackoff` (30s/2m/5m × 3, jittered) Is Operationally Safe

**Statement:** The default transport backoff `AdapterResilienceStrategy.CreateDefault` falls back to — `MaxRetries = 3`, `DelaySequence = [30s, 2m, 5m]`, `UseJitter = true`, `JitterRatio = 0.2` — is conservative enough to avoid hammering rate-limited APIs while aggressive enough to recover from transient blips within a single collection window. Vendor-specific factories can override.

**Status:** OPEN

**Resolution path:** Verifier checks that the default isn't more aggressive than typical vendor APIs tolerate. If a vendor has a stricter rate-limit cycle than the default's 30s base, that collector's `retryableDecisionFactory` should supply a tighter plan.

---

## A15 — Phase B Workers Stay Within Their Collector Directory (Carryover From A11)

**Statement:** A Phase B worker only touches files under `Collectors/<X>Collector/**` plus its test project. Shared default tweaks, new `IAdapterFailurePolicy` variants, or cross-collector changes are out of scope.

**Status:** VALIDATED (operator hard constraint)

**Resolution:** If a worker discovers vendor knowledge requires a new shared policy or a cross-cutting change, the worker stops and reports a blocker. The orchestrator escalates rather than letting the worker silently expand scope.
