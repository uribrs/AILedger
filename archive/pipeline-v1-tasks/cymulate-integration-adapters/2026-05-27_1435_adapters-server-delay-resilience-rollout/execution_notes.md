# Execution Notes

## 2026-05-27 Contract Refresh

- Updated the task from "blocked on ISB A1/A7" to "adapter migration with ISB fix available as prerequisite."
- Captured the agreed adapter-side contract: mutate `AdapterProgressContext.AdapterState`, call `AdvancePage(0, 0)`, then return `AdapterResult.PartialResult(...)`.
- Preserved the important boundary: implement in Shared resilience; avoid Falcon production-code edits unless evidence forces scope expansion.
- Reframed tests around `PartialWaitRequired` and checkpoint snapshot behavior instead of custom publish-request assertions.
- Left A8 open only for the observable effect of synthetic zero-item page advancement. The next implementation pass should verify counters and resume behavior rather than assuming nobody looks at the page increment.

## 2026-06-01 Phase 1 — ServerSuggestedRetryDelayPolicy

- **Commit:** `e11ade6` on branch `codex/adapter-resilience-strategy`.
- **Files touched:**
  - NEW: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/Policies/ServerSuggestedRetryDelayPolicy.cs`
  - MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/Logic/AdapterResilienceStrategy.cs` (one-line policy ordering insert between `ProgrammerBugPolicy.Definitive` and `RetryableTransportFailurePolicy`).
  - MODIFY: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.DummyCollector.Test/AdapterResilienceStrategyTests.cs` (two new tests + local `CreateSuggestedDelay` helper).
- **A9 resolution — RESOLVED (outer-match-sufficient).** `Cymulate.Http.Package.DefensiveToolkit/Policies/RetryPolicy.cs:70-74` has a dedicated `catch (ServerSuggestedRetryDelayException) { ...; throw; }` that rethrows the exception unwrapped, so Polly does not box it inside `OperationCanceledException` or a Polly-internal wrapper. The outer-type check is the common path; the `InnerException` walk implemented in `FindServerSuggestedDelay` is defensive only (e.g. against user code that rebrands the exception). The walk is mandatory per the contract regardless of Polly's behavior.
- **Tests:** Filtered run `--filter "FullyQualifiedName~ServerSuggested|FullyQualifiedName~AdapterResilienceStrategy"` on `DummyCollector.Test`: 11 passed / 0 failed / 0 skipped (9 pre-existing + 2 new). Both new tests assert the `RequestDeferredRecovery` decision type, exact `DelaySequence[0]` equality with `ex.Delay`, `MaxRetries == 1`, `UseJitter == false`, error code `SERVER_SUGGESTED_DELAY`, severity `Warning`, and `Reason == "server-suggested-delay:<source>"`. Direct-exception and wrapped-as-`InvalidOperationException.InnerException` paths both green.
- **Builds:** `Cymulate.Integration.Adapters.Shared.csproj` clean (0 warn / 0 err). `DummyCollector.Test.csproj` clean (0 warn / 0 err).
- **No forbidden file changes.** Scope held to the three allowed paths.

## 2026-06-01 Phase 2 — Executor migration to AdapterResult.PartialResult

- **Commit:** `a23baf0` on branch `codex/adapter-resilience-strategy`.
- **Files touched (14 total):**
  - MODIFY (4 production): `AdapterFailureDecisionExecutor.cs` (executor now returns `AdapterResult.PartialResult(schedule.Delay, $"deferred-recovery:{recover.Reason}", data)` after `AdvancePage(0, 0)`); `AdapterFailureDecisionExecutionContext.cs` (dropped `RequestDeferredRecoveryAsync` parameter); `AdapterBusStrategyFlowExecutor.cs` and `CollectorResumeStrategyExecutor.cs` (removed publish wiring).
  - MODIFY (2 doc): `Shared/Resilience/README.md` rewrite; `Shared/Recovery/README.md` step 5 rewrite to describe `AdvancePage(0, 0) + PartialResult` instead of publishing `AdapterDeferredRecoveryRequest`.
  - DELETE (2): `AdapterDeferredRecoveryRequest.cs`, `AdapterDeferredRecoveryMetadata.cs`.
  - MODIFY (5 test): `FalconCollectorTests.cs`, `FalconResumeRunnerTests.cs`, `AdapterBusEntrypointRunnerPolicyTests.cs`, `CollectorResumeRunnerPolicyTests.cs`, `AdapterFailureDecisionExecutorTests.cs`. `AdapterBackoffAndBudgetTests.cs` and `AdapterResilienceStrategyTests.cs` were on the list but already migrated/unaffected — no edits required.
  - MODIFY (1 outside listed scope, surfaced): `Tools/LocalAdapterRunner/Execution/LoggingAdapterExecutionContext.cs` had an `else if (request is AdapterDeferredRecoveryRequest dr)` branch. Removing the deleted type left the LocalAdapterRunner project non-compilable. The branch was deleted in this commit. The Recovery README similarly contained a stale step 5; updated to keep the grep gate clean.
- **A8 verdict — RESOLVED.** `AdapterProgressContext.AdvancePage(0, 0)` increments `CurrentPage` by 1 and invokes `OnCheckpoint`, but `Interlocked.Add(ref _processedItems, 0)` and `Interlocked.Add(ref _processedFindings, 0)` leave the externally-visible item/finding counters unchanged. The new executor test `ExecuteAsync_WhenRecoveryContinues_UpdatesBudgetAndReturnsPartialWaitResult` asserts this directly: `CurrentPage == pageBefore + 1`, `ProcessedItems == itemsBefore`, `ProcessedFindings == findingsBefore`. Synthetic page bump is the only externally-visible side effect, and it is the intended checkpoint trigger.
- **Test migration notes:** The previous tests relied on `AdapterBackoffPlan.None with { MaxRetries = 1 }`, which returns `TimeSpan.Zero` from `GetDelay(...)`. Under the new contract, `AdapterResult.PartialResult(...)` throws on `<= TimeSpan.Zero`. Tests on the recovery-continues path were updated to use concrete short delays (2–7 s) consistent with how production policies (`ServerSuggestedRetryDelayPolicy`, Falcon's recovery plan) supply real positive delays. Tests on decline / budget-exhaustion / hook-throws paths still use `.None with { MaxRetries = 1 }` because they exit through the fallback decision before reaching `PartialResult`.
- **Tests:** `DummyCollector.Test` 50/50 passed, `FalconCollector.Test` 69/69 passed. Solution-wide `dotnet build` clean (0 warn / 0 err).
- **Grep gate:** `rg "AdapterDeferredRecoveryRequest|AdapterDeferredRecoveryMetadata" src` returns zero hits.
- **Scope deviations surfaced:** Two files outside the explicit allow-list were edited because the deletion of `AdapterDeferredRecoveryRequest` would otherwise break the solution build and the grep gate: `Tools/LocalAdapterRunner/Execution/LoggingAdapterExecutionContext.cs` (single `else if` branch removed — production code, mechanically required by the deletion) and `Shared/Recovery/README.md` (one bullet rewritten — documentation, required by grep gate). Both are minimal, non-behavioral, and necessary to satisfy the explicit success criteria. No collector vendor logic was touched.

## 2026-06-01 Phase 4 — SessionRetryDefaults externalization + Qualys/TenableIo propagation

- **Commit:** `492cf97` on branch `codex/adapter-resilience-strategy`.
- **Files touched (3 production, all in allowed scope):**
  - MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Session/SessionRetryDefaults.cs` — `CreateTransient()` now sets `ExternalizeServerSuggestedDelays = true` and `MaxServerSuggestedDelay = TimeSpan.FromHours(24)` in the object initializer alongside existing `MaxAttempts`, `Delay`, `BackoffStrategy`, `ShouldRetryExceptionAsync`.
  - MODIFY: `src/Cymulate.Integration.Adapters/Collectors/QualysCollector/Processing/Configuration/QualysCollectorConfiguration.cs` — `CreateRetryDefaults()` propagates the same two settings after seeding custom `RetryStatusCodes` (409/500/503/504). The values would be inherited from `CreateTransient()` regardless; explicit assignment matches the contract's requirement that the factory's returned `RetryOptions` carry them and survives any future reset of the shared baseline.
  - MODIFY: `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector/Processing/Configuration/TenableIoCollectorConfigurationBuilder.cs` — `CreateTenableIoRetryOptions()` propagates the same two settings after adding `HttpStatusCode.InternalServerError` (500).
- **Builds:** Solution-wide `dotnet build src/Cymulate.Integration.Adapters/Cymulate.Integration.Adapters.sln` clean (0 warn / 0 err, 7.85s).
- **Tests:** `DummyCollector.Test` 50/50 passed (Phase 4 gate). No strategy/executor behavior change in this phase, as expected.
- **Scope deviations:** None. Strict adherence to the three-file allowlist.
- **Verification note on `RetryOptions` default:** `MaxServerSuggestedDelay` had a default of `TimeSpan.FromMinutes(1)` in `Cymulate.Http.Package/DefensiveToolkit/Contracts/Options/RetryOptions.cs`. The 24h override is now in effect for every collector path: the 13 standard collectors via `SessionRetryDefaults.CreateTransient()` (used by `DefaultSessionFactory`), Qualys via its dedicated `CreateRetryDefaults()`, and TenableIo via its dedicated `CreateTenableIoRetryOptions()`.

## Phase A (W-PA) — 2026-06-01

- **Commit:** `448fa11` on branch `codex/adapter-resilience-strategy`.
- **Closes:** B1 (RecoveryHandler Continue), B2 (same root cause at session layer), M1 (executor end-to-end test), M2 (MaxRetries=3) from the code review.
- **Files touched (21 total):**
  - MODIFY (1 policy): `Shared/.../Resilience/Policies/ServerSuggestedRetryDelayPolicy.cs` — `MaxRetries = 1` → `MaxRetries = 3`. Single-line edit; `DelaySequence` stays single-element and `GetDelay` clamps via `Math.Min`, so all 3 attempts honor the same vendor-supplied delay.
  - MODIFY (15 collectors): swapped `RecoveryHandler` from `Decline("no vendor-specific recovery")` to `Continue(new AdapterRecoveryContinuation(..., ctx.WorkItem, no-op))` in `CloudGuardCollector.cs`, `CortexXdrCollector.cs`, `DefenderForCloudCollector.cs`, `DefenderVmCollector.cs`, `GuardicoreCollector.cs`, `InsightVmCollector.cs`, `InsightVmCloudCollector.cs`, `IsbLoadTestCollector.cs`, `MicrosoftEntraIdCollector.cs`, `QualysCollector.cs`, `SentinelOneCollector.cs`, `ServiceNowCmdbCollector.cs`, `TaegisCollector.cs`, `TenableIoCollector.cs`, `TenableScCollector.cs`. SentinelOne previously used `static _ =>`; normalized to `static ctx =>`. All 15 already had `using Cymulate.Integration.Adapters.Shared.Resilience;`, so no using-directive changes needed.
  - ADD test: `UnitTests/.../DummyCollector.Test/AdapterFailureDecisionExecutorTests.cs` — new test `ExecuteAsync_WhenServerSuggestedDelayFlowsThroughContinueHook_ReturnsPartialWaitResult` exercises the full strategy + executor pipeline end-to-end with `ServerSuggestedRetryDelayException` (delay=42s), asserts `PartialWaitRequired`, `ResumeAfter == 42s`, `WaitReason == "deferred-recovery:server-suggested-delay:Retry-After"`, and that neither `PublishFailureAsync` nor `CompletePartialAsync` fires. Added class-level docstring framing the regression-guard intent. Required `using System.Net; using System.Net.Http; using Cymulate.Http.Package.DefensiveToolkit.Contracts.Exceptions; using Cymulate.Http.Package.DefensiveToolkit.Contracts.Models.Retry; using Microsoft.Extensions.Logging.Abstractions;`.
  - MODIFY assertions: `UnitTests/.../DummyCollector.Test/AdapterResilienceStrategyTests.cs` — bumped `MaxRetries.Should().Be(1)` to `.Be(3)` on the ServerSuggested test.
  - MODIFY assertions (out-of-original-allowed-scope): `UnitTests/.../{CloudGuard,Taegis,TenableSc}Collector.Test/<X>ServerSuggestedDelayTests.cs` — bumped `MaxRetries.Should().Be(1)` to `.Be(3)`. These 3 collector smoke tests pin the same shared-policy assertion; without bumping them, gate 3 (solution-wide `--filter "FullyQualifiedName~ServerSuggested"`) fails. Mechanical one-line edits, intent-aligned with M2. Other 12 collector smoke tests do not pin MaxRetries.
- **Builds:** Solution-wide `dotnet build` clean (0 warn / 0 err, 7.85s pre-fix; 5.37s post-fix).
- **Tests (4 gates, all green):**
  - Gate 1 — `DummyCollector.Test --filter "FullyQualifiedName~ServerSuggested|FullyQualifiedName~AdapterResilienceStrategy|FullyQualifiedName~AdapterFailureDecisionExecutor"`: **17/17 passed** (51 ms).
  - Gate 2 — same filter, isolated to DummyCollector project (run as part of Gate 1).
  - Gate 3 — solution-wide `--filter "FullyQualifiedName~ServerSuggested"`: 15 collector projects + DummyCollector all green (18 total `Passed!` lines including the 3 fixed smoke tests).
  - Gate 4 — Falcon full test suite: **69/69 passed** (3 s). No regression from `MaxRetries = 3` propagation — Falcon's `FalconResilienceStrategyFactory.Create()` does not use the shared `ServerSuggestedRetryDelayPolicy.BackoffPlan` for its primary retry semantics.
- **Scope deviations:**
  - 3 collector smoke test files (CloudGuard, Taegis, TenableSc) edited despite not appearing in the allowed-files list. They pin `MaxRetries == 1` on the shared-policy decision; without bumping them, gate 3 fails by design. Decision: bump in place rather than block. Mechanical, one-line, intent-aligned with the explicitly-sanctioned M2 change. Flagged in commit body via the M2 reference and in this section.
- **Surprises:**
  - SentinelOne was the only collector using `static _ =>` instead of `_ =>`. Pattern-matched on the literal `static _ =>` shape for that one edit; preserved `static` modifier in the replacement (now `static ctx =>`). All 15 replacements use `static ctx =>` uniformly.
  - IsbLoadTestCollector lives one directory level deeper (`Collectors/IsbLoadTestCollector/Cymulate.Integration.Adapters.Collectors.IsbLoadTestCollector/IsbLoadTestCollector.cs`) than the harness allowed-files list literally specified. Treated as the same intent and proceeded.
  - DummyCollector.Test reports 3 total ServerSuggested tests in gate 3 (vs 17 in gate 1) because gate 3's filter is `ServerSuggested` alone — narrower than gate 1's union filter.

## Phase B (W-PB) — 2026-06-01

- **Commit:** `625b03a` on branch `codex/adapter-resilience-strategy`.
- **Scope:** documentation only; no behavior changes.
- **Files touched (3 production):**
  - MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/README.md` — added `Overview` and `Hybrid Retry Boundary` sections at the top; expanded `Decision Flow` to enumerate the actual `CreateDefault` chain (UserCancellationPolicy → ProgrammerBugPolicy.Definitive → ServerSuggestedRetryDelayPolicy → RetryableTransportFailurePolicy → vendor → MappedFailurePolicy → ProgrammerBugPolicy.Ambiguous → UnknownFlowFailurePolicy → FallbackFailurePolicy); added `Per-Vendor Classifiers` section clarifying that the baseline returns `null` for all exceptions and Falcon is the only non-trivial classifier; renamed `RecoverAndRetry Contract` → `RecoverAndRetry / RequestDeferredRecovery Contract` and added the Continue-with-no-op-continuation pattern + Decline semantics. Preserved `Decisions`, `Ownership`, `Structure`, `Continuations`, `Falcon Reference Slice`, `Test Expectations`, and budget keys sections. No references remain to `AdapterDeferredRecoveryRequest`, `MaxRetries = 1`, or `defaultTransportBackoff`.
  - MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/Policies/ServerSuggestedRetryDelayPolicy.cs` — added class-level XML doc summarizing catch surface (`ServerSuggestedRetryDelayException` + `InnerException` chain walk for defensive matching), emitted decision shape (`RequestDeferredRecovery`, `MaxRetries = 3`, single-element `DelaySequence = [ex.Delay]`, `UseJitter = false`), `MaxRetries = 3` rationale (bumped from `1` in `448fa11` to give sustained-quota cases two more chances before publish-failure), and chain position (between `ProgrammerBugPolicy.Definitive` and `RetryableTransportFailurePolicy`). Body byte-for-byte identical.
  - MODIFY: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Resilience/Logic/AdapterFailureDecisionExecutor.cs` — added XML doc on `CompleteRecoveryAttemptAsync` covering the Continue/Decline branching: Continue → `BuildPartialWaitResult` → `AdvancePage(0, 0)` + `AdapterResult.PartialResult` (success path); Decline (or any non-Continue) → `ExecuteFallbackAsync` (recurses into embedded `FallbackDecision`, typically `PublishFailure`). Explains the no-op continuation default for hook-less collectors and the `Decline` semantics (Falcon's no-safe-watermark case). Method body byte-for-byte identical.
- **Builds:** Solution-wide `dotnet build src/Cymulate.Integration.Adapters/Cymulate.Integration.Adapters.sln` clean (0 warn / 0 err, 8.54s).
- **Tests:**
  - `DummyCollector.Test`: **51/51 passed** (identical to post-W-PA count; W-PA added one new executor end-to-end test, so 50 → 51 carried forward).
  - `FalconCollector.Test`: **69/69 passed** (identical to W-PA).
- **Scope deviations:** None. Strict adherence to the three-file allowlist. No collector code, no test code, no other docs touched.

