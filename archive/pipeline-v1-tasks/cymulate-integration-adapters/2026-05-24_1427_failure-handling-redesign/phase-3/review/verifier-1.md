# Verifier-1 — Phase 3

## Verdict
PASS

## Success Criteria Coverage (SC1-SC10)

- **SC1 — `dotnet build` 0/0.** PASS. Solution build at HEAD: `Build succeeded. 0 Warning(s) 0 Error(s)` in 3.26s. No new warnings vs P2 baseline.
- **SC2 — DummyCollector.Test under 30s.** PASS. `dotnet test ... DummyCollector.Test.csproj --no-build`: **111/111 passed in 121 ms**. Matches executor's claim (127 ms). C10 still closed (no regression vs P2's ~21-min hang).
- **SC3 — RetryBudgetTests covers Load/Write/Clear + bad data + round-trip.** PASS. `RetryBudgetTests.cs:46-241` provides 15 tests:
  - Load: null checkpoint (line 47), missing keys (line 57), all-three keys parse (line 73), bad int (line 91), negative int (line 104), bad timestamp (line 117), empty strings ↔ absent (line 132).
  - Write: populates 3 keys (line 151), null code → empty string (line 164), negative attempt → throws (line 175), null context → throws (line 185).
  - Clear: overwrites with empty strings (line 192), null context → throws (line 205).
  - Round-trip Write→Load (line 212). Reserved-prefix regression (line 231).
- **SC4 — cap-exhaustion test.** PASS. `DefaultFailurePolicyTests.cs:214-242` asserts `AttemptNumber=4`, `maxRetriesAcrossRedeliveries=4`, `ClassifiedRetryEnabled=true` → `FailFast` with `ErrorCode="RETRY_BUDGET_EXHAUSTED"`, `IsRetryable=false`, Message contains "Retry budget exhausted" + "VENDOR_RETRY". Plus a one-below boundary test (`AttemptNumber=3` → still `RetryInProcess`) at line 245-268.
- **SC5 — ctor cap=0 throws, 1/100 don't.** PASS. `DefaultFailurePolicyTests.cs:270-288`: Theory with `[0,-1,101]` throws `ArgumentOutOfRangeException`; `[1,4,100]` does not throw. Implementation at `DefaultFailurePolicy.cs:85-91`.
- **SC6 — All Phase 2 tests still pass unchanged.** PASS. 111/111 in the project includes the P2 test classes (`DefaultFailurePolicyTests`, `DelayPlannerTests`, `ProgrammerBugClassifierTests`, `UnknownFlowRetryPolicyDelaysTests`, `ClassifiedRetryablePolicyTests`, `CurrentSequenceIdGuardTests`, `AdapterFlowRunnerFacadeTests`, `DummyCollectorTests`).
- **SC7 — Resume retry transition increments `_retry.attemptCount` + sets `_retry.lastErrorCode` + `_retry.lastAtUtc`.** PASS at the helper-level. Runner code at `AdapterFlowRunner.cs:1206-1224` calls `RetryBudget.Write(progressContext, attemptCount: failureContext.AttemptNumber + 1, retry.Handling.ErrorCode, DateTime.UtcNow, logger)` before throwing the marker. Test coverage uses `RetryBudgetTests.Write_PopulatesAllThreeKeys` (line 151) + `WriteThenLoad_RoundTrips` (line 212) with a real `AdapterProgressContext`. SC7 wording suggested a Moq'd context test; the executor used real `AdapterProgressContext.FromPlatformEvent` instead — equivalent or stronger coverage. No end-to-end runner integration test; per D-P3-17 that's deferred to P4.
- **SC8 — Resume success clears all three keys to empty strings.** PASS at the helper-level. Runner code at `AdapterFlowRunner.cs:1407` calls `RetryBudget.Clear(progressContext, logger)` immediately before `AdapterResult.SuccessResult(...)`. Test coverage: `RetryBudgetTests.Clear_OverwritesAllThreeKeysWithEmpty` (line 192).
- **SC9 — LoC budget within bounds.** PASS. New files: 156 (`RetryBudget.cs`) + 242 (`RetryBudgetTests.cs`) = 398 LoC new. Modified-file production deltas: DefaultFailurePolicy.cs +55, AdapterFlowRunner.cs +79 (these numbers include P2 patches D-P2-17/18 carried in the working tree, so the P3-only production delta is smaller). Executor's own measurement: P3 total ~548 net LoC. Production-only subset ~236 LoC, under the 250 stretch budget; combined under the 600 hard ceiling.
- **SC10 — No edits to out-of-scope files.** PASS. P3-introduced edits limited to the 5 declared files. The other working-tree modifications (`DelayPlanner.cs`, `FailureContext.cs`, `ProgrammerBugClassifier.cs`, `ClassifiedRetryablePolicy.cs`, `UnknownFlowRetryPolicy.cs`, `DelayPlannerTests.cs`, `DummyCollectorTests.cs`, `ProgrammerBugClassifierTests.cs`, `DummyCollector.cs`) are pre-P3 carry-ins from P1/P2/P0b — not introduced by Phase 3. `Directory.Packages.props` and `Directory.Build.props` untouched.

## D-P3-N Decision Honour Check

- **D-P3-1 (direct path)** — orchestration_plan.md confirms direct path; single executor cluster. ✓
- **D-P3-2 (LoC budget 250/600)** — Within both budgets. ✓
- **D-P3-3 (explicit AdvancePage flush)** — `RetryBudget.Write` calls `progressContext.AdvancePage(0, 0)` at line 113. `RetryBudget.Clear` at line 141. ✓
- **D-P3-4 (`_retry.` prefix collision-free)** — Pre-validated by W2 subagent grep. ✓
- **D-P3-5 (downstream consumer filter assumption)** — Documented as deferred follow-up in execution_notes lines 126-132. ✓ (A-P3-13 still OPEN.)
- **D-P3-6 (`RetryBudget` is static class)** — `public static class RetryBudget` at line 28. ✓
- **D-P3-7 (snapshot is readonly record struct)** — `public readonly record struct RetryBudgetSnapshot(int AttemptCount, string? LastErrorCode, DateTime? LastAtUtc)` at lines 153-156. ✓
- **D-P3-8 (cap = 4 default, range [1,100])** — `DefaultMaxRetriesAcrossRedeliveries = 4` (DefaultFailurePolicy.cs:61), range check `< 1 || > 100` (line 85). Cap consultation in `RetryInProcess` arm (line 134). Synthesises `FailFast` with `ErrorCode="RETRY_BUDGET_EXHAUSTED"`, `IsRetryable=false` (lines 136-141). ✓
- **D-P3-9 (InvariantCulture serialization)** — `attemptCount.ToString(CultureInfo.InvariantCulture)` line 107, `lastAtUtc?.ToString("O", CultureInfo.InvariantCulture)` line 111. Mirrored on Load (lines 51, 70). ✓
- **D-P3-10 (Load tolerates bad data)** — Bad int → default 0 + warn (line 53-58). Bad timestamp → null + warn (line 76-80). ✓
- **D-P3-11 (Clear writes empty strings)** — All three `SetState(*, string.Empty)` at lines 137-139. ✓
- **D-P3-12 (Write swallows AdvancePage exception)** — `try { … AdvancePage(0,0); } catch (Exception ex) { logger?.LogError(…); }` at lines 105-122. Symmetric in `Clear` at lines 135-146. ✓
- **D-P3-13 (Load called after RestoreProgress)** — `RestoreProgress(...)` at AdapterFlowRunner.cs:1117-1121, immediately followed by `RetryBudget.Load(checkpoint, logger)` at line 1126. ✓
- **D-P3-14 (no new FailureContext fields)** — `grep "LastErrorCode\|LastAtUtc" FailureContext.cs` returned zero hits. ✓
- **D-P3-15 (Write called BEFORE marker rethrow / Task.Delay with `+1`)** —
  - Resume arm: `AdapterFlowRunner.cs:1212` calls `Write` with `attemptCount: failureContext.AttemptNumber + 1` BEFORE throwing `ClassifiedRetryableTriggerException` at line 1245. ✓
  - Fresh-run arm: `AdapterFlowRunner.cs:855` calls `Write` with `attemptCount: attemptNumber` (the 1-based runner-local counter, which equals `failureContext.AttemptNumber + 1` per the `attemptNumber - 1` translation at line 672) BEFORE the `Task.Delay` at line 864. ✓
- **D-P3-16 (Clear called on success paths)** —
  - Fresh-run: `AdapterFlowRunner.cs:332` calls `Clear` after the inner pipeline returns no failure, before `PublishAsync(CompletionRequest(Success: true))`. ✓
  - Resume: `AdapterFlowRunner.cs:1407` calls `Clear` after `flowResult is null` check, before constructing `AdapterResult.SuccessResult`. ✓
- **D-P3-17 (test scaffolding in DummyCollector.Test)** — `RetryBudgetTests.cs` (new) + `DefaultFailurePolicyTests.cs` (2 new tests + 2 ctor-boundary tests, totalling 4 new tests in the file). ✓

## Out-of-Scope Verification

- `UnknownFlowRetryPolicy.IsUnknownRetryCandidate` body — **unchanged.** Confirmed via direct read of lines 108-128: identical to P2 state (`OperationCanceledException`, `HttpRequestException/TimeoutException`, transport-classified short-circuits unchanged).
- `ClassifiedRetryablePolicy.ShouldHandle` body — **unchanged.** `args.Outcome.Exception is ClassifiedRetryableTriggerException` predicate intact at line 99.
- `ClassifiedRetryableTriggerException` type — **unchanged.** Lines 57-61 of `ClassifiedRetryablePolicy.cs`.
- No collector file modified except the carry-in `DummyCollector.cs` test injection hook from P2. No production cloud-collector edits.
- `Directory.Packages.props`, `Directory.Build.props` — `git status` shows neither modified.
- `FailureContext` shape — no new fields (D-P3-14).
- `phase-0a/`, `phase-0b/`, `phase-1/`, `phase-2/`, `plan.md` — untouched by this phase (working with git status it shows no edits under those phase dirs).
- Polly pipeline files (`Polly`/pipeline-builder helpers) — untouched.

## Findings

- **(Low) SC7/SC8 lacks an end-to-end runner-level integration test.** The contract suggested a Moq'd `AdapterProgressContext` to spot-check that after `runAsync` throws on `RetryInProcess`, the `_retry.*` keys mutate as expected on the actual progress context held by the runner. The shipped tests exercise the `RetryBudget.Write` / `RetryBudget.Clear` helpers with real `AdapterProgressContext.FromPlatformEvent` instances (which is arguably stronger than mocking), but no test wires `ResumeCoreAsync` → runAsync-throws → assertion-on-progressContext-state. Per D-P3-17 ("light-touch in this phase; the heavy integration test arrives in P4"), this is deliberate scope. The runner call sites are inspected statically and read correctly. Risk: a future refactor that detaches Write/Clear from the runner's `RetryInProcess` arm or the success path could silently regress without test coverage. Recommend P4 add a focused runner-level test.
- **(Low) `Write_NegativeAttemptCount_Throws` swallows a behavioural ambiguity.** `RetryBudget.Write` validates `attemptCount < 0` BEFORE the `try/catch`, so it throws `ArgumentOutOfRangeException` outside the swallow-and-log block. The xmldoc + D-P3-12 say "swallows AdvancePage exception" — input validation correctly escapes the swallow. Not a defect; calling out for code-reviewer awareness.
- **(Info) Cap-exhaustion message includes raw error code, not vendor classification.** `DefaultFailurePolicy.cs:137` uses `handling.ErrorCode` directly. The contract suggested `context.VendorClassification?.ErrorCode ?? handling.ErrorCode`. Since `handling` is the unpacked `context.VendorClassification` (the branch is guarded by `if (context.VendorClassification is FlowExceptionHandling handling)` at line 127), the two are byte-identical here. No defect.

## Repair Recommendations

None required for this phase. The Low findings are scope-deferred items (D-P3-17 + carry-over P2 OCE guard), not P3 repairs.

## Accepted Risks / Open

- **A-P3-13** — platform-team consumer check status: OPEN. Downstream consumers may not filter `_retry.*` keys; flagged in execution_notes.md:126-132. Recommended: operator follow-up before P4 ship.
- **Cancellation OCE guard** — carried from P2 Major #3. Resume path's outer catch (`AdapterFlowRunner.cs:1415-1418`) still does not differentiate caller-cancellation from internal `OperationCanceledException`. Not P3 scope; flagged for separate review.

---

PASS / 0 critical, 0 major, 2 low, 1 info / Phase 3 ships clean: SC1-SC10 all pass, all 17 D-P3-N honoured, no out-of-scope edits.
