# Execution Notes — resilience YAML control panel + watermark seed + runs anchor

## Outcome
All four items shipped. `dotnet build` clean; `dotnet test` → **92/92** (88 prior + 4 new). No existing
test changed; the 5 existing profiles set none of the new knobs → behavior byte-identical (no regression).

## Per item
- **A — watermark seed.** `PaginationSpec.WatermarkSeed` (`watermark_seed`, templated). `FetchStepExecutor.RunAsync`
  at step entry: when `runCtx.Watermark` is null/empty (fresh run) and a seed is declared, render it against a
  `BuildContext(inputs/config)` and use it as the initial floor. A resumed `seed.Watermark` is never overridden.
  Seeded `integrations/crowdstrike-falcon.yaml` assets + findings with `watermark_seed: "{{input.base_date}}"`.
  Tests: `WatermarkSeed_FreshRun_SeedsFloorFromInput` (first `/things/query` carries `wm=<base_date>`, 2 records vs
  4 unseeded) + `WatermarkSeed_Resume_UsesPersistedWatermark_IgnoresSeed` (`wm=2026-01-03`, seed ignored, 1 record).
- **B1 — server_delay_strategy (VERIFY-FIRST → REJECTED, documented).** Verified against
  `Cymulate.Http.Package.Session 2.0.2`: `RetryOptions` exposes only GETTERS for the delay/header source
  (`get_RateLimitDelaySource`/`get_DelaySource`; `RetryRateLimitDelaySource`/`RetryServerSuggestedDelaySource` are
  *reported* sources), no settable strategy enum, and `KnownIndustryHeaders`/`RetryAfterOnly` are not in the
  constructable surface. ⇒ NO code change (the existing on/off boolean wiring is already correct); **docs corrected**
  to state `server_delay_strategy` is honor-vs-`disabled` only and does NOT pick a header-parsing strategy. No faked knob.
- **B2 — rate-limiter + circuit-breaker YAML knobs (headline).** `ResilienceSpec.RateLimit` (`rate_limit`:
  requests_per_second/burst/queue_limit) + `ResilienceSpec.CircuitBreaker` (`circuit_breaker`:
  failure_threshold/break_duration_seconds), both nullable. `SessionFactory.Build` sets `SessionSpec.RateLimiter`
  (`RateLimiterOptions{Kind=TokenBucket, TokenBucket{TokenBucketRefillRate,TokenBucketCapacity,QueueLimit,
  AutoReplenishment=true}}`) and `SessionSpec.CircuitBreaker` (`CircuitBreakerOptions{FailureThreshold,
  BreakDuration=FromSeconds(...)}`) **only when the sub-spec is non-null**; absent ⇒ left null ⇒
  `DefaultSessionFactory` substitutes the Shared default. Tests: present → populated mapped values; absent → null.
- **C — runs anchor.** `Runner/Program.cs`: default runs root = `<repoRoot>/runs` via `ResolveRepoRoot()`
  (walk up from CWD then `AppContext.BaseDirectory` to the dir holding `CollectorBase.slnx`; fallback CWD).
  `--out`/`CS_OUT` still wins. Verified: launched from `Runner/`, output landed at `<repo>/runs/<ts>/`.
- **DOCS.** capabilities.md (resilience surface: new sub-blocks + corrected server_delay_strategy + watermark_seed
  pagination note) + yaml-contract.md (resilience table rows + watermark_seed row + server_delay_strategy truth).

## No-regression confirmation
A no-knobs profile (`NoResilienceKnobsProfile`) → `SessionSpec.RateLimiter`/`CircuitBreaker` both null (unit-tested);
full suite 92/92 with zero edits to existing tests.

## Assumptions
- A1 → REJECTED (B1; documented honestly, no code).
- A2 (null preserves Shared default) → VALIDATED (SessionFactory test + DefaultSessionFactory `?? new …`).
- A3 (seed format comparable) → holds for Falcon (ISO8601); profile-author responsibility for other vendors.
- A4 (option field names) → VALIDATED (read from Shared source).

## Residual risk
None functional. `server_delay_strategy` remains coarse (honor/disabled) by package limitation — documented, not a bug.

## Commands
`dotnet build CollectorBase.slnx` → clean. `dotnet test CollectorBase.slnx` → 92/92.
