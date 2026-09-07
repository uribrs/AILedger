# Verifier-1 — atomic-objects-and-batch-scoping carry

**VERDICT: PASS** (2 trivial documentation nits, no code repairs required)

Independent verification of the carry on branch `carry/atomic-objects-and-batch-scoping`
(uncommitted) against the adapters-repo source of truth: changeset B = `git show 832754a`,
changeset A = `git diff 8948fba..7218abc -- Shared/*`. I re-derived every fact below from the
source and target files directly; I did not trust execution_notes.

---

## Build & Test (re-run by me)

- `dotnet build IntegrationInfra.slnx -f net8.0`: **Build succeeded, 0 errors** (9 warnings, all
  pre-existing/unrelated: NU1507 package-source-mapping + XML cref).
- `dotnet test IntegrationInfra.slnx -f net8.0 --no-build`: **188/188 passed, 0 failed, 0 skipped.**
  Reporting 15, Kernel 37, FaultGovernance 20, Conversation 11, Job 31, Conducting 31, **Emission 43**.
  Emission 43 = 15 pre-existing (EmissionDefaults + ResultsRecordFormatter, incl. Theory cases) + 14
  atomic + 14 batch-scoped.

---

## Success-Criterion table

| # | Criterion | Result | Evidence |
|---|-----------|--------|----------|
| 1 | Build 0 errors; all suites green incl. two ported files | PASS | build 0 errors; 188/188; Emission 43 |
| 2 | Ported test coverage intact through translation | PASS | all listed behaviors present; assertions compared line-by-line vs FluentAssertions originals (below) |
| 3 | Grep-clean; `"storageUrl"` literal→const at sessions + 7 sites; wire JSON stays literal | PASS | 0 `_multipartFinalized`/`record-too-large`/`batch-boundary`/`EnsureAppendWithinBatchLimit`/`FlushIfHasDataAsync` in code/tests/src-docs; storageUrl audit clean (below) |
| 4 | DAG intact; FaultGovernance→Envelopes.Common permissible | PASS | only pre-existing Conducting→Emission edge; BSS SDK-only; FaultGov edge sanctioned by faultgovernance-carry docs |
| 5 | README updated; D7-SUPERSEDED appended; Version=1.0.0-preview.3 | PASS | README accurate; D7 original intact + dated note appended; `<Version>1.0.0-preview.3</Version>` |
| 6 | execution_notes 1:1 parity checklist present | PASS | present; every hunk mapped; 2 N/A rows genuine |

---

## Parity sampling — source hunk vs target (line-by-line, modulo namespace/D8)

All 12 sampled hunks are semantically identical to source; no deviation found.

1. **FinalizeAsync + CompleteStartedMultipartAsync** (both twins) — identical. `_records>0 → FlushAsync("end")`
   else `_multipartStarted && !_multipartCompleted → CompleteStartedMultipartAsync`; catch → `TryAbortMultipartAsync(ct); throw`. (`NdjsonBatchSession.cs:104-146`, `NdjsonUtf8BatchSession.cs:106-148`)
2. **TryAbortMultipartAsync exactly-once guard** — identical: `if (_multipartSession is null || _multipartAborted || _multipartCompleted) return; _multipartAborted = true;` then logged-never-thrown `catch (Exception ex)`. (`:429-449` / `:420-440`)
3. **CommitIncomplete gate** (both publisher cores) — identical: `FinalizeAsync` then `if (session.CommitIncomplete) throw new DataPipelineException($"Multipart upload for '{targetPath}' did not complete; refusing to report success.")`; `DataPipelineException` from `Kernel.Exceptions` (imported). (`ResultsBatchPublisher.cs:129-137, 267-275`)
4. **AdapterPlatformEventFactory key-vs-value split** — exact: guard + `SetIfMissing(..., BatchScopedStorage.StorageUrlMetadataKey, ReadScalarAsString(metadataObj, "storageUrl"))` — KEY is const, VALUE read stays literal. (`AdapterPlatformEventFactory.cs:32, 93`)
5. **CollectorResumeLegacyExecutor return restructure** — exact: `if (flowResult is not null) return flowResult;` then `RestoreBase(context.ProgressContext); return BuildSuccessResult(...)`. RestoreBase correctly runs ONLY on the success-result path, not when the flow returned its own result. (`:37-47`)
6. **ThrottlingOptions env/config threading** — exact: `DefaultSoftRecordWarningBytes` const; `SoftRecordWarningBytes` prop defaulted; `Resolve` reads env `PublishThrottling__SoftRecordWarningBytes`; `FromConfiguration` reads section via new `TryReadOptionalLong`. (`ThrottlingOptions.cs:15,27,62,88,120-124`)
7. **FlushAsync isEnd `_multipartCompleted=true`** — identical, with source comment. (`:331-335` / `:326-330`)
8. **DisposeAsync abort prologue** — identical: `await TryAbortMultipartAsync(CancellationToken.None)` first. (`:545` / `:541`)
9. **WarnIfRecordExceedsSoftThreshold** (replaces both `record-too-large` throw and `EnsureAppendWithinBatchLimit`) — identical log-only guard `SoftRecordWarningBytes <= 0 || recordBytes <= threshold`. (`:233-245` / `:232-244`)
10. **BatchScopedStorage.cs** — `diff` confirms **byte-identical modulo the namespace line**; homed in `Envelopes/Common`, ns `Cymulate.IntegrationInfra.Envelopes.Common`, only dep `using Cymulate.Integration.Sdk.Contracts;`.
11. **All 7 RestoreBase call sites** — each matches source (using + call placement + comment text where source had one; PartialSuccessPublisher pair carry no comment, matching source). AdapterFailureDecisionExecutor: Envelopes.Common using already present (line 1), so none added — correct.
12. **Two N/A rows genuine**: FalconCollector `ResultsBatchPublisherConstraintsTests` (collector-project test; giant-record + sub-min-buffer behavior is covered by ported `AtomicStreamedObjectsTests`) and collector-side changeset-A files (contract scopes to the Shared slice) — both correctly out of scope.

## Twins symmetry
`diff NdjsonBatchSession.cs NdjsonUtf8BatchSession.cs`: the only differences are **pre-existing
asymmetries** — class name, `TelemetryMode` value, writer type, `AppendRecordAsync` signature +
`recordBytes` computation, log-message text, and comment presence. **Every carried block**
(two-flag fields, FinalizeAsync, CompleteStartedMultipartAsync, TryAbortMultipartAsync,
WarnIfRecordExceedsSoftThreshold, isEnd completion, DisposeAsync prologue, ApplyStorageUrlPrefix
const) is byte-for-byte identical between the twins. Symmetry claim holds.

## D7 replacement
- 0 `_multipartFinalized` in any `.cs` (src+tests), confirmed three ways. Remaining hits are all in
  **exempt history/docs** (emission-carry decisions.md + execution_notes.md, this task's docs, and
  `ai/reviews/full-repo-2026-07-01/reviewer-1.md`).
- Dispose-without-complete still aborts: `DisposeAsync` → `TryAbortMultipartAsync(CancellationToken.None)`
  (`NdjsonBatchSession.cs:545`, `NdjsonUtf8BatchSession.cs:541`), guarded by the exactly-once check,
  proven by `DisposeWithoutComplete_AbortsInFlightMultipart` (AbortCalls==1, CompleteCalls==0).

## Test translation fidelity (assertions vs FluentAssertions originals)
Compared >6 methods across both files; no weakening found:
- **Cancellation subclass-tolerance** (the risk case): `Assert.ThrowsAnyAsync<OperationCanceledException>`
  preserves FluentAssertions' subclass tolerance (`Assert.ThrowsAsync` would break on TaskCanceledException).
  Same pattern for `DataPipelineException`/`Exception`.
- **Byte equality**: `Assert.Equal(byte[], byte[])` sequence equality for single-PUT-vs-multipart and the
  M1 post-append-flush assembled object.
- **Exact counts / Single / Empty**: `.Should().Be(n)`→`Assert.Equal(n,..)`, `.ContainSingle()`→`Assert.Single`,
  `.BeEmpty()`→`Assert.Empty`; ordered parts via `Assert.Equal(Enumerable.Range(1,n).ToArray(), ...)` (subsumes ascending).
- **Warning-count**: `CountingLogger.WarningCount` (counts `Log(LogLevel.Warning,…)`) is semantically identical
  to the source's `Mock<ILogger>.Invocations` warning filter.
- **Emission test csproj references only xunit + Test.Sdk (+ runner)** — **no FluentAssertions, no Moq**.
  `RecordingPublisher` extracted as one shared helper (byte-identical to source inner class); hand-rolled
  `FakeServiceProvider`/`FakeExecutionContext`/`CountingLogger` in `EmissionTestDoubles.cs`.

## storageUrl literal audit (every survivor in src/, obj/bin excluded)
All wire-contract or canonical — none is a converted metadata-key read:
- `BatchScopedStorage.StorageUrlMetadataKey = "storageUrl"` (the single canonical const) + its XML-doc prose.
- `AdapterRunMetadata.cs:25`, `AdapterEventMetadata.cs:28`: `[JsonPropertyName("storageUrl")]` (wire serialization).
- `AdapterPlatformEventFactory.cs:93`: payload VALUE read (KEY uses const) — matches source.
- `Job/AdapterRunEnvelopeParser.cs:100`: `Get(md,"storageUrl")` — wire-envelope payload parse alongside all
  other envelope keys; untouched by this carry and by source changeset A (Job is out of the Shared slice).

## DAG
- Conducting→Emission: only `Conducting/Collectors/Triggers/CollectorTriggerParsing.cs`
  (pre-existing `AdapterGlobalDefaults` constants edge, constraint-permitted). No new edge; the RestoreBase
  sites import `Envelopes.Common`.
- `BatchScopedStorage` in `Envelopes/Common`, dep = SDK only (Envelopes is a leaf).
- FaultGovernance→Envelopes.Common: pre-existing (AdapterFailureDecisionExecutor using already present).
  faultgovernance-carry docs (`2026-06-30_1146_faultgovernance-carry` prompt_contract:33, execution_notes:12-13)
  **relocated** the envelope DTOs INTO Envelopes.Common — the edge is sanctioned, not forbidden. Phase 0's
  A6 claim is accurate.

## Housekeeping
- `<Version>1.0.0-preview.3</Version>` (was preview.2). 
- Emission README accurate against the changed code (atomicity/abort-once/commit, SoftRecordWarningBytes
  24 MiB + env, MaxBytesPerBatch part-discipline, ThrottlingAdapterExecutionContext hard-cap called out as
  deliberately unchanged — that file is clean/unmodified, BatchScopedStorage opt-in section).
- emission-carry decisions.md: original D7 entry intact; dated `D7-SUPERSEDED (2026-07-07)` note appended
  after it (append-only honored).
- state.json consistent (status execution_complete; verifierRun/codeReviewerRun false — expected pre-review).

---

## Findings

### LOW-1 (documentation only) — execution_notes internal test-count breakdown is off
execution_notes says "Emission 43 = 16 pre-existing + 14 atomic + 13 batch-scoped". Actual: **15
pre-existing + 14 atomic + 14 batch-scoped = 43** (BatchScopedStorageTests has 14 `[Fact]`, not 13).
The headline totals (Emission 43, 188/188) are correct. No code impact. Repair: adjust the two numbers
in execution_notes.md if desired.

### LOW-2 (documentation only) — execution_notes undercounts exempt `_multipartFinalized` history hits
execution_notes says "two `_multipartFinalized` hits under ai/active/…/emission-carry/". There are more
(emission-carry execution_notes.md:47 and `ai/reviews/full-repo-2026-07-01/reviewer-1.md:115`). All are
exempt history/docs; the operative criterion (zero in code/tests) holds. No code impact.

Neither finding affects any success criterion. No repairs required.
