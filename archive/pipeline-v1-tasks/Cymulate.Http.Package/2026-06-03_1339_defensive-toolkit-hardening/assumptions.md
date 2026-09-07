# Assumptions

- **[VALIDATED]** Item 4 saturation semantics = retryable backpressure (user decision 2026-06-03).
- **[VALIDATED]** Item 6 = preserve fractional refill rate (user decision 2026-06-03).
- **[VALIDATED]** Item 10 = lightweight drain with narrow scope (user decision 2026-06-03, see decisions.md).
- **[VALIDATED]** Policy order is final; not to be changed.
- **[VALIDATED 2026-06-03]** No code anywhere reads `.Response` / `.SuggestedDelay` off the exception/record (grep for `.SuggestedDelay`/`ex.Response`/`.Response.Dispose` = empty). The live response is carried but NEVER consumed and never disposed — pure leak. Removing `Response` from the contract is safe; no consumer breaks.
- **[VALIDATED 2026-06-03]** `ServerSuggestedRetryDelay` / `ServerSuggestedRetryDelayException` are constructed only internally (ServerSuggestedDelayExternalizer.cs:39,46). Item-1 shape change is internal-only.
- **[VALIDATED 2026-06-03]** `RateLimitRejectedException` (Polly) is referenced by RateLimiterPolicy.cs:70,86 and TWO tests: DefensiveToolkitLoadTests.cs:43, SessionRetryAttemptLaneTests.cs:73 (asserts ThrowsAsync). Item-4 must update those tests (saturation becomes retryable backpressure → the assert-throws test changes meaning).
- **[VALIDATED 2026-06-03]** `IrequestBoundRateLimiter` has zero references — safe to delete (item 9).
- **[OPEN — verify during execution]** The intermittent test failure (SessionBehaviorTests.ExecuteRequestAsync_LogsProgress_WhenReauthorizationReplaySucceeds, "Collection was modified") is a test-logger artifact vs a real concurrent-logging defect — to be determined while root-causing.
- **[ASSUMPTION]** Existing `RateLimiterOptions.Validate()` is the right home to extend for range validation; per-policy ctors add defensive guards (item 5).
