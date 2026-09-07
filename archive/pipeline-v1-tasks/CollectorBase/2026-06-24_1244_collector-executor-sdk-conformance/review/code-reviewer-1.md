# Code Review — byte-sliced output paging (AssetsPage/FindingsPage + SliceByBytes)

Scope: the three changed files only — `CheckpointState.cs`, `Seams.cs` (`RunContext`),
`CollectorExecutorRunner.cs` (RunAsync resolve/restore, RunFetchStepAsync publish loop,
SliceByBytes, ToBytesAsync, WriteCheckpoint). Judged as written.

## Overall verdict
Largely correct and idiomatic. The serialize-once goal is met, byte accounting matches the
publisher exactly, and the monotonic page counters survive resume and for_each correctly. Two
real defects worth fixing: a mid-page partial-publish-failure leaves a persisted page counter
that double-counts on resume (major), and an oversized single record will throw out of the
publisher *outside* the loop's catch filter and crash the run (major). The rest are minor/nit.

## Findings

### MAJOR — partial publish failure persists an advanced page counter → resume re-emits / gap
`CollectorExecutorRunner.cs:336-348` (publish loop) + `:344-345` (early return on failure).
Within one page's emit, `SliceByBytes` can yield N slices. Each slice does
`++runCtx.FindingsPage/AssetsPage` then awaits a publish. If slice k succeeds and slice k+1
returns `!publishResult.Success`, the method returns `StepResult.Done(TransientFailure)` at
line 345 — but `WriteCheckpoint` (line 355) is never reached for this page, so the *position*
checkpoint is not updated. However the counter mutation (`++runCtx.FindingsPage`) already
happened in-memory and is lost on the failure return... except the partial-success path: the
terminal failure is handled upstream and the run ends. The concrete problem is the asymmetry
between in-memory counter advance and persisted counter: the last *persisted* `FindingsPage`
is from the prior page's `WriteCheckpoint`, while slices 1..k of the current page were already
published to files numbered above that persisted base. On resume the page re-runs from the
persisted base and re-publishes slices 1..k to the *same* file numbers (idempotent overwrite —
fine) but if the record set changed size, the slice boundaries can differ and produce a
different page count, leaving an orphaned higher-numbered file from the first attempt that is
never overwritten (gap/stale-file). This is the documented "deterministic idempotent overwrite"
assumption silently breaking when slicing is non-deterministic across attempts. Low blast radius
(needs a mid-page publish failure + changed payload), hence major not blocking, but the
"contiguous + gap-free" invariant in the comments is not actually guaranteed here.

### MAJOR — oversized single record throws outside the loop's catch filter → uncaught crash
`CollectorExecutorRunner.cs:338-343` + publisher `NdjsonUtf8BatchSession.cs:73-79`.
`SliceByBytes` deliberately lets a single record larger than `maxBytes` form its own slice
(comment: "never dropped"). But the publisher throws `record-too-large` (an egress exception,
not `AdapterHttpRequestFailedException`/`JsonException`/`XmlException`) when
`record.Length + 1 > MaxBytesPerPart`. The page loop's two catch filters (`:366`, `:373`) do
not match that exception type, and `PublishUtf8PageAsync` is awaited *inside* the loop, so the
throw propagates past the `try`, past the `for` loop in RunAsync (`:192-228`), and is only
caught by the outer `ServerSuggestedRetryDelayException`/`OperationCanceledException` handlers —
which also don't match. Net: the whole `RunAsync` throws, bypassing the partial-success
invariant (any records emitted on earlier pages are reported, but the adapter surfaces an
unhandled exception rather than a clean FailureResult/partial). The "never dropped" intent and
the publisher's hard cap are in direct conflict. Either pre-check slice size and emit a clean
terminal, or catch the egress exception in the publish path.

### MINOR — ThrottlingOptions.Resolve can throw at RunAsync:126, before the loop's safety net
`CollectorExecutorRunner.cs:126`. `Resolve` calls `ValidateOrThrow` and throws
`InvalidOperationException` if a DI-registered `ThrottlingOptions` has `MaxBytesPerBatch <= 0`.
That call sits after the registry/credential validation block but is not wrapped, so a
misconfigured host throws an uncaught exception instead of returning an `AdapterResult`. In
practice DI is host-controlled and the default is 50 MiB, so this is unlikely — but it is the
one place `MaxBytesPerBatch <= 0` could occur, and the code below (`SliceByBytes`) defends
against `maxBytes > 0` as if 0 were reachable, which is internally inconsistent: if Resolve
guarantees > 0, the `maxBytes > 0` guard in SliceByBytes is dead defensive code; if it does not,
this line crashes. Pick one model.

### MINOR — `maxBytes <= 0` in SliceByBytes silently disables slicing (single mega-page)
`CollectorExecutorRunner.cs:957`. The `&& maxBytes > 0` guard means a non-positive budget never
splits: every record lands in one slice, producing one giant page. Given the publisher rejects
any batch where a *record* exceeds `MaxBytesPerPart`, and the real `MaxBytesPerBatch` is always
>= 5 MiB (publisher floor) and > 0 (Resolve), this branch can only fire if a future caller
passes 0 directly. As written it fails open (one unbounded page) rather than closed. If the
guard exists to tolerate "unlimited", a named sentinel/comment would be clearer than `> 0`.

### MINOR — `StepResult.Completed(... emitNone ? 0 : page)` no longer reflects emitted pages
`CollectorExecutorRunner.cs:397`. The page metric returned to the flow accumulator is the
*pagination* `page` counter, not the number of output files actually published. With byte
slicing one pagination page can now emit multiple output pages (slices), so the `pages` total
in the final `SuccessResult` (`:243`, `:247`) undercounts published files. This was already the
pre-change behavior for the metric, but slicing widens the gap between "pages reported" and
"files written". Cosmetic (metric/telemetry only), but the success message
"N record(s) across M page(s)" is now misleading. Consider reporting slice count.

### NIT — `ToBytesAsync` `await Task.Yield()` per record is pure overhead
`CollectorExecutorRunner.cs:970-979`. The records are already materialized in a `List` in
memory; the async-enumerable wrapper exists only to satisfy the publisher's
`IAsyncEnumerable<ReadOnlyMemory<byte>>` signature. `await Task.Yield()` after every record
forces a continuation hop per record with no I/O benefit (it does not relieve memory pressure or
enable cancellation beyond the `ThrowIfCancellationRequested` already present). For large pages
this is thousands of needless scheduler bounces. Drop the `Task.Yield()`; the method can be a
plain `async IAsyncEnumerable` that only yields the buffered items (the compiler still satisfies
the async signature), or use a synchronous-to-async adapter without the per-item yield.

### NIT — `SliceByBytes` allocates a fresh `byte[]` per record via `Encoding.UTF8.GetBytes`
`CollectorExecutorRunner.cs:955`. Serialize-once is achieved (good), but
`r.ToJsonString()` then `GetBytes` does string→UTF8 with two allocations per record (the JSON
string + the byte array). For bounded-memory page-by-page processing this is acceptable, but
`JsonSerializer.SerializeToUtf8Bytes(r)` (or a pooled buffer) would halve allocations and skip
the intermediate UTF-16 string. Minor given page sizes are bounded; flagging for the hot path.

### Confirmed correct (no action)
- Serialize-once: `ToJsonString()` called exactly once per record in `SliceByBytes`;
  `ToBytesAsync` replays stored bytes — no double serialization. (`:955`, `:973`)
- Byte accounting matches the publisher: `b.Length + 1` for the newline equals the publisher's
  `normalizedUtf8Record.Length + 1`. (`:956` vs `NdjsonUtf8BatchSession.cs:71`)
- Page-counter monotonicity across multi-page steps and for_each items: counters live on the
  shared `runCtx` and are never reset in `RunFetchStepAsync` (only the pagination position is
  reseeded), so output numbering is contiguous across pages and items. (`:267-271`, `:340`)
- Resume restore: `runCtx.AssetsPage/FindingsPage` restored from persisted state under the same
  fingerprint+step-shape guard as the rest of the position. (`:162-163`)
- Checkpoint persistence: `WriteCheckpoint` writes both counters, and the
  state-before-AdvancePage ordering invariant is preserved (counters are part of the state
  written at `:355`, AdvancePage at `:356`). (`:630`)
- No concurrency concern: the flow loop and for_each are strictly sequential; `runCtx` is
  single-threaded throughout; the counters are not shared across tasks.
