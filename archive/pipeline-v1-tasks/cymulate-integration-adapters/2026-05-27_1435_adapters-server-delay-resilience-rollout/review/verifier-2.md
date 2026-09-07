# Verifier Report — TASK-20260527-1435 (run 2)

## Summary

**Verdict: PASS.**

W-PA + W-PB close all four findings (B1, B2, M1, M2) that code-reviewer-1 surfaced against P1-P4. The simplified scope chosen by the operator (drop `defaultTransportBackoff` plumbing, drop per-vendor classifier rollout) was respected: 23 files touched across two commits, no out-of-partition edits, no Falcon production-code edits. All build and test gates re-run by the verifier are green.

## Findings Closure

- **B1: PASS** — All 15 non-Falcon `*Collector.cs` entrypoints now wire `RecoveryHandler` to return `Continue` with a no-op continuation matching the plan spec.

  Evidence (CloudGuardCollector.cs, post-diff):
  ```csharp
  RecoveryHandler = static ctx => new ValueTask<AdapterRecoveryResult<CloudGuardCollectorTriggerRequest>>(
      new AdapterRecoveryResult<CloudGuardCollectorTriggerRequest>.Continue(
          new AdapterRecoveryContinuation<CloudGuardCollectorTriggerRequest>(
              "no vendor-specific state to rewire; honor scheduled wait",
              ctx.WorkItem,
              static (req, progressCtx, ct) => Task.FromResult(0))))
  ```
  Same shape in QualysCollector.cs (with `QualysCollectorTriggerRequest`) and the other 13. Falcon's `FalconCollector.cs` and `RecoverFreshAsync` are untouched in this range (`git diff 492cf97..HEAD -- Collectors/FalconCollector` → 0 lines).

- **B2: PASS** — Closed transitively by B1. Same root cause: with externalization on, `ServerSuggestedRetryDelayException` produces `RequestDeferredRecovery`; the executor now invokes the Continue recovery hook for all 15 collectors and routes through `BuildPartialWaitResult` → `AdapterResult.PartialResult`. The 429+Retry-After regression for non-Falcon collectors is resolved.

- **M1: PASS** — `AdapterFailureDecisionExecutorTests.ExecuteAsync_WhenServerSuggestedDelayFlowsThroughContinueHook_ReturnsPartialWaitResult` added (100 lines including a class-level docstring framing the regression-guard intent). The test:
  - Constructs a real `ServerSuggestedRetryDelayException` (delay = 42 s, source = `Retry-After`, status = 429).
  - Runs the full `AdapterResilienceStrategy.CreateDefault()` chain → asserts `RequestDeferredRecovery`.
  - Builds an `AdapterFailureDecisionExecutionContext<object>` with a Continue-returning `RecoverAsync` and mock `PublishFailureAsync`/`CompletePartialAsync`.
  - Asserts `result.Status == PartialWaitRequired`, `ResumeAfter == 42s`, `WaitReason == "deferred-recovery:server-suggested-delay:Retry-After"`, and that neither publish nor complete-partial fires.

  Test run: `dotnet test ... --filter "FullyQualifiedName~AdapterFailureDecisionExecutor"` → 6/6 passed, 58 ms.

- **M2: PASS** — `ServerSuggestedRetryDelayPolicy.cs:28` flipped from `MaxRetries = 1` to `MaxRetries = 3`. Three test files have matching assertion bumps:
  - `AdapterResilienceStrategyTests.cs:214` → `.Be(3)`
  - `CloudGuardServerSuggestedDelayTests.cs:33` → `.Be(3)`
  - `TaegisServerSuggestedDelayTests.cs:33` → `.Be(3)`
  - `TenableScServerSuggestedDelayTests.cs:34` → `.Be(3)`

  The 3 ancillary collector-test files were flagged in `execution_notes.md` (W-PA section) as out-of-original-allowed-scope but mechanically required to keep gate 3 green. The operator-state record accepts this deviation: `state.json` `steps[PA].fileScope` enumerates them with the explicit "out-of-original-scope but mechanically required for gate 3" note. Intent-aligned with M2; mechanical one-line edits.

## Docs Verification (Phase B)

- **`Shared/Resilience/README.md`** (50-line diff):
  - No `AdapterDeferredRecoveryRequest` references (verified by grep — 0 hits in the file).
  - No `MaxRetries = 1` references (verified — 0 hits).
  - No `defaultTransportBackoff` references (verified — 0 hits).
  - Policy chain order accurately listed in §"Decision Flow": `UserCancellationPolicy → ProgrammerBugPolicy.Definitive → ServerSuggestedRetryDelayPolicy → RetryableTransportFailurePolicy → optional vendor policies → MappedFailurePolicy → ProgrammerBugPolicy.Ambiguous → UnknownFlowFailurePolicy → FallbackFailurePolicy`. Matches the actual `AdapterResilienceStrategy.CreateDefault` chain.
  - Adds §"Hybrid Retry Boundary" (Polly sub-30s in-process vs ISB long-wait via `PartialResult`), §"Per-Vendor Classifiers" (baseline returns null; Falcon is the only non-trivial classifier), and rewrites §"RecoverAndRetry / RequestDeferredRecovery Contract" with the Continue-with-no-op pattern and Decline semantics.

- **`ServerSuggestedRetryDelayPolicy.cs`** — class-level XML docstring added (46 net-added lines). Covers:
  - Catch surface (direct `ServerSuggestedRetryDelayException` + `InnerException` chain walk via `FindServerSuggestedDelay`).
  - `RetryPolicy` rethrow behavior (outer-match-sufficient; inner walk is defensive).
  - Emitted decision shape: `RequestDeferredRecovery`, `MaxRetries = 3`, single-element `DelaySequence`, `UseJitter = false`.
  - `MaxRetries = 3` rationale (bumped from 1 in `448fa11` for sustained-quota patience).
  - Chain position between `ProgrammerBugPolicy.Definitive` and `RetryableTransportFailurePolicy`.

- **`AdapterFailureDecisionExecutor.cs`** — XML docstring on `CompleteRecoveryAttemptAsync` (35 net-added lines). Covers Continue → `BuildPartialWaitResult` → `AdapterResult.PartialResult` (success path) and Decline → `ExecuteFallbackAsync` recurses into embedded `FallbackDecision` (typically `PublishFailure`). Also documents the `Continue`-with-no-op default for hook-less collectors and `Decline`'s reserved use for "I cannot safely resume" cases (Falcon's no-safe-watermark path).

- **No behavior changes in W-PB code files** — verified: `git diff 448fa11..625b03a` for `ServerSuggestedRetryDelayPolicy.cs` and `AdapterFailureDecisionExecutor.cs` shows only `///`-prefixed lines added; no other code changed (`grep -v "^[+-][ ]*///"` returns empty).

## Build / Test Gates (re-run)

- **Build**: `dotnet build src/Cymulate.Integration.Adapters/Cymulate.Integration.Adapters.sln --nologo` → **Build succeeded. 0 Warning(s) 0 Error(s). Time Elapsed 00:00:12.56.**
- **DummyCollector.Test** (full suite): **51/51 passed**, 4 m. Includes the new M1 executor end-to-end test.
- **FalconCollector.Test** (full suite): **69/69 passed**, 5 s. `MaxRetries = 3` did not regress Falcon's policy chain (Falcon's `FalconResilienceStrategyFactory.Create()` does not consume `ServerSuggestedRetryDelayPolicy.BackoffPlan` for its primary cursor/auth retry semantics).
- **Solution-wide ServerSuggested filter**: 15 collector projects (CloudGuard, CortexXdr, DefenderForCloud, DefenderVm, Guardicore, InsightVm, InsightVmCloud, IsbLoadTest, MicrosoftEntraId, Qualys, SentinelOne, ServiceNowCmdb, Taegis, TenableIo, TenableSc) all green (1/1 each); DummyCollector ServerSuggested-only filter green (3/3). Total 18 passing across 16 test projects.

All four required gates pass.

## Operator-Scope Respect

Verified the diff did NOT touch:

- **Falcon production files**: `git diff 492cf97..HEAD -- Collectors/FalconCollector` → 0 lines.
- **`MappedFailurePolicy.cs`** — not in diff (`git diff 492cf97..HEAD --name-only` shows no match). Confirmed the dropped `defaultTransportBackoff` scope is not partially shipped.
- **`AdapterResilienceStrategy.cs`** — not in diff. Confirmed `CreateDefault` signature unchanged.
- **Per-vendor `Processing/Resilience/<X>FlowExceptionClassifier.cs`** — not in diff (the per-vendor classifier rollout was dropped from scope).
- **`Processing/Validation/<X>FlowExceptionClassifier.cs`** — not in diff (the deletion was scoped out).
- **ISB source / Indicators** — not in diff.

The 23-file diff is exactly: 15 × `*Collector.cs`, 3 × Shared files (`README.md`, `ServerSuggestedRetryDelayPolicy.cs`, `AdapterFailureDecisionExecutor.cs`), 5 × test files (1 new executor test, 1 strategy-test assertion bump, 3 ancillary smoke-test assertion bumps). The 3 ancillary smoke-test edits (CloudGuard, Taegis, TenableSc) sit outside the originally-allowlisted scope but are recorded in `state.json` `steps[PA].fileScope` with an explicit "out-of-original-scope but mechanically required for gate 3" note. Mechanical one-line edits, intent-aligned with M2.

## Open Assumptions

- **A12 — VALIDATED.** Continue with a no-op continuation safely produces `PartialResult` for hook-less collectors. Proven by the new `AdapterFailureDecisionExecutorTests` end-to-end test (which is exactly the Phase A.1 contract for all 15 collectors).
- **A13 — N/A (scope dropped).** Per-vendor classifier porting was removed from this round; assumption is moot. `state.json` workflow block records the scope drop ("orchestrator-scope-revision" skill entry).
- **A14 — N/A (scope dropped).** `defaultTransportBackoff` plumbing was removed; no consumer exists without per-vendor classifiers. `state.json` workflow block records this.

The assumptions file at `/Users/user/Dev/cymulate-integration-adapters/ai/active/2026-05-27_1435_adapters-server-delay-resilience-rollout/assumptions.md` still lists A13/A14 as `OPEN`. The operator's scope-revision (recorded in `state.json` skillsRun) effectively closes both as "scoped out". Recommend a docs-only follow-up to mark A13/A14 as `REJECTED — scope dropped per operator decision 2026-06-01` so the open-assumption ledger stays current. Not a blocker.

## Unresolved Gaps

None for this verification pass.

Note from verifier-1 about Qualys `QUALYS_INSTANCE_BUSY` / DefenderVm rate-limit / InsightVmCloud `INVALID_FILTER` mappings being dropped from the entrypoint classifier (Phase 3's accepted risk) remains an accepted risk — operator chose not to address it in this simplified scope. Surfaced for awareness; not a contract violation.

## Verdict

**PASS.** B1, B2, M1, M2 are closed by `448fa11`; Phase B docs in `625b03a` describe the actual current state of the resilience pipeline with no stale references. Operator-scope-discipline held: no out-of-partition edits, Falcon untouched, dropped scopes (`defaultTransportBackoff`, per-vendor classifier rollout) not partially shipped. All four re-run gates green. Recommend proceeding to code-reviewer-2.
