# Code-Reviewer-1 — Phase 3

## Verdict
REQUEST-CHANGES

## Findings

### Blockers

1. **`RetryBudget.Write` / `Clear` use `AdvancePage(0, 0)` as a flush primitive, but the SDK's documented contract for `AdvancePage` says it "Advances the page counter and adds to processed items count" — i.e. it is not a no-op when the deltas are zero.**
   The SDK xmldoc for `Cymulate.Integration.Sdk.Contracts.AdapterProgressContext.AdvancePage(Int32,Int32)` (verified in
   `~/.nuget/packages/cymulate.integration.sdk/3.0.0/lib/net8.0/Cymulate.Integration.Sdk.xml` line 367-374) reads "Advances the page counter and adds to processed items count". Nothing in the documented surface indicates that passing `(0, 0)` is a special "flush-only" mode; the parameter doc only requires non-negativity. The `RetryBudget` xmldoc (RetryBudget.cs lines 17-20) and the class summary directly contradict this: "calls `AdapterProgressContext.AdvancePage` with zero arguments to trigger persistence without affecting progress counters".

   If the SDK behaviour matches its public contract (i.e. `CurrentPage` is incremented even on `(0, 0)`), every `RetryBudget.Write` and `RetryBudget.Clear` call bumps `CurrentPage`. On the resume path, `RunResumeAsync` at line 1117-1121 re-positions the resume scan from `checkpoint.CurrentPage` — so a single retry transition would cause the next resume to start one page later than the page the retry happened on, **silently dropping a page of vendor data**.

   The new test `RetryBudgetTests` never asserts the value of `pc.CurrentPage`, `pc.ProcessedItems`, or `pc.ProcessedFindings` after `Write` / `Clear`, so this side-effect is not exercised. Before merging, do at least one of the following:
   - Add a test that calls `Write`, then asserts `pc.CurrentPage == 1` (the post-`FromPlatformEvent` initial value) and `pc.ProcessedItems == 0`. If that assertion passes, the SDK does in fact treat `(0,0)` as a no-op for counters, and the only fix needed is to revise the SDK xmldoc and update the call sites with a comment that pins this empirically verified behaviour. If it fails, the runner is currently emitting a page-skip bug per retry.
   - Or stop using `AdvancePage` as the flush primitive. The SDK exposes `ReportHeartbeat(string)` (xmldoc line 375-385: "extends progress-based timeouts without affecting the checkpoint state") as the explicit "I'm alive, no progress" call. If a separate "flush state to checkpoint" hook exists, prefer that. If not, document why no other hook is available and consider raising an SDK feature request rather than relying on observed-but-undocumented behaviour of `(0,0)`.
   - Or, if the team has out-of-band confirmation from the SDK owner that `(0,0)` is a sanctioned flush convention, capture that confirmation in a comment on `RetryBudget.cs` and add a regression test that fails if the SDK ever changes that behaviour.

### Major

2. **`RetryBudget.Write` is invoked on the resume path **before** the marker exception is thrown — but the runner's gating uses the same `AdvancePage` call to trigger `OnCheckpoint` persistence. If `OnCheckpoint` is async (or itself throws), the marker rethrow that follows can lose the persisted budget or the rethrow path order matters.**
   In `AdapterFlowRunner.cs` line 1212-1224 the resume `case FailureAction.RetryInProcess` calls `RetryBudget.Write(...)` (which internally calls `AdvancePage(0,0)`) and then `throw new ClassifiedRetryableTriggerException(...)`. The `OnCheckpoint` callback is "Optional callback invoked after each page advance to persist a checkpoint" (SDK xmldoc line 230-234). The callback is host-supplied and may dispatch RabbitMQ acks, write to durable storage, etc.

   - If `OnCheckpoint` throws synchronously, the exception will propagate up *instead of* the marker exception. The outer classified-retry Polly pipeline will see whatever the OnCheckpoint sink produced, not the marker, and the cadence logic breaks.
   - If `OnCheckpoint` is fire-and-forget async (the SDK's typical pattern is `Func<Task>`), the persist may not complete before the marker is rethrown, so a worker that dies between the `AdvancePage` call and the actual flush will resume with a stale budget.
   - `RetryBudget.Write` swallows exceptions from the flush per its own doc — but that swallow only catches `AdvancePage` exceptions raised *synchronously* (e.g., from the OnCheckpoint callback's awaited task). It does not wait on a backgrounded persist.

   Same concern applies on the fresh-run side at line 855-860 (write before sleep) and on the success-clear sites at lines 332 and 1407. The success Clear writes empty strings then publishes a completion event; if the Clear is not durable before the platform acks completion, a redelivery (e.g., due to message ack timing) sees a non-empty `_retry.*` from the prior run.

   At minimum, document the expected `OnCheckpoint` durability semantics that the budget relies on, and either (a) make `Write` / `Clear` return a `ValueTask` and `await` the flush, or (b) add a comment on each call site noting that the host's `OnCheckpoint` is expected to flush synchronously before `AdvancePage` returns.

3. **Resume FailFast / PublishAndExit / PartialSuccess arms do not clear the persistent budget, so a `RETRY_BUDGET_EXHAUSTED` decision becomes a permanent terminal state on the checkpoint.**
   On the resume path, `RetryBudget.Clear` is only called on the success branch at line 1407. The PublishAndExit (line 1290), PartialSuccess (line 1250), and FailFast (line 1309) arms all publish a failure result and return without clearing.

   For `RETRY_BUDGET_EXHAUSTED` specifically: the policy returns FailFast because the budget is saturated. The runner publishes a failure. The bus may or may not redeliver depending on `IsRetryable` (which is `false` for `RETRY_BUDGET_EXHAUSTED`). But for a `PROGRAMMER_BUG` FailFast on a checkpoint that *already had* `_retry.*` keys from prior retries (e.g., 3 successful retries, then a 4th attempt produces a programmer-bug exception), the budget keys remain. If the platform decides to redeliver (e.g., a `RetryableException` from somewhere else later in the pipeline), the next resume loads `AttemptCount=3`, and even a single new vendor-retryable failure will jump to attempt 4 of 4 instead of starting fresh.

   The redesign claims the budget should span redeliveries — agreed for the retryable case. But for terminal FailFast / PartialSuccess outcomes, the run is "done" from the policy's perspective; the budget should be cleared so a future redelivery (manual or automated replay) doesn't inherit it. Either:
   - Call `RetryBudget.Clear(progressContext, logger)` in the FailFast and PartialSuccess arms (and probably PublishAndExit too), or
   - Document the policy that the budget is intentionally sticky on failure and an operator action is required to recover.

   The current behaviour is neither documented nor tested.

4. **Cap test `DecideAsync_BelowRetryBudget_StillReturnsRetryInProcess` proves the boundary but does not exercise the `RetryInProcess.Delay` value at that boundary — and the budget snapshot in production passes `LastErrorCode` and `LastAtUtc` that the policy does not consume but the test never confirms get round-tripped through the policy.**
   The test asserts only `action.Should().BeOfType<FailureAction.RetryInProcess>()`. With `AttemptNumber=3` and the planner's classified delay array of length 4, `_planner.PickClassified(3) = RetryDelays[3] = 30m`. If the planner clamps incorrectly at the boundary or if a future refactor swaps the planner's clamp logic to "no retry at index == length - 1", this test passes silently. Add `retry.Delay.Should().BeGreaterThan(TimeSpan.Zero)` or a tighter range assertion.

   Separately, the runner uses `retryBudget.LastErrorCode` and `retryBudget.LastAtUtc` only for diagnostic write-back (line 1221-1224), not for the policy decision. That's fine, but the public surface area of `RetryBudgetSnapshot` carries those two fields with no test asserting that the runner passes them through correctly on the marker-rethrow boundary. Worth a single integration-style test that writes a budget, simulates a resume, asserts the snapshot still carries the correct LastErrorCode/LastAtUtc after a Write inside the loop.

### Minor

5. **`RetryBudget.Write` and `Clear` swallow exceptions but log at `Error` level — this masks production issues that would otherwise surface.**
   Per D-P3-12 the design is "best-effort; flow continues". Fine. But if the OnCheckpoint sink starts throwing (a broken durable store, a permission change, a serialization regression), the operator sees only a logger.LogError that buries the issue inside an otherwise-successful flow log. Consider:
   - Adding a metric counter on swallowed write/clear failures so dashboards can alert on it.
   - Including the `progressContext.CorrelationId` or `EventId` in the log so a single failed write is correlatable.

6. **`RetryBudget` const keys are public but the encoded value formats are not documented as a public contract.**
   `_retry.attemptCount` is an integer in `InvariantCulture`. `_retry.lastErrorCode` is a free-text string. `_retry.lastAtUtc` is `DateTime.ToString("O", InvariantCulture)`. The `Load` method tolerates absence and malformed values, but external readers of the checkpoint (dashboards, manual ops, future tools) will hit these keys and need to know the encoding. Either:
   - Document the encoding in the xmldoc on each const, or
   - Mark the consts `internal` and expose `static bool TryGetAttemptCount(AdapterCheckpoint, out int)` accessors that own the parse contract.

7. **`RetryBudget.Write` argument validation rejects negative `attemptCount` but accepts `int.MaxValue`. The cap is 100 by `DefaultFailurePolicy`, so a `MaxValue` write is impossible via the policy, but a misuse from a future caller would silently persist a huge number.**
   Minor defensive measure: also reject `attemptCount > 1_000_000` (or similar) with a clear `ArgumentOutOfRangeException`. The intent is "this is a small counter; a bug that drives it to billions is a logic error worth surfacing".

8. **`RetryBudgetSnapshot` is a `readonly record struct`, which is appropriate (small, immutable, value-typed). But `LastErrorCode` is a `string?` reference field — the struct is not strictly value-only, and equality compares strings by content (correct, but worth noting). No action needed; flagging only because the team has been moving toward `readonly record struct` for hot-path snapshots.**

9. **Test `Load_BadTimestamp_DefaultsToNull` populates only `AttemptCountKey` and `LastAtUtcKey`. `LastErrorCode` is implicitly missing. The assertion does not check `LastErrorCode.Should().BeNull()`, but a future change that flips the missing-key default behaviour would not be caught.**

10. **The string literal `"PROGRAMMER_BUG"` is used in three places: `DefaultFailurePolicy.cs` line 113, and `AdapterFlowRunner.cs` lines 787 and 1311. Phase 2 review flagged similar magic strings; this Phase 3 patch adds two new occurrences in the runner's FailFast arms. The same pattern applies to `"RETRY_BUDGET_EXHAUSTED"` (one occurrence in the policy, zero in the runner — but a future "log RETRY_BUDGET_EXHAUSTED at Error too" would mean a fourth occurrence). Promote to a `const` on `FailureAction.FailFast` or on a new `FailureErrorCodes` static class.**

11. **`AdapterFlowRunner.cs` line 1218-1224 builds a new `RetryBudgetSnapshot` immediately after calling `RetryBudget.Write`. The two operations duplicate the new state (in-memory snapshot, persisted state). If `Write` fails (swallowed), the in-memory snapshot still gets bumped, so the next iteration's `failureContext.AttemptNumber` is higher than the persisted count. After a worker crash + restart, the resume re-loads the persisted (lower) count and may overrun the cap by one. Consider:**
   - Update the in-memory snapshot only if Write succeeded (return a bool from Write), or
   - Document the divergence is intentional ("in-memory ahead of disk is the safe direction — we may under-count retries across crashes, never over-count").

### Nit

12. **`RetryBudget.cs` xmldoc for `Load` says "Old checkpoints round-trip cleanly (D-P3-10)" — but the test `WriteThenLoad_RoundTrips` exercises only the new path. The "old checkpoints" forward-compat is implicit (Load tolerates missing keys), not actively tested. Add a one-liner: a checkpoint built with realistic adapter state but no `_retry.*` keys, Load it, assert default snapshot.**
   Actually this is exactly what `Load_CheckpointWithoutRetryKeys_ReturnsDefaultSnapshot` does. The xmldoc is fine; ignore.

13. **`RetryBudgetSnapshot` lacks an xmldoc on its `AttemptCount` property. The summary says "Immutable snapshot of the persistent retry budget" — useful but doesn't say AttemptCount is 0-based. Mirror the constant's xmldoc on the property.**

14. **`DefaultFailurePolicy.cs` line 47-51: range check `[1, 100]` is reasonable but the upper bound is arbitrary. A comment or named constant (`MaxAllowedRetryCap = 100`) would be clearer than the inline literal. Tests at lines 277-285 of `DefaultFailurePolicyTests` lock the boundary, so the literal is at least anchored.**

15. **`DefaultFailurePolicyTests.DefaultFailurePolicy_PublicCtor_AcceptsCustomPlanner` is a near-tautological test — it just constructs the policy and asserts non-null. The constructor is exercised transitively by every other test in the file. Either remove it or add an assertion that the constructed policy actually uses the supplied planner (e.g., a follow-up `DecideAsync` call that verifies the delay).**

16. **`RetryBudgetTests` line 80 builds an `AdapterCheckpoint` with `CreatedAtUtc = DateTime.UtcNow` — deterministic-test-wise this is fine because the test doesn't assert on the timestamp, but using a fixed timestamp would future-proof against any time-sensitive validators added later.**

17. **`AdapterFlowRunner.cs` line 1218 — the comment "Refresh the in-memory snapshot so subsequent loop iterations within the same resume see the bumped count" is helpful but glosses over the fact that "subsequent loop iterations" here means Polly re-invoking `runInner`. The closure captures `retryBudget` by reference (lifted to a closure class field), so the assignment is visible across re-invocations. Worth one extra sentence so a future maintainer doesn't try to "improve" this by making `retryBudget` `readonly`.**

### Observations

- The Phase 3 diff cleanly addresses the two Phase 2 majors via the post-execution patch comments. The runner gating predicate now invokes the policy for programmer bugs, and the fresh-run `AttemptNumber` is now passed as `attemptNumber - 1`. The wiring is internally consistent.
- `DefaultMaxRetriesAcrossRedeliveries = 4` matches `ClassifiedRetryablePolicy.MaxRetries = 4`. xmldoc claim is verified.
- The two-shape RetryInProcess design persists: fresh-run sleeps then continues (now also writes the budget before sleeping), resume rethrows the marker (now also writes the budget before rethrowing). The two paths use the same `RetryBudget.Write` primitive, so the budget semantics are uniform. Good.
- The `RetryBudget` xmldoc's reserved-prefix verification claim ("D-P3-4, the reserved prefix `_retry.` has been verified collision-free") is an assertion the tests don't verify dynamically. A regression-guard test that iterates over all known checkpoint helpers and asserts none of their keys start with `_retry.` would lock the claim, but that's gold-plating; the test `Write_ReservedKeys_Match_TheReservedPrefix` is sufficient documentation of the convention.
- The `RetryBudgetSnapshot` `readonly record struct` is appropriate — small, immutable, used as a value type on the runner's call sites.
- The new test `DecideAsync_RetryBudgetExhausted_ReturnsFailFastWithRetryBudgetExhausted` constructs a full `FailureContext` rather than using the helper `Build`. That's because `Build`'s default `AttemptNumber=1` (Phase 2 off-by-one carry-over) wouldn't reach the cap boundary cleanly. Worth a comment in the test explaining why it skips the helper.
- The Phase 3 changes do not touch `ProgrammerBugClassifier`, `DelayPlanner`, or `UnknownFlowRetryPolicy`. Coverage for those remains as Phase 2 left it.

## Per-File Notes

### RetryBudget.cs (new)
- API surface: three public const keys + Load/Write/Clear static methods + `RetryBudgetSnapshot` record struct. Surface is well-shaped for the static-class pattern. See Blocker #1 (`AdvancePage(0,0)` contract risk) and Minor #6 (encoded value contract).
- Defensive programming: `ArgumentNullException.ThrowIfNull` on `progressContext`; non-negative check on `attemptCount`; Load tolerates null checkpoint, missing keys, empty strings, malformed values. Good.
- Exception swallow + log convention: see Minor #5.
- xmldoc cross-references `D-P3-3`, `D-P3-4`, `D-P3-10`, `D-P3-11`, `D-P3-12`, `D-P3-15` — pinning the decisions in code is good practice.
- `CultureInfo.InvariantCulture` is used correctly for both `int.TryParse` and `DateTime.ToString("O", ...)`. `DateTimeStyles.RoundtripKind` matches the `"O"` write format.

### DefaultFailurePolicy.cs (modified)
- New ctor parameter `maxRetriesAcrossRedeliveries` with range check `[1, 100]`. Default matches `ClassifiedRetryablePolicy.MaxRetries`. Good.
- Cap branch placement: inside the vendor-classifier verdict block, gated on `IsRetryable && ClassifiedRetryEnabled`. Correct — the cap is meaningful only for retry decisions.
- xmldoc updated to describe the cap, with `D-P3-8` reference.
- The cap branch uses `context.AttemptNumber >= _maxRetriesAcrossRedeliveries`. With `AttemptNumber` as the 0-based count of retries done, `>= cap` is "the cap-th retry would be the next one, which is one too many". This is the correct boundary semantics for the comment "matches `ClassifiedRetryablePolicy.MaxRetries`".
- See Minor #10 (magic string `RETRY_BUDGET_EXHAUSTED`).

### AdapterFlowRunner.cs (modified)
- Fresh-run success Clear (line 332): correct location, after `tryExecuteFlowAsync` returns success but before publishing the completion event. The placement means a Clear failure is logged but doesn't block completion.
- Fresh-run RetryInProcess Write (line 855-860): correct location, before the optional `Task.Delay`. `attemptNumber` is the runner-local 1-based "attempts done so far"; writing it is "retries-initiated-so-far". Consistent with the resume path.
- Resume Load (line 1126): correct — runs once at resume entry, before the inner pipeline starts.
- Resume RetryInProcess Write + snapshot refresh (line 1212-1224): see Major #2 (durability ordering) and Minor #11 (in-memory ahead of disk).
- Resume success Clear (line 1407): correct location, only on the no-flowResult path. Inside the same `try` block as the inner pipeline, so a Clear failure is logged but doesn't prevent the success result construction.
- FailFast arms (line 787, 1311): `LogError` only fires for the magic string `"PROGRAMMER_BUG"`. See Minor #10.
- Resume failure arms (FailFast / PartialSuccess / PublishAndExit) do NOT call Clear: see Major #3.

### RetryBudgetTests.cs (new)
- Real `AdapterProgressContext` via `FromPlatformEvent` is the right choice — proves the SetState/GetState interplay matches production. See Blocker #1 about the missing CurrentPage / ProcessedItems / ProcessedFindings assertions.
- `BuildCheckpoint` helper sensible; uses `DateTime.UtcNow` — see Nit #16.
- Round-trip test exercises only Write→snapshot→Load, not the full progress-context→OnCheckpoint→AdapterCheckpoint→Load path (test contexts have no `OnCheckpoint` callback wired). Worth a comment in the test class xmldoc noting that the OnCheckpoint side-effect is not exercised here.
- `Write_NegativeAttemptCount_Throws` correctly asserts on the constructor-side validation. `Write_NullProgressContext_Throws` correctly asserts on `ArgumentNullException.ThrowIfNull`.
- `Write_ReservedKeys_Match_TheReservedPrefix` is a good regression guard.

### DefaultFailurePolicyTests.cs (modified — bottom additions)
- `DecideAsync_RetryBudgetExhausted_ReturnsFailFastWithRetryBudgetExhausted`: full FailureContext, explicit `AttemptNumber=4`, explicit policy with `maxRetriesAcrossRedeliveries=4`. Correct. See Observation about why the `Build` helper isn't reused.
- `DecideAsync_BelowRetryBudget_StillReturnsRetryInProcess`: boundary at one-below-cap. See Major #4 (delay value not asserted).
- `DefaultFailurePolicy_OutOfRangeRetryCap_Throws` `[Theory]` covers 0, -1, 101 — good.
- `DefaultFailurePolicy_BoundaryRetryCap_DoesNotThrow` `[Theory]` covers 1, 4, 100 — good.
- `DefaultFailurePolicy_PublicCtor_AcceptsCustomPlanner` is near-tautological — see Nit #15.

## Summary
The Phase 3 abstractions (`RetryBudget`, `DefaultFailurePolicy` cap branch, runner load/write/clear wiring) are coherent and the Phase 2 majors are visibly addressed in the post-execution patches. The main concern is that `RetryBudget.Write` / `Clear` rely on `AdapterProgressContext.AdvancePage(0, 0)` as a flush primitive, but the SDK's documented contract for `AdvancePage` says it "Advances the page counter and adds to processed items count" — not a no-op when arguments are zero. The new tests do not assert that `CurrentPage` is unchanged after a Write/Clear, so a page-skip side-effect would slip through silently. Secondary concerns are: the resume failure arms (FailFast / PartialSuccess / PublishAndExit) do not Clear the persistent budget, so terminal failures leave a saturated budget on the checkpoint with no documented recovery path; the durability ordering between `RetryBudget.Write` and the marker rethrow is not pinned to a synchronous `OnCheckpoint` flush; and a cap-boundary test omits the delay-value assertion that would catch a future clamp regression. The remaining items are doc/style/test-tightening cleanups.
