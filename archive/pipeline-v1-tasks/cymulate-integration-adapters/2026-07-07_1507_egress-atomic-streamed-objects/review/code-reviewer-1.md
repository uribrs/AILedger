# Code Review — Egress atomic streamed objects (oversized-record guard removal + multipart abort semantics)

**Change type:** shared library / infrastructure (NDJSON egress used by every collector).
**Risk level:** High — shared data plane, streaming, retries, persistence boundary, large data.
**Stack:** C# / .NET 8, async streaming, S3-style multipart via `IAdapterDataPublisher`.

Files reviewed (working tree):
`NdjsonBatchSession.cs`, `NdjsonUtf8BatchSession.cs`, `NdjsonOptions.cs`, `ThrottlingOptions.cs`, `ResultsBatchPublisher.cs`, `CollectorGlobalDefaults.cs`, `README.md`, `README.Publishing.md`, `ResultsBatchPublisherConstraintsTests.cs`, new `AtomicStreamedObjectsTests.cs`. Context read read-only: `ThrottlingAdapterExecutionContext.cs`, `MultipartPartPlanner` references, SDK contracts.

---

## Blockers

None.

---

## Majors

### M1 — Multipart object can return `Success` without ever calling `Complete`, then gets aborted on dispose → silent data loss

**Where:** `NdjsonBatchSession.cs` — `FlushIfHasDataAsync` (lines 91–97), `Complete` reachable only at the `isEnd` branch (lines 263–266), new abort in `DisposeAsync` (line 496); surfaced by `ResultsBatchPublisher.cs` return path (lines 259–276). Identical in `NdjsonUtf8BatchSession.cs` (`FlushIfHasDataAsync` ~line 93, `DisposeAsync` ~line 496).

**Problem:** `CompleteMultipartUploadAsync` is called *only* from the `reason == "end"` flush, and the end flush only runs when `_records > 0` (`FlushIfHasDataAsync` guards on `_records > 0`). A **post-append flush** (`ShouldFlushAfterAppend`, fired under sustained memory pressure or an elapsed `FlushInterval` once `_bytes >= MinPartSizeBytes`) uploads a part and resets `_records = 0`. If that post-append flush is the *last* action before the source enumeration ends, then:

1. `FlushIfHasDataAsync` sees `_records == 0` → no end flush → `Complete` is never called.
2. `ResultsBatchPublisher` sees `PublishedRecords > 0` → returns `PublishResult.Ok(FirstLocation ?? "", PublishedRecords, UploadedBytes)` with **`Success == true`** (note `FirstLocation` is still `null`, because intermediate parts synthesize an empty `StorageLocation` at line 270 and only `Complete` populates it).
3. `await using` then disposes the session → new `DisposeAsync` abort fires (`_multipartCompleted` is false) → **all uploaded parts are aborted**; no object materializes in storage.

Net effect: the publisher reports success, the collector keys its checkpoint on that success and advances its watermark, and the object silently never exists. This directly contradicts the contract this very change documents ("the checkpoint/commit signal … is produced once per call, only after `Complete`/single-PUT, never on failure").

**Impact:** Silent data loss for a page/object, timing-dependent. Realistically reachable on long scans under the memory-pressure tier (a designed condition), where post-append flushes are frequent; the loss occurs whenever a scan ends immediately after one.

**Provenance (be fair):** The `Complete`-only-on-end logic and the `_records > 0` end gate are **pre-existing** (unchanged context). The change does not worsen the *data* outcome (the object never materialized before either — it just lingered as an incomplete upload rather than being aborted). But the change (a) formalizes an atomicity/commit contract that this path violates, (b) adds the deterministic abort that finalizes the loss, and (c) reworks exactly this state machine without covering the case. It should be fixed here.

**Recommended fix:** Make finalization independent of residual buffer state: if a multipart upload was started (`_multipartStarted && !_multipartCompleted`) then `Complete` must run at end regardless of `_records`. Simplest local patch — have `FlushIfHasDataAsync` (or a dedicated `FinalizeAsync`) force the end flush when `_multipartStarted` even if `_records == 0`, and make the `_records == 0` early-return in `FlushAsync` still complete a started-but-uncompleted multipart. Alternatively, gate the `PublishResult.Ok` success on `_multipartStarted ⇒ _multipartCompleted`. Local patch, not a refactor.

---

## Minors

### m1 — Docs now over-claim "no object-size cap" while `ThrottlingAdapterExecutionContext` still hard-caps on `MaxBytesPerBatch`

**Where:** `README.Publishing.md` / `README.md` (the "size-unbounded / not an object-size cap" statements) vs unmodified `ThrottlingAdapterExecutionContext.cs:64–76`.

**Problem:** The READMEs now state, for shared egress broadly, that "there is no per-record or per-object size limit" and "`MaxBytesPerBatch` … is not an object-size cap." But `ThrottlingAdapterExecutionContext.ValidateWithinLimitsOrThrow` still throws `NDJSON batch exceeds MaxBytesPerBatch` for any seekable JSON-lines `StreamBatchRequest` over `MaxBytesPerBatch`, and its test still asserts that throw (`ThrottlingAdapterExecutionContextTests.cs:119`). That decorator guards a *different* publish route (`IAdapterExecutionContext.PublishAsync(StreamBatchRequest)`), which the `ResultsBatchPublisher`/`IAdapterDataPublisher` path bypasses — so it is not a functional regression for the reviewed path. It is a documentation-consistency gap: a reader taking the new "size-unbounded" doctrine at face value will be surprised by that still-live cap.

**Impact:** Documentation inaccuracy / future confusion; no runtime break on the reviewed path.

**Recommended fix:** Either scope the README claims to the `ResultsBatchPublisher` streamed path explicitly, or reconcile the decorator (align its role wording with "part discipline" or note it guards the legacy `StreamBatchRequest` route only). Doc-level; local.

### m2 — Test gap: abort when `Complete` itself throws is never exercised

**Where:** `AtomicStreamedObjectsTests.cs` `RecordingPublisher` — `FailOnPartNumber` only fails *part* uploads; `CompleteMultipartUploadAsync` never throws or returns `Success=false`.

**Problem:** The failure path where the final `Complete` throws/returns failure (→ `catch` → `TryAbortMultipartAsync`, with `_multipartCompleted` still false so abort *should* fire) is untested. This is a named concern for this change and a plausible real failure (S3 CompleteMultipartUpload can fail after parts succeed).

**Recommended fix:** Add a `FailOnComplete` toggle to `RecordingPublisher` and assert `AbortCalls == 1`, `CompleteCalls == 1` (attempted), and the call throws. Test-only.

### m3 — Test gap: the M1 loss path (post-append flush ends the stream) is uncovered

**Where:** `AtomicStreamedObjectsTests.cs`.

**Problem:** No test drives a multipart run that ends exactly on a post-append/memory-pressure flush with an empty residual buffer. This is precisely the M1 scenario; adding it would both prove the bug and guard the fix. `MemoryPressureOptions.ForceFlushAlwaysForTesting` (referenced in the session) plus a record stream that ends on a flushed part boundary can make it deterministic.

**Recommended fix:** Add a test asserting that such a run still calls `Complete` exactly once and does **not** abort. Test-only (pairs with M1).

---

## Observations (no action required)

- **O1 — Bounded, intentional memory tradeoff.** With the two size gates gone, in-buffer memory is bounded by roughly `max(MaxBytesPerBatch, largest single record + sub-5 MiB remainder)`, not by object size — each flush disposes the stream and resets `_bytes`, so memory does not accumulate across a large multipart object. The only new spike vector is a single vendor record far larger than `MaxBytesPerBatch`; the new `SoftRecordWarningBytes` (24 MiB default) logs it. Reasonable and documented. Part-list growth is bounded by `EnsureMultipartPartLimit` (`MaxPartCount`), which still throws — no unbounded part accumulation.
- **O2 — Abort state machine is otherwise correct.** `TryAbortMultipartAsync` sets `_multipartAborted = true` before the await and guards on `null || _multipartAborted || _multipartCompleted`, so: no double-abort (catch-then-dispose second call is a no-op — verified by `MidStreamPartFailure` asserting `AbortCalls == 1`), no abort-after-complete (`GrowingCall` asserts `AbortCalls == 0` after a successful complete + dispose), and single-PUT/empty sessions never abort (null-session guard). `_multipartCompleted` is set only after the `Complete` success check, so it faithfully means "object committed."
- **O3 — Concurrency assumption holds.** Sessions are driven from a single `await foreach` and disposed only after it unwinds (`await using`); there is no concurrent dispose-during-append, so the non-volatile bool flags are safe. If a future caller ever shares a session across concurrent appends this breaks, but nothing in-tree does.
- **O4 — Twin parity confirmed.** `NdjsonBatchSession` and `NdjsonUtf8BatchSession` are byte-for-byte equivalent across all changed logic (completed/aborted flags, `WarnIfRecordExceedsSoftThreshold`, `TryAbortMultipartAsync`, `DisposeAsync` abort). No divergence.
- **O5 — Tests assert behavior, not shape.** New tests check mock call counts, byte-identity (`AssembleMultipart` == single-PUT), ascending contiguous part numbers, `>= 5 MiB` non-final parts, scoped paths, and warn-firing. Deterministic (byte thresholds force multipart; no wall-clock/`FlushInterval`/network deps). Good quality.

---

## Overall assessment

The abort/complete state machine introduced here is, in isolation, well-constructed: the `_multipartAborted`/`_multipartCompleted` flags give correct exactly-once abort semantics across the catch → dispose sequence, the twins stay in lockstep, and the tests lock behavior rather than implementation. Removing the two hard size gates in favor of size-unbounded streamed objects is a coherent, bounded-memory tradeoff and is documented. The one serious issue is M1: finalization is tied to residual buffer state (`_records > 0`), so a multipart run that ends on a post-append/memory-pressure flush never calls `Complete`, returns `Success` anyway, and then aborts its own parts on dispose — a silent data-loss window that contradicts the change's own newly written commit contract and is untested. It is pre-existing logic rather than a fresh regression, but it lives in the exact state machine this change reworks and formalizes, so it should be fixed (and tested) here. The doc-vs-`ThrottlingAdapterExecutionContext` inconsistency and the two failure-path test gaps are minor cleanups.

**Counts:** Blockers 0 · Majors 1 · Minors 3 · Observations 5.
