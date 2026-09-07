# Code Review — Recovery Budget: progress-anchored consecutive-no-progress gate

## Classification

- **Change type:** shared library / infrastructure (resilience subsystem used by every resumable collector).
- **Risk level:** High. Touches retry/recovery semantics, persistence (checkpoint `AdapterState`), distributed re-invocation, and runs in a shared multi-collector host. A defect here either burns money (livelock of long scans) or silently drops coverage (premature hard-stop).
- **Review depth:** Full — failure semantics, recovery/idempotency, concurrency, observability, long-term coupling, test invariants.

## Verification performed (evidence, not assumption)

I reflected the pinned SDK (`Cymulate.Integration.Sdk 3.1.5`, the version in `Directory.Packages.props`) and empirically ran `AdapterProgressContext` to confirm the load-bearing claims:

- **`AdvancePage(0,0)` DOES bump `CurrentPage` by 1** (observed 1→2 with zero items). This is real: the prior `AdvancePage(0,0)` snapshot would have inflated `CurrentPage` on every wait, and — because `CurrentPage` is part of the progress coordinate — would have masqueraded as forward progress and defeated the no-progress gate. The rewrite to `OnCheckpoint.Invoke` is **correct and necessary**, not cosmetic.
- **Initial `CurrentPage` is `1`, not `0`** (constructed via `FromPlatformEvent`). Relevant to the coordinate-collision analysis below.
- **`ProcessedItems` / `ProcessedFindings` / `CurrentPage` / `CurrentSequenceId` are get-only**; mutated only through `AdvancePage` / `RestoreProgress`. So the coordinate cannot drift from anything except those two methods.
- **`AdapterState` and `Metadata` are plain `Dictionary<string,string>` / `Dictionary<string,object>`** — NOT concurrent collections. See concurrency finding below.
- **`SetCheckpointMetadata(kind, reason, itemsInBatch, findingsInBatch)` exists in the SDK but is non-public.** The executor genuinely cannot call it; redeclaring the four `_checkpoint.*` const keys and poking `Metadata` directly is the only option from outside the SDK, and it exactly mirrors the established `FalconFindingsCheckpointWriter` pattern (`WriteCursorlessYieldCheckpoint`, `OnAssetsStageCompletedWithoutPublishedPage`). The comment claiming parity with the Falcon writer is accurate.
- **`RecoverAndRetry.UseRecoveryBudget` defaults to `true`** — so the rewritten `UnknownFlowFailurePolicy` path does flow through `EvaluateBudget`, and its `RethrowForUnknownRetry` fallback fires on genuine exhaustion. Correct.
- **Policy order** (`AdapterResilienceStrategy.CreateDefault`): UserCancellation → ProgrammerBug.Definitive → ServerSuggestedRetryDelay → RetryableTransport → vendor → mapped → ProgrammerBug.Ambiguous → **UnknownFlow** → Fallback. Unknown is a near-last catch-all; it cannot shadow Mapped. Correct.
- **`RetryAttemptNumber` readers:** only `FalconResilienceStrategyFactory.cs:140,142` in product code, and only for a log line + a *display* delay calc (`GetDelay`), never to drive control flow. The "vestigial / observability-only" comment is accurate and benign.

All of the above check out. The core mechanism is sound. The findings below are about edge cases the mechanism does not cover, and about shape.

---

## Findings by severity

### BLOCKER

None. The change is functionally correct for the cases it targets and is safe to ship after considering the Majors.

### MAJOR

**M1 — Concurrency claim in the constants comment is misleading; the real shared-state risk is elsewhere and unaddressed.**
`AdapterFailureDecisionExecutor.cs:10-14`. The comment asserts the `readonly` backstop constants are "safe for the concurrent multi-collector host (no torn reads, no cross-test mutation)." That part is true but trivial — they're immutable ints/TimeSpan. The comment implies concurrency has been *reasoned about*, which invites a false sense of safety. The actual concurrency surface is `AdapterProgressContext.AdapterState` / `Metadata`, which I confirmed are **plain non-thread-safe `Dictionary`** instances. `EvaluateBudget` does `Load` (read) then `ScheduleRecoveryAttempt` does `Write` (multiple `SetState`), and `PersistDeferredWaitSnapshot` mutates `Metadata` then invokes `OnCheckpoint`. If two failures for the *same* progress context were ever decided concurrently, you'd get a torn read-modify-write on the budget counters and a `Dictionary` corruption/`InvalidOperationException`.
- **Impact:** Per-invocation a collector flow is single-threaded through the failure path, so this is *likely* not reachable today — but nothing in this layer enforces or documents that invariant, and the comment actively misdirects a future reader toward "concurrency handled."
- **Fix:** Reword the comment to scope its claim ("the constants are immutable; per-context budget mutation assumes the SDK invokes the failure path single-threaded per progress context") rather than implying the subsystem is concurrency-safe. Local patch, not a refactor. Do **not** add locking — that would be over-engineering against an invariant the host already provides.
- **Confidence:** High on the dictionary types; Medium that single-threaded-per-context holds (couldn't see the host scheduler — state as assumption).

**M2 — `firstBudgetedDeferAtUtc ?? now` lets the 24h age backstop reset on the unbudgeted-wait path, opening a livelock the backstop was meant to close.**
`AdapterFailureDecisionExecutor.cs:239` combined with `ScheduleRecoveryAttempt` unbudgeted branch (`:300`, `AdapterRecoveryBudget.Clear`). `firstBudgetedDeferAtUtc` is loaded from `AdapterState`; the *unbudgeted* path (`UseRecoveryBudget=false` — server-suggested delays, planned yields) calls `Clear`, which wipes `FirstBudgetedDeferAtUtcKey`. So a run that **alternates** budgeted no-progress defers with any unbudgeted wait (e.g. a vendor that intermittently emits `Retry-After`) resets `firstBudgetedDeferAtUtc` to `now` on the next budgeted defer. Both backstops key off cleared state: the 24h age clock restarts, and `TotalDeferralCount` also resets to 0. The whole-collection backstop — the thing explicitly described as "NEVER reset by forward progress" — *is* reset by an interleaved unbudgeted wait.
- **Impact:** The slow-livelock scenario the backstop exists to bound is reachable for any vendor that mixes server-suggested delays with retryable failures. This is the exact "just enough progress to dodge the gate forever" case, one indirection over.
- **Fix:** Either (a) don't clear the backstop fields (`TotalDeferralCount`, `FirstBudgetedDeferAtUtc`) on the unbudgeted path — only clear the consecutive-no-progress fields — or (b) explicitly document that unbudgeted waits are a full budget reset and accept the consequence. (a) is the safer default and a small change to `Clear` (split into `ClearConsecutive` vs `ClearAll`, or pass a flag). Requires a focused patch, not a refactor.
- **Confidence:** High that `Clear` wipes the backstop fields (confirmed: `AllKeys` includes all three and `Clear` loops over `AllKeys`). Medium that the interleave is operationally common — depends on vendor behavior.

**M3 — Test suite does not lock M2, and does not lock the unbudgeted-clear-then-rebudget transition at all.**
The tests cover backstop trip (`...ExceedBackstop_FallsBack...`) and the budgeted increment/reset paths well, but there is **no test** that: defers budgeted → takes an unbudgeted wait → defers budgeted again and asserts the backstop totals survived (or intentionally reset). Given M2, this is the single most important missing invariant. Also missing: a test that the unbudgeted path clears the consecutive count but the next budgeted defer starts fresh as intended. As written, the suite would stay green even if M2 silently changed behavior.
- **Fix:** Add a test that seeds `TotalDeferralCount` near the backstop, routes one unbudgeted (`UseRecoveryBudget=false`) decision through the executor, then a budgeted one, and asserts the chosen semantics. This test will *fail* today and force a decision on M2.

### MINOR

**m1 — Integer overflow is not a practical risk, but `TotalDeferralCount` increments unconditionally before the backstop check, so the counter is unbounded across resumes in theory.**
`AdapterFailureDecisionExecutor.cs:237`. `totalDeferralCount = prior + 1` then trips fallback at `>50`. Because fallback terminates the run, the persisted value can't climb far past 51 in normal operation, and `int` won't overflow at human timescales. The only path to growth is M2 (reset masks it). No fix needed beyond M2; calling it out only because the prompt asked about overflow — it's a non-issue here.

**m2 — Coordinate format ambiguity is theoretically present but practically benign.**
`AdapterRecoveryBudget.ComputeProgressCoordinate` (`:156`) builds `"{page}:{items}:{findings}"`. The three components are non-negative ints (get-only, only ever advanced), so `:` cannot appear inside a component and the tuple is unambiguous. Two *different* states cannot collide. The only "collision" is the intended one (same counters ⇒ same coordinate ⇒ no progress). Fine as-is; do not reach for a structured/hashed encoding — that would be over-engineering. Confidence: High.

**m3 — Legitimate-progress-with-zero-net-counters edge: a page that commits but advances none of (page, items, findings) reads as no-progress.**
By construction `AdvancePage` increments `CurrentPage`, so any *committed* page advances the coordinate even with 0 items/findings — confirmed empirically (`AdvancePage(0,0)` → page 2). So "real progress, 0 net items/findings" still moves the coordinate via the page counter. The only way to make forward progress *without* moving the coordinate is to do work that never calls `AdvancePage` — which by definition isn't checkpointed progress and *should* count as no-progress for budget purposes. So this edge is handled correctly by the page component. Worth an explicit one-line test (`AdvancePage(0,0)` between two defers resets the consecutive count) to lock it; the existing `...DeferredWaitDoesNotAdvancePage...` test asserts the *inverse* (snapshot must NOT advance) but not the positive (a real zero-item page *does* reset).

**m4 — `Load` silently coerces a malformed coordinate to a present value.**
`AdapterRecoveryBudget.cs:48,61`. `LastProgressCoordinate` is only null-coalesced on whitespace; any non-empty garbage string is treated as a valid prior coordinate. A corrupted checkpoint with a junk coordinate would compare unequal to the real one ⇒ read as progress ⇒ reset to 1. That fails *open* (more lenient), which is the safer direction for a corrupted checkpoint, so acceptable — but unlike the int/datetime readers it logs nothing. Minor observability gap.

### NIT

**n1 — Field placement.** `AdapterFailureDecisionExecutor.cs:24` — `ExecuteAsync` immediately follows the `private const` block with no blank line, so the method visually merges into the const declarations. Add a blank line.

**n2 — Duplicated metadata-key constants** across `AdapterFailureDecisionExecutor` and `FalconFindingsCheckpointWriter` (and the comment admits it). Both redeclare the SDK's non-public keys. Acceptable given the SDK keeps them private, but see Structure note S3 for the proper home.

**n3 — `BuildPartialWaitData` writes `attemptNumber`/`maxRetries` into both `partialCompletion.AdditionalData` and the top-level `data` dict** (`:425-426` and `:441-442`). Pre-existing, not introduced here, but the duplication is now carrying the renamed-semantics `NextAttemptNumber` (which is the consecutive-no-progress count, not a lifetime attempt) under the externally-visible key `attemptNumber`. Consumers reading `attemptNumber` will misinterpret it. See S4.

### OBSERVATION

- The XML doc on `RecoverOrFallbackAsync` (`:104-110`) still describes the budget gate as *"once `AttemptNumber` reaches `BackoffPlan.MaxRetries`"* — stale wording from the old lifetime-count design. The gate is now progress-aware consecutive-no-progress. Update the doc to match, or it will mislead the next reader into the exact bug the `IAdapterFailurePolicy` remark warns against.
- `ScheduleRecoveryAttempt` unbudgeted branch uses `GetDelay(0)` (`:297`) — correct for a continuity wait, and consistent with the prior behavior.

---

## Structure & naming

The mechanism is correct; the **packaging is where this change is weakest**. The budget concept is now smeared across three files and two folders, and the central type name lies.

**S1 — `AttemptCount` / `AttemptCountKey` no longer mean "attempt count."** They mean *consecutive-no-progress defers*. The change keeps the key string for checkpoint compatibility (good, and correctly justified), but the *C# identifier* `AttemptCount` on `AdapterRecoveryBudgetSnapshot` and the `attemptCount` parameter on `Write` are free to rename without breaking persistence. They aren't. The result: `Write(..., b.CandidateCount, ...)` stores a "consecutive-no-progress count" into a property called `AttemptCount`, and the externally-published `data["attemptNumber"]` is actually that count. This is the single biggest comprehension trap in the change.
- **Recommendation:** Rename the in-memory identifiers to `ConsecutiveNoProgressCount` (snapshot property + `Write` param), keep `AttemptCountKey`'s *string value* but rename the const to `ConsecutiveNoProgressCountKey` (the const name is not persisted). Required-ish: this is a high-risk shared type and the dishonest name will cause a future regression. Low effort, no persistence impact.

**S2 — `BudgetEvaluation` + `EvaluateBudget` are misplaced inside the executor.** The executor's job is "turn a decision into an ISB-scheduled wait or a fallback." The *budget decision* (compute coordinate, compare, apply stuck gate + backstop) is a cohesive, pure, highly-testable unit that currently lives as a private method + private record struct buried in a 470-line orchestration file. It's pure (no IO, no async) and is exactly what you'd want to unit-test in isolation — yet today it can only be tested through the full executor pipeline (see S5).
- **Recommendation:** Extract a `RecoveryBudgetEvaluator` (or method on `AdapterRecoveryBudget`) with signature roughly `BudgetDecision Evaluate(AdapterRecoveryBudgetSnapshot prior, string currentCoordinate, int maxRetries, DateTime now)`. Move `MaxTotalBudgetedDeferrals` / `MaxBudgetedRecoveryAge` onto it. The executor then just calls it and acts on the result. This makes the budget concept *one named thing in one place*, directly unit-testable, and shrinks the executor back to orchestration. Worth doing now — the budget logic is the riskiest part and deserves to be isolated and pure-tested.

**S3 — Folder layout obscures the design.** `Logic/AdapterRecoveryBudget.cs` (persistence + coordinate + seeding) + `Logic/AdapterFailureDecisionExecutor.cs` (evaluation + backstop constants) + `Models/AdapterRecoveryBudgetSnapshot.cs` (DTO) means a new engineer has to read three files in two folders to reconstruct "how the budget works," and the backstop constants live in the *executor*, not anywhere named "budget." The `Models/` vs `Logic/` split is fine in principle, but the budget evaluation logic ended up in the failure-executor's file purely by accretion.
- **Recommendation:** After S2, the budget concept is: `AdapterRecoveryBudget` (persistence I/O + coordinate), `RecoveryBudgetEvaluator` (decision + thresholds), `AdapterRecoveryBudgetSnapshot` (state). All three named "budget," discoverable by name. That alone makes the shape legible. Do **not** create a new `Budget/` subfolder for three files — that's ceremony; keeping them in the existing `Logic/` + `Models/` with honest names is enough.

**S4 — `AdapterRecoveryBudget` is doing slightly too much, but not egregiously.** It owns: key constants, `Load`/`Write`/`Clear`, `ComputeProgressCoordinate`, `SeedFromPersistedState`, and the parse helpers. Coordinate computation and seeding are arguably separate concerns, but they're small, cohesive with persistence, and splitting them would scatter the concept further. **Leave them here** — the only thing that genuinely belongs elsewhere is the *evaluation* (S2). Calling for more decomposition than that would be over-engineering.

**S5 — Tests assert through the pipeline; the pure budget math has no direct test.** `AdapterBackoffAndBudgetTests` tests `ComputeProgressCoordinate`, `Write`/`Load` round-trip, and `Clear` — all good, genuine invariants, not tautological. `AdapterFailureDecisionExecutorTests` drives the budget through the full executor, which is valuable as integration coverage but means the stuck-gate vs backstop *interaction* (e.g. backstop trips while candidate count is also over MaxRetries — which reason wins?) and the OR-combination ordering are only tested indirectly. The current tests assert real behavior (not implementation-against-itself), but they leave the precedence of the three fallback reasons (`backstop-total` > `backstop-age` > `stuck`) unlocked. After S2's extraction, add direct evaluator tests for: (a) backstop and stuck both true → which `FallbackReason`; (b) the M2/M3 unbudgeted-interleave; (c) m3 zero-item page resets.

**Net:** the logic is right and the persistence/migration story is careful and well-commented. The deliverable's weakness is that the budget is conceptually one thing implemented as a private method inside an orchestrator with a dishonest name. S1 (rename) and S2 (extract + pure-test) are the two changes that would most improve maintainability and are proportional to the risk. Everything past that is gilding.

---

## Summary table

| # | Sev | Location | One-liner |
|---|-----|----------|-----------|
| M1 | Major | Executor:10-14 | Concurrency comment overclaims; real shared-state is non-thread-safe `AdapterState`/`Metadata` dicts |
| M2 | Major | Executor:239 + :300 | Unbudgeted-wait `Clear` wipes backstop fields ⇒ interleaved waits reset the whole-collection backstop |
| M3 | Major | tests | No test locks the unbudgeted→rebudget backstop-survival invariant (would stay green through M2) |
| m1 | Minor | Executor:237 | `TotalDeferralCount` unbounded in theory; non-issue in practice (fallback caps it) |
| m2 | Minor | Budget:156 | Coordinate format unambiguous given non-negative int components; leave as-is |
| m3 | Minor | Budget:156 | Zero-net-item page still advances coordinate via page counter — handled; add a positive test |
| m4 | Minor | Budget:48,61 | Malformed coordinate silently coerced (fails open, but unlogged) |
| n1 | Nit | Executor:24 | Missing blank line before `ExecuteAsync` |
| n2 | Nit | Executor/Falcon | Duplicated SDK metadata-key consts (SDK keeps them private — acceptable) |
| n3 | Nit | Executor:425-441 | `attemptNumber` published twice and now carries renamed semantics |
| S1 | Struct | Snapshot/Budget | `AttemptCount`/`attemptCount` identifiers lie; rename in-memory (keep key string) |
| S2 | Struct | Executor | Extract `EvaluateBudget`+`BudgetEvaluation` into a pure `RecoveryBudgetEvaluator` |
| S3 | Struct | folders | Backstop constants live in executor, not anywhere named "budget"; consolidate by name (no new folder) |
| S4 | Struct | Budget | Mild over-broad responsibility; leave as-is except S2 |
| S5 | Struct | tests | Add direct evaluator tests for reason-precedence + the M2/m3 edges |
