# Verifier Report — TASK-20260527-1435 (run 1)

## Summary

**Verdict: PASS with accepted risks.**

All formal Success Criteria in `prompt_contract.md` are met. Build is clean, all tests pass, grep gate returns zero, every collector has its strategy factory + entrypoint wiring, `SessionRetryDefaults.CreateTransient()` carries the externalization knobs, and Qualys/TenableIo factories propagate them.

The Phase 3 rollout produced an **inconsistency** (not a contract violation) in how the entrypoint's `FlowExceptionClassifier` was wired across collectors: some workers preserved the pre-existing vendor-mapping classifier on the entrypoint property; others replaced it with the new trivial Resilience classifier, dropping vendor-specific behaviors that pre-dated this task. The contract and `decisions.md` line 31 explicitly sign off on the trivial baseline, so this is policy, not bug. It is surfaced below under "Behavior Drift" because the loss of pre-existing vendor mappings has user-visible consequences (Qualys 409 no longer surfaces `QUALYS_INSTANCE_BUSY`; DefenderVm rate-limit no longer flags `IsRetryable=true`; InsightVmCloud `INVALID_FILTER` mapping is gone).

## Success Criteria Coverage

- [PASS] `dotnet build src/Cymulate.Integration.Adapters/Cymulate.Integration.Adapters.sln` clean. — Build output: `Build succeeded. 0 Warning(s) 0 Error(s) Time Elapsed 00:00:09.38`.
- [PASS] All affected unit tests pass. — Phase 1/2/3 filtered tests: 15 collector smoke tests pass + DummyCollector 50/50 + FalconCollector 69/69.
- [PASS] `rg "AdapterDeferredRecoveryRequest|AdapterDeferredRecoveryMetadata" src` returns zero hits. Verified.
- [PASS] `Shared/Resilience/README.md` describes the new `AdvancePage(0, 0) + PartialResult(...)` flow. Lines 23, 66, 85, 88, 90, 102 reference the new contract; no stale references to publishing a custom request remain.
- [PASS] Every collector (Falcon + 15 non-Falcon) has a `<X>ResilienceStrategyFactory.Create()` and entrypoint wiring. Files enumerated: CloudGuard, CortexXdr, DefenderForCloud, DefenderVm, Falcon, Guardicore, InsightVm, InsightVmCloud, IsbLoadTest, MicrosoftEntraId, Qualys, SentinelOne, ServiceNowCmdb, Taegis, TenableIo, TenableSc.
- [PASS] `SessionRetryDefaults.CreateTransient()` returns `RetryOptions` with `ExternalizeServerSuggestedDelays = true` and `MaxServerSuggestedDelay = TimeSpan.FromHours(24)`. `Shared/Session/SessionRetryDefaults.cs:30-32`.
- [PASS] Per-collector smoke tests green. Test filter `FullyQualifiedName~ServerSuggested` returns 1 passing test per collector test project (15 collectors).

## Phase Verification

- **Phase 1 — PASS.** `ServerSuggestedRetryDelayPolicy` at `Shared/Resilience/Policies/ServerSuggestedRetryDelayPolicy.cs` matches direct-and-inner exceptions via `FindServerSuggestedDelay`, emits `RequestDeferredRecovery` with `MaxRetries=1`, single-element `DelaySequence`, error code `SERVER_SUGGESTED_DELAY`, reason `"server-suggested-delay:{source}"`. Inserted in `AdapterResilienceStrategy.CreateDefault` between `ProgrammerBugPolicy.Definitive` (line 28) and `RetryableTransportFailurePolicy` (line 30). A9 resolved (outer-match-sufficient; inner walk is defensive).
- **Phase 2 — PASS.** `AdapterFailureDecisionExecutor.BuildPartialWaitResult` at lines 244-258 calls `progressContext.AdvancePage(0, 0)` then returns `AdapterResult.PartialResult(schedule.Delay, $"deferred-recovery:{recover.Reason}", data)`. `AdapterRecoveryBudget.Write` is invoked earlier in `ScheduleRecoveryAttempt` (lines 145-152). `RequestDeferredRecovery` correctly funnels into `RecoverOrFallbackAsync` by wrapping into a synthetic `RecoverAndRetry` (lines 38-46). Dead types deleted; grep clean.
- **Phase 3 — PASS (formal), inconsistency flagged.** All 15 non-Falcon collectors have `<X>FlowExceptionClassifier.cs` and `<X>ResilienceStrategyFactory.cs` under `Processing/Resilience/`. Entrypoints set `FlowExceptionClassifier`, `ResilienceStrategy`, `RecoveryHandler`. See "Behavior Drift" below for which collectors swapped to the trivial classifier on the entrypoint vs preserved the pre-existing one.
- **Phase 4 — PASS.** `SessionRetryDefaults.CreateTransient()`, `QualysCollectorConfiguration.CreateRetryDefaults()`, `TenableIoCollectorConfigurationBuilder.CreateTenableIoRetryOptions()` all set both knobs.

## Build / Test / Grep Gates

- **Build:** Clean. `0 Warning(s) 0 Error(s)`, 9.38s.
- **Test:** All run targets pass — DummyCollector.Test 50/50, FalconCollector.Test 69/69, per-collector smoke tests 1/1 across 15 projects.
- **Grep:** `rg "AdapterDeferredRecoveryRequest|AdapterDeferredRecoveryMetadata" src` → 0 hits.

## Behavior Drift / Regressions

The contract permits trivial classifiers (`decisions.md` line 31). However, on some collectors the entrypoint's pre-existing `FlowExceptionClassifier` property was *replaced* with the new trivial Resilience classifier, dropping vendor-specific mappings that existed at baseline `40c98cc`. Result: documented user-facing error codes / retryability flags are gone.

Pre-existing vendor logic **dropped on the entrypoint** in these collectors:

- **Qualys** — `QualysCollector.cs:226` now points to `Processing.Resilience.QualysFlowExceptionClassifier` (trivial). Baseline pointed to `Processing.QualysFlowExceptionClassifier`, which maps `HttpRequestException` with HTTP 409 → `QUALYS_INSTANCE_BUSY` (retryable error). Same in `QualysResumeRunner.cs:56`. **The 409 → QUALYS_INSTANCE_BUSY behavior is gone end-to-end.**
- **DefenderVm** — `DefenderVmCollector.cs:186` now points to `Processing.Resilience.DefenderVmFlowExceptionClassifier` (trivial). Baseline `Processing.Validation.DefenderVmFlowExceptionClassifier` mapped `DefenderVmRateLimitExceededException` → `ADAPTER_EXCEPTION` + `IsRetryable=true`, plus `NotSupported`/`Argument` mappings. **All three mappings are gone on the entrypoint.** Resume runner (`DefenderVmResumeRunner.cs:78,140`) imports `Processing.Validation`, so it still resolves to the old classifier — behavior preserved in resume only. Asymmetric.
- **SentinelOne** — `SentinelOneCollector.cs:204` now points to `Processing.Resilience.SentinelOneFlowExceptionClassifier` (trivial). Baseline mapped `NotSupportedException` and `ArgumentException`. Both gone. Resume runner imports `Processing.Resilience` too → resume also degraded.
- **InsightVmCloud** — `InsightVmCloudCollector.cs:217` now resolves to `Processing.Resilience.InsightVmCloudFlowExceptionClassifier` (trivial, because only `Processing.Resilience` is imported; `Processing.Validation` is not). Baseline mapped `ArgumentException` → `INVALID_FILTER`. Gone on entrypoint and resume.
- **CloudGuard, Guardicore, Taegis** — same pattern via `ResilienceClassifier` alias. Baseline `NotSupportedException` and `ArgumentException` mappings dropped on entrypoint. Resume runners import `Processing.Validation`, so resume path still maps these.
- **ServiceNowCmdb** — same pattern. `NotSupportedException` and `ArgumentException` mappings dropped on entrypoint. Resume runner imports unclear; pattern matches the others.
- **IsbLoadTest** — switched to trivial; baseline behavior not load-bearing for a load-test collector.

Vendor logic **preserved on the entrypoint** in these collectors (worker explicitly qualified the namespace to keep the old classifier):

- **CortexXdr** — `CortexXdrCollector.cs:237`: `Processing.Validation.CortexXdrFlowExceptionClassifier.TryClassify` (qualified). Old mappings preserved.
- **MicrosoftEntraId** — `MicrosoftEntraIdCollector.cs:203`: `Processing.Validation.MicrosoftEntraIdFlowExceptionClassifier.TryClassify` (qualified). Old mappings preserved.
- **DefenderForCloud** — `DefenderForCloudCollector.cs:201`: unqualified `DefenderForCloudFlowExceptionClassifier` resolves to `Processing.Validation` (only `Processing.Validation` and `Processing` imported, not `Processing.Resilience`). Old mappings preserved.
- **InsightVm** — no pre-existing classifier; new trivial is net-neutral.
- **TenableIo, TenableSc** — no pre-existing classifier; new trivial is net-neutral.

**Severity assessment:**

- Qualys 409 → `QUALYS_INSTANCE_BUSY` is the most impactful loss. The old mapping flagged `IsRetryable=true` with a specific user-facing error code. Now a 409 falls through to the resilience strategy's generic handling, which will likely classify it via `RetryableTransportFailurePolicy` (409 is in Qualys's `RetryStatusCodes`, so transport retries continue) — but the surfaced error code on terminal failure changes from `QUALYS_INSTANCE_BUSY` to whatever the fallback produces. Observability + customer-facing diagnostics regress.
- DefenderVm `RateLimitExceededException` → `ADAPTER_EXCEPTION` retryable: this drops a clean retryable signal on the entrypoint path. Resume path still has it via `Processing.Validation` import.
- The `NotSupportedException` / `ArgumentException` → `FLOW_NOT_SUPPORTED` / `INVALID_CONFIGURATION` mappings (CloudGuard, Guardicore, Taegis, ServiceNowCmdb, SentinelOne, DefenderVm) are useful but lower-stakes: those exceptions still terminate the run; only the surfaced error code changes.

Contract-wise this is sanctioned by `decisions.md` line 31 ("trivial baselines … vendor-specific refinements are explicit follow-ups"). Operationally, the user should know that:
1. The "trivial baseline" decision was applied unevenly across collectors.
2. Qualys-specific 409 handling is the most concrete user-visible behavior change and should probably be restored or moved into the shared policy chain before merge.

## Open Assumptions Resolution

- **A8 — RESOLVED.** `AdvancePage(0, 0)` increments `CurrentPage` by 1 and calls `OnCheckpoint`, but does not inflate `ProcessedItems` / `ProcessedFindings`. The new `AdapterFailureDecisionExecutorTests.ExecuteAsync_WhenRecoveryContinues_UpdatesBudgetAndReturnsPartialWaitResult` test (per execution_notes) asserts this directly. Phase 2 README documents the trade-off at line 88.
- **A9 — RESOLVED (outer-match sufficient).** `Cymulate.Http.Package.DefensiveToolkit/Policies/RetryPolicy.cs:70-74` rethrows `ServerSuggestedRetryDelayException` unwrapped. Outer-type check is the common path; inner walk in `FindServerSuggestedDelay` is defensive (per execution_notes Phase 1).
- **A10 — RESOLVED.** `Collectors/TenableScCollector/` exists; received Phase 3 treatment. 16 total collectors wired (Falcon + 15).

## Scope Deviations

- **`Tools/LocalAdapterRunner/Execution/LoggingAdapterExecutionContext.cs`** — Minimal, behavioral. Removed a single `else if (request is AdapterDeferredRecoveryRequest dr)` branch that became dead with the deletion. Required to keep the LocalAdapterRunner project compilable. The publish branch was diagnostic-only logging and had no production effect. Verdict: acceptable.
- **`Shared/Recovery/README.md`** — Docs only. Line 78 and line 86 rewritten to describe `AdvancePage(0, 0) + PartialResult(...)`. Required for the grep gate to pass (the README mentioned `AdapterDeferredRecoveryRequest`). Non-behavioral. Verdict: acceptable.

Both deviations were surfaced in `execution_notes.md` and are correctly minimal.

## Unresolved Gaps

- The Qualys 409 → `QUALYS_INSTANCE_BUSY` mapping is gone from both the entrypoint and the resume runner. If the operator wants the user-facing error code preserved, a small repair is warranted: either (a) re-point `QualysCollector.cs:226` and `QualysResumeRunner.cs:56` to `Processing.QualysFlowExceptionClassifier.TryClassify`, or (b) accept the loss as an explicit follow-up and document it in `decisions.md`.
- Same call for DefenderVm `RateLimitExceededException` mapping on the entrypoint (resume path is still covered).
- Same call for InsightVmCloud `INVALID_FILTER` mapping.

These are accepted-risk items, not contract violations. Flagging for operator review.

## Verdict

**PASS with accepted risks.** Contract success criteria all green. Code-bearing regression of pre-existing vendor classifier mappings on the entrypoint path is contract-sanctioned (`decisions.md` line 31) but operationally meaningful for Qualys, DefenderVm, and InsightVmCloud. Recommend surfacing to operator before merge to confirm whether the "trivial baseline" applies to *new* classifiers only (leaving pre-existing entrypoint wiring intact) or also overrides pre-existing vendor-mapping wiring. The latter is what shipped on 8 of 15 collectors.
