# Execution Notes

## Path
Decompose — 6 parallel workers (exclusive file ownership) + orchestrator synthesis. researchNeeded=false (OPEN assumptions resolved by grep, not external behavior).

## Workers (all returned)
- **W1** (I2,I8): TimeoutRouterPolicy made async (span now covers the awaited op); CircuitBreakerPolicy callbacks no longer tag `Activity.Current` — state tagging moved to the local activity in ExecuteAsync. +TelemetryFixesTests.
- **W2** (I7,I9): RateLimitHeaderReader continues past blank header aliases; deleted empty IrequestBoundRateLimiter.cs. +RateLimitHeaderReaderBlankAliasTests.
- **W4** (I10): Lightweight drain — `_disposing`/`_activeOps` (Interlocked), BeginOperation/ActiveOpToken, bounded grace drain (5s) then cancel background + continue; injected HttpClient never disposed; StreamResponseAsync uses option (b) documented contract. Root-caused the intermittent logger test → test-only (background refresh loop logging into a `List<>`); fixed TestLogger to ConcurrentQueue. +SessionDisposeDrainTests.
- **W5** (D1): docs/defensive-toolkit-contracts.md + docs/policy-ordering-rationale.md.
- **W6** (I3,I5): Clone() on Retry/CircuitBreaker/Timeout options; SessionBuilder clones+validates them at build; range validation in every options Validate(). +DefensiveOptionsValidationTests.
- **W7** (I1,I4,I6): Removed live HttpResponseMessage from ServerSuggestedRetryDelay(Exception) — both no longer IDisposable; externalizer disposes the response before throwing a metadata-only exception (leak impossible by construction). New package RateLimiterSaturationException replaces Polly's, classified retryable backpressure in RetryPolicy. Token-bucket refill preserves fractional rate (TokensPerPeriod=1, ReplenishmentPeriod=1/rate). Updated 4 affected existing tests. +CoreOwnershipAndSaturationTests.

## Orchestrator synthesis repairs
1. Added `DefensiveToolkit/Properties/AssemblyInfo.cs` with `InternalsVisibleTo("UnitTests")` (DefensiveToolkit is a separate assembly; W2's internal-type test couldn't compile).
2. Fixed `Assert.Null(value, message)` → `Assert.Null(value)` (no such xUnit overload) in CoreOwnershipAndSaturationTests.
3. **Contract fix in HttpSession dispose-drain:** W4 set `_disposed=1` before the drain and made `UpdateLastUsed()` no-op on disposing||disposed, which broke regression `SessionMethods_ThrowObjectDisposedException_AfterDispose`. Reworked so `_disposed` is set AFTER drain completes; `UpdateLastUsed()` now THROWS once fully disposed but NO-OPs during the drain window (in-flight transport bookkeeping survives). New + old contracts both green.
4. Serialized the listener-based TelemetryFixesTests via a `DisableParallelization` collection — it was flaky under the parallel full suite (global ActivityListener interference); passes 5/5 in isolation and now deterministic in-suite.

## Result
- `dotnet build` green. `dotnet test` 267/267 green across two consecutive runs (was 200 tests pre-change).
- I11 (collapse IResiliencePolicy/IDefensivePolicyRunner; extract server-delay gate; IResiliencePolicy<T>) intentionally DEFERRED (lowest priority, depends on I1) — recorded as follow-up.

## Review passes
- **Verifier (review/verifier-1.md): PASS** — every in-scope criterion met, constraints honored, 267/267 (ran independently). 3 minor non-blocking notes, none requiring a repair.
- **Code-reviewer (review/code-reviewer-1.md): no blockers; 3 Major, 3 Minor.**
  - **M1 (TOCTOU race in dispose-drain) — REPAIRED.** `BeginOperation` checked `_disposing` before incrementing `_activeOps`; a disposer could read `_activeOps==0`, drain, and tear down while a just-passed operation then ran against disposed policies. Reordered to increment-first-then-check-and-back-out (closes the race; drain sets `_disposing` before reading the counter). Removed now-unused `ThrowIfDisposed()`. Build clean, 267/267 twice after the fix.
  - **M2 (StreamResponseAsync body outside drain) — ACCEPTED.** Documented option-b contract; body reads don't touch session-owned state. No change.
  - **M3 (saturation retry re-enters limiter / exception-type contract break) — ACCEPTED + release-noted.** Under sequential use with backoff this converges; the Polly→package exception type change is a documented internal-only breaking change.
  - m1 (`Math.Max(0.001, 1/rate)` floor caps ~1000 tokens/s) — accepted; per-credential SaaS limits are far below that. m2 (`_lastUsedLock` reads unsynchronized) — pre-existing/out of scope. m3 (unused exception ctor) — harmless, left.

## Post-review round 2 (Codex review of the delivered work) — fixes applied
- **Policy-ctor validation/freezing (was partial-delivery of I5):** RetryPolicy and CircuitBreakerPolicy ctors now `Clone()`+`Validate()` their options (freeze + fail-fast) so DIRECT DefensiveToolkit users — not just SessionBuilder — are protected. TimeoutPolicy ctor calls `Validate()` only (no clone: it is built per-request by the router from already-cloned options and holds only a value-type Timeout — avoids hot-path allocation).
- **429 contract doc was wrong:** defensive-toolkit-contracts.md §8 claimed "the circuit breaker does not open on 429s," but CircuitBreakerPolicy's default handled set includes 429/408. Corrected the doc to state actual behavior + how to exclude 429 via `ShouldHandleResponse`/`FailureStatusCodes`. (Behavior unchanged, per the standing "don't change policy behavior" decision.)
- **Stale public docs (removed-disposable contract):** updated PoliciesReadme, Session.Readme, RateLimitHeadersReadme, Session.ExamplesReadme, and Example.md — removed all "dispose the exception / attached HttpResponseMessage" guidance now that the exception is metadata-only.
- **policy-ordering-rationale.md** wrong exception name `TimeoutRejectedException` → `TimeoutException`.
- **IDE warning cleanup:** SessionBuilder spurious `?.` on non-null `profile.Timeout`; two test `is IDisposable` checks (statically false → CS0184) replaced with reflection-based interface assertions.
- **CB telemetry wording clarification:** I8 was implemented by DROPPING the ambient `Activity.Current` tagging in onBreak/onReset/onHalfOpen and tagging circuit state on the policy's own ExecuteAsync activity instead (not by capturing a per-callback activity). The earlier "capture explicitly" wording overstated it; the callback tags were intentionally dropped — no telemetry loss because the triggering request's ExecuteAsync activity carries the state.

## Streaming post-send response leak (original cluster "B2") — FIXED (user-approved scope addition)
TrueStreamingTransportService acquired the response then could throw before handing ownership to the
caller (e.g. `ReadAsStreamAsync` faults, or cancellation lands post-headers) — the catch blocks
rethrew WITHOUT disposing it, leaking the connection. Fixed in BOTH streaming methods
(`StreamResponseAsync` and the twin `StreamRequestAsync`, which had the identical pattern): hoisted the
`response` local out of the `try` and added `response?.Dispose()` to every catch. Disposing in a catch
is always safe because ownership transfers to the caller only on the success-path return (catches never
run for a returned response). Regression test: StreamResponseLeakTests proves the response is disposed
when content-stream acquisition throws. Suite now 268/268.

## Documentation + release note (round 3)
- Full doc audit: all stale "dispose the exception / attached response" guidance removed (Session.Readme, PoliciesReadme, RateLimitHeadersReadme, ExamplesReadme, Example.md). Confirmed clean by grep.
- Found + fixed an additional stale-behavior class: three docs described `QueueLimit = 0` as "fail-fast quota enforcement," which is no longer true now that saturation is retryable backpressure (item 4). Corrected in Session.Readme, RateLimitHeadersReadme, ExamplesReadme.
- Documented the new `RateLimiterSaturationException` + retryable-backpressure semantics (new "Saturation Behavior" subsection in PoliciesReadme; referenced in 4 other docs).
- **CHANGELOG.md created** (Keep-a-Changelog) at repo root with a `[2.0.0] - Unreleased` entry. Breaking changes surfaced with migration steps: (1) metadata-only ServerSuggestedRetryDelay(Exception) — no Response/IDisposable; (2) package RateLimiterSaturationException replacing Polly's, now retryable (QueueLimit=0 no longer fail-fast); (3) fail-fast option validation at construction; (4) removed empty IrequestBoundRateLimiter. Plus Changed/Fixed/Docs sections.
- **Version is 1.6.3; breaking changes require a major bump to 2.0.0.** Directory.Build.props NOT yet bumped — flagged in the changelog footer as a pre-release action for the user to confirm.

## Known / acceptable
- **Token-bucket fractional test** proves construction + the 1/rate formula but not live admission timing (a real ~2s wait). Acceptable proxy; a behavioral timing test would be slow/flaky.

## Breaking changes (internal-only, verified no external consumers)
- `ServerSuggestedRetryDelay` / `ServerSuggestedRetryDelayException` no longer expose `Response` and are no longer `IDisposable`.
- `RateLimiterPolicy` throws package `RateLimiterSaturationException` instead of Polly `RateLimitRejectedException`, and saturation is now retried (backpressure) rather than terminal.
