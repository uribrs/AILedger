# Code Review — FaultGovernance concern

**Reviewer:** independent senior-engineer code review (isolation mode).
**Stack:** C# / .NET 8 library concern.
**Change type:** shared library code (failure-policy decision engine + recovery budget + checkpoint helpers).
**Risk level:** High — retries, deferred recovery, persistence boundaries, livelock bounds, and shared/static decision state. Operational impact dominates over code size here.

Bounding context honored: relocated-verbatim style is not flagged; `Kernel.Transport` classification is out of scope; envelope DTOs deliberately live in `Envelopes.Common`; the fixed policy-chain order is an intentional invariant.

---

## Summary by severity

| Severity | Count | Items |
|---|---|---|
| Blocker | 0 | — |
| Major | 0 | — |
| Minor | 4 | M1 jitter validation placement; M2 single-shot `MaxRetries<=0` edge; M3 `RecoveryParsingHelper.TryGetInt` accepts negatives / non-invariant culture; M4 test coverage gaps on the high-risk units |
| Nit | 4 | N1 `Assert.Equal(true, ...)`; N2 `PersistDeferredWaitSnapshot` mutates caller metadata; N3 `Multiply` clamp relies on saturating conversion (document it); N4 `CheckpointAdapter.HasData` double-allocates |
| Observation | 3 | O1 `AdapterFlowFailureHandling` mixed responsibility; O2 budget RMW concurrency invariant; O3 `Random.Shared` thread-safety |

No blocking or merge-gating defects found. The code is coherent, the budget/executor separation is clean, and the most dangerous path (exponential backoff overflow) is **correct** on this runtime. Findings are improvements and test-coverage gaps, not dangers.

---

## Detail

### Focus-area verdicts (the items called out for scrutiny)

**`AdapterBackoffPlan.GetDelay` — exponential overflow / clamp: CORRECT.**
I verified the `Multiply` clamp empirically on .NET 8 x64 (compiled probe). The chain `delay.Ticks * Math.Pow(2, n)` (long × double → double) → `Math.Min((double)long.MaxValue, product)` → `(long)` saturates to `long.MaxValue` for any attempt number (tested n = 30…100), yielding ~10.67M days rather than wrapping negative. .NET Core/5+ made float→int conversion saturating (IEEE-754), so the legacy `(long)Math.Min(long.MaxValue, …)` pattern is safe here. Note: this same code on .NET Framework would wrap to `long.MinValue` and produce a negative `TimeSpan` — see N3. `DelaySequence` indexing (`Math.Min(retryAttemptNumber, Count-1)`) correctly clamps to the last entry; the `{ Count: > 0 }` guard is right. Jitter math `(NextDouble()*2-1)*ratio` gives a symmetric ±ratio band and `Math.Max(0, …)` floors at zero — correct.

**`RecoveryBudgetEvaluator` logic: CORRECT.** Backstop (total then age, OR-combined, progress-independent) is evaluated before the stuck gate, matching the documented "whichever trips first" and "slow-livelock bound" intent. Missing prior coordinate → treated as progress → reset to 1 is the safe fail-open direction. `consecutiveNoProgressCount > maxRetries` is the right boundary (count starts at 1, so `maxRetries` real no-progress retries are allowed). Pure, no IO, directly testable — good separation. The `> MaxTotalBudgetedDeferrals` (strict) with `totalDeferralCount` pre-incremented means the 51st deferral trips at value 51 — internally consistent.

**Checkpoint persist/restore null/parse handling: CORRECT and defensively thorough.** `AdapterRecoveryBudget.Load` null-guards `state`, coerces malformed ints/dates/coordinate to safe defaults with observability logging, and treats blank/absent as "progressed" (graceful migration for pre-upgrade checkpoints — documented). `Write` validates non-negative counts and non-negative delay, uses `"O"` round-trip + `InvariantCulture` consistently. `ReadCoordinate` / `IsWellFormedCoordinate` validate the `page:items:findings` shape and fail open to null. Round-trip integrity (Write uses `CultureInfo.InvariantCulture`; Load reads with `NumberStyles.Integer, InvariantCulture` / `DateTimeStyles.RoundtripKind`) is matched. No issues.

**Record/equality correctness for decision models: CORRECT.** Discriminated-union pattern (sealed `abstract record` + private ctor + nested sealed records) is idiomatic and exhaustively matched in the executor `switch` with a throwing default. `RecoveryBudgetDecision` and `AdapterRecoveryBudgetSnapshot` as `readonly record struct` give correct value equality. `AdapterFailureHandling` round-trip (`To/FromFlowExceptionHandling`) preserves all four fields — covered by a test and verified. No mutable reference fields leak into equality in a way that misbehaves for the value types.

### Minor

**M1 — `ApplyJitter` validates `JitterRatio` after the early-zero paths.**
`AdapterBackoffPlan.GetDelay` → `ApplyJitter` throws `InvalidOperationException` for out-of-range `JitterRatio` only when `UseJitter` is true *and* delay > 0 reaches the multiplier branch — but the range check sits before the `!UseJitter` short-circuit, so it actually throws even when `UseJitter=false`? Re-reading: the throw is the *first* statement, so a plan with `UseJitter=false, JitterRatio=2.0` still throws. That is a latent surprise: `None` and the single-shot plans set `UseJitter=false` and never set `JitterRatio` (defaults 0.20, fine), so no current caller trips it, but a future `UseJitter=false` plan that carries a junk ratio would throw on a path that ignores jitter entirely. Impact: low (no current caller). Fix: move the range validation inside the `UseJitter && JitterRatio > 0` branch, or validate in an initializer. Local patch.

**M2 — `MaxRetries <= 0` returns `TimeSpan.Zero` regardless of `DelaySequence`.**
`GetDelay` short-circuits to zero when `MaxRetries <= 0`. The single-shot/server-suggested plans set `MaxRetries = 1`, so they are unaffected, and `None` (MaxRetries=0) intentionally yields zero. This is correct for current callers, but the precedence (retries gate beats an explicitly supplied `DelaySequence`/`InitialDelay`) is a subtle coupling — a caller who builds a `DelaySequence` plan but forgets `MaxRetries` silently gets zero delay rather than an error. Impact: low. Consider an explicit `ArgumentException` when `DelaySequence` is non-empty but `MaxRetries <= 0`, or document the precedence. Local patch / deferrable.

**M3 — `RecoveryParsingHelper.TryGetInt` accepts negatives and uses ambient culture.**
`int.TryParse(str, out value)` (no `NumberStyles`/`IFormatProvider`) parses with the current culture and accepts negative values, unlike the stricter `AdapterRecoveryBudget.ReadNonNegativeInt` (invariant + `>= 0`). For checkpoint counters written with invariant culture this is a latent locale mismatch (e.g. a culture using non-ASCII digits/grouping) and lets a negative page/item count through. This is a public helper collectors call for their own state. Impact: low–medium depending on how collectors use it. Fix: `int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)` and reject `< 0` if these are always counts. Local patch.

**M4 — Test coverage gaps on the highest-risk units.**
The two test files cover (a) the value-type round-trip/mapping and (b) chain-order short-circuits. The chain-order tests are **meaningful, not vacuous**: each asserts a distinct decision type reached through the real `DecideAsync` surface with an exception that only the intended policy claims (cancellation, NRE→fail-fast, IOException→deferred when backoff configured, InvalidOperationException→rethrow, HttpRequestException→fallback publish), plus the empty-policies guard. Good. But the most operationally dangerous logic has **no direct tests**:
- `RecoveryBudgetEvaluator.Evaluate` — zero tests. This is the pure livelock-bound owner; it is explicitly designed to be unit-testable and is exactly the unit a regression would silently break (off-by-one on the stuck gate, backstop precedence, coordinate-progress reset). This is the most valuable missing coverage.
- `AdapterBackoffPlan.GetDelay` — no tests for `DelaySequence` clamping, exponential growth, `MaxDelay` capping, the overflow-saturation behavior, or jitter bounds.
- `AdapterRecoveryBudget.Load`/`Write` round-trip and malformed-value coercion — no tests.
- `RecoveryParsingHelper` / `RecoveryStateHelper.TryLoad` (try/catch swallow) — no tests.

Impact: medium. None of this blocks merge, but for High-risk persistence/retry code the evaluator and `GetDelay` deserve table-driven tests. Recommend adding before this is depended upon broadly. Not a refactor.

### Nit

**N1 —** `Assert.Equal(true, error.Retryable)` / `Assert.Equal(false, …)` in `AdapterFailureHandlingTests` — `Retryable` is `bool?`; use `Assert.True(error.Retryable)` / `Assert.False(...)` (or `Assert.Equal((bool?)true, …)`) for clarity. Cosmetic.

**N2 —** `PersistDeferredWaitSnapshot` writes four `_checkpoint.*` keys into `progressContext.Metadata` and never clears them. Subsequent non-deferred checkpoints on the same context could inherit stale `kind=StateSnapshot`/`reason=deferred-recovery` metadata unless the host resets it. Likely fine given the documented single-threaded-per-context host contract and that each checkpoint rewrites these keys, but worth confirming the host overwrites kind on a normal page commit. Observation-level; no change required if host always sets these.

**N3 —** `Multiply`'s clamp correctness depends on .NET Core's saturating float→int conversion. It is correct on .NET 8 (verified) but would silently wrap negative on .NET Framework. Since this is .NET 8-only, no action — but a one-line comment ("relies on .NET saturating double→long conversion") would protect against a future port. Cosmetic.

**N4 —** `CheckpointAdapter.HasData` calls `GetData(checkpoint).Count > 0`, allocating a fresh dictionary copy just to check emptiness. Cold path (validation), so negligible, but a direct `checkpoint?.AdapterState is { Count: > 0 }` avoids the copy. Cosmetic.

### Observation (no action required)

**O1 — `AdapterFlowFailureHandling` mixed responsibility (classification + publishing).** The type combines `ClassifyUnhandledException` (pure governance classification, consumed by `FallbackFailurePolicy.CreateDecision`) with `PublishErrorAndFailureCompletionAsync` and friends (Reporting/Conducting behavior — they call `context.PublishAsync`). This *is* a real concern split, and the file's own header flags it as a future-separation candidate. Given the verbatim-relocation constraint and that the publishing half is `internal` and self-contained (each publish is individually try/caught and swallowed-with-warning, which is the right call for best-effort error reporting), this is an acceptable carried tradeoff — not accidental complexity. The classification half being the only part the policy chain consumes makes the eventual split low-risk. Defer to the Events/Reporting carve as the README states. No change now.

**O2 — Budget read-modify-write concurrency.** `AdapterFailureDecisionExecutor` performs a load→evaluate→write over the non-thread-safe `AdapterState`/`Metadata` dictionaries and the static budget keys. The executor documents that it relies on the host driving the failure path single-threaded per progress context (one failure decision per flow invocation). That is an externally-provided invariant, not enforced here. Correct to document rather than over-engineer a lock — adding synchronization would be premature given the stated host contract. Flagged only so the invariant stays visible; if the host contract ever changes, this becomes a torn-state hazard.

**O3 — `Random.Shared` in `ApplyJitter`.** Defaulting to `Random.Shared` is the right .NET 6+ choice — it is explicitly thread-safe, unlike a shared `new Random()`. The `Random?` parameter lets tests inject determinism. No issue; noted because jitter + shared static is exactly where a `new Random()` mistake usually hides, and this code avoids it.

---

## Bottom line

Technically safe, idiomatic, and maintainable on its own merits. The separation between the pure `RecoveryBudgetEvaluator`/policies and the IO-bearing executor is the right shape for this risk class, the discriminated-union decision model is exhaustively handled, and the persistence helpers are defensively correct. The single most worthwhile follow-up is **M4** — table-driven tests for `RecoveryBudgetEvaluator.Evaluate` and `AdapterBackoffPlan.GetDelay`, the two units whose silent regression would be operationally expensive. Everything else is local polish.
