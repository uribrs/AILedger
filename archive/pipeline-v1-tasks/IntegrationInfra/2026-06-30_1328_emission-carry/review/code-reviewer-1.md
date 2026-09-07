# Code Review — Emission concern (IntegrationInfra)

**Scope reviewed:** `src/IntegrationInfra/Emission/**` (root + `Ndjson/`, `Multipart/`, `Telemetry/`) and `tests/IntegrationInfra.Emission.Tests/**`.

**Stack:** C# / .NET 8. **Change type:** shared library / publish engine. **Risk level:** High — IO, streaming, multipart upload state machine, byte-budget math, resource disposal, large data.

**Calibration note:** This code was relocated largely verbatim from a battle-tested source; pre-existing style and the two-codepath duplication (string vs UTF-8) are intentional. The static entry point and assets/findings filename constants are deliberately retained. I have not flagged those as issues. Review focuses on runtime behavior, resource safety, concurrency, byte math, and test coverage.

---

## Severity-ranked findings

### High

**H1 — `DisposeAsync` does not abort an in-flight multipart upload (orphaned S3 upload + cost leak).**
`NdjsonBatchSession.DisposeAsync` / `NdjsonUtf8BatchSession.DisposeAsync` (lines 485–498 / 481–494) only dispose `_writer` and `_stream`. They never abort `_multipartSession`. The publisher (`PublishCoreAsync` / `PublishUtf8CoreAsync`) only ever calls `FlushIfHasDataAsync` on the happy path; on a non-final exception the multipart abort *is* attempted inside `FlushAsync`'s `catch`, but there are reachable paths where a multipart upload has been initiated and then the session is disposed **without** any abort:
- A cancellation/exception occurs in the record-producing `await foreach` loop (e.g., the source enumerable throws, or `cancellationToken` fires) *after* at least one intermediate part has already been uploaded (so `_multipartStarted == true` and `_records == 0` because the last action was a successful flush). `FlushIfHasDataAsync` is never reached; `await using` disposes the session; `DisposeAsync` does nothing about the open multipart upload.
- `OperationCanceledException` specifically bypasses even the outer `catch` filter (`when (ex is not OperationCanceledException)`), so on cancel there is no abort attempt anywhere.

**Impact:** Orphaned S3 multipart uploads accumulate incomplete-part storage charges indefinitely (until a lifecycle rule reaps them, if one exists). This is a real operational/cost leak on the cancellation path, which is not exotic for a long-running paged publish.
**Fix:** In `DisposeAsync`, if `_multipartStarted && _multipartSession != null` and the upload was never completed, call `TryAbortMultipartAsync(_multipartSession, CancellationToken.None)` before disposing the stream. Track a `_multipartCompleted` flag so a normally-completed upload is not double-aborted. Requires a local patch, not a refactor.

---

### Medium

**M1 — `(int)stream.Length` cast can silently corrupt very large multipart parts.**
`UploadMultipartPartAsync` (NdjsonBatchSession line 370, Utf8 line 361) builds `new ReadOnlyMemory<byte>(seg.Array!, seg.Offset, (int)stream.Length)`. `MaxBytesPerPart == MaxBytesPerBatch`, which is operator-configurable with no documented upper bound (`ValidateOrThrow` only checks `> 0`). If a deployment sets `MaxBytesPerBatch > int.MaxValue` (~2.1 GiB), the cast overflows to a negative or truncated length and either throws `ArgumentOutOfRangeException` or uploads a wrong-sized part. `MemoryStream` itself is also capped at `int.MaxValue` capacity, so a batch that large would fail in `MemoryStream` first — but the failure mode is an opaque crash rather than a clear "MaxBytesPerBatch too large" validation error.
**Impact:** Misconfiguration produces a confusing runtime failure deep in the upload path instead of a clear config rejection. Default (50 MiB) is safe; this only bites on extreme misconfig.
**Fix:** Add an upper-bound check in `ThrottlingOptions.ValidateOrThrow` (e.g., `MaxBytesPerBatch <= int.MaxValue`, or a saner cap like a few hundred MiB) so misconfiguration is rejected at resolution time with a clear message. Local patch.

**M2 — `MaxBufferedRecords` / `MaxBufferedBytes` caps are silently overridden by the 5 MiB multipart floor.**
Once a flush would start (or has started) multipart, `FlushAsync` returns early whenever `_stream.Length < MinPartSizeBytes` (NdjsonBatchSession lines 216–219, Utf8 215–218). That means a `MaxBufferedRecords`-triggered or `MaxBufferedBytes`-triggered flush below 5 MiB is a no-op, and the buffer keeps growing until it reaches either 5 MiB or `MaxBytesPerPart`. So the "peak memory in the publisher" guarantee these options advertise (BufferingOptions doc comment) is effectively floored at ~5 MiB regardless of how small the operator sets the caps.
**Impact:** An operator who sets `MaxBufferedBytes`/`MaxBufferedRecords` low to constrain peak memory does not get that behavior in the multipart path. Memory is still bounded (by `MaxBytesPerPart`), so this is correctness-of-contract / surprise, not a leak. The validation already forces `MaxBufferedBytes >= 5 MiB`, which partially acknowledges this, but the record cap has no such floor and the doc comment overpromises.
**Fix:** No code change strictly required, but the BufferingOptions XML doc should state that in multipart mode the effective floor is `MinPartSizeBytes`. If a true small-memory cap is ever needed it requires the single-vs-multipart strategy to be chosen up front, which is a reshape — defer. Documentation patch.

**M3 — Test coverage is thin relative to the risk surface.**
Only two test files exist: `ResultsRecordFormatterTests` (string formatter only) and `EmissionDefaultsTests`. There is **zero** coverage of the highest-risk code:
- The batch-session state machine (single vs multipart promotion, the 5 MiB skip logic, part-count limit, abort-on-failure, byte accounting).
- `Utf8ResultsRecordFormatter` (the UTF-8 minify/validate path) — completely untested, despite being a parallel authoritative validator.
- The UTF-8 publish path's "has newline => normalize, else parse-and-discard" branch in `PublishUtf8CoreAsync` (lines 223–252).
- Byte-budget boundary conditions (`recordBytes > MaxBytesPerPart`, `EnsureAppendWithinBatchLimit`).
- `AsyncEnumerableAdapters` cancellation/disposal.
- `ThrottlingAdapterExecutionContext` guard logic.
- Options `Resolve`/`FromConfiguration`/`ValidateOrThrow` precedence and rejection paths.

**Impact:** The most operationally dangerous logic (multipart state, abort, byte math) is unverified. Given H1 and M1 are both in that untested region, this gap is what let those defects survive. Sessions are `internal` but `InternalsVisibleTo` + a stub `IAdapterDataPublisher` would make them testable.
**Fix:** Add session-level tests with a fake `IAdapterDataPublisher` asserting: single-upload for sub-5MiB end flush; multipart promotion + part sequence + complete; abort called on publish failure; abort called on cancellation (covers H1); `record-too-large` and `batch-boundary` throws; UTF-8 formatter round-trip + invalid-JSON throw. Required before relying on this engine.

---

### Low

**L1 — `Utf8ResultsRecordFormatter` always allocates a fresh array even when already minified.**
`NormalizeToSingleLine` (line 29) always does `buffer.WrittenSpan.ToArray()`, allocating a copy on every record. In the UTF-8 publish path, this is only invoked for records that contain `\n`/`\r` (the common already-minified case is parse-and-discard, line 246), so the hot path avoids it. Acceptable as-is; note only because the string-path equivalent (`ResultsRecordFormatter`) re-serializes unconditionally. No action.

**L2 — `ResultsRecordFormatter.MinifiedJson` uses default `JsonSerializerOptions` escaping.**
`JsonSerializer.Serialize(doc.RootElement, MinifiedJson)` uses the default (HTML-safe) encoder, which escapes `<`, `>`, `&`, `+` as `\uXXXX`. The UTF-8 path (`Utf8JsonWriter` with default `JsonWriterOptions`) does the same, so the two paths are consistent and output stays valid JSON — just more verbose for those characters. Not a correctness bug (NDJSON readers parse `\uXXXX` fine). Flagging only because "minify" can be misread as byte-minimal; it is not. No action unless byte-exact minimality is a requirement.

**L3 — `IsUnderMemoryPressure()` calls `GC.GetGCMemoryInfo()` on every append under load.**
When `FlushOnMemoryLoadRatioOver` is configured, `GC.GetGCMemoryInfo()` is invoked in both `ShouldFlushBeforeAppend` and `ShouldFlushAfterAppend` for every record. It is not free (it snapshots GC state). For a high-record-count publish this is per-record overhead on a hot path. In practice the default leaves memory-pressure flushing disabled (null), so this is dormant. If enabled at scale, consider sampling (every N records or every M ms).
**Impact:** Minor CPU overhead, only when the feature is enabled. No correctness issue.

**L4 — `ResultsStreamWriter` flush-then-dispose ordering relies on `StreamWriter` semantics; partial-write window exists but is handled.**
In `FlushAsync` the `finally` disposes `writer` (a `StreamWriter` over the `MemoryStream`) after the stream has already been consumed by the publisher. `StreamWriter.DisposeAsync` will attempt a final flush to the underlying `MemoryStream`; since `leaveOpen: true` and the stream is disposed immediately after, this is benign. The explicit `await writer.FlushAsync()` before reading `stream.Length` (line 228) is correct and necessary. No action — noting that the ordering is load-bearing and correct.

---

### Nit

**N1 — Reason strings are stringly-typed control flow.** `FlushAsync` branches on `reason == "end" / "pre-append" / "post-append"` (magic strings shared with the telemetry tag). An enum would be safer against typos, but this is verbatim-carried and low-risk. No action.

**N2 — `EnsureBatch` two-line null-coalescing is order-dependent.** `_stream ??= ...; _writer ??= new ResultsStreamWriter(_stream, ...)` is correct because `_writer` is always created against the current `_stream`, and both are nulled together in `finally`. Fine; noting the coupling.

**N3 — Duplicated session implementations.** `NdjsonBatchSession` and `NdjsonUtf8BatchSession` are ~95% identical (state machine, flush logic, multipart, prefix normalization). Per the relocation charter this duplication is intentional and a unification belongs to the planned `ISink`/`StreamKind` reshape. No action now, but H1's fix must be applied to **both** files.

---

## Things checked and found correct

- **`AsyncEnumerableAdapters`**: cancellation is checked each `MoveNextAsync`; `DisposeAsync` disposes the inner enumerator. Synchronous `IEnumerator` wrapped as async is fine for the in-memory adapter use case. No leak.
- **Stream ownership**: `Utf8ResultsStreamWriter` and `ResultsStreamWriter` honor `leaveOpen`; sessions create writers with `leaveOpen: true` and own the `MemoryStream` lifecycle themselves. The "publisher may dispose the stream it consumes" hazard is explicitly handled by capturing `flushBytes` before publish and never touching the stream afterward (good defensive comment + code).
- **Byte accounting**: `_bytes += recordBytes` where `recordBytes` includes the `+1` newline in both paths; the `record-too-large` guard uses the same basis. The `(_bytes + nextRecordBytes) > MaxBytesPerPart` comparisons are `>` not `>=`, which is the correct boundary (a batch exactly at the cap is allowed). `long` arithmetic throughout; no realistic overflow in the accumulator (bounded by `MaxBytesPerPart`).
- **Multipart part-count limit**: `EnsureMultipartPartLimit` checks `< MaxPartCount` before incrementing, so part numbers stay in `[1, 10000]`. Correct.
- **`Utf8JsonWriter`** is disposed via `using` before `buffer.WrittenSpan` is read — flush ordering correct.
- **`GcMemorySnapshot.Capture`** guards the `Process.GetCurrentProcess()` call (can throw under restricted hosts) and divide-by-zero on `TotalAvailableMemoryBytes`. Telemetry-only, no GC forcing. Correct.
- **Options resolution precedence** (DI → IConfiguration section → env → default) is consistent across all four options types and validates on resolve.
- **`ThrottlingAdapterExecutionContext`** correctly only guards explicit JSON-lines content types and skips the byte check on non-seekable streams (logging instead of guessing). Reasonable.

---

## Summary

| Sev | ID | Issue |
|-----|----|-------|
| High | H1 | Multipart upload not aborted on cancellation/dispose → orphaned S3 uploads (cost leak). Fix in both session `DisposeAsync`. |
| Medium | M1 | `(int)stream.Length` cast + unbounded `MaxBytesPerBatch` → opaque failure on extreme misconfig. Add upper-bound validation. |
| Medium | M2 | Buffering caps silently floored at 5 MiB in multipart mode; doc overpromises. Documentation fix. |
| Medium | M3 | No tests for the batch sessions, UTF-8 formatter, or byte-budget/abort logic — the highest-risk code is unverified. |
| Low | L1–L4 | Allocation/escaping/GC-call-frequency/flush-ordering notes; no action required. |
| Nit | N1–N3 | Stringly-typed reasons, coupling, intentional duplication. |

**The one finding that should block merge of a publish engine is H1** (resource/cost leak on the cancellation path), and it is unguarded today because of M3 (no session tests). Everything else is misconfiguration-hardening, documentation, or stylistic.
