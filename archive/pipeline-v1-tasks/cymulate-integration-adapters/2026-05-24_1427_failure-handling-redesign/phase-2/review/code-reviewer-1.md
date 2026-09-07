# Code-Reviewer-1 — Phase 2

## Verdict
REQUEST-CHANGES

## Findings

### Blockers
(none)

### Major

1. **`DefaultFailurePolicy`'s programmer-bug branch is unreachable in production via the runner's gating.**
   In `AdapterFlowRunner` the fresh-run catch (lines 644-649) and the resume catch (lines 1135-1140) both rethrow without invoking the policy when `ex is not OperationCanceledException && !transportRetryable && vendorVerdict is null`. A `NullReferenceException` / `ArgumentException` with no vendor classifier and no transport hit satisfies that predicate, so the policy is never consulted; the rethrow propagates into `UnknownFlowRetryPolicy`, which treats `IsUnknownRetryCandidate(nre) == true` and retries the bug three times before giving up.
   The xmldoc on `DefaultFailurePolicy` advertises a "programmer-bug" arm that "fires after cancellation, before transport check", and the unit tests assert it works in isolation, but the production wiring never reaches it for the exact exception types it was added to handle. Either (a) the gating must add `|| ProgrammerBugClassifier.IsBug(ex)` so programmer-bug exceptions reach the policy, or (b) the xmldoc must be made honest that the branch only fires when a vendor classifier or transport hit *also* matches (i.e. effectively never for the canonical NRE/ArgEx cases). The current state will mislead the next maintainer.

2. **Fresh-run `AttemptNumber` is off-by-one when used for delay-table lookup (latent).**
   `AdapterFlowRunner.RunFreshAsync` (lines 605-694) starts `attemptNumber = 0` and increments it *before* running, so when the policy is consulted after the first failure `FailureContext.AttemptNumber == 1`. `DefaultFailurePolicy` then calls `_planner.PickClassified(context.AttemptNumber)`, which returns `RetryDelays[1] == 5m` for the very first retry — skipping the 1m entry at `[0]`. The Polly-driven resume path correctly uses `args.AttemptNumber` starting at 0, so the two paths produce different first-retry delays for the same flow.
   `DefaultFailurePolicyTests.DecideAsync_ClassifierRetryableTrue_AndClassifiedRetryEnabled_ReturnsRetryInProcess` bakes the bug into a regression by asserting `~5m` for `AttemptNumber=1`. This is dormant in Phase 2 (the comment at line 600 notes "no fresh-run caller wires the policy to ever return RetryInProcess in Phase 1") but will silently mis-pace Phase 4 / Phase 5 retries. Fix: either pass `attemptNumber - 1` into `FailureContext` (matching Polly's 0-based retry index) or change `DelayPlanner.PickClassified` semantics to "1-based attempt".

3. **Runner gating allows `OperationCanceledException` to reach the policy on the resume path with no special treatment.**
   The gating predicate `ex is not OperationCanceledException && !transportRetryable && vendorVerdict is null` admits any `OperationCanceledException` into `policy.DecideAsync`. On the resume path, when the dispatch cancellation token *was not* the one that cancelled (e.g. a cancellation raised by collector internals, or a `TaskCanceledException` produced by an HTTP timeout that should not actually short-circuit), the policy unconditionally returns `CancelWithoutPublish` and the switch arm rethrows. The original behaviour at this layer was to also rethrow but only after checking `cancellationToken.IsCancellationRequested`. Consider checking `cancellationToken.IsCancellationRequested` before treating OCE as cancellation, or document explicitly that any OCE is treated as a user cancel.

4. **`DelayPlanner` xmldoc claims `Random.Shared` is used when null but understates the thread-safety risk for non-`Shared` callers.**
   The constructor xmldoc says `Random.Shared` is used when null. It does not warn that a caller-supplied `Random` instance is not thread-safe; concurrent calls from inside Polly retry callbacks (which can be invoked on any pool thread) would corrupt internal state and could produce identical jitter or skewed distributions. Given the planner is presented as a process-shared singleton and is referenced from `UnknownFlowRetryPolicy.DelayGenerator` (which Polly may invoke on any thread), the docs should at minimum say "If you pass a custom `Random`, you are responsible for thread-safety; prefer `Random.Shared`". Better: drop the `Random?` parameter and accept only a seed; construct an internal `ThreadLocal<Random>` from it for jitter. The current production callers go through `Random.Shared`, so this is a forward-compat trap, not a present-day bug.

### Minor

5. **`DelayPlanner.Default_HasNonZeroJitter` test is mis-named and self-contradictory.**
   The test is named `Default_HasNonZeroJitter` and the inline comment reads "Confirm it is not the same singleton object across constructions" — but the assertion is `DelayPlanner.Default.Should().BeSameAs(DelayPlanner.Default)`, which is the *opposite* of the comment, and nothing in the test actually exercises jitter. Either rename to `Default_IsSingleton` and drop the comment, or add a follow-up assertion that drives `PickUnknown(0)` against a fixed-seed planner and proves the result is different from the un-jittered delay.

6. **`ProgrammerBugClassifier.IsBug` does not unwrap `AggregateException` / `TargetInvocationException`.**
   Modern async paths unwrap automatically, so this is rare, but any code that calls `.Result` / `.Wait()` or invokes via reflection (some test scaffolds do) will produce a wrapper whose `InnerException` is the real bug, and `IsBug` will return `false`. Consider an explicit unwrap step, or a comment noting the deliberate scope limit.

7. **`DefaultFailurePolicy.DecideAsync` does not guard `context.Exception == null`.**
   The struct field is `required`, but `required` is a compile-time contract; reflection-built or default-struct callers (e.g. `default(FailureContext)`) will hand a null exception through and the policy will NPE on `context.Exception is OperationCanceledException` evaluation against null (returns false, no throw) then on `ProgrammerBugClassifier.IsBug(context.Exception)` which throws `ArgumentNullException`. The runner can never produce this state today, but a one-line `ArgumentNullException.ThrowIfNull(context.Exception)` at the top of `DecideAsync` would give a clearer error if it ever happens.

8. **Duplicated `FailFast` arm across fresh-run (line 770) and resume (line 1253).**
   The two switch arms are byte-identical apart from the publish-helper indirection (`PublishAndReturnFreshAsync` vs. inline `PublishErrorAndFailureCompletionAsync`). Both log `LogError` only when `ErrorCode == "PROGRAMMER_BUG"`, which couples log severity to a magic string. If a future failure policy reports `FailFast` for, say, an auth-revoked terminal state, neither path will log at `Error`. Either extract a `LogFailFast(handling)` helper that decides log level from `handling.Severity`, or document the convention.

9. **`DummyCollector.cs` line 216 uses `_context.Services?.GetService(...)` but `IAdapterExecutionContext.Services` is non-nullable.**
   Every concrete implementation declares `public IServiceProvider Services` (non-nullable). The `?.` makes it look like the property can be null and obscures the actual contract. Drop the `?.` or change the interface to `IServiceProvider?` if a null implementation is legitimately allowed.

10. **`DummyCollector.cs` `FlowRetryPipelineBuilder` lambda uses production-vs-test branching on whether the host has registered a `DelayPlanner`.**
    The comment is candid ("Test-injectable… Fixes C10"), but threading test-only seams through DI lookups in production code is a smell. A cleaner pattern: expose a `DelayPlanner` parameter on the constructor (default to `DelayPlanner.Default`) and let the test inject the zero-delay planner the same way it injects loggers. As written, every future maintainer reading the lambda has to load the entire C10 history to understand why the lookup is there.

11. **`ClassifiedRetryablePolicy.ClassifiedRetryableTriggerException` is not marked `[Serializable]` (legacy nicety only) and has a single ctor signature — fine for current use, but no `Trigger(string)` overload means callers must always supply an inner. Worth a one-line xmldoc warning since the resume path always wraps the original exception.**

12. **`DefaultFailurePolicy` xmldoc bullet for case 7 ("handling.IsRetryable && !ClassifiedRetryEnabled — byte-identical fallback") does not explain what happens when `HasWatermarkFloor=true` in addition.**
    Per the code, retryable + watermark-floor + classified-retry-disabled falls through to PublishAndExit, *not* PartialSuccess. That's a defensible choice but the xmldoc decision tree as written has no item that says "watermark floor is only honored when `!IsRetryable`". Add a one-liner so the reader doesn't have to reverse-engineer the if-ladder.

### Nit

13. **`DelayPlannerTests.Pick_JitterFractionTwentyPercent_AppliesSymmetricRelativeJitter` is technically still capable of flaking** if some future BCL change makes `new Random(42).NextDouble()` return exactly `0.5` (offset would be `0.0`, the assert `NotBe(baseDelay)` would fail). Probability against today's PRNG: zero. Robustify by asserting `Within(TimeSpan.FromTicks(1))` is *not* satisfied or by sampling 2-3 picks and checking at least one differs from baseline.

14. **`DelayPlanner` allocates a new `Random` indirectly per pipeline if `null` is passed — actually no, it uses `Random.Shared`. The xmldoc parameter line "thread-shared `<see cref="Random.Shared"/>`" should drop the word "thread-shared" (it's redundant) or replace with "the process-wide thread-safe `Random.Shared` instance".**

15. **`UnknownFlowRetryPolicy.CreatePipeline` overload — the 3-arg version delegates to 4-arg with `planner: null`. Consider `[OverloadResolutionPriority]` (`.NET 9`) or just inline `DelayPlanner.Default` in the 3-arg overload to avoid the null-then-coalesce hop. Microoptimisation, not a real concern.**

16. **`DefaultFailurePolicy.DecideAsync` synchronously returns `ValueTask`s wrapping pre-built records. Each call allocates a `FlowExceptionHandling` record + a `FailureAction` derived record. Hot path is not hot (policy is invoked only on exception), so allocation is fine — note for the record.**

17. **Magic string `"classified-retryable"` for `RetryInProcess.Reason` appears in `DefaultFailurePolicy.cs` line 106. Promote to a `const` on `FailureAction.RetryInProcess` or a static class to avoid drift if/when other reasons are added.**

### Observations

- The two-shape `RetryInProcess` design (fresh-run sleeps + continues; resume rethrows marker for outer Polly) is well documented and the `Delay` field is annotated "Informational on resume; honored on fresh-run" — but the log on resume (line 1183) emits `retry.Delay` while the outer Polly pipeline uses its own `DelayGenerator` that *also* calls `planner.PickClassified`. If a caller wires a *different* planner to the `BuildClassifiedRetryPipeline` factory than the one inside their `DefaultFailurePolicy`, the logged delay and the actual sleep will diverge. Worth a follow-up: have the runner log the *real* Polly delay (from the outer `OnRetry` callback) and the policy delay only as "policy.requestedDelay".
- Cyclic `using` between `Shared.Failure` (uses `UnknownFlowRetryPolicy.RetryDelays`) and `Shared.Session.TransportErrorHandling` (uses `DelayPlanner`) is safe today because both types' static-readonly field initializers complete before their static constructors observe the other type. But this is the kind of cross-namespace static coupling that bites people on the second order — e.g. someone moves the validation into a `static T Field = Validate(...)` and inadvertently introduces a re-entrant init. Consider moving the shared `RetryDelays` arrays to a `Shared.Failure` types-only file and have the policies pull from there.
- `DefaultFailurePolicy.Instance` constructs eagerly at type init. `BeforeFieldInit` semantics + the captured `DelayPlanner.Default` reference are both lazy at use, so first-touch cost is fine. No concern, just an observation.

## Per-File Notes

### DelayPlanner.cs
- Jitter math is correct: `(NextDouble * 2 - 1) * jitter` gives offset in `[-jitter, +jitter)`. Symmetric in expected value; very slight asymmetry at the boundaries (closed on lower, open on upper) — not material.
- `(long)(baseDelay.Ticks * multiplier)` truncates toward zero. Within long range for all plausible delays (≤ 30 min × 1.2 ≈ 21.6 trillion ticks).
- `attemptNumber < 0` throws; `attemptNumber == 0` works; clamps high. Correct.
- Public ctor + `Default` singleton both leak the same `_random` reference (`Random.Shared`) by default; safe.
- See Major #4 on `Random` thread-safety contract.

### ProgrammerBugClassifier.cs
- Switch-expression-with-`or`-pattern is idiomatic. Coverage matches the plan's D-P2-7 decision.
- See Minor #6 on `AggregateException` / `TargetInvocationException`.

### DefaultFailurePolicy.cs
- Decision tree matches the xmldoc bullet list.
- Programmer-bug branch is unreachable in production — see Major #1.
- See Minor #7 (null exception guard), #12 (watermark-floor doc), Nit #17 (magic string).

### UnknownFlowRetryPolicy.cs
- 4-arg overload preserves all source-compatibility (3-arg delegates). `DelayPlanner.Default` fallback is correct.
- `args.AttemptNumber + 1` formatting in `OnRetry` log is the 1-based attempt counter (correct for human readers).

### ClassifiedRetryablePolicy.cs
- Mirror of `UnknownFlowRetryPolicy`. Marker exception pattern is sound. xmldoc still references `CollectorResumeRunner` in two places — wait, the diff updated them to `AdapterFlowRunner`. Confirmed accurate.

### AdapterFlowRunner.cs (switch arms ~770 and ~1244)
- Fresh-run FailFast (line 770): publish-and-return is correct. `LogError` only fires for `PROGRAMMER_BUG` code (see Minor #8).
- Resume FailFast (line 1253): same shape; both arms publish via different helpers, which is a duplication risk. See Minor #8.
- `UnreachableException` default arm is the right safety net for the sealed-record hierarchy.
- See Major #1 (gating bypasses policy) and Major #2 (off-by-one on fresh-run AttemptNumber).

### DummyCollector.cs (~line 211)
- See Minor #9 (`Services?.` against non-nullable property) and Minor #10 (DI-based test seam).

### ProgrammerBugClassifierTests.cs
- Good per-type coverage. Null guard test is fine.
- Note: `ArgumentOutOfRangeException` and `ArgumentNullException` are caught by `is ArgumentException` — test validates that via positive cases. Good.

### DelayPlannerTests.cs
- Mostly deterministic. See Nit #13 on the `NotBe(baseDelay)` assert.
- `Default_HasNonZeroJitter` is misleading — see Minor #5.
- `Constructor_OutOfRangeJitterFraction_Throws` does not test exactly `0.0` or exactly `1.0` boundary — both should be valid (inclusive `[0,1]`). Add explicit `Constructor_ExactBoundary_DoesNotThrow` cases (`0.0`, `1.0`) to lock the contract.

### DefaultFailurePolicyTests.cs
- Branch-by-branch coverage is good.
- `DecideAsync_ClassifierRetryableTrue_AndClassifiedRetryEnabled_ReturnsRetryInProcess` asserts ~5m, which bakes in the off-by-one identified in Major #2. Either change the production code and update the test, or add a comment in the test explaining the 1-based attempt semantics.
- `DecideAsync_NoClassifierVerdict_NotTransport_NotCancel_FallbackPublishAndExitDefensive` — good defensive test; the body comment is accurate.
- No test exercises `IsRetryable=true + HasWatermarkFloor=true + ClassifiedRetryEnabled=false` (the "watermark floor not honored" edge). Add one to make the decision concrete.

### DummyCollectorTests.cs (DelayPlanner injection)
- Zero-delay planner is correctly wired through the mocked `IServiceProvider`. The comment block ("Phase 2 / C10 fix") is clear.
- The other test at line 514 (`AdapterFlowRunner_UnknownFlowException_RetriesAndThenSucceeds`) constructs a separate fast planner and wires it via `BuildFlowRetryPipeline` rather than via DI. Two test scaffolds for the same wiring; consider unifying.

## Summary
The Phase 2 abstractions (`DelayPlanner`, `ProgrammerBugClassifier`, `DefaultFailurePolicy`) are clean, well-documented in isolation, and well-tested at the unit level. The two material concerns are: (1) the programmer-bug branch is unreachable through the production runner because the policy-invocation gating rethrows before invoking the policy for the exact NRE/ArgumentException types the classifier was added to short-circuit — the xmldoc oversells what the wiring actually delivers; and (2) the fresh-run path passes a 1-based `AttemptNumber` to `DelayPlanner.PickClassified`, while the resume path passes the 0-based Polly attempt index, so the first-retry delay disagrees between paths and the unit test bakes the off-by-one into a regression. Both are dormant in Phase 2 but will surface as soon as Phase 4 wires fresh-run RetryInProcess. The remaining items are doc/style/test cleanups.
