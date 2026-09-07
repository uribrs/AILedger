# Verifier-1 Report — Egress Atomic Streamed Objects

## VERDICT: PASS

The work satisfies the original request and every Success Criterion in `prompt_contract.md`.
Build is clean, all three required suites pass at the exact counts claimed, the two fail-fast
guards are fully removed from both session twins, abort semantics are correct and idempotent,
the new tests assert real behavior (bytes, call counts, ordering) rather than shapes, and the diff
is confined to scope. One LOW documentation gap (A7 merge-reconciliation note) is recorded as an
accepted residual — it is not a Success-Criterion violation.

Findings by severity: **HIGH: 0, MEDIUM: 0, LOW: 1.**

---

## Re-run evidence (ran myself, not trusted from notes)

- `dotnet build Cymulate.Integration.Adapters.sln` → **Build succeeded. 0 Warning(s) 0 Error(s)**.
- `dotnet vstest` (built dlls under `artifacts/bin/ut`, never `dotnet test`, never ISBLoadTest/Dummy):
  - Shared.Tests: **Passed! Failed 0, Passed 73, Total 73**
  - FalconCollector.Test: **Passed! Failed 0, Passed 167, Total 167**
  - JsonTests: **Passed! Failed 0, Passed 44, Total 44**
  - AtomicStreamedObjectsTests filter: **Passed 12/12** (confirms the 12 new tests are live in the 73).
- Executor's claimed counts (73/167/44) reproduced exactly.

---

## Per-Success-Criterion table

| # | Criterion | Verdict | Evidence |
|---|-----------|---------|----------|
| build | Solution builds, 0 errors | PASS | build output 0/0 |
| 1 | Small-object single-PUT byte-identical (path, naming, content) | PASS | `AtomicStreamedObjectsTests.cs:73-120` — asserts `InitiateCalls==0`, exact path `/page-000001.json`, exact bytes `{"a":1}\n{"a":2}\n{"a":3}\n` (string + utf8) |
| 2 | Multipart escalation, ≥5MiB parts, atomic Complete, correct assembly | PASS | `AtomicStreamedObjectsTests.cs:124-154` — Initiate==1, Complete==1, Abort==0, ascending contiguous part numbers, every non-final part `>= MinPartSizeBytes` |
| 3 | Byte-for-byte object equality single-PUT vs multipart | PASS | `AtomicStreamedObjectsTests.cs:158-181` — `AssembleMultipart()` (ordered part concat, actual bytes) `.Should().Equal(singleShot.SinglePayloads.Single())` |
| 4 | Hasher hash-identical invariant preserved + size | PASS (via deviation) | A7: `NdjsonContentHasher`/`Hash=` do **not exist** on this branch (grep clean). Intent met by the stronger byte-identity test (crit 3) + existing completion-log size/records fields (`ResultsBatchPublisher.cs:138-143,268-273`). See A7 judgment below. |
| 5 | Mid-stream failure → Abort exactly once, no checkpoint, clean retry | PASS | `AtomicStreamedObjectsTests.cs:185-207` — `FailOnPartNumber=2` → Abort==1, Complete==0, whole-call retry against healthy publisher succeeds |
| 6 | Cancellation → Abort; dispose-without-complete → Abort | PASS | `AtomicStreamedObjectsTests.cs:211-269` — cancellation: Abort==1/Complete==0; dispose path (invalid JSON throws in enumeration, outside FlushAsync): Abort==1/Complete==0 |
| 7 | Checkpoint hook once per call, only after Complete | PASS | `AtomicStreamedObjectsTests.cs:273-299`; code: `PublishResult` returned only after `FlushIfHasDataAsync` (`ResultsBatchPublisher.cs:129,146`); failure throws before any result |
| 8 | Soft-threshold warning fires only past threshold | PASS | `AtomicStreamedObjectsTests.cs:303-323` — fat record warns ≥1, small record warns 0, via `ILogger` invocation count filtered on `LogLevel.Warning` |
| 9 | Giant record (>50MiB) publishes end-to-end | PASS | `AtomicStreamedObjectsTests.cs:327-345` — 52 MiB record, single PUT, payload length `> 52*MiB` |
| 10 | BatchScopedStorage interplay — scoped path, one object, single announce after Complete | PASS | `AtomicStreamedObjectsTests.cs:349-367` — scoped `stg/raw/run/batch_000003/findings_000003.json`, Initiate==1, Complete==1, no single-PUT |
| cleanup | Deleted paths inventoried w/ justification; ambiguous kept+documented | PASS | execution_notes Phase 0/1/2; verified in code (below) |
| collectors | Affected collector suites green; exclusions honored | PASS | Falcon 167/167; ISBLoadTest/Dummy never run; no other collector code touched so no other suite affected |
| docs | Egress README + Stable Output Rules + session docs + ai/skills + ops handoff | PASS | README.md + README.Publishing.md diffs correct; ai/skills has no stale 50MiB content; ops handoff recorded |
| versions | csproj bumps only where collector code touched | PASS | zero collector code touched → zero csproj changes (verified `git diff --name-only \| grep csproj` empty) |

---

## Deep checks (verifier obligations 2–10)

**2 — Deleted guards fully gone.** `record-too-large` and `batch-boundary` removed from BOTH
`NdjsonBatchSession.cs` and `NdjsonUtf8BatchSession.cs` (identical twin diffs). Grep across
`*.cs`/`*.md` (excl. artifacts): `record-too-large` none; `EnsureAppendWithinBatchLimit` none;
`batch-boundary` only a Falcon-test *comment* describing old behavior. No orphaned helpers, no dead
exception codes, nothing else throws on record/object size in the session path.

**3 — Abort semantics.** `TryAbortMultipartAsync` (`NdjsonBatchSession.cs:380-400`) guards on
`_multipartSession is null || _multipartAborted || _multipartCompleted` and sets `_multipartAborted`
before the call → fires **exactly once**. Wired on all three paths: FlushAsync `catch` (`:302-306`),
cancellation (same catch, or DisposeAsync), and `DisposeAsync` (`:491-496`). Abort failures caught
and `LogWarning`-ed, never thrown (`:395-399`). `_multipartCompleted` set only after Complete
returns success (`:288-292`, inside try, after `result.Success` check) → a committed object is never
aborted; flush-catch and dispose share the once-flag so the same failed session aborts once. No true
race (single-threaded async flow). Twin identical.

**4 — Regression law load-bearing.** Byte-identical small-call assertions are genuine (exact bytes +
exact path, not lengths/shapes). Multipart-vs-single-PUT is a real assembled-bytes equality (crit 3).
The 12 new tests assert behavior via a `RecordingPublisher` that captures actual payload bytes,
ordered parts, and Initiate/Complete/Abort counts with `FailOnPartNumber`.

**5 — Hook ordering.** `PublishResult` (the commit/checkpoint signal) is produced once, only after
`FlushIfHasDataAsync` completes Complete/single-PUT (`ResultsBatchPublisher.cs:129-146,259-276`); on
failure the exception propagates and no result is returned. Session sets `Progress = null` on the
single request; no per-record progress callback fires.

**6 — Four-tier memory semantics unchanged.** `ShouldFlushBeforeAppend`/`ShouldFlushAfterAppend`
(throttling byte-cap, buffered-records, buffered-bytes, memory-pressure, time) retained verbatim;
5 MiB non-final-part skip retained (`:211-214`); `EnsureMultipartPartLimit` 10,000-part ceiling
retained (`:413-424`). `MaxBytesPerBatch` survives only as `MaxBytesPerPart` (part/flush threshold).
`SoftRecordWarningBytes` default 24 MiB (`CollectorGlobalDefaults.cs`), env override
`PublishThrottling__SoftRecordWarningBytes` + section key, wired exactly like `MaxBytesPerBatch`
(`ThrottlingOptions.cs:59-97`).

**7 — Cleanup inventory accurate.** TenableIo byte-budget KEPT (`TenableIoFindingsFlow.cs:415`,
`TenableIoAssetsFlow.cs:391`) — matches Phase 0. `ThrottlingAdapterExecutionContext` KEPT (0 lines
changed) — distinct direct-publish surface. S3 10K-part guard KEPT. No remaining references to the
deleted codes/messages in code, tests, or docs (grep clean; the two "exceeds MaxBytesPerBatch" hits
belong to the kept `ThrottlingAdapterExecutionContext` + its test).

**8 — A7 deviation: ACCEPTED (sound).** Contract criterion 4 named `NdjsonContentHasher`/`Hash=`;
grep confirms neither exists on `fix/oversized-record-guard`. Executor correctly declined to invent
a per-byte SHA256 subsystem on the hot path (unjustified cost when atomic Complete already guarantees
integrity) and instead protected the *actual* invariant — same logical input yields a byte-identical
object whether single-PUT or multipart — with a direct assembled-bytes equality test that is strictly
stronger than a hash-equality check. Observability clause satisfied by the pre-existing completion-log
`Records` + `BytesUploaded` fields. This meets the **intent** of criterion 4. Fork was surfaced to
team-lead in execution_notes RESIDUAL RISKS. (See LOW finding on the merge-reconciliation note.)

**9 — Scope guard.** Diff = 8 files under Egress + `Glossary/CollectorGlobalDefaults.cs` + the two
test files. Zero collector code, zero csproj, zero ISB/feature-branch edits, `BatchScopedStorage`
untouched (its interplay covered by test 10 through the unmodified `CollectorNdjsonPublisher`).

**10 — Contract items.** ai/skills: only egress reference is `collector-tests/SKILL.md:47` ("large
publish payloads flip to multipart without changing the target path") — still accurate, no stale
50MiB/fail-fast content to fix. Ops handoff (S3 `AbortIncompleteMultipartUpload` lifecycle rule)
recorded in both README.Publishing.md and execution_notes Phase 5. Execution notes complete.

---

## Findings

### LOW-1 — A7 merge-reconciliation with the Falcon branch's hasher not explicitly documented
execution_notes frames the absent `NdjsonContentHasher`/`Hash=` as living "on a Tenable/Falcon
feature branch, out of scope" and the hash observability as a "clean additive follow-up." That
understates the interaction: the Falcon branch's hasher and `Hash=` completion log touch the very
session methods (`AppendRecordAsync`, `FlushAsync`, the completion log) this branch heavily rewrote,
so the eventual merge is a **reconciliation**, not a purely additive add. Verifier obligation 8
asked that this be recorded; it currently is not called out as a merge-conflict risk.
- **Suggested repair:** add one line to execution_notes RESIDUAL RISKS: "On merge with the
  Falcon/Tenable branch that carries `NdjsonContentHasher` + `Hash=`, expect a conflict in the
  session twins' `AppendRecordAsync`/`FlushAsync`/completion-log; reconcile the hasher against the
  new size-unbounded streaming + atomic-Complete semantics (the hash must cover the full assembled
  object, computed incrementally per part)."
- **Impact:** documentation only; no code or behavior effect. Accepted as residual.

---

## Accepted residuals
- LOW-1 (doc-only merge note).
- Per-part `data.ToArray()` copy in the ISB publisher — out of scope (ISB repo), noted A1.
- Spark line discipline / record slicing — deferred out of scope per contract.
- Giant single record publishes as single PUT (indivisible), bounded by S3 5 GB single-PUT limit —
  correct and documented.
