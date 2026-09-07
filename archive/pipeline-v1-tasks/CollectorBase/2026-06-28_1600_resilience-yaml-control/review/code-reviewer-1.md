# Code review — resilience YAML control panel + watermark seed + Runner repo-root fix

Scope: only the listed changed areas, judged on their own merits. Build is clean (0 errors); the 4 new
tests pass.

## Claim verification

### (a) Absent `rate_limit`/`circuit_breaker` ⇒ `SessionSpec` fields stay NULL — CONFIRMED
`CollectorExecutor/Execution/CollectorExecutorSessionFactory.cs:46-58` and `:59-65` are true
`retry.RateLimit is { } rl ? new RateLimiterOptions {...} : null` / `retry.CircuitBreaker is { } cb ? new
CircuitBreakerOptions {...} : null` ternaries. Nothing else in the factory fabricates these objects (grep:
the two `new RateLimiterOptions`/`new CircuitBreakerOptions` sites are the only ones, both inside the
conditionals). When null, the Shared default applies at `Shared/.../Session/DefaultSessionFactory.cs:110`
(`spec.RateLimiter ?? new RateLimiterOptions {...}`) and `:122` (`spec.CircuitBreaker ?? new
CircuitBreakerOptions {...}`). Existing profiles (none declare these knobs) are byte-for-byte unchanged.
Test `SessionFactory_ResilienceKnobsAbsent_LeaveSessionSpecNull_SharedDefaultPreserved` (CollectorExecutorTests.cs:1075)
asserts both are null. Verified.

### (b) Watermark seed applies ONLY on a fresh run — CONFIRMED
`FetchStepExecutor.cs:38` sets `runCtx.Watermark = seed.Watermark;` (the resumed/persisted value, sourced
from `CheckpointState.Watermark` via `CollectorExecutorCheckpointManager.cs:45`) BEFORE the guard at
`:42`: `if (string.IsNullOrEmpty(runCtx.Watermark) && !string.IsNullOrWhiteSpace(step.Pagination.WatermarkSeed))`.
A resumed non-empty watermark therefore short-circuits the seed; the seed only fires when there is no
persisted floor. Test `WatermarkSeed_Resume_UsesPersistedWatermark_IgnoresSeed` (line 1027) proves the
resumed `2026-01-03` wins and the seed `2026-01-02` is never requested. `WatermarkSeed_FreshRun_...` (line
1009) proves the fresh path seeds. Verified. Note: ordering here is load-bearing — if a future refactor moves
the seed block above line 38 it silently breaks resume; the inline comment flags this but there is no
structural guard. Acceptable.

### (c) Field mapping — CONFIRMED correct
- `requests_per_second` → `TokenBucketRefillRate` (SessionFactory.cs:52) ✓ (`int`→`double` safe widening)
- `burst` → `TokenBucketCapacity` (:53) ✓
- `queue_limit` → `QueueLimit` (:54) ✓
- `failure_threshold` → `FailureThreshold` (:62) ✓
- `break_duration_seconds` → `BreakDuration` via `TimeSpan.FromSeconds(...)` (:63) ✓
Confirmed against the option type fields in `Cymulate.Http.Package/.../TokenBucketOptions.cs` and
`CircuitBreakerOptions.cs`. Test `SessionFactory_ResilienceKnobsPresent_PopulateSessionSpec` (line 1057)
asserts all five with the exact profile values (7/9/11, 4, 45s). `AutoReplenishment = true` is set
explicitly (matches the Shared default); `QueueProcessingOrder` left at its `OldestFirst` default (fine).

---

## Findings

### MAJOR — degenerate resilience values throw `ArgumentOutOfRangeException` at session build, no pre-validation
`TokenBucketOptions.Validate()` throws when `TokenBucketRefillRate <= 0` or `TokenBucketCapacity <= 0`, and
`CircuitBreakerOptions.Validate()` throws when `FailureThreshold <= 0` or `BreakDuration <= TimeSpan.Zero`
(see the option types + `Cymulate.Http.Package/Session/Logic/Wiring/SessionBuilder.cs:49/90/109` and
`Policies/RateLimiterPolicy.cs:27`). These `Validate()` calls run at the SessionBuilder construction
boundary — i.e. the moment the CollectorExecutor builds the real session, AFTER auth and BEFORE any HTTP.

The new spec types declare these as plain `int` with default `0` (`Profile.cs:59-61`, `66-68`), and
`ProfileLoader.Validate` does NOT range-check them. So a profile that declares the block but omits or
zeroes a field — `rate_limit: { burst: 9 }` (requests_per_second defaults to 0), or
`circuit_breaker: { failure_threshold: 4 }` (break_duration_seconds defaults to 0) — produces a degenerate
options object that throws deep in http.package at session build. The exception surfaces as an opaque
`ArgumentOutOfRangeException` rather than the project's normal fail-closed "Invalid profile: ..." message
from `ProfileLoader`.

This violates the repo's stated fail-closed-before-HTTP discipline (CLAUDE.md: "resolve strategies ...
fail-closed, before any HTTP") and the yaml-contract intent that a malformed profile is rejected at load
with a clear message. A partially-specified `rate_limit`/`circuit_breaker` is an easy and likely authoring
mistake (YAML inline maps make it trivial to forget a key).

Recommendation: when the block is present, validate in `ProfileLoader.Validate` that
`requests_per_second >= 1`, `burst >= 1`, `queue_limit >= 0`, `failure_threshold >= 1`,
`break_duration_seconds >= 1`, with the same `errors.Add(...)` pattern as the rest of the loader. Cheap,
keeps the failure at the declared-contract boundary, and matches how every other knob in this file is
guarded. (The alternative — documenting "all fields required, all > 0" — is weaker because nothing enforces
it.)

### MINOR — `queue_limit` semantics not validated against `burst`/capacity
No cross-field check. `TokenBucketOptions` itself only requires `QueueLimit >= 0`, so this is not a crash,
but a `queue_limit` far below `burst` under sustained throttling silently rejects requests (the token
bucket's documented `0 = reject when full` behavior scales up). Not a defect in the mapping; flagging only
because the YAML exposes the raw knob with no guidance. A one-line doc note on `RateLimitSpec.QueueLimit`
would help authors. Optional.

### NIT — comments on `RateLimitSpec`/`CircuitBreakerSpec` are accurate; `int` vs `double` undocumented
`requests_per_second` is `int` in YAML but the bucket refill rate is `double`. Sub-1-rps throttles
(e.g. 1 request / 2s) are therefore not expressible. No current profile needs it (Falcon uses the Shared
default). Fine as-is given "simplest solution"; note it as a known limitation if sub-1-rps is ever requested.

### `ResolveRepoRoot()` — CORRECT and SAFE
`Runner/Program.cs:186-195`. Walks up from `Directory.GetCurrentDirectory()` then `AppContext.BaseDirectory`
to the first dir containing `CollectorBase.slnx`; falls back to CWD if not found. The fallback means the
worst case is the prior behavior (runs/ under CWD) — no throw, no null. The double-start (cwd + binary dir)
correctly covers both `dotnet run` from `Runner/` and a published-binary launch. `Path.Combine(...,"runs")`
on the result is safe. The `--out`/`CS_OUT` override still takes precedence (`Program.cs:107-109`) and is
`Path.GetFullPath`'d relative to CWD as documented. No issue.

The profile-not-found guard (`Program.cs:87-94`) is a clean improvement: it surfaces the resolved absolute
path and the cwd instead of letting `File.ReadAllText` throw a bare `FileNotFoundException`. Returns exit 2.
Correct.

---

## Nullability / parse pitfalls (new spec types, YamlDotNet snake_case)
- `RateLimitSpec` / `CircuitBreakerSpec` are non-nullable `int` properties; `ResilienceSpec.RateLimit` /
  `.CircuitBreaker` are nullable reference types. YamlDotNet with `UnderscoredNamingConvention` maps
  `requests_per_second`→`RequestsPerSecond` etc. with no `[YamlMember]` alias needed (verified: names are
  plain snake_case of the property). `IgnoreUnmatchedProperties()` is on, so a typo'd key
  (`request_per_second`) silently maps to nothing and the field stays `0` — which feeds straight into the
  MAJOR above (a typo'd `requests_per_second` ⇒ refill rate 0 ⇒ throw). Another reason to range-check at load.
- Absent block ⇒ `RateLimit`/`CircuitBreaker` stay null (RTN default) ⇒ correct null-propagation in the
  factory. No NRE risk: `is { } rl` pattern guards before access.
- `WatermarkSeed` is `string?`; unresolved template tokens render to `""` (`Interpreter.cs:17`), and the
  `!string.IsNullOrWhiteSpace(seeded)` guard at `FetchStepExecutor.cs:46` prevents seeding with an empty
  result — so a `watermark_seed: "{{input.missing}}"` correctly no-ops rather than leaking a `{{...}}`
  literal or an empty floor. Good.

## Value-range issues
Covered by the MAJOR. The degenerate-token-bucket case the prompt asked about (0 requests_per_second) is
real and currently throws at session build rather than being rejected at load.

---

## Verdict
**APPROVE WITH CHANGES.** The three claims (a/b/c) are all confirmed correct, the mapping is right, the
opt-in/null-default design is sound (zero regression for existing profiles, verified by test), and
`ResolveRepoRoot()` is correct and safe. The one substantive issue is the MAJOR: declaring the resilience
block with a missing/zero field throws an opaque `ArgumentOutOfRangeException` deep in http.package instead
of a fail-closed "Invalid profile" at load — add range validation in `ProfileLoader.Validate` for the new
knobs. The MINOR/NIT items are optional. No BLOCKER.
