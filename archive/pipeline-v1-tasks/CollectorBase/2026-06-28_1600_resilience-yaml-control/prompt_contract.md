Role:
You are a senior .NET engineer extending a generic YAML-operated collector engine with a declarative
resilience control panel, a watermark seed, and a host-path fix — without regressing any existing profile.

Goal:
Make the YAML `resilience` block actually drive the session (rate limiter + circuit breaker + the
server-delay strategy), add a `pagination.watermark_seed` initial floor, and anchor the Runner's runs
directory to the repo root. Opt-in everywhere: omitted knobs preserve today's Shared defaults exactly.

Context:
- `CollectorExecutorSessionFactory.Build` currently sets only `SessionSpec.Retry`; `RateLimiter` +
  `CircuitBreaker` are null → `DefaultSessionFactory.cs:~110` substitutes Shared defaults
  (TokenBucket refill 50/s, cap 35, queue 60; CircuitBreaker FailureThreshold 3, BreakDuration 30s).
- `server_delay_strategy` only flips `RespectRetryAfterHeader`/`ExternalizeServerSuggestedDelays`; the
  RetryOptions header-parsing strategy is never set (runs as KnownIndustryHeaders).
- `cursor_watermark` profiles render `{{watermark}}` empty on a fresh run → `>=''` → vendor 4xx/5xx.
- Field surfaces (validated): `TokenBucketOptions{TokenBucketCapacity,TokenBucketRefillRate,QueueLimit,
  AutoReplenishment}`, `CircuitBreakerOptions{FailureThreshold,BreakDuration}`, `RateLimiterKind.TokenBucket`,
  `SessionSpec{Retry,RateLimiter,CircuitBreaker}`.
- Engine internals: `PaginationSpec` (Profile.cs), `FetchStepExecutor.RunAsync` (Steps/), `RunContext`
  (Seams.cs), `StepExecutionScope`. Tests in Tests/CollectorExecutor.Test (88 today).

Constraints:
- See constraints.md. ZERO regression; opt-in null-preservation; fixed policy order (values only);
  watermark_seed only on fresh run; B1 verify-first (no faked knob); generic, no vendor identity; net8.0.

Success Criteria:
1. `dotnet build CollectorBase.slnx` clean; `dotnet test` → 88/88 + new tests (watermark_seed
   fresh-vs-resume; rate_limit/circuit_breaker populate SessionSpec when present; null when absent).
2. A — fresh fetch with `watermark_seed` → the request's `{{watermark}}` carries the seeded base_date
   (NOT empty); on resume the persisted watermark is used and the seed is ignored.
3. B2 — when `rate_limit`/`circuit_breaker` present, `SessionSpec.RateLimiter`/`CircuitBreaker` are
   populated with the mapped values; when absent, both are null (Shared default preserved). Unit-tested
   on the SessionFactory.
4. B1 — `server_delay_strategy` maps to the real header strategy IF the package supports it; otherwise
   documented honestly. The verifier states which outcome occurred.
5. C — the Runner's default runs root resolves to the repo root regardless of the launch directory;
   `--out`/`CS_OUT` override still works.
6. Docs updated: `CollectorExecutor/docs/capabilities.md` (resilience section) and
   `docs/yaml-contract.md` (resilience table); `integrations/crowdstrike-falcon.yaml` seeded with
   `watermark_seed: "{{input.base_date}}"` on assets + findings.

Execution Rules:
- Incremental: A → B1 → B2 → C, build + test green between each; STOP on red that isn't trivial wiring.
- B1: verify the package surface FIRST (read the RetryOptions type / its usage); only wire if it exists.
- Do not assume missing data; respect constraints strictly; no vendor branches in the runner.

Output Format:
- Engine: `PaginationSpec.WatermarkSeed`, `ResilienceSpec.RateLimit`/`CircuitBreaker` (+ small spec types),
  FetchStepExecutor seed logic, SessionFactory mapping. Runner: runs-root resolver. Profile + docs edits.
  New tests. Updated `execution_notes.md` + `state.json`.

Stop Conditions:
- Goal achieved (criteria 1–6), OR
- A no-knobs build/suite goes red (regression — STOP), OR
- B1's package surface is ambiguous in a way that would fake an unsupported knob (document + STOP wiring).
