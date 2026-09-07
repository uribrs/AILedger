# Task — YAML resilience control panel + watermark seed + runs anchor

A live Falcon run proved the YAML `resilience` block barely reaches the session: rate limiter +
circuit breaker fall to Shared machine-profile defaults, `server_delay_strategy` is half-wired, and
`cursor_watermark` profiles send an empty floor (`>=''`) on a fresh run. User approved all four fixes.

Four items:
- **A. Watermark seed** — new `pagination.watermark_seed` (templated); seeds the initial `{{watermark}}`
  floor on a fresh run; a resumed watermark always wins.
- **B1. `server_delay_strategy` wiring** (verify-first) — map it to the real RetryOptions header
  strategy if the package exposes one; otherwise document honestly (no faked knob).
- **B2. Rate-limiter + circuit-breaker YAML knobs** — optional `resilience.rate_limit` +
  `resilience.circuit_breaker`, wired to `SessionSpec.RateLimiter`/`CircuitBreaker`. Opt-in: omitted ⇒
  null ⇒ Shared default preserved.
- **C. Runs-dir anchor** — default the runs root to the repo root regardless of launch directory.

Plus: seed the Falcon profile, update `capabilities.md` + `yaml-contract.md`.

Hard rule: zero regression — the 5 existing profiles set none of the new knobs, so behavior is
unchanged and 88/88 stay green.
