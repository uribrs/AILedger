# Verifier-1 — Defensive Toolkit Hardening

Verification done against the ACTUAL code (files read directly), not the execution notes.
Full suite run independently: **267/267 passed, 0 failed, 0 skipped** (`dotnet test Cymulate.Http.Package.sln`, exit 0).

## Per-criterion verdict

### I1 — Server-suggested delay carries no live response — PASS
- `ServerSuggestedRetryDelay` (Contracts/Models/Retry/ServerSuggestedRetryDelay.cs:8-25): metadata-only record, no `HttpResponseMessage`, not `IDisposable`.
- `ServerSuggestedRetryDelayException` (Contracts/Exceptions/ServerSuggestedRetryDelayException.cs:14): `: Exception` only; no `Response` property; not `IDisposable`. Exposes only scalar metadata.
- `ServerSuggestedDelayExternalizer.cs:33-45`: extracts metadata, records telemetry, then `response.Dispose()` **before** `throw` — leak impossible by construction.
- Throw sites: RetryPolicy.cs:177,244,253,270 all route through `ThrowIfConfigured(response, ...)`.
- Catch sites: TransportService.cs:105-121 and TrueStreamingTransportService.cs:124-140 / 234-250 read only scalar metadata (`ex.StatusCode`, `ex.Reason`) — no `.Response`, nothing to dispose.
- Grep confirms zero `.Response`/`.SuggestedDelay.Response` consumers anywhere. All `SuggestedDelay` consumers are telemetry tag writers (scalar reads).
- Leak test `CoreOwnershipAndSaturationTests.ExternalizedDelay_DisposesResponse_BeforeThrowing` uses a `TrackingResponse` and asserts `Disposed == true` after throw — real, not trivial.

### I2 — TimeoutRouterPolicy async/await — PASS
- TimeoutRouterPolicy.cs:26-53: `async Task<T>`, `using var activity` at line 33, `return await policy.ExecuteAsync(...)` at line 52 — span covers the full awaited operation.

### I3 — Options cloned/frozen at construction — PASS
- `Clone()` present on RetryOptions (RetryOptions.cs:83), TimeoutOptions (TimeoutOptions.cs:13), CircuitBreakerOptions (CircuitBreakerOptions.cs:47); collections deep-copied, delegates copied by reference (correct — they are stateless build specs).
- SessionBuilder.cs: Timeout cloned+validated (47-48), CircuitBreaker (88-89), Retry (107-108), RateLimiter cloned (67). The **frozen** instances are the ones handed to the policies (lines 60-63, 72, 93, 115). Post-build mutation of the original profile cannot affect a running session.
- Test: `DefensiveOptionsValidationTests` includes a clone-isolation test (mutates original after clone, asserts clone unchanged).

### I4 — Package-owned saturation exception, retryable — PASS
- `RateLimiterSaturationException` (Contracts/Exceptions/RateLimiterSaturationException.cs) is package-owned; thrown by RateLimiterPolicy.cs:70.
- Polly `RateLimitRejectedException` fully removed — grep finds zero references in src/tests.
- Classified retryable in RetryPolicy.cs:297 (`RateLimiterSaturationException => true`) in the default transient policy.
- Tests updated: SessionRetryAttemptLaneTests.cs:78 (asserts package exception surfaces after retries exhausted; `SendCount==1` proves retry attempted, not terminal), DefensiveToolkitLoadTests.cs:43, plus `CoreOwnershipAndSaturationTests.RateLimiterSaturation_Retries_AndSucceedsWhenLimiterFreesUp`.

### I5 — Fail-fast on zero/negative ranges — PASS
- Validate() rejects bad values across all options: RetryOptions (MaxAttempts<1, Delay<0, MaxServerSuggestedDelay<0), TimeoutOptions (Timeout<=0), CircuitBreakerOptions (FailureThreshold<=0, BreakDuration<=0), FixedWindowOptions (PermitLimit<=0, Window<=0, QueueLimit<0), SlidingWindowOptions (+SegmentsPerWindow<=0), TokenBucketOptions (TokenBucketRefillRate<=0, TokenBucketCapacity<=0, QueueLimit<0). RateLimiterOptions.Validate() cascades to the active kind.
- Policy ctors enforce: RateLimiterPolicy.cs:27 calls `options.Validate()`; CircuitBreaker/Retry/Timeout validation enforced at the SessionBuilder construction boundary (clone+Validate). Note: CircuitBreakerPolicy ctor itself does NOT call `_options.Validate()` (plan W1 item) — see Gaps.
- Tests: `DefensiveOptionsValidationTests` covers rejection of bad values.

### I6 — Token-bucket fractional rate preserved — PASS
- RateLimiterPolicy.cs:145-147: `TokensPerPeriod = 1`, `ReplenishmentPeriod = TimeSpan.FromSeconds(Math.Max(0.001, 1.0/refillRate))`. 0.5/s → 2.0s/token; 0-rate impossible (rate `<=0` rejected by Validate, and the `Max(0.001, ...)` floor prevents a 0-period). Tests: 0.5 and integer-rate period assertions in CoreOwnershipAndSaturationTests.

### I7 — RateLimitHeaderReader continues past blank alias — PASS
- RateLimitHeaderReader.cs:11-14 and 19-22: on a blank/whitespace matched value, `continue`s to the next candidate name in both the response-header and content-header branches. Test: `RateLimitHeaderReaderBlankAliasTests`.

### I8 — CircuitBreaker callbacks don't read Activity.Current — PASS
- CircuitBreakerPolicy.cs onBreak/onReset/onHalfOpen (31-56) only log; no `Activity.Current` anywhere in the file. State tagging done on the local `activity` inside ExecuteAsync (77, 87, 97, 108). Test: TelemetryFixesTests I8 case (injects a sentinel ambient activity, asserts no tags written to it).

### I9 — IrequestBoundRateLimiter.cs deleted — PASS
- File gone (git status shows `D`); directory now holds only IDefensivePolicyRunner.cs + IResiliencePolicy.cs; zero references in repo.

### I10 — Lightweight drain — PASS
- HttpSession.cs: active-op tracking via `_activeOps` (Interlocked) + `BeginOperation`/`ActiveOpToken` (226-245); new ops rejected after dispose-start (`BeginOperation`→`ThrowIfDisposed`, throws when `_disposing==1`, lines 216-219, 228); bounded grace wait `DrainActiveOperationsAsync` (5s, 50ms poll, 430-448); cancel-and-continue on expiry (`_refreshCts.Cancel()` after drain regardless of outcome, 383-385); injected HttpClient NEVER disposed (`DisposeOwnedResourcesAsync` disposes Policies + Auth only, 450-493); `UpdateLastUsed` throws when fully disposed (165-166) but no-ops during drain window (167-168) — preserves in-flight transport bookkeeping. `_disposed` set AFTER drain (381), correctly fixing the regression noted in execution_notes.
- StreamResponseAsync: option (b) documented contract (XML doc 138-157) — counter decremented at headers-received; caller must dispose returned response before session dispose. Matches constraints.md.
- Tests: SessionDisposeDrainTests covers dispose-during-acquire, dispose-while-holding, grace-expiry continue, idempotent dispose, injected-client-not-disposed, stream-headers-received non-block, in-flight completes after dispose-start.
- No ref-count framework. PASS.

### I11 — Deferred — ACCEPTABLE
- Recorded as deferred in execution_notes (lowest priority, depends on I1). Contract permits deferral. No partial/broken half-implementation left behind.

### Docs — PASS
- DefensiveToolkit/docs/defensive-toolkit-contracts.md (9 principles incl. disposal-ownership) and DefensiveToolkit/docs/policy-ordering-rationale.md (FINAL order rationale) both present and substantive.

### Intermittent logger test — PASS (root-caused)
- SessionBehaviorTests TestLogger changed `List<LogEntry>` → `ConcurrentQueue<LogEntry>` (concurrent Log() from background refresh loop vs test-thread enumeration). Root cause = test artifact, matching the OPEN assumption. Reasonable fix.

## Constraint checks
- **Policy execution order UNCHANGED** — PASS. DefensivePolicyRunner.ExecuteChainedAsync (63-92) wraps inside-out: operation → CircuitBreaker → (retry=null here) → Timeout → RateLimiter(outer). Retry is layered outermost via RetryAttemptPolicyRunner wrapping the DefensivePolicyRunner (SessionBuilder.cs:96-116). Effective order = Retry → RateLimiter → Timeout → CircuitBreaker → operation. Matches FINAL.
- **Injected HttpClient never disposed** — PASS (see I10).
- **No new external dependencies** — PASS. csproj PackageReferences unchanged; only Directory.Build.props version bump 1.6.2→1.6.3. (Polly.RateLimiting ref remains but is no longer relied on for saturation — not a new dep.)
- **Session state O(1)/bounded** — PASS. Drain state is two ints (`_disposing`, `_activeOps`) + a nullable `_lastOutcome`; no per-request accumulation.
- **Breaking changes internal-only** — PASS. `ServerSuggestedRetryDelay(Exception)` lost `Response` + IDisposable; types are public but constructed only by the internal externalizer, and grep confirms no consumer (internal or external in-repo) reads `.Response`. `RateLimiterSaturationException` replaces Polly's; the 2+2 affected tests updated. Both documented in execution_notes "Breaking changes".

## Gaps / risks (minor, non-blocking)
1. **CircuitBreakerPolicy ctor does not call `_options.Validate()`.** The orchestration plan (W1) said it would. In practice validation is enforced at the SessionBuilder boundary (frozenCircuitBreaker.Validate(), line 89), so any session-built CB is validated. Risk only if a consumer constructs `CircuitBreakerPolicy` directly bypassing the builder — then a zero FailureThreshold / BreakDuration would reach Polly unvalidated. RateLimiterPolicy and the options classes self-validate; CB is the lone outlier. Low severity (builder is the supported path), but it is a deviation from the stated plan and a thin gap in the "fail fast at construction across the options types / policy ctors" wording of I5.
2. **Saturation classification is bypassed by a user-supplied `ShouldRetryExceptionAsync`** (RetryPolicy.cs:285-286 returns the custom delegate result before the default switch). This is the pre-existing delegate-precedence design, not a regression; a consumer who supplies a custom classifier owns saturation handling themselves. Worth noting, not a defect.
3. **StreamResponseAsync body consumption is outside the drain window** by design (option b). A caller streaming a large body past `DisposeAsync` is not waited on. This is the explicitly chosen + documented contract per constraints.md; acceptable, but it is a real (accepted) limitation rather than full in-flight protection for streaming.

## Overall verdict
**PASS.** All in-scope success criteria (I1–I10) are met in the actual code with backing tests; I11 is an acceptable recorded deferral; both docs exist; all constraints honored (policy order unchanged, injected HttpClient never disposed, no new deps, O(1) session state); breaking changes are internal-only and documented. Independent full-suite run is green at 267/267. The only items worth flagging are minor: CircuitBreakerPolicy ctor lacks a self-`Validate()` call (mitigated by builder-side validation), and the two accepted-by-design behaviors (custom-classifier precedence over saturation default; streaming body outside the drain window). None block acceptance.
