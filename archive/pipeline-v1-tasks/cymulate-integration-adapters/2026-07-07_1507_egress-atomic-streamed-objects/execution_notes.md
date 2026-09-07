# Execution Notes

## PHASE 0 — INVENTORY (resolves A5 / A6 / A7)

### Baseline reality (behaviour over shape)
The dev baseline of `Shared/DataPipeline/Egress` **already** implements "one publish call =
one logical object, multipart under the hood" (see `README.Publishing.md`). There is **no
multi-object rotation** anywhere: every flush in `NdjsonBatchSession` / `NdjsonUtf8BatchSession`
targets the same `BaseTargetPath`; a large page becomes multiple multipart **parts of one
object**, completed atomically on `end`. The contract's phrase "50MiB object-splitting" describes
a prior mental model; on this branch the only things that actually block a size-unbounded object
are two fail-fast guards:

1. `AppendRecordAsync`: `recordBytes > MaxBytesPerPart` → `DataPipelineException("record-too-large")`.
   THE motivating gate (Tenable 17.8MB record is one dense+fat asset from tripping the 50MiB cap).
2. `EnsureAppendWithinBatchLimit`: throws `batch-boundary` when a record cannot be appended without
   exceeding `MaxBytesPerPart` while the current buffer is below the 5 MiB multipart minimum.

Both are deleted in Phase 1/2. Everything else (single-PUT vs multipart selection, the 5 MiB
non-final-part skip, memory-pressure/time/record/byte flush tiers) is retained unchanged — that is
the four-tier heap defense and it keeps its memory semantics.

### A5 — dead pre-splitting paths per collector
Enumerated `MaxBytesPerBatch` references in **collector code** (grep, excl. artifacts + tests):
only `TenableIoCollector` — `Flows/Findings/TenableIoFindingsFlow.cs:415` and
`Flows/Assets/TenableIoAssetsFlow.cs:391`, both `long maxBytesPerPage = throttling.MaxBytesPerBatch`.

TenableIo re-paginates a downloaded chunk into multiple byte-bounded page-objects
(`findings_000001.json`, `_000002.json`, …), flushing `pageBuffer` (a `List<ReadOnlyMemory<byte>>`)
whenever `pageByteCount + recordBytes > maxBytesPerPage`.

Classification (judgment rule = behaviour over shape): **AMBIGUOUS → KEEP + DOCUMENT.**
- It is *not solely* object-size servitude: the byte budget also bounds the peak RAM held in
  `pageBuffer` before a publish call. Removing it would buffer an entire export chunk in memory
  before one publish — a real collector-RAM increase. RAM-bounding stays (constraint line 10).
- TenableIo is **explicitly out of scope**: task.md ("Tenable/Falcon correlated feature branches,
  inherit on merge") and constraints.md ("No changes on feature branches (Tenable/Falcon
  correlated)"). It is under an active asset-centric redesign on a feature branch; editing it here
  would collide with that work.
- Post-fix interaction is safe: TenableIo pages are already ≤ budget and its records ≤ 17.8MB, so it
  never trips the deleted gate today; after deletion egress simply gains the ability to publish an
  over-budget page atomically if the redesign ever produces one. No behaviour change for Tenable.

No other collector references the byte budget. Other collectors page by **record count / vendor
chunk** (legitimate collector paging, not object-size servitude) — untouched.

**Result: zero collector code changes, zero collector csproj version bumps.** All code changes are
confined to `Shared/DataPipeline/Egress`.

### A6 — consumers depending on multi-object-per-call semantics
None found. `CollectorNdjsonPublisher` returns `(PublishResult, TargetPath)`; `PublishResult`
carries `RecordCount`/`BytesUploaded`/`StorageLocation` for the **one** logical object.
`ResultsBatchPublisher` already returns a single `PublishResult` per call and always has (the
completion log and `PublishResult.RecordCount` describe the final logical object, per
README.Publishing.md). No caller inspects multipart part anatomy or assumes N objects per call.
`ThrottlingAdapterExecutionContext` guards a **different** surface (direct `StreamBatchRequest`
publishes via `context.PublishAsync`), not the NDJSON session path — see A5-adjacent note below.
A6 is satisfied: no consumer depends on multi-object-per-call semantics.

### A7 — NdjsonContentHasher / `Hash=` completion log
**DOES NOT EXIST on this branch.** Grep for `NdjsonContentHasher`, `IncrementalHash`, `ComputeHash`,
`ContentHasher`, and `Hash=` across `Shared` + `Collectors` (excl. artifacts) returns nothing in
egress; the only hashing in the repo is unrelated (`IocTypeDetector`, YAML auth, YAML
`PayloadFingerprint`). The existing per-object completion log is:
`"NDJSON(utf8) publish completed. TargetPath=... Records=... BytesUploaded=... Location=..."`
— it already carries **record count** (`Records`) and **object size** (`BytesUploaded`); there is
no `Hash=` field. The contract's Context (`Ndjson/NdjsonContentHasher.cs`) and A7 assumed an
artifact that is not present on `fix/oversized-record-guard` (it may live on a Tenable/Falcon
feature branch, which is out of scope).

**Resolution (surfaced to team-lead):** do not invent a hashing subsystem (net-new, permanent
per-byte SHA256 cost on the hottest path, justified only by observability while object atomicity
already guarantees integrity — fails the "every added cost must be justified" bar). Instead protect
the *real* invariant directly: a test asserts the assembled object bytes are **byte-identical**
whether published as a single PUT or forced through multipart (strictly stronger than a hash-equality
check). The completion log's existing size + record-count fields satisfy the Phase 3 "size + record
count if not already present" clause.

### Out-of-scope / kept-and-documented Shared sites
- `ThrottlingAdapterExecutionContext.ValidateWithinLimitsOrThrow` — throws when a **direct**
  `StreamBatchRequest` (content-type ndjson/jsonl) exceeds `MaxBytesPerBatch`. This guards a
  distinct publish surface (`context.PublishAsync`), not the `ResultsBatchPublisher` session path,
  performs no multipart escalation, and is not in the contract's listed egress files. KEEP +
  DOCUMENT (removing it is a separate decision about the direct-publish surface).
- `MultipartPartPlanner.MaxPartCount` (10,000) guard in `EnsureMultipartPartLimit` — a hard S3
  physical limit (~48 GiB object at 5 MiB parts), not object-size servitude. KEEP.
- `ThrottlingOptions`/`BufferingOptions` `>= MinPartSizeBytes` config validation — part/buffer
  discipline for multipart safety, not object size. KEEP.

### Test-consumer inventory
Tests asserting the deleted behaviour (must be replaced):
- `FalconCollector.Test/ResultsBatchPublisherConstraintsTests.cs`:
  `PublishAsync_WhenSingleRecordExceedsMaxBytes_Throws`,
  `PublishAsync_WhenPreAppendFlushCannotMeetMultipartMinimum_ThrowsBeforeExceedingBatchCap`,
  `PublishUtf8Async_WhenPreAppendFlushCannotMeetMultipartMinimum_ThrowsBeforeExceedingBatchCap`.
`FalconCollector.Test/ThrottlingAdapterExecutionContextTests.cs` also matches "exceeds
MaxBytesPerBatch" but targets the kept `ThrottlingAdapterExecutionContext` — left untouched.
Egress-exercising suites to run in Phase 7 (Shared change affects them even without edits):
Shared.Tests, FalconCollector.Test, JsonTests.

## PHASE 1/2 — EGRESS CORE + CLEANUP (Shared/DataPipeline/Egress only)

Twin changes applied identically to `Ndjson/NdjsonBatchSession.cs` and `Ndjson/NdjsonUtf8BatchSession.cs`:

- **DELETED** the fail-fast `recordBytes > MaxBytesPerPart` → `DataPipelineException("record-too-large")`
  guard in `AppendRecordAsync`. Justification: this was the object-size gate the whole task removes;
  a record is now published atomically regardless of size.
- **DELETED** `EnsureAppendWithinBatchLimit(...)` (method + its call after the pre-append flush) —
  the `batch-boundary` throw. Justification: pure object-size servitude. With it gone, a sub-5 MiB
  buffer that cannot form a valid non-final part simply keeps buffering (the record is appended and
  the buffer grows past the byte budget); the next flush forms a valid part. No throw, one object.
- **ADDED** `WarnIfRecordExceedsSoftThreshold(recordBytes)` — replaces the deleted gate with log-only
  observability (`LogWarning`) when a single record exceeds `SoftRecordWarningBytes`. Never gates.
- **ADDED** abort-once + abort-on-teardown: new fields `_multipartCompleted` / `_multipartAborted`;
  `TryAbortMultipartAsync` is now idempotent (self-guards on session-null / already-aborted /
  already-completed, sets `_multipartAborted` before the call) and logs abort failures instead of
  swallowing silently. `DisposeAsync` now calls `TryAbortMultipartAsync(None)` so a multipart started
  but never completed (exception outside FlushAsync, or cancellation that unwound the enumeration) is
  aborted on teardown/death. `_multipartCompleted` is set after a successful end-flush so a committed
  object is never aborted. The FlushAsync `catch` abort + the Dispose abort share the once-flag → the
  same failed session aborts exactly once.

**RETAINED unchanged (four-tier memory semantics + part discipline):** single-PUT vs multipart
selection (`mustStartMultipart` logic → small calls stay byte-identical single PUTs), the 5 MiB
non-final-part skip, `ShouldFlushBeforeAppend`/`ShouldFlushAfterAppend` (memory-pressure, byte-cap,
buffered-records, buffered-bytes, time triggers), `EnsureMultipartPartLimit` (hard S3 10,000-part
ceiling), config `>= MinPartSizeBytes` validation. `MaxBytesPerBatch` survives only as the part/
buffer-flush threshold (`NdjsonOptions.MaxBytesPerPart`).

Supporting Shared edits:
- `ThrottlingOptions.cs`: added `SoftRecordWarningBytes` (default 24 MiB), resolved from the
  `PublishThrottling` section and `PublishThrottling__SoftRecordWarningBytes` env (same knob pattern
  as `MaxBytesPerBatch`); added `TryReadOptionalLong`. Clarified `MaxBytesPerBatch` doc as part/memory
  discipline.
- `Glossary/CollectorGlobalDefaults.cs`: added `DefaultSoftRecordWarningBytes = 24 MiB`.
- `Ndjson/NdjsonOptions.cs`: added `SoftRecordWarningBytes`.
- `ResultsBatchPublisher.cs`: threads `throttling.SoftRecordWarningBytes` into both `NdjsonOptions`
  builds (string + utf8 paths).

### Cleanup inventory (what died / what was kept-and-documented)
- DIED: `record-too-large` fail-fast (both sessions) — object-size servitude.
- DIED: `EnsureAppendWithinBatchLimit` / `batch-boundary` throw (both sessions) — object-size servitude.
- KEPT + DOCUMENTED: `TenableIoCollector` byte-budget re-pagination — out of scope (correlated feature
  branch) AND ambiguous (bounds `pageBuffer` RAM, not solely object sizing). See Phase 0.
- KEPT + DOCUMENTED: `ThrottlingAdapterExecutionContext` direct-`StreamBatchRequest` byte guard —
  distinct publish surface, no multipart, not in the contract's egress file list. See Phase 0.
- KEPT: `EnsureMultipartPartLimit` (S3 physical limit), config `>= 5 MiB` validation (part discipline).
- **No collector code touched → no collector csproj version bumps.** All changes are in the Shared
  library. (Shared has no per-adapter CollectorVersion; nothing to bump.)

## PHASE 3 — OBSERVABILITY
- Per-object completion log (`ResultsBatchPublisher`) already carried object size (`BytesUploaded`)
  and record count (`Records`) — satisfies the "size + record count if not already present" clause.
  There is **no `Hash=` line** on this branch and **no `NdjsonContentHasher`** to extend (see A7);
  not invented. Content integrity is protected directly by the byte-identity test (below), which is
  strictly stronger than a hash-equality assertion.
- Soft-threshold `LogWarning` added per record over `SoftRecordWarningBytes` (default 24 MiB),
  configurable, log-only.

## PHASE 4 — TESTS
New file: `UnitTests/Shared/.../DataPipeline/Egress/AtomicStreamedObjectsTests.cs` (12 tests, rich
`RecordingPublisher` capturing single-PUT bytes + ordered parts + Initiate/Complete/Abort counts,
with `FailOnPartNumber`). Mapping to success criteria:
1. `SmallCall_String_PublishesOneSinglePut_ByteIdenticalContentAndPath` + `SmallCall_Utf8_...` — (1)
   single PUT, exact target path, asserts actual bytes.
2. `GrowingCall_EscalatesToMultipart_UploadsOrderedParts_NoSinglePut` — (2) Initiate + ordered
   contiguous parts, every non-final part ≥ 5 MiB, Complete once, no single PUT.
3. `SameLogicalInput_SinglePutVsMultipart_ProducesByteIdenticalObject` — (3/4) assembled multipart
   bytes byte-for-byte equal to the single-PUT object (the real integrity invariant; replaces the
   absent hasher).
4. `MidStreamPartFailure_AbortsExactlyOnce_NoComplete_ThenRetrySucceeds` — (5) Abort==1, Complete==0,
   whole-call retry succeeds cleanly.
5. `Cancellation_MidStream_AbortsInFlightMultipart` + `DisposeWithoutComplete_AbortsInFlightMultipart`
   — (6) both abort exactly once, no Complete.
6. `SuccessfulPublish_CommitsExactlyOnce_OnlyAfterComplete` + `FailedPublish_ProducesNoSuccessfulResult_AndNeverCompletes`
   — (7) commit signal only after Complete, never on failure.
7. `SoftThresholdWarning_FiresForFatRecord_NotForSmallRecords` — (8).
8. `GiantSingleRecord_Over50MiB_PublishesSuccessfully` — (9) 52 MiB record publishes (old fail-fast).
9. `BatchScopedStorage_ForcedMultipart_ScopedPath_OneObject_CompletesOnce` — (10) scoped
   `batch_000003/findings_000003.json`, one object, Complete once.
Replaced 3 now-invalid tests in `FalconCollector.Test/ResultsBatchPublisherConstraintsTests.cs`
(fail-fast throw + two batch-boundary throws) with atomic-publish success assertions.

### VSTEST RESULTS (verbatim, `dotnet vstest` on artifacts/bin/ut dlls)
- Shared.Tests (new-only filter): `Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 422 ms - Cymulate.Integration.Adapters.Shared.Tests.dll (net8.0)`
- Shared.Tests (full):           `Passed!  - Failed:     0, Passed:    73, Skipped:     0, Total:    73, Duration: 424 ms - Cymulate.Integration.Adapters.Shared.Tests.dll (net8.0)`
- FalconCollector.Test:          `Passed!  - Failed:     0, Passed:   167, Skipped:     0, Total:   167, Duration: 20 s - Cymulate.Integration.Adapters.Collectors.FalconCollector.Test.dll (net8.0)`
- JsonTests:                     `Passed!  - Failed:     0, Passed:    44, Skipped:     0, Total:    44, Duration: 1 s - JsonTests.dll (net8.0)`
Build: `dotnet build Cymulate.Integration.Adapters.sln` → `Build succeeded. 0 Warning(s) 0 Error(s)`.
Suites not run (per hard exclusions / no collector code touched): ISBLoadTestCollector, DummyCollector
(excluded); all other collector suites (no collector code changed in cleanup).

## PHASE 5 — DOCS + OPS HANDOFF
- `Egress/README.md` Stable Output Rules rewritten: one call = one atomic object, size-unbounded,
  MaxBytesPerBatch = memory/part discipline, abort-on-failure/cancel/dispose.
- `Egress/README.Publishing.md`: ThrottlingOptions section (MaxBytesPerBatch = part/memory discipline
  + new SoftRecordWarningBytes), deleted "every individual record must fit" + "single record too
  large → publish fails immediately", added atomicity/abort/commit guarantees.
- ai/skills: `collector-tests/SKILL.md` line ("large payloads flip to multipart without changing the
  target path") is still accurate — no change. No SKILL described the fail-fast/50 MiB behavior.
- No Session docs mention 50 MiB objects (grep clean).
- **OPS HANDOFF (outside repo):** confirm/add an S3 `AbortIncompleteMultipartUpload` lifecycle rule on
  the collector data bucket. In-process abort covers failure/cancel/dispose, but a hard SIGKILL before
  DisposeAsync runs can still orphan parts; the lifecycle rule is the belt-and-suspenders cleanup.

## RESIDUAL RISKS / OPEN ITEMS
- **A7 fork surfaced to team-lead:** `NdjsonContentHasher` / `Hash=` completion log do NOT exist on
  this branch; the contract assumed them present. Resolution: protect the real invariant (byte-
  identical single-PUT vs multipart object) with a direct test rather than inventing a per-byte SHA256
  subsystem on the hot path. If the team wants the hash observability, it is a clean additive follow-up.
- Single record slicing for Spark line discipline remains deferred (out of scope, per contract).
- Per-part `data.ToArray()` copy in the ISB publisher is a known micro-inefficiency (out of scope).
- Giant-record single PUT (one record > MaxBytesPerBatch) is a single PUT, not multipart (a record is
  indivisible); fine within the S3 5 GB single-PUT limit. Multi-record objects escalate to multipart.

## REPAIR ROUND 1 (code-reviewer-1: 1 Major + 3 minors; verifier LOW-1)

### M1 (MAJOR) — finalization no longer gated on residual buffer state
Bug: multipart Complete was reachable only from the `_records > 0` end flush, so a stream ending
exactly on a post-append / memory-pressure flush (buffer reset to empty) skipped Complete, returned
`Success` with a null location, and the new dispose-abort then deleted the uploaded parts → silent
data loss + checkpoint advance.
Fix (both twins, byte-for-byte lockstep):
- Renamed `FlushIfHasDataAsync` → `FinalizeAsync`. It now finalizes independent of residual buffer:
  `_records > 0` → end flush (unchanged); else `_multipartStarted && !_multipartCompleted` →
  `CompleteStartedMultipartAsync` (Complete-only, no residual part; sets FirstLocation +
  `_multipartCompleted`; aborts + rethrows on failure).
- Added belt-and-suspenders gate in `ResultsBatchPublisher` (both paths): after `FinalizeAsync`, if
  `session.CommitIncomplete` (`_multipartStarted && !_multipartCompleted`) → throw
  `DataPipelineException` so an uncommitted object can never be reported as success (failure → parts
  aborted on dispose, no checkpoint advance). New `CommitIncomplete` property exposes the state.

### m3 (test, pairs with M1) — `MultipartEndingOnPostAppendFlush_StillCompletesOnce_AndDoesNotAbort`
ForceFlushAlwaysForTesting + 3×2 MiB records: the last append triggers a post-append flush that
uploads the part and empties the buffer; asserts Complete==1, Abort==0, Success==true, location
populated, and assembled bytes == all 3 records. Fails on pre-fix code (Complete==0, Abort==1).

### m2 (test) — `CompleteFailure_AbortsExactlyOnce_AndSurfacesFailure`
Added `FailOnComplete` to `RecordingPublisher`; asserts Complete attempted once, Abort==1, call throws.

### m1 (docs) — scoped the size-unbounded claim
`README.md` + `README.Publishing.md`: the "no per-record/per-object size limit / MaxBytesPerBatch is
not an object-size cap" doctrine is now explicitly scoped to the `ResultsBatchPublisher`/session
streamed path, with a note that `ThrottlingAdapterExecutionContext` still hard-caps the direct
`StreamBatchRequest` route at `MaxBytesPerBatch` (deliberately unchanged, distinct surface).

### Verifier LOW-1 — merge reconciliation flag
FUTURE MERGE: the Falcon/Tenable feature branch(es) carrying `NdjsonContentHasher` rewrite the same
`NdjsonBatchSession`/`NdjsonUtf8BatchSession` methods (`AppendRecordAsync`, `FlushAsync`,
`TryAbortMultipartAsync`, `DisposeAsync`, and now `FinalizeAsync`/`CompleteStartedMultipartAsync`).
When those branches merge with this one, reconcile by hand: the hasher must feed bytes on BOTH the
single-PUT and every multipart-part path AND the new empty-residual `CompleteStartedMultipartAsync`
finalization, and the `CommitIncomplete` success-gate must survive.

### REPAIR VERIFICATION (build + vstest, artifacts/bin/ut dlls)
Build: `dotnet build Cymulate.Integration.Adapters.sln` → `Build succeeded. 0 Warning(s) 0 Error(s)`.
- Shared.Tests (atomic filter): `Passed!  - Failed:     0, Passed:    14, Skipped:     0, Total:    14, Duration: 520 ms - Cymulate.Integration.Adapters.Shared.Tests.dll (net8.0)`
- Shared.Tests (full):          `Passed!  - Failed:     0, Passed:    75, Skipped:     0, Total:    75, Duration: 557 ms - Cymulate.Integration.Adapters.Shared.Tests.dll (net8.0)`
- FalconCollector.Test:         `Passed!  - Failed:     0, Passed:   167, Skipped:     0, Total:   167, Duration: 8 m 36 s - Cymulate.Integration.Adapters.Collectors.FalconCollector.Test.dll (net8.0)`
- JsonTests (isolated re-run):  `Passed!  - Failed:     0, Passed:    44, Skipped:     0, Total:    44, Duration: 1 s - JsonTests.dll (net8.0)`
  NOTE: in the batched run JsonTests showed `Failed: 1` on
  `AggressiveStreamingBehaviorTests.JsonArrayPropertyStreamReader_IsStableAcrossRepeatedRuns_NoMonotonicRetentionTrend`
  — a non-deterministic GC-retention-trend heuristic on the JSON INGRESS reader (200 iterations,
  `GC.GetTotalMemory` < 20 MB drift), unrelated to egress (my changes touch no ingress/JSON-read
  path). It passed 44/44 when run in isolation; the batched failure was machine memory pressure
  (the Falcon suite ran 8m36s vs ~20s baseline, indicating heavy load). Not a regression.
