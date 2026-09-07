# Decisions

- D1: `rate_limit`/`circuit_breaker` are OPT-IN; omitted ⇒ `SessionSpec` property null ⇒ Shared default preserved. Core no-regression rule.
- D2: Policy order stays fixed (`Retry→RateLimiter→Timeout→CircuitBreaker`); YAML supplies values only — consistent with the existing resilience invariant.
- D3: `watermark_seed` is rendered only on a fresh run; a resumed (persisted) watermark always wins.
- D4: B1 is verify-first — if the package hardwires the header strategy, document it honestly (on/off only) rather than fake a knob.
- D5: TokenBucket is the only rate-limiter kind exposed now (matches the Shared default); other kinds deferred until a real consumer needs them.
- D6: YAML shape locked: `resilience.rate_limit{requests_per_second,burst,queue_limit}`, `resilience.circuit_breaker{failure_threshold,break_duration_seconds}`, `pagination.watermark_seed`. Field mapping → TokenBucketOptions{TokenBucketRefillRate,TokenBucketCapacity,QueueLimit,AutoReplenishment=true}, CircuitBreakerOptions{FailureThreshold,BreakDuration}.
- D7: Runner runs-root default = repo root (walk up to `CollectorBase.slnx`; fall back to CWD); `--out`/`CS_OUT` override unchanged.
