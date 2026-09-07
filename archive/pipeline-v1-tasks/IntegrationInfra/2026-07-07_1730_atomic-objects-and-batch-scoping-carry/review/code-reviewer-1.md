# Code Review — atomic-objects-and-batch-scoping carry

**Change type:** shared library / infrastructure (NuGet package, single assembly) used by long-running K8s data-collection adapters.
**Risk level:** High — persistence boundary, multipart upload state machine, checkpoint/idempotency, resume/failure publication paths, memory profile on hot egress path.
**Stack:** C# / .NET 8.

## Overall assessment

This is a careful, well-reasoned rewrite. The multipart complete/abort state machine is now correct on the axes that matter: abort fires **exactly once** (`_multipartAborted` latch set before the await, plus `_multipartCompleted` guard), never after a completed upload, and the catch→dispose handoff no-ops the second abort. The `FinalizeAsync` split fixes a real latent data-loss bug (a stream ending on a post-append/memory-pressure flush left the buffer empty but the upload uncommitted; the old `FlushIfHasDataAsync` would silently skip completion and dispose would then abort a would-be-successful object). The `BatchScopedStorage` helper is correctly homed in `Envelopes/Common` (not `Emission`), so the new `Conducting → Envelopes.Common` edges do **not** deepen the tracked `Conducting → Emission` coupling, and there is no dependency cycle. `RestoreBase` has a genuine no-op guarantee for never-scoped runs, and all seven publication/resume call sites invoke it before the event is published/snapshotted. The removal of the two hard size gates is intentional, documented, and matched by the `SoftRecordWarningBytes` warning; peak buffer memory remains bounded by `~MaxBytesPerPart + one record` because the sub-5-MiB skip-flush guard only defers flushes below the S3 minimum, never above it.

The one finding I would not merge without a conscious decision is the **test coverage asymmetry between the two twins**: every critical multipart/abort/finalize/cancellation test exercises only the UTF-8 session, leaving the byte-identical string session's rewritten state machine unverified. Given the whole point of the change is data-safety in code that is hand-duplicated across two twins, that gap is worth closing. Everything else is minor or observational.

I did not run the test suite (review only); findings are from source reading and cross-file tracing.

---

## Findings

### Major

**M1 — Critical multipart state-machine paths are tested only on the UTF-8 twin, not the string twin.**
- **Files:** `tests/IntegrationInfra.Emission.Tests/AtomicStreamedObjectsTests.cs` vs `src/IntegrationInfra/Emission/Ndjson/NdjsonBatchSession.cs`
- **Problem:** `NdjsonBatchSession` (string) and `NdjsonUtf8BatchSession` (UTF-8) are hand-maintained twins carrying identical, freshly-rewritten complete/abort/finalize logic. The new tests drive the data-loss-critical behaviors — multipart escalation, mid-stream part failure → abort-once, cancellation → abort, dispose-without-complete → abort, complete-failure → abort-once, and the M1 post-append-flush finalization regression — **exclusively through `ResultsBatchPublisher.PublishUtf8Async`** (the UTF-8 session). The string session is exercised only by two small single-PUT byte-identical assertions (`SmallCall_String_*`, and the small-payload `BatchScopedStorageTests`), which never start a multipart upload. The string session's `FinalizeAsync` / `CompleteStartedMultipartAsync` / `TryAbortMultipartAsync` / `CommitIncomplete` paths have **zero direct coverage**.
- **Impact:** Twin drift is a known hazard in this codebase. If the string twin regresses (e.g., a future edit reintroduces a residual-buffer dependency in `FinalizeAsync`, or drops the abort latch), the suite stays green while collectors on the string path silently orphan uploads or report false success. This is exactly the failure class the change exists to prevent, on half the surface.
- **Fix (local patch, not refactor):** Parameterize (or duplicate) at least the abort-exactly-once, complete-failure, and post-append-flush finalization tests across both twins — e.g. run them through `PublishAsync` as well as `PublishUtf8Async`. Consciously accepting the gap is defensible only if the string path is slated for removal.

### Minor

**m1 — `CommitIncomplete` post-`FinalizeAsync` check is unreachable in normal flow (dead-but-defensive).**
- **Files:** `Emission/ResultsBatchPublisher.cs:131` and `:269`; `Emission/Ndjson/NdjsonBatchSession.cs:96`, `NdjsonUtf8BatchSession.cs:98`
- **Problem:** After a *successful* `FinalizeAsync`, `CommitIncomplete` (`_multipartStarted && !_multipartCompleted`) is always `false`: the `_records>0` branch completes via `FlushAsync("end")`, the `_records==0 && started` branch completes via `CompleteStartedMultipartAsync`, and any failure in either re-throws before the check is reached. The guard can therefore never trip on a returning path.
- **Impact:** None functionally — it is genuine belt-and-suspenders and the comment says so. Only a mild future-reader cost (someone may hunt for the reachable case).
- **Fix:** Optional. Keep it (cheap insurance against future `FinalizeAsync` edits) or drop it; no action required. Do not expand it.

**m2 — Metadata-key constant not applied at three remaining `"storageUrl"` literal sites.**
- **Files:** `Job/AdapterRunEnvelopeParser.cs:100`; `Envelopes/Common/AdapterRunMetadata.cs:25`; `Envelopes/Common/AdapterEventMetadata.cs:28`
- **Problem:** The change centralizes the key as `BatchScopedStorage.StorageUrlMetadataKey` and swaps the progress-context access points (factory, both sessions, entrypoint) to it, but the envelope parser and the two JSON DTO `[JsonPropertyName("storageUrl")]` attributes still use the bare literal.
- **Impact:** Cosmetic/consistency only — they resolve to the same `"storageUrl"` string. The DTO attributes are arguably better left as literals (a DTO depending on `BatchScopedStorage` is odd); the parser could adopt the const.
- **Fix:** Optional. Low priority.

### Observations (no action required)

**O1 — Single oversized record is fully materialized in memory (inherent, now un-gated).**
`NdjsonBatchSession`/`Utf8` buffer a whole record before any flush, and a single record never spans multipart parts (you cannot split a JSON line). With the hard `record-too-large` gate removed, a pathological multi-GiB record buffers entirely in a `MemoryStream` and single-PUTs. On a memory-limited pod this is an OOMKill risk, not data corruption (no upload starts for a lone record, so nothing is orphaned). This is inherent to NDJSON, the soft warning flags it, and the README already tracks the S3 `AbortIncompleteMultipartUpload` lifecycle mitigation. Realistic per-record vendor payloads keep this cold. Noted only because the removed gate previously masked it.

**O2 — `BatchScopedStorage.StripBatchSegment` has a documented false-positive window.**
A legitimate run storage URL literally ending in `/batch_` + exactly six digits would be wrongly stripped as a synthetic batch tail. The code comment argues the deterministic tail "can only originate here," which holds because run roots are GUIDs/timestamps in practice. The assumption is explicit and tested (`BeginPage_DoesNotStripNonBatchTails`). Fine as an intentional tradeoff; flagged so it stays on the radar if run-root naming ever changes.

**O3 — Cancellation-path abort reliability confirmed.**
`MultipartUploadOptions.AbortWithCallerCancellationToken` defaults to `false`, so `TryAbortMultipartAsync` aborts with `CancellationToken.None` even on the flush-catch cancellation path, and `DisposeAsync` passes `None` unconditionally. Abort under cancellation is therefore reliable by default; the exactly-once latch does not strand an un-aborted upload. (Behavior preserved from the pre-refactor code.)

**O4 — Twin logic is consistent.**
Line-by-line, the two sessions' new members (`FinalizeAsync`, `CommitIncomplete`, `CompleteStartedMultipartAsync`, `WarnIfRecordExceedsSoftThreshold`, `TryAbortMultipartAsync`, `DisposeAsync`, the `_multipartCompleted`/`_multipartAborted` flags) are identical in control flow; the only difference is the pre-existing `AppendRecordAsync` signature (string passes `recordBytes`, UTF-8 computes it). No divergence introduced by this change — see M1 for the test-side asymmetry.

**O5 — README matches code.** The Emission README's "Stable Output Rules" (one publish = one atomic object, size-unbounded, exactly-once abort, commit only after Complete, `ThrottlingAdapterExecutionContext` still hard-caps the direct `PublishAsync` route) and the `BatchScopedStorage` ordering contract accurately describe the implemented behavior.

---

## Severity counts

- Blocker: 0
- Major: 1
- Minor: 2
- Observation: 5

### Majors (one line each)
- **M1:** The rewritten multipart complete/abort/finalize state machine is tested only on the UTF-8 twin; the byte-identical string session's data-loss-critical paths have no direct coverage, so silent twin drift would pass CI.
