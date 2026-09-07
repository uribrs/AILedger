# Verifier-1 — I11 defensive-toolkit cleanup

Verified against actual code (not notes). All file:line refer to current working tree.

## Sub-item 1 — interface boundary docs — PASS
- `IResiliencePolicy.cs:3-8` — XML `<summary>` states "a single resilience concern ... does not compose other policies. Composition/orchestration ... is `IDefensivePolicyRunner`'s role, not a policy's." Correct distinction.
- `IDefensivePolicyRunner.cs:3-14` — `<summary>` states it "composes and orchestrates multiple `IResiliencePolicy` instances ... is NOT itself a single resilience policy"; `<remarks>` notes the shared `ExecuteAsync<T>` shape is "intentional, not a duplication to be collapsed."
- NO signature/API change. Both interfaces keep identical `Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ...)` (runner retains its `= default`). `git diff` shows additions are comment-only on both files. PASS.

## Sub-item 2 — extract server-delay gate — PASS (behavior preserved)
New collaborator `Policies/ServerSuggestedDelayGate.cs` (internal sealed, line 22) owns relocated logic. RetryPolicy delegates via single `_serverDelayGate` field (`RetryPolicy.cs:19`, ctor `:31`).

Removed from RetryPolicy (confirmed via `git diff`, all four methods + both fields deleted):
- `ResolveResponseRetryDelay`, `CreateClassifierDelayResolution`, `ClampServerSuggestedDelay`, `ExternalizeResponseDelayBeforeRetryGates`
- fields `_retryDelayResolver`, `_serverSuggestedDelayExternalizer`
- now-unused `using ...RateLimiting` (diff line `-using ...RateLimiting`)

1:1 logic mapping (old removed code vs gate), verified byte-for-byte from diff:
- `Resolve` (gate:40-71) == old `ResolveResponseRetryDelay`: header→clamp→ForServerHeader; else custom→clamp→ForCustomResponseDelay; else null. Identical.
- `ForClassifierDelay` (gate:74-86) == old `CreateClassifierDelayResolution`. Identical.
- private `Clamp` (gate:110-119) == old `ClampServerSuggestedDelay`. Identical.
- `ExternalizeFromHeaders` (gate:93-101) == old `ExternalizeResponseDelayBeforeRetryGates`: early-return on `!ExternalizeServerSuggestedDelays`, Resolve, ThrowIfConfigured. Identical.
- `Externalize` (gate:107-108) is a thin pass-through to `_externalizer.ThrowIfConfigured` (the same call old RetryPolicy made directly). Guards (`ExternalizeServerSuggestedDelays` + `Delay > 0`) still in externalizer.

`RateLimitHeaderRetryDelayResolver` instantiated once in gate ctor (gate:31), matching old single-field construction — no change in resolver instance/count.

### Behavior preservation — call sites map 1:1, same order/count — PASS
- ExecuteAsync post-success: `RetryPolicy.cs:53` `_serverDelayGate.ExternalizeFromHeaders` (was `ExternalizeResponseDelayBeforeRetryGates`, same position).
- ShouldHandleAsync pre-gate: `RetryPolicy.cs:121` `_serverDelayGate.ExternalizeFromHeaders` (same, first statement before retry-allowed/exception/classify branches — precedence intact).
- ClassifyResultAsync classifier-with-delay: `:233-234` `ForClassifierDelay` + `Externalize` (was `CreateClassifierDelayResolution` + `ThrowIfConfigured`).
- ClassifyResultAsync classifier-without-delay: `:241-243` `Resolve` + guarded `Externalize`.
- ClassifyResultAsync no-classifier retryable: `:258-260` `Resolve` + guarded `Externalize`.

Header-before-classifier precedence intact: pre-gate `ExternalizeFromHeaders` runs in `ShouldHandleAsync` (line 121) BEFORE classification (line 149→`ClassifyResultAsync`); classifier-supplied delays externalize only on the not-short-circuited path. Unchanged from prior structure.

### Throw + pre-throw disposal unchanged — PASS
`ServerSuggestedDelayExternalizer.cs` is NOT in the working-tree diff (untouched). `ThrowIfConfigured` (:20-46) still: guard enabled → guard `Delay<=0` → build metadata → record telemetry → `response.Dispose()` (:43) → `throw ServerSuggestedRetryDelayException` (:45). The dispose-before-throw invariant is preserved.

## Sub-item 3 — HTTP-first generic note — PASS
`IResiliencePolicy.cs:9-14` `<remarks>`: package is HTTP-first; result-classifying policies inspect only `HttpResponseMessage`, other `T` is pass-through for result handling, exception handling is generic. No generics rework. PASS.

## Resolve-count NOT deduplicated — PASS (defensible behavior-preservation, not unfinished)
execution_notes records this consciously. The contract Success Criterion says "resolved at most once per response (no 3x re-resolution)." This is the one criterion NOT literally met. Assessment: collapsing to a single resolution point would change the observable invocation count of the caller-supplied `GetRetryDelayFromResponse` delegate (gate:56 invokes it per `Resolve` call) AND/OR reorder header-vs-classifier externalization. Both are behavior changes. The contract's governing mandate is behavior preservation ("If consolidation would require any behavior change to stay correct, STOP and surface"), and the stop-condition explicitly authorizes leaving it. The LOGIC is consolidated into one collaborator (the SRP win); the call structure/counts are preserved deliberately. This is correctly a documented trade-off, not a correctness gap. The "at most once" sub-clause is therefore PARTIAL on the letter but the overriding behavior-preservation mandate is satisfied — and the contract itself prioritizes the latter.

## Constraint checks — PASS
- Policy execution order: untouched (no pipeline-composition file changed; only RetryPolicy internals + gate). PASS.
- No runtime-semantics change: relocation + delegation only; tests confirm. PASS.
- No new dependencies: gate `using`s reference only existing internal namespaces (Models.Retry, Options, RateLimiting, Logging, Diagnostics). No csproj/Directory.Build.props/Directory.Packages.props change (`git diff` empty for props). PASS.
- Directory.Build.props still 1.6.3 (no diff). PASS.
- No CHANGELOG entry (no diff matching *CHANGELOG*/*.csproj). PASS.
- Nothing committed: HEAD still bf55d6d; working tree = 3 modified + 1 untracked (the 4 expected files only). PASS.

## Test results — PASS
- `dotnet test Cymulate.Http.Package.sln`: Passed 268, Failed 0, Skipped 0 — 268/268 green.
- Spot-checks: RetryPolicyContractTests 40/40, SessionRetryEdgeCaseTests 6/6, SessionRetryContractTests 18/18, CoreOwnershipAndSaturationTests 9/9 — all pass.
- Build: succeeded, warnings are pre-existing NU1900 (offline package source), no new code warnings.

## Behavior-drift hunt — none found
- No call site changed semantics; every removed inline call has an exact gate counterpart at the same position.
- No missed precedence: pre-gate short-circuit before classification preserved; guarded `Externalize` (null-check) preserved on all three resolve paths.
- No externalization guard moved incorrectly: the `ExternalizeServerSuggestedDelays`/`Delay<=0` guards remain in the externalizer where they always were; `ExternalizeFromHeaders` retains its own redundant early-return exactly as the old method had it.

## OVERALL VERDICT: PASS
Contract Success Criteria met and the behavior-preservation mandate is fully satisfied. The single literal deviation (resolve-count not deduped) is an explicitly authorized, correctly-documented behavior-preservation decision — the contract ranks behavior preservation above the "resolve once" optimization and provides a stop-condition for exactly this case. Build green, 268/268 tests pass, version unchanged, nothing committed.
