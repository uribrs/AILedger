# Decisions

- **Local rate-limiter saturation = retryable backpressure.** (Item 4) Package-owned rejection exception is classified retryable by the retry layer (with backoff), not terminal. Rare under sequential usage; transient by nature.
- **Token-bucket refill preserves fractional rate.** (Item 6) Derive `ReplenishmentPeriod` from the rate (e.g. 0.5/s → 1 token / 2s) so sub-1/s SaaS quotas work; 0-rate stall must be impossible.
- **Dispose-vs-in-flight = lightweight drain, narrowly scoped.** (Item 10) Track active ops; reject new ops once disposal starts; bounded grace wait; on expiry cancel session-owned background work and continue teardown; never dispose the injected HttpClient. Handle StreamResponseAsync via disposable wrapper or a documented "dispose returned streams before session disposal" contract. NO ref-count framework, NO lifecycle framework.
- **Policy order is FINAL.** Retry(outer) → RateLimiter → Timeout → CircuitBreaker → operation. Rate limiter is the primary control; circuit breaker is an intentionally-unleaned-on backstop; timeout-not-counted-by-breaker is accepted by design. Rationale to be documented once so reviews stop relitigating it.
- **Codex leads the analysis; its ranking is authoritative** where Claude and Codex differ.
- **Defensive options must be frozen/cloned at construction** (Item 3) — RetryOptions/CircuitBreakerOptions/TimeoutOptions, matching the existing RateLimiterOptions clone. Profiles are build specs, not shared mutable runtime state.
