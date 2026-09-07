# Code Review — g3 runner terminal outcome

Scope: 3 changed files only. Build green; the two new tests pass (`Failed: 0, Passed: 2`).

## Verdict
No BLOCKERs. The RetryOptions wiring (A-M1) is **correct**, including the edge cases the prompt
flagged. The broad publish-loop catch (A-M2) is acceptable but slightly broader than needed, and the
new catch path is **not exercised by any test** — the headline test for change #2 hits a different
branch.

---

## A-M1 — RetryOptions externalization wiring: CORRECT

`CollectorExecutorSessionFactory.cs:33-35`
```
ExternalizeServerSuggestedDelays = retry.ServerDelayStrategy != "disabled",
MinExternalizedServerSuggestedDelay = TimeSpan.FromSeconds(Math.Max(0, retry.MaxInProcessDelaySeconds)),
MaxServerSuggestedDelay = TimeSpan.FromHours(24),
```

Verified against the decompiled package (`Cymulate.Http.Package.DefensiveToolkit` 2.0.2):

- **Externalization fires for `resolution.Delay > MinExternalizedServerSuggestedDelay`**, where
  `resolution.Delay` is first *clamped* to `MaxServerSuggestedDelay`
  (`ServerSuggestedDelayGate.Clamp` → `ServerSuggestedDelayExternalizer.ThrowIfConfigured`). So the
  effective externalize window is `(Min, Max]`, with anything above Max clamped *down* to Max and
  still externalized at the clamped value. Short delays (`<= Min`) stay in-process. This is exactly
  the intended "short in-process, long externalized" split. Correct.

- **Validation throw risk (Max <= Min):** `RetryOptions.Validate()` only throws when
  `ExternalizeServerSuggestedDelays && Min > Zero && Max <= Min`. With `Max = 24h` fixed, the only
  way to trip it is `MaxInProcessDelaySeconds >= 86400` (24h). `ResilienceSpec` default is 60s
  (`Profile.cs:48`); a profile would have to declare an in-process cap of ≥ 24h to break — absurd and
  self-inflicted. Not a real risk, but see MINOR-1 for a cheap guard.

- **`MaxInProcessDelaySeconds = 0`:** `Min = Zero`. Validation's `Min > Zero` guard is false →
  Validate is skipped (no throw). Externalizer fires for `delay > 0`, i.e. *every* server-suggested
  delay externalizes and none is honored in-process. That is the correct, coherent reading of
  "0s in-process budget." Fine.

- **`MaxInProcessDelaySeconds` huge (but < 24h):** e.g. 3600. `Min = 1h`, `Max = 24h`, `Max > Min` →
  no throw. Delays ≤ 1h honored in-process, (1h, 24h] externalized, > 24h clamped to 24h and
  externalized. Coherent.

- **`Math.Max(0, ...)`** correctly floors a negative declared value to 0, avoiding the
  `Min < Zero` validation throw. Good.

Test `LongServerSuggestedDelay_Externalized_NotSlept` confirms the live path: `Min = 1s`, 120s
Retry-After → clamped to 120s → `120s > 1s` → `ServerSuggestedRetryDelayException` →
runner catch (`CollectorExecutorRunner.cs:204-210`) → `PartialResult` → `PartialWaitRequired`. The
stopwatch (`< 10s`) proves no 120s in-process sleep. **Non-trivial**: with externalization off, the
retry policy would honor Retry-After 120s in-process and blow the budget.

---

## A-M2 — broad publish catch

`CollectorExecutorRunner.cs:349-370`. The slice-loop is wrapped:
```
catch (Exception ex) when (ex is not OperationCanceledException)
{
    return StepResult.Done(emitted > 0
        ? PartialSuccess(emitted, page, vendorName, flowName)
        : AdapterResult.TransientFailure($"Publishing failed: {ex.Message}", "PUBLISH_FAILED"));
}
```

- **OperationCanceledException exclusion is correct.** Excluding it lets a real cancel propagate to
  the outer `catch (OperationCanceledException) when (ct.IsCancellationRequested)` at line 211, which
  is the intended cancel handling. Note `TaskCanceledException : OperationCanceledException`, so an
  HTTP/timeout cancel is also excluded — correct, it should not be reported as `PUBLISH_FAILED`.

- **`emitted` scope is read correctly.** `emitted` here is the per-step-call local (line 245),
  incremented at line 361 only *after* a slice publishes successfully. So inside the catch,
  `emitted > 0` means "≥1 slice already pushed downstream this step call" → partial-success; else
  transient. This matches every sibling terminal exit (lines 280-282, 358-360, 391-393, 406-408,
  `TerminalResult`/`ExecuteDecisionAsync`). Consistent and correct.

- **Breadth concern (MINOR-2):** `catch (Exception ...)` will also swallow a genuine programming
  error thrown from inside the loop — an NRE, an `InvalidOperationException`, etc. — and re-label it
  `PUBLISH_FAILED` / partial-success. The loop body calls `SliceByBytes` (serialization),
  `ToBytesAsync`, `PublishFindings/AssetsUtf8PageAsync`, and `++runCtx.*Page`. None of those should
  NRE in normal operation, but if one ever did, the bug would be masked as a transient publish
  failure and (with `emitted > 0`) reported as *success-with-partial* — i.e. silently swallowed. This
  is a real-but-low-likelihood masking risk. The existing sibling catches are all *narrow*
  (`JsonException`/`XmlException`, `AdapterHttpRequestFailedException`/circuit-breaker) precisely to
  avoid this. The justification in the comment (lines 342-348) is that egress surfaces failure as a
  *thrown exception* from the streaming session and the exception type isn't known/narrow here —
  that's a fair reason the catch is broad, but it does come at the cost of also catching logic bugs.
  Acceptable given partial-success-wins is the design invariant, but worth a comment or an
  exclusion of obviously-bug exceptions if you want defense in depth. Not blocking.

- **Wrapping scope is correct.** The `try` opens at line 349, *after* record-building, watermark
  advance, and envelope application (lines 286-335), and closes at 371 before pagination/checkpoint
  (373+). It wraps only the publish slice-loop. The two outer per-iteration catches (388-394 parse,
  395-416 HTTP/circuit) remain intact and are unaffected. No interaction with the recovery-budget /
  `ExecuteDecisionAsync` path — the new catch returns a `StepResult.Done` directly and never reaches
  the failure-decision executor (that path is only for `AdapterHttpRequestFailedException`/circuit at
  line 395+). So the broad catch does **not** perturb recovery-budget gating.

---

## Test quality

- `LongServerSuggestedDelay_Externalized_NotSlept` — **good, proves behavior** (see A-M1).

- `PublishFailure_ZeroEmitted_IsTransientFailure` — proves the **result-branch** (line 357-360):
  `FailOnNthStreamPublisher(1)` returns `Success = false` on the first call (it never throws), so the
  code path taken is the `if (!publishResult.Success)` branch, *not* the new `catch`. The assertion
  (`ErrorCode == "PUBLISH_FAILED"`, status != Success) is correct and the test is non-trivial for
  that branch. But note the prompt frames this test as covering the new catch — it does not.

- **MAJOR (test gap, not a code defect): the new `catch` block (lines 365-370) is exercised by no
  test.** Neither publisher double throws from `PublishStreamAsync` — `FailOnNthStreamPublisher`
  returns a non-success *result*, and `CapturingAdapterDataPublisher` always succeeds. So the
  thrown-exception path, the `ex is not OperationCanceledException` filter, and the
  partial-success-vs-transient split *inside the catch* are all unverified. Since handling a *thrown*
  egress exception is the stated motivation for change #2 (comment, lines 344-348: "Egress surfaces
  failure ... more commonly ... a thrown exception ... otherwise a publish exception escapes every
  partial-success-aware catch"), the central new behavior is untested. Add a `ThrowOnNthStreamPublisher`
  (throws an arbitrary non-OCE, e.g. `IOException`) and assert: (a) zero-emitted → `PUBLISH_FAILED`
  transient; (b) ≥1-emitted (multi-slice via small `MaxBytesPerBatch`) → partial-success
  (`Status == Success`, `data["partial"] == true`). Both branches of the new catch then have
  coverage.

---

## MINOR / NIT

- **MINOR-1** `CollectorExecutorSessionFactory.cs:35` — `MaxServerSuggestedDelay = 24h` is a magic
  constant that silently caps the validity of `MaxInProcessDelaySeconds`. A profile declaring an
  in-process cap ≥ 24h would throw `ArgumentOutOfRangeException` from deep inside session
  construction (an opaque failure, not a clean `ValidationFailure`). Cheap guard: clamp Min to just
  under 24h, e.g. `MinExternalizedServerSuggestedDelay = TimeSpan.FromSeconds(Math.Clamp(retry.MaxInProcessDelaySeconds, 0, 86399))`,
  or document that the in-process cap must be < 24h. Low priority — no realistic profile hits it.

- **NIT** `CollectorExecutorRunner.cs:369` — the catch passes `exception: null` implicitly
  (`TransientFailure(msg, code)` overload) and folds `ex.Message` into the string, so the original
  exception/stack is dropped from the `AdapterResult`. Sibling transient exits that have the
  exception in hand (e.g. line 408) pass `ex`. Consider passing `ex` here too for diagnosability, or
  at minimum `_logger.LogError(ex, ...)` before returning — a swallowed programming error (MINOR-2)
  would otherwise leave no stack trace anywhere.

## Bottom line
Change is sound and the invariant alignment is correct. One MAJOR test gap (the new catch's
thrown-exception path is untested — and that path is the whole point of the change), plus two minor
hardening notes. No correctness blocker in the production code.
