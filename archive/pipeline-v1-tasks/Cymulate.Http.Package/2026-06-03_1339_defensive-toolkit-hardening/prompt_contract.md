# Prompt Contract

## Role
You are a senior .NET engineer specializing in resilient HTTP transport libraries (Polly, System.Threading.RateLimiting, long-lived clients).

## Goal
Implement fix items 1–11 (priority order) for the DefensiveToolkit and the thin session/transport seams it touches, plus the required tests and the two required docs, without changing the policy execution order or expanding scope.

## Context
- Long-lived (multi-day), sequential-usage collector library; concurrency is supported in code but not the production usage.
- Synthesis of a Claude + Codex review (Codex leading). Three design decisions already resolved (see decisions.md).
- Code anchors verified against current source. Policy order is FINAL.

## Constraints
- See constraints.md (hard boundaries, technical constraints, public-surface discipline, item-10 scoping). All binding.
- Do not change policy execution order. No new dependencies. Do not dispose the injected HttpClient. Keep session state O(1).

## Success Criteria
- **Item 1:** No `HttpResponseMessage` is reachable through `ServerSuggestedRetryDelayException` / `ServerSuggestedRetryDelay`; both stop being `IDisposable`. The live response is disposed on the policy/transport path. A test proves no response/connection leaks when a server-suggested delay is externalized. Throw sites (ServerSuggestedDelayExternalizer.cs:39,56; RetryPolicy.cs:53,121,244,253,270) and catch sites (TransportService.cs ~105-120; TrueStreamingTransportService.cs ~234-249) updated consistently.
- **Item 2:** `TimeoutRouterPolicy.ExecuteAsync` is async/await; the activity span covers the full operation (parity with RateLimiterRouterPolicy).
- **Item 3:** RetryOptions/CircuitBreakerOptions/TimeoutOptions are cloned/frozen at construction; post-build profile mutation cannot change a running session. A test proves it.
- **Item 4:** A package-owned saturation exception replaces `Polly.RateLimit.RateLimitRejectedException`; it is classified **retryable backpressure** by the retry layer. A test proves saturation is retried, not failed terminally.
- **Item 5:** Construction fails fast on zero/negative Timeout, Window, PermitLimit, QueueLimit, FailureThreshold, BreakDuration, SegmentsPerWindow, TokenBucketCapacity, refill rate (RateLimiterOptions + RateLimiterKindOptions + TimeoutOptions + CircuitBreakerOptions + RetryOptions + policy ctors). Tests cover rejection of bad values.
- **Item 6:** Token-bucket refill preserves fractional rate; 0.5/s yields ~1 token/2s; a 0-rate stall is impossible. Test covers fractional + boundary.
- **Item 7:** `RateLimitHeaderReader` continues to the next candidate name when a matched header's value is blank. Test covers blank-alias fallback.
- **Item 8:** CircuitBreaker callbacks no longer read `Activity.Current`; the policy's own activity is captured explicitly.
- **Item 9:** `IrequestBoundRateLimiter.cs` deleted (or defined + used). No dangling references.
- **Item 10:** Lightweight drain per decisions.md: active-op tracking, new-op rejection after dispose starts, bounded grace wait, cancel-and-continue on expiry, injected HttpClient never disposed, StreamResponseAsync handled (disposable wrapper or documented contract). Tests cover dispose-during-acquire and dispose-while-holding-lease. No ref-count framework.
- **Item 11 (later/lowest):** IResiliencePolicy vs IDefensivePolicyRunner clarified or collapsed; server-delay externalization gate extracted from RetryPolicy (only after item 1 lands); IResiliencePolicy<T> over-promise addressed only if cheap. May be deferred without failing the task if higher items consume the budget — record deferrals.
- **Tests:** all listed defense-lifecycle/edge-case tests present and green; intermittent logger test root-caused (fixed if a real defect, otherwise stabilized).
- **Docs:** (a) contractual-principles doc written; (b) policy-ordering rationale doc written (order FINAL).
- **Build + full test suite green** at the end (`dotnet test Cymulate.Http.Package.sln`).

## Execution Rules
- Do not assume missing data; the three design decisions are in decisions.md.
- Respect constraints strictly; do not change policy order or scope.
- Item 1 before item 11's gate-extraction. Item 5 and item 6 touch the same options/limiter area — coordinate.
- Verify the two OPEN assumptions (externalized-response consumers; external constructors) before removing `Response` from the contract.
- Record any unavoidable breaking change and any deferral in execution_notes.md.

## Output Format
- Code changes in-repo, matching existing idioms.
- New/updated tests under UnitTests/DefensiveToolkit and UnitTests/Session as appropriate.
- Two docs under the repo's docs location (or alongside the toolkit if no docs dir).
- execution_notes.md appended with: what changed, breaking changes, deferrals, verification results.

## Stop Conditions
- Goal achieved (all in-scope success criteria met, build + tests green).
- A constraint cannot be honored without scope expansion (stop, surface).
- An OPEN assumption is found false in a way that changes the item-1 design (stop, surface).
