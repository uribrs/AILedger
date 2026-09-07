# Constraints

## Hard boundaries (out of scope)
- Do NOT change the policy execution order (Retry → RateLimiter → Timeout → CircuitBreaker → operation). It is FINAL.
- Do NOT rework the auth concurrency tier (separate backlog).
- Do NOT touch the adaptive machine-profile limiter container-awareness (separate decision).
- Do NOT expand beyond fix items 1–11 + their required tests + the two required docs.

## Technical constraints
- No new external dependencies.
- Long-lived (multi-day), sequential-usage collector library: leak/ownership fixes are the priority.
- Session-level state must stay O(1) / bounded; never per-request accumulation.
- Do NOT dispose the injected `HttpClient` (caller-owned).
- Match existing code idioms (logging via SourceContext/CorrelationId, telemetry via DefensiveDiagnostics, `internal sealed` where consistent).
- Prefer built-in / existing-pattern solutions over new abstraction (per global engineering principles).

## Public-surface discipline
- Do not break the public surface beyond what items 1, 3, 4, 9 strictly require.
- Any unavoidable breaking contract change must be called out explicitly in execution_notes.md.
- `ServerSuggestedRetryDelayException` / `ServerSuggestedRetryDelay` shape WILL change (item 1) — document the migration.

## Item-10 scoping (lightweight drain only)
- Track active session operations; reject new operations once disposal starts.
- DisposeAsync waits a bounded grace period for active operations, then cancels session-owned background work and continues teardown.
- No broad ref-count framework, no distributed lifecycle.
- StreamResponseAsync is NOT complete while the caller still owns the returned stream/response: either track via a disposable wrapper OR document that returned streams must be disposed before session disposal.
