# Code Review — Recovery Budget Progress Anchor

**Change type:** shared library + orchestration code (retry/recovery/persistence boundary)
**Risk level:** High — touches retries, pagination/resume state, persistence round-trip, and process-global mutable config in a multi-tenant host process.
**Review depth applied:** failure semantics, concurrency, determinism, persistence/restore round-trip, integer/parse edges, idiomatic C#, test fidelity.

---

## Overall Verdict

The core idea is sound and the local mechanics (parse hardening, round-trip Write/Load, OR-combined backstops, reset-aware backoff index) are mostly clean and well-tested *in isolation*. However, the central correctness claim — "consecutive recoveries that make no forward progress are bounded by a progress coordinate" — rests on an assumption that the tests never actually exercise: that the coordinate computed on the **resume leg** is comparable to the one persisted on the **process leg**. The change deliberately does *not* seed collector progress state on resume (only `_resilience.*` keys), and `ComputeProgressCoordinate` hashes exactly that un-seeded collector state. This is the one finding I'd block on until it's either proven safe or the coordinate input is constrained. Two Major items (process-global mutable statics in a shared host; tests asserting on a self-fulfilling coordinate) should be resolved before merge.

---

## Findings

### BLOCKER

**B1 — Progress coordinate may not be comparable across the resume hop; the "no-progress" gate can silently never trip (or trip falsely).**
`AdapterRecoveryBudget.ComputeProgressCoordinate` (AdapterRecoveryBudget.cs:158-180) hashes the non-`_resilience.*` entries of `AdapterState` plus `CurrentPage/ProcessedItems/ProcessedFindings`. The whole budget pivots on comparing the coordinate computed *now* against `prior.LastProgressCoordinate` (AdapterFailureDecisionExecutor.cs, `EvaluateBudget`).

The problem is the asymmetry between the two legs:
- **Process leg:** `AdapterState` contains whatever collector progress keys the flow has written (cursor/watermark/etc.) at the moment of failure.
- **Resume leg:** `CollectorResumeSetup.TryCreateExecutionContext` (CollectorResumeSetup.cs:59-69) calls `RestoreProgress(...)` (page/items/findings counters only) and then `SeedFromPersistedState(progressContext, data)` which by design copies **only `_resilience.*` keys**. The inline comment is explicit: "Collector progress keys are intentionally not seeded — the flow re-derives those on resume."

So at the instant `EvaluateBudget` runs on a resumed invocation, the collector progress portion of `AdapterState` is whatever the flow has re-derived *so far in this invocation* — which is timing-dependent and generally **not** the state that produced `LastProgressCoordinate` on the prior leg. Three failure modes follow:

1. If the flow re-derives its cursor/watermark into `AdapterState` *before* the next failure reaches `EvaluateBudget`, the coordinate can differ even when zero net forward progress was made → every defer looks like "progress" → `candidateCount` resets to 1 forever → **the stuck-no-progress gate never fires.** The 24h / 50-deferral backstops become the *only* real bound, silently demoting the headline feature to dead weight.
2. If the flow has *not* yet re-derived its progress keys when the failure hits (fails early on resume), the coordinate reflects empty/partial collector state and can collide across genuinely-different positions → false "no progress" → premature fallback on a run that was actually advancing.
3. The page/items/findings counters *are* restored (so they're stable across the hop), but the dictionary-derived portion is not — meaning the coordinate is a mix of one stable input and one unstable input. That is the worst case for a hash meant to be an equality anchor.

**Impact:** The budget's primary guarantee is not established by the code as written; correctness depends on per-collector flow timing that this shared layer does not control. High operational risk because it fails *open* (mode 1) — a truly stuck run keeps deferring up to the 24h/50-defer backstop instead of the intended `MaxRetries`.

**Why the tests don't catch it:** every executor test runs with `IsResume = false` and computes the "current" coordinate from the *same* empty-business-state `AdapterState` it seeded the prior coordinate into (`CurrentCoordinate(ctx)` + `SeedPriorBudget(ctx, …, coordinate: CurrentCoordinate(context))`, AdapterFailureDecisionExecutorTests.cs:52-77). The coordinate is identical by construction because the business state is empty and never changes between seed and evaluate. No test seeds collector progress keys, restores a different set on resume, and then re-evaluates. The round-trip that matters operationally is untested.

**Recommended fix (pick one; this is a refactor, not a local patch):**
- Preferred: stop deriving the coordinate from the free-form `AdapterState` dictionary and base it only on inputs that are *guaranteed identical across the hop* — the restored `CurrentPage/ProcessedItems/ProcessedFindings/sequenceId` plus an explicit, flow-supplied watermark token that the resume path restores deterministically (same source as `RestoreProgress`). That removes the un-seeded-dictionary dependency entirely.
- Alternative: if the dictionary really must contribute, also seed the collector progress keys on resume from `data` (so both legs hash the same snapshot) — but that contradicts the stated design ("flow re-derives those") and risks stale collector state, so I do not recommend it without the flow owners signing off.
- At minimum, add a resume-leg test: persist a coordinate from business-state {cursor=A}, simulate resume that restores counters but leaves business-state empty/re-derived to {cursor=A}, and assert the coordinate matches; then mutate to {cursor=B} and assert it differs. If you cannot write that test green, B1 is confirmed in practice.

---

### MAJOR

**M1 — `MaxTotalBudgetedDeferrals` / `MaxBudgetedRecoveryAge` are process-global mutable statics in a shared multi-collector host.**
AdapterFailureDecisionExecutor.cs:~36-37. The provided context states multiple collectors/flows run concurrently in one process and these props have public setters ("Static so a future host config can override the defaults").

- **Concurrency:** plain `int` / `TimeSpan` static auto-properties are not guaranteed atomic for torn-read purposes across threads in the general case, and more importantly there is no memory-ordering guarantee — a writer on one thread and readers on collector threads can race. `TimeSpan` is a multi-field struct; a concurrent write during a read is a torn-read hazard.
- **Blast radius:** a setter is global. One collector (or a test — see M2) mutating these changes the budget for *every* collector in the process. That is a cross-tenant correctness/isolation problem, not just a style issue.
- **Test pollution:** xUnit runs tests in a class sequentially but parallelizes across classes by default. Any test that sets these statics (none do today, but the setter invites it) leaks into other test classes nondeterministically.

**Impact:** latent concurrency hazard + global coupling in exactly the "shared infrastructure / retries / concurrency" category that warrants Major severity regardless of code size.
**Fix:** make them `static readonly` (or `const`-like `get`-only) defaults and thread the actual limits through the decision/`RecoverAndRetry`/`BackoffPlan` per-invocation, the same way `MaxRetries` already flows. If a host-config override is genuinely needed "in the future," add it then, scoped per flow — do not ship a global mutable seam speculatively (YAGNI; it's also the cheaper change). Local patch acceptable: drop the setters now (`{ get; } = …`), defer the config seam.

**M2 — Tests assert behavior against a coordinate they themselves compute from the same mutable state, so they validate the implementation against itself, not against the spec.**
AdapterFailureDecisionExecutorTests.cs:52-58, 67-77, and the no-progress tests (`WhenNoProgressWithinBudget_IncrementsConsecutiveCount`, `WhenNoHookAndNoProgressBudgetExhausted_AppliesFallback`). `SeedPriorBudget(ctx, …, coordinate: CurrentCoordinate(context))` seeds the prior coordinate by calling the *same* `ComputeProgressCoordinate` the executor will call, over an `AdapterState` that does not change between seed and evaluate. These tests therefore prove only "if the prior coordinate equals the function's output now, count increments" — a tautology — not "no forward progress ⇒ same coordinate." A bug in `ComputeProgressCoordinate` (e.g. it hashed nothing, or hashed only resilience keys) would still pass every no-progress test.

**Impact:** the suite gives false confidence on the load-bearing invariant; tied directly to B1.
**Fix:** the "advanced" and "no-coordinate" tests are fine (they hardcode a stale coordinate). For the no-progress tests, seed a *hardcoded* coordinate string and arrange the context's business state so the executor independently recomputes that exact value — i.e. assert the function maps a fixed input to a fixed output, then assert the gate uses it. Better still, add the cross-leg test from B1.

---

### MINOR

**M3 — `SeedFromPersistedState` skips empty-string values, which is the right call for counters but breaks "explicitly cleared" semantics on resume.**
AdapterRecoveryBudget.cs:142-148 copies a key only when `!string.IsNullOrEmpty(value)`. `Clear` writes `string.Empty` to every key (AdapterRecoveryBudget.cs:117-120) as the "cleared" marker. If a checkpoint was persisted in the cleared state and then resumed, `SeedFromPersistedState` will *not* copy the empty markers, leaving those keys absent in the live context. For the current readers that is benign (absent and empty both parse to 0/null), so no live bug — but it means "cleared" and "never set" are indistinguishable after a resume, which is a latent trap if any future reader special-cases empty-vs-absent.
**Impact:** none today; fragility for future readers.
**Fix:** either document that absent ≡ empty ≡ unset for all resilience keys (one-line comment), or copy empties too. Local patch / optional.

**M4 — Control-character delimiters (U+0001 / U+0002) in the hash input are clever but undocumented and untested as the actual separators.**
AdapterRecoveryBudget.cs:171. Using ``/`` to avoid delimiter collision with key/value content is a legitimate technique (better than `:` or `|`, which appear in timestamps/URLs). But (a) there is no comment explaining *why* these bytes were chosen, so a future editor may "tidy" them into printable chars and reintroduce a collision (`{"a":"b|c"}` vs `{"a|b":"c"}`); and (b) the determinism test (`ComputeProgressCoordinate_IsDeterministic…`) checks ordering/exclusion/advance but never asserts the delimiter actually prevents a `key|value` boundary-shift collision.
**Impact:** correctness of the anchor depends on an unexplained, untested invariant.
**Fix:** add a one-line comment naming the chars and the collision they prevent; add a test: `Compute({"a"="b","c"="d"})` ≠ `Compute({"ab"="","c"="d"})` and ≠ `Compute({"a"="bcd"})`-style boundary cases. Local patch.

**M5 — SHA-256 hex is 64 chars persisted on every budgeted defer; fine, but note the cost is paid into the checkpoint, not just memory.**
AdapterRecoveryBudget.cs:178-179 + Write persists `LastProgressCoordinateKey`. SHA-256 over a small string is cheap CPU-wise and this is a cold (failure) path, so no perf concern. The only note: the coordinate is stored in the durable checkpoint every defer. That's intended. A non-cryptographic 64-bit hash (e.g. `XxHash64`) would halve the stored bytes and is collision-safe enough for an equality anchor over tiny inputs, but SHA-256 is a defensible, dependency-free choice. **Observation, no action required.**

---

### NIT

**N1 — `EvaluateBudget` orders backstop checks before the stuck check; a run can be reported as "backstop-total-deferrals" when it was also stuck.** AdapterFailureDecisionExecutor.cs `EvaluateBudget`. OR-combined "first trip wins" is documented and fine; just be aware the emitted `FallbackReason` is order-dependent and may under-report the stuck condition in logs. No action.

**N2 — `candidateCount > recover.BackoffPlan.MaxRetries` vs the delay index `GetDelay(b.CandidateCount - 1)`.** AdapterFailureDecisionExecutor.cs. With `MaxRetries = N`, the gate allows `candidateCount` up to `N`, so `GetDelay` is called with indices `0..N-1` — N deferrals. That matches the reset-aware comment and is internally consistent. Confirm the operational intent is "N consecutive no-progress defers then fall back" (not N+1); the `WhenNoHookAndNoProgressBudgetExhausted` test (MaxRetries=1, prior=1 ⇒ candidate=2 ⇒ fallback) locks N=1 ⇒ one defer then stop, which reads correctly. No action.

**N3 — `int totalDeferralCount` and `int AttemptCount` are unbounded-by-type but bounded-by-logic.** `totalDeferralCount` is capped by `MaxTotalBudgetedDeferrals` (50) before it could ever approach `int.MaxValue`, and `AttemptCount` resets to 1 or is bounded by `MaxRetries`. No realistic overflow. The `ReadNonNegativeInt` guard also rejects negatives from a tampered checkpoint. No action.

---

## What's good (so the signal is balanced)

- `ReadNonNegativeInt` consolidation (AdapterRecoveryBudget.cs:182-202) is a clean dedupe of the prior inline parse, and correctly treats unparseable/negative as 0 with a warning rather than throwing on a poisoned checkpoint — right call for a resume path.
- The `AdvancePage(0,0)` → `PersistDeferredWaitSnapshot` switch is well-reasoned: `AdvancePage` bumped `CurrentPage`, which is restored across the hop and would have fed straight into the coordinate as fake progress. The regression test `DeferredWaitDoesNotAdvancePageAndConsecutiveDefersAccumulate` locks exactly that. Good defensive thinking — and ironically it's the proof that the team *knows* the coordinate is sensitive to restored-across-resume inputs, which is why B1 (the un-restored inputs) matters.
- `AllKeys` array driving both `Clear` and `SeedFromPersistedState` removes the prior copy-paste-five-keys hazard.
- Optional positional params on `Write` + defaulted record-struct members keep the round-trip backward-compatible with in-flight checkpoints; `WriteAndLoad_RoundTrips…` locks it.
- Migration path (missing coordinate ⇒ progress ⇒ reset to 1) is explicitly tested (`WhenPriorCheckpointHasNoCoordinate_TreatsAsProgress`) and fails *open* in the safe direction.

---

## Severity summary

| ID | Sev | One-liner |
|----|-----|-----------|
| B1 | Blocker | Coordinate not provably comparable across resume; stuck-gate can fail open (or false-trip). Untested round-trip. |
| M1 | Major | Process-global mutable static budget limits in a shared concurrent host. |
| M2 | Major | No-progress tests assert against a self-computed coordinate (tautological). |
| M3 | Minor | `SeedFromPersistedState` skips empties → cleared ≡ unset after resume. |
| M4 | Minor | Control-char delimiters undocumented + collision case untested. |
| M5 | Observation | SHA-256 persisted per defer; fine, could be a cheaper hash. |
| N1-N3 | Nit | Reason ordering, off-by-interpretation, overflow — all no-action. |

Resolve B1 before merge (or prove it safe with the cross-leg test). M1 and M2 should be fixed before merge unless consciously accepted.
