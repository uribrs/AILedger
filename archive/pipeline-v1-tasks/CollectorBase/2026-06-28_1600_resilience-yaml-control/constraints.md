# Constraints

- ZERO regression: the 5 existing profiles set no `rate_limit`/`circuit_breaker`/`watermark_seed` → behavior unchanged; 88/88 stay green.
- OPT-IN null-preservation: an omitted `rate_limit`/`circuit_breaker` sub-block ⇒ leave `SessionSpec.RateLimiter`/`CircuitBreaker` NULL ⇒ Shared default applies (per `DefaultSessionFactory` `?? new …`). NEVER construct a non-null options object from absent YAML.
- Policy ORDER stays fixed (`Retry → RateLimiter → Timeout → CircuitBreaker`); YAML supplies VALUES only.
- `watermark_seed` rendered ONLY on a fresh run (seed.Watermark null/empty); a resumed watermark always wins — never overridden.
- B1 is VERIFY-FIRST: confirm the package `RetryOptions` exposes a settable header/rate-limit strategy before wiring. If it does not, document the truth (on/off only) — do NOT fabricate an unsupported knob.
- Generic engine, no vendor identity; secrets never from YAML (rate-limit/CB values are non-secret).
- TokenBucket is the only rate-limiter kind exposed now (matches Shared default); other kinds deferred.
- net8.0; solution `CollectorBase.slnx`; build clean + 88/88 green after each item (incremental).
- Runner `--out`/`CS_OUT` override is preserved; only the DEFAULT runs root changes (repo-root anchored).
