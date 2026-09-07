# Verifier-2 Report — Egress Atomic Streamed Objects (post-repair-round-1 final confirm)

## VERDICT: PASS

Repair round 1 fixed the one Major (M1) correctly, added the two missing failure-path tests
(m2/m3) as genuine behavioral tests, scoped the docs (m1), and recorded the LOW-1 merge note.
Build is clean, all three suites are green, twin lockstep holds, and scope did not drift.
The whole task passes post-repair.

Findings this round: **HIGH 0, MEDIUM 0, LOW 0** (round-1 M1/m1/m2/m3/LOW-1 all resolved).

---

## Re-run evidence (ran myself)

- `dotnet build Cymulate.Integration.Adapters.sln` → **0 Warning(s), 0 Error(s)**.
- `dotnet vstest` (built dlls; never `dotnet test`; never ISBLoadTest/Dummy):
  - Shared.Tests: **Passed! Failed 0, Passed 75, Total 75** (was 73 → +2 for m2/m3)
  - AtomicStreamedObjectsTests filter: **14/14** (was 12 → +2)
  - FalconCollector.Test: **Passed! Failed 0, Passed 167, Total 167**
  - JsonTests: **Passed! Failed 0, Passed 44, Total 44** (incl. the flagged GC-trend test — passed)

---

## M1 fix — verified correct

**Finalization decoupled from residual buffer** (`NdjsonBatchSession.cs:103-113`, twin identical):
`FinalizeAsync` → if `_records > 0` end-flush (unchanged behavior, completes multipart or single
PUT); **else if** `_multipartStarted && !_multipartCompleted` → `CompleteStartedMultipartAsync`.
This closes the exact loss window: a stream ending on a post-append/memory-pressure flush (empty
residual) now still Completes the started multipart.

**Flag ordering in `CompleteStartedMultipartAsync`** (`:115-145`): Complete is attempted, success is
checked, `FirstLocation` set, then `_multipartCompleted = true` — set **only on success**, inside the
try. On any failure (throw or non-success result) the local `catch` calls `TryAbortMultipartAsync`
(idempotent once-flag) and rethrows → abort fires exactly once, `_multipartCompleted` stays false.
Correct.

**Empty-session / single-PUT untouched**: `FinalizeAsync` calls Complete only when `_multipartStarted`.
An empty session (`_records==0`, no multipart) hits neither branch → no-op, no spurious Complete. A
single-PUT session ends with `_records > 0` → end-flush → single PUT (multipart never started). No
regression to the byte-identical small-call path. Verified by the existing SmallCall tests (still green).

**Twin lockstep**: the `CommitIncomplete` + `FinalizeAsync` + `CompleteStartedMultipartAsync` block is
**byte-identical** between `NdjsonBatchSession` and `NdjsonUtf8BatchSession` (diffed the extracted blocks
— zero difference).

**Belt in `ResultsBatchPublisher`** (`:128-135` string path, `:266-273` utf8 path): after
`FinalizeAsync`, both paths check `session.CommitIncomplete` and throw `DataPipelineException`
**before any success return** (before the `PublishedRecords == 0` check and the `PublishResult.Ok`).
Cannot fire after a successful Complete — `CommitIncomplete = _multipartStarted && !_multipartCompleted`,
and Complete sets `_multipartCompleted = true`. So an uncommitted multipart can never report success;
the throw propagates → dispose aborts the parts → no checkpoint advances. Defensive layer is genuine.

---

## New tests — genuine, and m3 would fail pre-fix

**m3 — `MultipartEndingOnPostAppendFlush_StillCompletesOnce_AndDoesNotAbort`** (`:280-308`):
constructs the empty-residual end deterministically via `MemoryPressureOptions.ForceFlushAlwaysForTesting
= true` + 3×2MiB records (the third append trips a post-append flush that uploads a 6 MiB part and
resets the buffer to empty, then the enumeration ends). Asserts `Success`, `RecordCount==3`,
`StorageLocation` contains `/pp.json`, `InitiateCalls==1`, **`CompleteCalls==1`**, **`AbortCalls==0`**,
and `AssembleMultipart()` equals the exact concatenated record bytes.
**Pre-fix judgment: yes, it fails on the old code** — old `FlushIfHasDataAsync` early-returns on
`_records==0`, so Complete is never called (`CompleteCalls==0` ✗), `StorageLocation` is empty
(FirstLocation null ✗), and dispose aborts the parts (`AbortCalls==1` ✗). Three independent assertions
break. The test genuinely proves the bug and guards the fix.

**m2 — `CompleteFailure_AbortsExactlyOnce_AndSurfacesFailure`** (`:313-324`): `FailOnComplete=true`,
8×2MiB → multipart; the end-flush Complete throws. Asserts the call throws, `CompleteCalls==1`
(RecordingPublisher increments before throwing — `:497-501`), and **`AbortCalls==1`**. Exercises the
previously-untested Complete-failure abort path.

Both use the byte-capturing `RecordingPublisher` (call counts, ordered parts, assembled bytes) — behavior,
not shape.

---

## m1 docs — accurate

README.md and README.Publishing.md now scope the size-unbounded doctrine explicitly to the
`ResultsBatchPublisher`/`CollectorNdjsonPublisher` streamed path and add a Scope note that
`ThrottlingAdapterExecutionContext` still hard-caps a direct `StreamBatchRequest` publish at
`MaxBytesPerBatch` on a deliberately-unchanged route. Matches the code (`ThrottlingAdapterExecutionContext.cs`
untouched, 0 lines changed) and its still-asserting test. No over-claim remains.

---

## LOW-1 — recorded

`execution_notes.md:247-253` records the future merge reconciliation with the Falcon/Tenable branch's
`NdjsonContentHasher`: names the rewritten session methods including the **new** `FinalizeAsync`/
`CompleteStartedMultipartAsync`, requires the hasher to feed bytes on the single-PUT path, every
multipart-part path, AND the empty-residual finalization, and requires the `CommitIncomplete`
success-gate to survive. Complete.

---

## Updated criterion rows (only where round 1 changed something)

| # | Criterion | Verdict | Evidence (post-repair) |
|---|-----------|---------|------------------------|
| 5 | Mid-stream failure → Abort once, no checkpoint, clean retry | PASS | now also covers Complete-failure: `AtomicStreamedObjectsTests.cs:313-324` Abort==1 |
| 7 | Commit signal once, only after Complete, never on failure | PASS (strengthened) | M1 fix + `CommitIncomplete` belt (`ResultsBatchPublisher.cs:128-135,266-273`); post-append-flush-end path now Completes (`:103-113`) and is tested (`:280-308`) |
| 9/giant | (unchanged) | PASS | — |
| docs | Egress README scoped + ThrottlingAdapterExecutionContext note | PASS | README diffs above |

All other criterion rows from verifier-1 remain PASS and unaffected.

---

## Consolidated ACCEPTED RESIDUALS

- **`ThrottlingAdapterExecutionContext` direct-route cap kept** — a distinct publish surface
  (`IAdapterExecutionContext.PublishAsync(StreamBatchRequest)`), no multipart, deliberately unchanged;
  now documented as such. Not object-size servitude on the reviewed path.
- **Falcon-branch `NdjsonContentHasher` merge reconciliation pending** — will conflict in the session
  twins on merge; hand-reconcile per execution_notes:247-253 (hasher over single-PUT + all parts + empty-
  residual finalize; keep `CommitIncomplete` gate). Doc-only, out of scope here.
- **JsonTests `AggressiveStreamingBehaviorTests.JsonArrayPropertyStreamReader_IsStableAcrossRepeatedRuns_
  NoMonotonicRetentionTrend`** — pre-existing GC-trend flake: asserts no monotonic retention trend using
  `GC.GetTotalMemory` (`AggressiveStreamingBehaviorTests.cs:87-153`), genuinely non-deterministic under
  concurrent memory pressure. It is on the JSON **ingress** read path, **untouched** by this diff
  (Egress-only), and passed cleanly in isolation (44/44). Accepted as unrelated pre-existing flake.
- **Ops handoff: S3 `AbortIncompleteMultipartUpload` lifecycle rule** on the data bucket — belt-and-
  suspenders against a hard SIGKILL before dispose; recorded in README.Publishing.md + execution_notes,
  to be actioned outside the repo.
- Per-part `data.ToArray()` copy in the ISB publisher (out of scope, ISB repo).
- Spark line discipline / record slicing (deferred, out of scope).

---

## Scope guard (post-repair)

Diff = same 10 files: 8 under `Egress/` + `Glossary/CollectorGlobalDefaults.cs` + the two test files.
Zero collector code, zero csproj bumps, zero ISB/feature-branch edits, `BatchScopedStorage` untouched.
No drift from round 1.
