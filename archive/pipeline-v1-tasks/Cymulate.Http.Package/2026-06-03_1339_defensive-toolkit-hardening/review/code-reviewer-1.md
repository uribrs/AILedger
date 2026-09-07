# Code Review — Defensive Toolkit Hardening

**Stack:** C# / .NET (long-lived HTTP transport library; sessions run for days streaming millions of findings)
**Change type:** Shared library / public-contract code + session lifecycle
**Risk level:** High — touches retries, rate limiting, resource disposal, connection lifetime, concurrency, and session teardown.

Scrutiny applied at the High level: failure semantics, concurrency, recovery, resource cleanup, long-term coupling. Build verified clean (`dotnet build`: 0 errors).

---

## Summary of changes reviewed

1. Live `HttpResponseMessage` removed from `ServerSuggestedRetryDelay` / `ServerSuggestedRetryDelayException`; response now disposed at the policy boundary before the exception is thrown (contract principle #9).
2. `Validate()` + `Clone()` added to all option types; `SessionBuilder` now freezes (clone+validate) Timeout / CircuitBreaker / Retry at build time (contract principles #1, #2).
3. `RateLimitRejectedException` (Polly) replaced with package-owned `RateLimiterSaturationException`, classified as retryable transient backpressure.
4. Token-bucket replenishment reworked to preserve fractional refill rates.
5. `RateLimitHeaderReader` no longer aborts the alias scan on a blank value.
6. `TimeoutRouterPolicy.ExecuteAsync` made `async/await` so the telemetry span covers the full operation.
7. `CircuitBreakerPolicy` callbacks no longer tag `Activity.Current`.
8. `HttpSession` gains a lightweight dispose-drain (grace period + active-op counter).

Most of this is high-quality, well-documented, and correctly motivated. The findings below are the material ones.

---

## Findings

### Blocker

None.

### Major

#### M1 — Dispose-drain has a TOCTOU race that can let an operation run against torn-down policies
**Files:** `Session/Logic/Lifecycle/HttpSession.cs` (`BeginOperation`, `DisposeAsync`, `DrainActiveOperationsAsync`)

`BeginOperation()` does:

```csharp
ThrowIfDisposed();                  // (1) reads _disposing
Interlocked.Increment(ref _activeOps);  // (2)
```

`DisposeAsync` does:

```csharp
Interlocked.Exchange(ref _disposing, 1);   // (A)
await DrainActiveOperationsAsync();         // (B) waits while _activeOps > 0
Interlocked.Exchange(ref _disposed, 1);
// ... cancels refresh, disposes policies/auth (rate limiter, etc.)
```

Interleaving: thread T calls `BeginOperation`, passes the check at (1) because `_disposing` is still 0. Before T reaches (2), the disposer runs (A) then (B); at (B) `_activeOps` is still 0, so the drain returns immediately and teardown proceeds to dispose the rate limiter and auth provider. T now executes (2) and proceeds into `_transportService.ExecuteRequestAsync`, running against an already-disposed `RateLimiterPolicy` / auth provider.

**Impact:** Rare under the documented single-session sequential usage, but this is shared infrastructure and dispose is a genuine concurrency boundary. The symptom is a confusing `ObjectDisposedException` from deep inside the limiter/auth (or undefined behavior), not the clean `ObjectDisposedException(nameof(HttpSession))` the contract promises. The drain's correctness guarantee ("wait for in-flight ops") does not actually hold for ops that are mid-entry.

**Fix (local):** Increment first, then check, and back out on loss:

```csharp
Interlocked.Increment(ref _activeOps);
if (Volatile.Read(ref _disposing) == 1)
{
    Interlocked.Decrement(ref _activeOps);
    throw new ObjectDisposedException(nameof(HttpSession));
}
return new ActiveOpToken(this);
```

The disposer then either sees `_activeOps > 0` (and drains/grace-waits) or the operation observes `_disposing` and backs out. This closes the window without a lock. Local patch, not a refactor.

#### M2 — `StreamResponseAsync` drain semantics can tear down the rate limiter / auth while a streamed body is still being read
**Files:** `HttpSession.cs` (`StreamResponseAsync`, drain), `DisposeOwnedResourcesAsync`

By design (option b, documented), the active-op counter is decremented when headers arrive, before the caller consumes `ContentStream`. Disposal then proceeds to dispose the auth provider and the rate-limiter policy while the underlying connection/stream is still live and being read.

For the injected `HttpClient` this is fine (never disposed, body keeps streaming). The concern is narrower: if any owned resource that the streamed read depends on is torn down mid-read. In the current wiring the body read does not re-enter the limiter or auth, so today this is **safe** — but it is an undocumented coupling assumption. If a future change makes streamed reads touch session-owned state (e.g. a wrapping stream that re-auths on a mid-stream 401, or per-chunk limiter accounting), this becomes a use-after-dispose.

**Impact:** No current defect; latent coupling trap on a hot path (streaming millions of findings is the primary workload). The XML doc tells callers to dispose `RawResponse` before disposing the session, but nothing enforces it and the drain explicitly does not wait.

**Recommendation (defer-able):** Record this assumption explicitly in `policy-ordering-rationale.md` / `defensive-toolkit-contracts.md` ("streamed body reads must not depend on session-owned policy/auth state; the drain does not protect them"). No code change required now; this is about preventing a future regression. Whether to track the op until `StreamedResponse` disposal is a real design decision — option b is a reasonable, justified tradeoff given current caller patterns, so I would not change it absent a concrete need.

#### M3 — Retrying `RateLimiterSaturationException` re-enters the same saturated limiter; can amplify queue pressure
**Files:** `DefensiveToolkit/Policies/RetryPolicy.cs`, `RateLimiterPolicy.cs`, `RateLimiterSaturationException.cs`

Policy order is Retry (outer) → RateLimiter (inner), confirmed in `policy-ordering-rationale.md`. Saturation is now retryable. The rationale ("under sequential usage the limiter frees up quickly") holds for the documented single-session sequential workload, and the retry waits outside the permit (contract #5), so this is a defensible tradeoff.

Two things to verify, because the failure mode is operational:

1. **Saturation is only thrown when `lease.IsAcquired == false`**, which for a configured `QueueLimit` happens when the queue itself is full. Retrying then re-queues into a still-full queue. With a fixed/exponential backoff and bounded `MaxAttempts` this converges, but under non-sequential / bursty use (which the contract says is out of scope, but which the code does not prevent) retries add load to an overloaded limiter rather than shedding it. The classification is reasonable; just confirm `MaxAttempts` and backoff are sized so that a persistently saturated limiter terminates rather than spinning to the attempt cap on every request.
2. **Behavior change on exhaustion:** previously the terminal exception was Polly's `RateLimitRejectedException`; now it is `RateLimiterSaturationException`. `SessionRetryAttemptLaneTests.cs:77` was updated to assert the package exception surfaces. Any downstream consumer catching `RateLimitRejectedException` by type will silently stop matching. This is a **public-contract break** for the package's thrown-exception surface — acceptable if intentional (it removes the Polly type leak, which is an improvement), but it should be called out in release notes / changelog. Confirm no consumer in the broader codebase catches the Polly type.

**Fix:** Documentation/release-note item + confirm attempt/backoff sizing. No code change required if the classification is the intended behavior.

### Minor

#### m1 — Token-bucket replenishment floor silently under-provisions very high refill rates
**File:** `RateLimiterPolicy.cs` (`TokenBucket` construction)

```csharp
TokensPerPeriod = 1,
ReplenishmentPeriod = TimeSpan.FromSeconds(Math.Max(0.001, 1.0 / refillRate));
```

The fractional-rate fix is correct and the right call (the old `(int)Math.Round(rate)` truncated 0.5/s to 0 tokens → permanent stall — a real bug this change fixes). But the `0.001` floor caps effective throughput at ~1000 tokens/s: a configured `TokenBucketRefillRate` of e.g. 5000/s silently delivers ~1000/s. Also note `AutoReplenishment` with a 1 ms period leans on the timer's resolution; sub-10ms periods are not reliably honored by the runtime timer, so effective rate at the high end is approximate regardless.

**Impact:** Low for this library's workload (per-session quotas are typically small), but it is a silent discrepancy between configured and actual rate. **Recommendation:** either document the upper bound in `TokenBucketOptions`/contracts, or for `refillRate > 1` keep the period at a sane floor and raise `TokensPerPeriod` proportionally (`TokensPerPeriod = ceil(rate * period)`), restoring high-rate accuracy. Defer-able.

#### m2 — `_lastUsedLock` now guards a field that is also written lock-free elsewhere is fine, but the lock is now near-vestigial
**File:** `HttpSession.cs`

`LastUsed` is written only under `_lastUsedLock` in `UpdateLastUsed`, and read without the lock (`SessionDiagnostics.RecordSessionInfo`, dispose logging). For a single `DateTimeOffset` the lock provides mutual exclusion between concurrent `UpdateLastUsed` callers but readers still see torn/stale values (no memory barrier on the read side). This predates the change and is not introduced here; flagging only because the drain work touches adjacent state. Not worth changing for a best-effort timestamp.

#### m3 — `RateLimiterSaturationException` exposes a public `(string, Exception)` ctor that is never used
**File:** `RateLimiterSaturationException.cs`

Minor surface bloat on a public exception type. Harmless; keep if you want the standard exception shape, drop if you prefer a minimal surface.

### Nit

- `TimeoutOptions.cs` and the rate-limiter option files are missing a trailing newline (`\ No newline at end of file`). Cosmetic.
- `IrequestBoundRateLimiter.cs` deletion: the file contained only a BOM + whitespace, so removing it is correct cleanup. Note the lowercase-`r` typo in the filename is gone with it — good.

### Observation (no action)

- **O1 — Removal of `IDisposable` from `ServerSuggestedRetryDelay` and its exception is correct and well-executed.** Metadata is fully extracted (`CreateServerSuggestedRetryDelay`) before `response.Dispose()`, and dispose happens before `throw`, so the connection-leak-by-construction claim holds. The ordering in `ServerSuggestedDelayExternalizer` (extract → record telemetry → dispose → throw) is right. This is a genuine improvement that removes the prior leak-if-caller-discards-exception footgun and aligns with contract #9.
- **O2 — `TimeoutRouterPolicy` async/await fix is correct.** The previous synchronous `return policy.ExecuteAsync(...)` disposed the `using var activity` (in the inner `TimeoutPolicy`) and returned a not-yet-completed Task, closing the span before the operation finished. Awaiting fixes span lifetime. The double-clone of `TimeoutOptions` (SessionBuilder clones, then the ctor clones again) is harmless.
- **O3 — `CircuitBreakerPolicy` callbacks no longer mutate `Activity.Current`.** Correct: Polly's `onBreak/onReset/onHalfOpen` fire on arbitrary ambient activities (often an unrelated caller span), so tagging `Activity.Current` was attributing breaker state to the wrong span. Removing it is the right fix; logging is retained.
- **O4 — `RateLimitHeaderReader` blank-alias fix is correct.** Previously a present-but-blank header alias returned `false` immediately, skipping later aliases that may have had a real value. The `continue` correctly falls through to the next alias. Good catch.
- **O5 — Clone/Validate freezing is sound** and directly implements contracts #1/#2. Clones copy `HashSet` collections (new set, shared values) and copy delegate references — appropriate shallow-clone semantics for build-time freezing. Validation throwing `ArgumentOutOfRangeException` at build time (fail-fast) is the right place for it.

---

## Verdict

No blockers. The headline changes (response-disposal-before-throw, options freezing, telemetry span/tagging fixes, fractional token-bucket refill, blank-alias header scan) are correct and improve safety on a long-lived high-throughput path.

The one finding worth fixing before merge is **M1** (dispose-drain TOCTOU) — a small lock-free reordering closes a real concurrency hole on the disposal boundary. **M2** and **M3** are documentation/release-note items: surface the streamed-body coupling assumption and the `RateLimitRejectedException → RateLimiterSaturationException` thrown-type change. **m1** (token-bucket high-rate floor) is a known-limitation note unless high refill rates are in scope.
