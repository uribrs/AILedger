# Execution Notes — I11

Direct path (contract-driven-execution). Behavior-preserving.

## Sub-item 1 — interface boundary docs (done)
- `IResiliencePolicy`: XML docs — a single resilience concern (retry/timeout/rate-limit/circuit-break) wrapping one operation; does NOT compose other policies.
- `IDefensivePolicyRunner`: XML docs — composes/orchestrates multiple policies into one execution; NOT itself a single policy; the matching `ExecuteAsync<T>` shape is intentional, not a duplication to collapse.
- No signature/code change. Decision per decisions.md (document, keep both).

## Sub-item 3 — HTTP-first generic contract note (done, cheap)
- Added an XML `<remarks>` note on `IResiliencePolicy` clarifying the package is HTTP-first: result-classifying policies (circuit breaker) only inspect `HttpResponseMessage`; other `T` is pass-through for result handling; exception handling is generic. No generics rework.

## Sub-item 2 — extract server-delay gate from RetryPolicy (done, behavior-preserving)
**New collaborator:** `DefensiveToolkit/Policies/ServerSuggestedDelayGate.cs` (internal sealed). Owns the
relocated resolution/clamp/externalize-orchestration logic; wraps the existing
`RateLimitHeaderRetryDelayResolver` and `ServerSuggestedDelayExternalizer` (the throw + pre-throw
response-dispose mechanism is unchanged, still in the externalizer).

Gate surface ↔ old RetryPolicy method:
- `Resolve(response)`            ← `ResolveResponseRetryDelay` (header→custom precedence + clamp)
- `ForClassifierDelay(...)`      ← `CreateClassifierDelayResolution`
- `ExternalizeFromHeaders(...)`  ← `ExternalizeResponseDelayBeforeRetryGates` (pre-gate: early-return if off; resolve; throw-if-positive)
- `Externalize(...)`             ← direct `ServerSuggestedDelayExternalizer.ThrowIfConfigured` wrapper
- private `Clamp`                ← `ClampServerSuggestedDelay`

**RetryPolicy changes:** removed the 4 methods above + the `_retryDelayResolver` and
`_serverSuggestedDelayExternalizer` fields; added a single `_serverDelayGate`. Removed the now-unused
`using ...RateLimiting`. RetryPolicy shrank ~385 → ~310 lines; it now owns pipeline construction,
classification, and telemetry only.

**Behavior preservation — call sites mapped 1:1, same order, same counts:**
- ExecuteAsync post-success (was line 56): `_serverDelayGate.ExternalizeFromHeaders` (identical to old pre-gate call).
- ShouldHandleAsync pre-gate (was line 124): `_serverDelayGate.ExternalizeFromHeaders` (identical).
- ClassifyResultAsync classifier-with-delay: `ForClassifierDelay` + `Externalize` (was CreateClassifierDelayResolution + ThrowIfConfigured).
- ClassifyResultAsync classifier-without-delay: `Resolve` + guarded `Externalize` (was ResolveResponseRetryDelay + guarded ThrowIfConfigured).
- ClassifyResultAsync no-classifier retryable: `Resolve` + guarded `Externalize` (was ResolveResponseRetryDelay + guarded ThrowIfConfigured).

**Precedence preserved exactly:** the pre-gate still externalizes header/custom delays BEFORE classification (short-circuit), and classifier-supplied delays are still externalized only when the pre-gate did not fire. Header-before-classifier precedence is unchanged.

**NOT done (deliberate — see contract stop-condition):** "resolve at most once per response / single invocation point" was NOT forced. Collapsing the resolution to one call would either (a) reorder the header-vs-classifier externalization precedence, or (b) change the observable invocation count of the user-supplied `GetRetryDelayFromResponse` delegate (currently invoked per resolution call). Both are behavior changes. The contract mandates behavior preservation, so the work consolidated the LOGIC into one cohesive collaborator while keeping the call structure/counts identical, rather than micro-optimizing resolution count. This is the safe, valuable SRP win; the resolve-count nuance is recorded here intentionally.

## Verification
- `dotnet build Cymulate.Http.Package.sln`: succeeded, 0 errors (11 warnings, unchanged from baseline).
- `dotnet test Cymulate.Http.Package.sln`: 268/268 passed — all existing retry/externalization tests pass unchanged. No new test added (pure relocation; existing tests already cover the gate's behavior through RetryPolicy).
- No version bump (Directory.Build.props still 1.6.3). No commit. No CHANGELOG change (behavior-preserving internal refactor + docs).

## Review outcomes
- **Verifier (review/verifier-1.md): PASS.** All sub-items met; behavior preserved (call sites 1:1, precedence intact, externalizer untouched); 268/268; no commit; 1.6.3. Resolve-count-not-deduped confirmed as a defensible, authorized behavior-preservation decision, not a gap.
- **Code-reviewer (review/code-reviewer-1.md): no blockers, no majors, safe to merge.** Confirmed concurrency-safe (all-readonly gate, frozen options, stateless collaborators). Two minors: M1 (doc precision on `Externalize`) — FIXED; M2 (double-guard on ExternalizeServerSuggestedDelays) — KEPT intentionally (outer guard usefully skips header parsing when externalization is off).
- Post-fix build green, 268/268.

## Residual risk
- Low. Pure relocation + delegation; behavior covered by the existing suite. The only conscious limitation is the un-deduplicated resolution count (documented above), preserved on purpose.
