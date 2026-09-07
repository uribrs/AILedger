# Code Review — Runner decomposition (CollectorExecutorRunner → focused step/outcome helpers)

Reviewer: independent code-reviewer
Scope: the 11 files listed; ExecutionModels.cs / Seams.cs read for reference only.

## Verdict

**APPROVE WITH MINOR FIXES.** The extraction reads as a faithful, well-factored relocation. All three
extraction claims (a) no-behavior-change, (b) RunContext-by-reference, (c) partial-success-wins applied at
every site — are **confirmed**. No BLOCKER and no MAJOR correctness regressions found. Findings below are
MINOR/NIT: one pre-existing cross-step partial-count behavior worth a conscious sign-off, a couple of dead
usings, and small readability notes.

---

## Claim verification

### (a) No behavior change — CONFIRMED
Control flow, return paths, and exception handling read as a straight relocation:
- The conductor's step dispatch (`for_each` / `poll_until` / `poll_and_drain` / else→fetch), the
  seed-only-when-`i == resumeSeed.StepIndex` rule, and the inter-step checkpoint advance (watermark reset +
  fresh fetch position, only when `i + 1 < steps.Count`) are intact.
- The two conductor-level catches (`ServerSuggestedRetryDelayException` → 6h-capped `PartialResult`;
  `OperationCanceledException when ct.IsCancellationRequested` → `CancelledResult`) are unchanged.
- The fetch page-loop ordering is preserved exactly: send → parse → `fail_if` → captures → map →
  watermark → envelope → publish → paginate → **checkpoint BEFORE `AdvancePage`** → next-url non-advance
  guard → `HasMore` break. The "checkpoint before AdvancePage" invariant
  (`FetchStepExecutor.cs:89-90`) is respected.
- Exception filters are byte-for-byte equivalent across executors (`JsonException or XmlException` for
  parse; `AdapterHttpRequestFailedException || IsCircuitBreakerException` for transport; plus the
  `poll_and_drain` 404→`EXPORT_GONE` special-case ordered *before* the generic transport catch, which is
  correct — a more-specific filter must precede the general one).

### (b) RunContext carried BY REFERENCE — CONFIRMED
- `RunContext` is a `sealed class` (Seams.cs:29), not a struct.
- `StepExecutionScope` is a `record` but `RunCtx` is a `required RunContext` **reference** property
  (`StepExecutionScope.cs:34`); the record's value-equality/`with` semantics copy the *reference*, not the
  object. No `with`-clone of the scope appears anywhere, and even if one did it would share the same
  `RunCtx` instance.
- Every executor does `var runCtx = scope.RunCtx;` and mutates in place
  (`runCtx.Cursor/Offset/Page/Watermark/ScrollDepth`, `++runCtx.AssetsPage/FindingsPage`,
  `runCtx.Captures`, `runCtx.CaptureLists[...]`). These are the same instance the conductor created at
  `CollectorExecutorRunner.cs:81` and mutates at lines 89/157. No accidental copy. The checkpoint manager
  reads counters/captures off this same instance (`Write`, lines 101-108). Confirmed correct.

### (c) Partial-success-wins applied at every site — CONFIRMED
`StepOutcomeFactory.PartialOrElse(emitted, page, …)` is the single gate, and every terminal-failure site in
the fetch loop routes through it with the correct local `emitted`/`page`:
- `fail_if` body error (`:60`), parse failure (`:103`), publish non-success (`:205`), publish exception
  (`:216`), reset-without-watermark (`:238`). ✔
- The Shared decision path goes through `FailureResolutionRunner` → `StepOutcomeFactory.TerminalResult` /
  `PartialSuccess`, which applies the same `emitted > 0` rule in `PublishFailureAsync`,
  `CompletePartialAsync`, and the `Rethrow` arm. ✔
- `poll_until` / `poll_and_drain` poll-request failures correctly pass `emitted: 0` / `emitted` and do
  **not** need `PartialOrElse` because a poll step itself emits nothing; emitted records from drained items
  in `poll_and_drain` are carried in `emitted` and the failure goes through the Shared decision executor
  which applies the invariant. ✔
- No site bypasses the gate by constructing a bare `FailureResult` after emitting. The hard-failure
  literals that skip `PartialOrElse` (`POLL_TIMEOUT`, `TOO_MANY_ITEM_FAILURES`, `EXPORT_GONE`,
  `poll_until` `fail_if`/`until`-failure) are all in steps/branches where the step's own `emitted` is 0 (or,
  for `for_each`/`poll_and_drain` ratio failures, the partial-success concern is explicitly delegated to the
  conductor per the in-code comment). Consistent with the documented design.

---

## Findings

### MINOR-1 — Cross-step partial counts are not surfaced in a mid-flow terminal result (verify intent)
`CollectorExecutorRunner.RunAsync` accumulates `emitted/findings/pages` across steps, but when an
individual step returns `sr.Terminal` (defer/failure/partial), the conductor returns that terminal
**as-is** (`:150-151`) — the terminal's `data` carries only *that step's* counts, not the flow-level
accumulation from previously-completed steps. The inline comment at `:147-148` explains the accumulator is
not double-counting, which is true, but it does not address that a partial-success terminal from step N>0
under-reports records emitted by steps 0..N-1.

This is almost certainly a faithful relocation of prior behavior (the per-step executor has no visibility
into the flow accumulator), and for single-step flows (the common case) it is a non-issue. Flag only so it
is a *conscious* carry-over rather than an assumed regression. No action required if pre-existing.

### MINOR-2 — Dead usings
- `ForEachStepExecutor.cs:2` — `using Cymulate.Integration.Sdk.Models;` is used (`AdapterResult`), keep.
  Re-checked: actually used. No issue.
- `PollUntilStepExecutor.cs` — imports `Interpreter`? No. It imports `Resilience`, `Profile`,
  `Shared.Resilience`, `Shared.Session.TransportErrorHandling`, `Sdk.Events`, `Sdk.Models`. All appear
  used (`AdapterFailureDecision`, `AdapterFailureHandling`, `ErrorSeverity`, `FailureContext`,
  `HttpTransportFailureClassifier`, `AdapterResult`). No dead using.
- `PollAndDrainStepExecutor.cs:3` — `using …Interpreter;` is used (`JsonNav`). Keep.

  Net: no dead usings found across the reviewed set. (Claim of "flag dead usings" → none present.)

### MINOR-3 — `record struct ItemOutcome` / `StepResult` count plumbing across `PublishSlicesAsync` is correct
Verified there is no double-count across the `PublishSlicesAsync` boundary: `PublishOutcome` returns the
**updated** `emitted/findingsEmitted` totals (the method receives them by value, increments by
`slice.Count`, returns them), and the caller does a straight assignment `emitted = pub.Emitted` (not `+=`)
at `FetchStepExecutor.cs:80`. `slice.Count` is the per-page record count (each slice element is one
serialized record from `SliceByBytes`; `ToBytesAsync` re-yields the same elements 1:1), so the byte-slicing
does not distort record counts. On a publish failure mid-page, the returned totals reflect only
fully-published slices — correct, and consistent with the "checkpoint not yet advanced → idempotent
re-publish on resume" comment. No miscount.

### NIT-1 — `SliceByBytes` serializes each record once, kept as bytes; good
`record.ToJsonString()` happens exactly once per record and the bytes flow straight to publish — no
double-serialization cost. Worth noting positively; no change.

### NIT-2 — `FetchStepExecutor` indentation
The `try`/`catch` block inside `while (true)` is indented two spaces (`:43-115`) against the four-space
house style, making the page-loop body visually shallow. Cosmetic; does not affect correctness.

### NIT-3 — `runCtx.Cursor/Offset/Page` set twice at fetch entry
`FetchStepExecutor.cs:37` sets them from the seed, then `:45` (first loop iteration) sets the same values
again from the locals. Harmless (same values), but the line-37 assignment is effectively redundant with the
top-of-loop assignment. The line-38 `Watermark`/`ScrollDepth` reset is *not* redundant (those aren't reset
in the loop), so the block can't simply be deleted. Leave as-is; readability only.

### NIT-4 — `ConnectionProbe` probe-step selection
`ConnectionProbe.cs:38` falls back to `step.Step?.Request ?? step.Request` when `Steps[0].Request.Path` is
empty. Correct for `for_each`/`poll_and_drain`-first profiles. The double-fallback to `step.Request` when
`step.Step` is null leaves the original empty-path request, which then probes `{base_url}` with no path —
the comment acknowledges this. Acceptable as a best-effort probe; no change needed.

---

## Async / resource correctness
- All awaited calls use `.ConfigureAwait(false)` consistently in the new helpers (the conductor itself
  omits it on a couple of awaits — `RunAsync` line 108/130/etc. — but that matches the original adapter
  surface and library context; not introduced by this extraction).
- `await using var handle = provider.Create(...)` disposal scope is preserved in both the conductor and
  `ConnectionProbe`. The session handle lifetime spans the full step loop. Correct.
- `Task.Delay(..., ct)` in `ItemToleranceRunner`, `PollUntilStepExecutor`, `PollAndDrainStepExecutor`
  correctly threads the token; cancellation surfaces as `OperationCanceledException` and is caught at the
  conductor. The `PublishSlicesAsync` catch explicitly excludes `OperationCanceledException`
  (`when (ex is not OperationCanceledException)`) so a cancel during publish is not masked as
  `PUBLISH_FAILED` — correct.
- No `ref`/`out` of `RunContext`, no struct-by-value pitfalls (the value-type records `FetchSeed`,
  `ResumeSeed`, `ItemOutcome`, `PublishOutcome` are all immutable carriers returned by value — appropriate).

## Readability
The split is clean: each executor is single-responsibility, the `StepExecutionScope` bundle removes the
~15-parameter threading, and `StepRequestSender` de-duplicates the four copies of request-send. Doc-comments
on each new type accurately describe behavior. The conductor is now genuinely slim and readable.
