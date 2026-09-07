# Execution Notes

## PHASE 0 — INVENTORY (before any edit)

### A6 — FaultGovernance → Envelopes.Common edge: PERMISSIBLE (no STOP)
FaultGovernance's own carry (`ai/active/2026-06-30_1146_faultgovernance-carry`) relocated its envelope
DTOs (`AdapterError`, `AdapterPartialCompletionMetadata`) INTO `Envelopes/Common` (decisions D3/D8,
assumption A5). `FaultGovernance/Logic/AdapterFailureDecisionExecutor.cs` already opens
`using Cymulate.IntegrationInfra.Envelopes.Common;` (line 1). The edge exists and is sanctioned.
Adding a `BatchScopedStorage` reference from that same file introduces no new edge. Conducting-carry
D-charter confirms Conducting also depends on `Envelopes.Common` (NOT Emission), so homing
`BatchScopedStorage` there keeps the DAG acyclic and Conducting apex. RESOLVED — proceed.

### A8 — Package sessions vs source pre-changeset baseline (git 8948fba): only delta is D7 + naming
Normalized diff (strip usings/namespace, unify GlobalDefaults name) of package
`NdjsonBatchSession`/`NdjsonUtf8BatchSession`/`NdjsonOptions`/`ResultsBatchPublisher`/`ThrottlingOptions`
against `git show 8948fba:...`:
- Sessions: two clusters of delta — (1) the D7 `_multipartFinalized` machinery (field, isEnd-finalize
  block, `!_multipartFinalized` catch guard + set, DisposeAsync abort prologue), and (2)
  `CollectorOutputDefaults` → `AdapterOutputDefaults` (D8 rename). No other drift.
- `NdjsonOptions`, `ThrottlingOptions`: byte-identical modulo namespace.
- `ResultsBatchPublisher`: only `CollectorOutputDefaults` → `AdapterOutputDefaults`.
Conclusion: D7 is the sole behavioral divergence (matches emission-carry A2/D7). Port shape is clean;
changeset B's session hunks apply onto the package's non-D7 code, D7 lines fully removed first. RESOLVED.

### Naming / type map (source → package)
- `Cymulate.Integration.Adapters.Shared.DataPipeline.Egress` → `Cymulate.IntegrationInfra.Emission` (+ `.Ndjson`)
- `CollectorGlobalDefaults` → `AdapterGlobalDefaults` (Emission)
- `CollectorOutputDefaults` → `AdapterOutputDefaults`
- `CollectorNdjsonPublisher` → `AdapterNdjsonPublisher`
- `Shared.Exceptions.DataPipelineException` → `Kernel.Exceptions.DataPipelineException` (publisher already imports)
- `BatchScopedStorage` home: source Egress → package `Envelopes/Common`, ns `Cymulate.IntegrationInfra.Envelopes.Common`
- `Orchestration/*` → `Conducting/*`; `Orchestration/AdapterFlowFailureHandling` → `FaultGovernance/AdapterFlowFailureHandling`;
  `Resilience/Logic/AdapterFailureDecisionExecutor` → `FaultGovernance/Logic/AdapterFailureDecisionExecutor`
- Test project: source `Shared.Tests` → package `tests/IntegrationInfra.Emission.Tests`

### Test-house-style decision
Package test csproj carries only xunit + Test.Sdk (no Moq, no FluentAssertions); `InternalsVisibleTo`
= `IntegrationInfra.Emission.Tests` is wired. Per constraint (plain Assert; hand-rolled fakes over Moq),
the source tests' `Mock<IServiceProvider>`/`Mock<IAdapterExecutionContext>`/`Mock<ILogger>` are replaced
by hand-rolled fakes; `RecordingPublisher` and a target-path-capturing publisher are hand-rolled. All
public types needed (`ResultsBatchPublisher`, `AdapterNdjsonPublisher`, `BatchScopedStorage`,
`ThrottlingOptions`, `MemoryPressureOptions`, `MultipartPartPlanner`) — no internals required.

## PHASE 1 — CHANGESET B / sessions (S1)
Both twins (`Emission/Ndjson/NdjsonBatchSession.cs`, `NdjsonUtf8BatchSession.cs`) edited symmetrically:
`_multipartFinalized` → `_multipartCompleted`+`_multipartAborted`; record-too-large throw → per-record
`WarnIfRecordExceedsSoftThreshold`; removed the post-pre-append-flush `EnsureAppendWithinBatchLimit` call;
`FlushIfHasDataAsync` → `FinalizeAsync` + `CommitIncomplete` prop + `CompleteStartedMultipartAsync`;
`EnsureAppendWithinBatchLimit` → `WarnIfRecordExceedsSoftThreshold`; FlushAsync isEnd block sets
`_multipartCompleted`; catch → `await TryAbortMultipartAsync(cancellationToken)`; `TryAbortMultipartAsync`
now takes only the token, exactly-once guard (`_multipartSession is null || _multipartAborted ||
_multipartCompleted`) + logged (never thrown) abort; DisposeAsync prologue → single
`await TryAbortMultipartAsync(CancellationToken.None)`. Twin symmetry re-verified by normalized diff: the
changed logic is byte-for-byte identical between twins (only pre-existing comment/append-signature
asymmetries differ).

## PHASE 2 — CHANGESET B / publisher+options (S2)
- `Ndjson/NdjsonOptions.cs`: `+ long SoftRecordWarningBytes` (log-only, 0 disables).
- `AdapterGlobalDefaults.cs`: `+ DefaultSoftRecordWarningBytes = 24 MiB` (maps source `CollectorGlobalDefaults`).
- `ThrottlingOptions.cs`: `+ DefaultSoftRecordWarningBytes` const; `+ SoftRecordWarningBytes` prop (defaulted);
  `Resolve` env branch reads `PublishThrottling__SoftRecordWarningBytes`; `FromConfiguration` reads section
  key via new `TryReadOptionalLong`. `MaxBytesPerBatch` doc reframed to part/memory discipline.
- `ResultsBatchPublisher.cs`: both `NdjsonOptions` blocks carry `SoftRecordWarningBytes`; both cores
  `FlushIfHasDataAsync`→`FinalizeAsync` + `CommitIncomplete` gate throwing `DataPipelineException`
  (Kernel.Exceptions, already imported).

## PHASE 3 — CHANGESET A + tests (S3, S4)
- `Envelopes/Common/BatchScopedStorage.cs`: verbatim carry, ns `Cymulate.IntegrationInfra.Envelopes.Common`,
  XML docs preserved. Its metadata-key consts are canonical.
- Both sessions' `ApplyStorageUrlPrefix`: literal `"storageUrl"` → `BatchScopedStorage.StorageUrlMetadataKey`
  (+ `using Cymulate.IntegrationInfra.Envelopes.Common;`).
- 7 RestoreBase sites (mirroring source call sites exactly, D-C1b: pure metadata, no token):
  `Conducting/AdapterBusEntrypointRunner` (success DONE, source comment) + its `ResolveRunMetadata`
  storageUrl-literal→const; `Conducting/Bus/Logic/AdapterBusPartialSuccessPublisher`;
  `Conducting/Collectors/Recovery/{CollectorResumeLegacyExecutor (return restructured), 
  CollectorResumePartialSuccessPublisher, CollectorResumeStrategyExecutor}`;
  `FaultGovernance/AdapterFlowFailureHandling` (source `Orchestration/AdapterFlowFailureHandling`);
  `FaultGovernance/Logic/AdapterFailureDecisionExecutor` (source `Resilience/Logic/...`, using already present).
- `Conducting/AdapterPlatformEventFactory`: guard read + `SetIfMissing` KEY → const; the payload VALUE read
  `ReadScalarAsString(metadataObj, "storageUrl")` STAYS literal (wire JSON property), mirroring source.
- Tests → `tests/IntegrationInfra.Emission.Tests`: `AtomicStreamedObjectsTests.cs` (14 facts) +
  `BatchScopedStorageTests.cs` (13 facts), plain xUnit `Assert` (no FluentAssertions). Shared hand-rolled
  `RecordingPublisher.cs`; shared `EmissionTestDoubles.cs` (`FakeServiceProvider`, `FakeExecutionContext`,
  `CountingLogger`, `EmissionTestContext.Build`). No Moq added — the 3-member `IAdapterExecutionContext` is
  hand-rolled; `Mock<ILogger>.Invocations` warning-count → `CountingLogger.WarningCount`.
  Assertion fidelity: exact counts kept; byte-array equality via `Assert.Equal(byte[],byte[])` (sequence);
  ordered-parts via `Assert.Equal(Enumerable.Range(1,n), partNumbers)` (subsumes ascending); exception
  hierarchy checks use `Assert.ThrowsAnyAsync<T>` (matches FluentAssertions subclass tolerance —
  OperationCanceledException/TaskCanceledException); `CollectorNdjsonPublisher`→`AdapterNdjsonPublisher`.

## PHASE 4 — docs/version (S5)
- `Emission/README.md`: added "Stable Output Rules" (atomicity/abort/commit/size-unbounded scoped to the
  `AdapterNdjsonPublisher`/`ResultsBatchPublisher` session path; ThrottlingAdapterExecutionContext hard-cap
  called out as the deliberately-unchanged route) + `BatchScopedStorage` opt-in section.
- `ai/active/2026-06-30_1328_emission-carry/decisions.md`: APPENDED dated `D7-SUPERSEDED (2026-07-07)` note
  referencing this task; original D7 text untouched.
- `src/IntegrationInfra/IntegrationInfra.csproj`: `Version` 1.0.0-preview.2 → 1.0.0-preview.3.

## PHASE 5 — verification results
- `dotnet build IntegrationInfra.slnx`: Build succeeded, 0 errors (pre-existing warnings only: NU1507 +
  3 unrelated XML cref warnings).
- Tests (`dotnet test IntegrationInfra.slnx -f net8.0 --no-build`): all 7 suites green, 0 failures —
  FaultGovernance 20, Kernel 37, Reporting 15, Conversation 11, Job 31, Conducting 31, Emission 43
  (43 = 15 pre-existing + 14 atomic + 14 batch-scoped). Total 188/188.
  [CORRECTED — original draft mis-stated the Emission breakdown as 16+14+13; actual is 15+14+14.]
  [SUPERSEDED by Repair round 1: Emission is now 49 (15 + 20 atomic + 14 batch-scoped) — see below.]
- Grep-clean: `_multipartFinalized` 0 in code/tests; `record-too-large` 0; `batch-boundary` 0;
  `EnsureAppendWithinBatchLimit` 0; `FlushIfHasDataAsync` 0. (The 4 `_multipartFinalized` hits under
  `ai/active/2026-06-30_1328_emission-carry/` — 1 in execution_notes.md + 3 in decisions.md — are the
  append-only D7 history record plus the new D7-SUPERSEDED note that names it to explain its removal;
  intended, not code remnants.)
- `"storageUrl"` literal survivors (all wire-contract / canonical, none improper):
  `BatchScopedStorage.StorageUrlMetadataKey` const (the single canonical literal) + its XML-doc prose;
  `AdapterRunMetadata`/`AdapterEventMetadata` `[JsonPropertyName("storageUrl")]` (wire serialization);
  `Job/AdapterRunEnvelopeParser` `Get(md,"storageUrl")` (wire envelope parser — not touched by source A);
  `AdapterPlatformEventFactory` payload VALUE read (wire JSON property; its metadata KEY uses the const).
- DAG: `grep "using Cymulate.IntegrationInfra.Emission" src/IntegrationInfra/Conducting` → only the
  PRE-EXISTING `CollectorTriggerParsing.cs` `AdapterGlobalDefaults` edge (constraint-permitted, untouched).
  No new Conducting→Emission edge; Conducting RestoreBase sites import `Envelopes.Common`. FaultGovernance→
  Envelopes.Common edge pre-existed (A6). DAG intact, Conducting apex.

## 1:1 PARITY CHECKLIST (every source hunk → target)

### Changeset B (`git show 832754a`)
| Source hunk | Target | Status |
|---|---|---|
| NdjsonBatchSession: `_multipartCompleted`/`_multipartAborted` fields | Emission/Ndjson/NdjsonBatchSession.cs | ✅ (replaces D7 `_multipartFinalized`) |
| NdjsonBatchSession: AppendRecordAsync drop record-too-large + EnsureAppend call → WarnIfRecordExceedsSoftThreshold | same | ✅ |
| NdjsonBatchSession: FlushIfHasDataAsync→FinalizeAsync + CommitIncomplete + CompleteStartedMultipartAsync | same | ✅ |
| NdjsonBatchSession: EnsureAppendWithinBatchLimit→WarnIfRecordExceedsSoftThreshold | same | ✅ |
| NdjsonBatchSession: FlushAsync isEnd `_multipartCompleted=true` | same | ✅ |
| NdjsonBatchSession: catch → TryAbortMultipartAsync(ct) | same | ✅ |
| NdjsonBatchSession: TryAbortMultipartAsync signature + exactly-once + logged | same | ✅ |
| NdjsonBatchSession: DisposeAsync abort prologue | same | ✅ |
| NdjsonUtf8BatchSession: all 8 above (mirror) | Emission/Ndjson/NdjsonUtf8BatchSession.cs | ✅ symmetric |
| NdjsonOptions: SoftRecordWarningBytes | Emission/Ndjson/NdjsonOptions.cs | ✅ |
| NdjsonUtf8BatchSession.cs / NdjsonBatchSession.cs (twin file) | (covered) | ✅ |
| ResultsBatchPublisher: SoftRecordWarningBytes x2 + Finalize/CommitIncomplete x2 | Emission/ResultsBatchPublisher.cs | ✅ |
| ThrottlingOptions: const+prop+Resolve+FromConfiguration+TryReadOptionalLong | Emission/ThrottlingOptions.cs | ✅ |
| CollectorGlobalDefaults: DefaultSoftRecordWarningBytes | Emission/AdapterGlobalDefaults.cs | ✅ (D3/D8 name map) |
| README.md + README.Publishing.md stable-output/atomicity edits | Emission/README.md (single doc) | ✅ folded |
| AtomicStreamedObjectsTests.cs (new, 14 facts) | tests/.../AtomicStreamedObjectsTests.cs | ✅ plain Assert |
| FalconCollector ResultsBatchPublisherConstraintsTests.cs edits | — | N/A (collector test; behavior subsumed by AtomicStreamedObjectsTests giant-record + sub-min-buffer facts) |

### Changeset A (`git diff 8948fba..7218abc -- Shared/*`)
| Source hunk | Target | Status |
|---|---|---|
| BatchScopedStorage.cs (new, 157 lines) | Envelopes/Common/BatchScopedStorage.cs | ✅ verbatim, ns-only |
| NdjsonBatchSession ApplyStorageUrlPrefix literal→const | Emission/Ndjson/NdjsonBatchSession.cs | ✅ |
| NdjsonUtf8BatchSession ApplyStorageUrlPrefix literal→const | Emission/Ndjson/NdjsonUtf8BatchSession.cs | ✅ |
| README.md BatchScopedStorage bullet | Emission/README.md | ✅ folded |
| AdapterBusEntrypointRunner: using + RestoreBase (success DONE) + storageUrl literal→const | Conducting/AdapterBusEntrypointRunner.cs | ✅ |
| AdapterFlowFailureHandling: using + RestoreBase | FaultGovernance/AdapterFlowFailureHandling.cs | ✅ |
| AdapterPlatformEventFactory: using + guard read→const + SetIfMissing key→const (value stays literal) | Conducting/AdapterPlatformEventFactory.cs | ✅ |
| AdapterBusPartialSuccessPublisher: using + RestoreBase | Conducting/Bus/Logic/AdapterBusPartialSuccessPublisher.cs | ✅ |
| CollectorResumeLegacyExecutor: using + RestoreBase (return restructured) | Conducting/Collectors/Recovery/CollectorResumeLegacyExecutor.cs | ✅ |
| CollectorResumePartialSuccessPublisher: using + RestoreBase | Conducting/Collectors/Recovery/CollectorResumePartialSuccessPublisher.cs | ✅ |
| CollectorResumeStrategyExecutor: using + RestoreBase | Conducting/Collectors/Recovery/CollectorResumeStrategyExecutor.cs | ✅ |
| AdapterFailureDecisionExecutor: using(already present) + RestoreBase | FaultGovernance/Logic/AdapterFailureDecisionExecutor.cs | ✅ |
| BatchScopedStorageTests.cs (new, 13 facts) | tests/.../BatchScopedStorageTests.cs | ✅ plain Assert |
| collector-side files (source changeset A touched collectors) | — | N/A (contract: Shared slice only; collectors not ported) |

## RESIDUAL RISKS
- Warning-count fidelity: source counted `Mock<ILogger>.Invocations` where `Arguments[0] is LogLevel.Warning`;
  `CountingLogger` counts `Log(LogLevel.Warning,...)` calls. Semantically identical for these tests (only the
  soft-threshold path logs a Warning here). No behavior lost.
- `Assert.Contains(substr, result.StorageLocation)` — if `StorageLocation` were ever null the assert throws
  (as FluentAssertions `.Contain` would); non-null in the success paths exercised. Matches source semantics.
- Envelopes/Common now referenced from Emission sessions, Conducting, and FaultGovernance — Envelopes is a
  leaf; no cycle introduced (verified by DAG grep).

## REPAIR ROUND 1 (2026-07-07)

Verifier-1 PASSED (12-hunk parity sampling clean; 2 LOW doc nits). Code-reviewer-1 found 1 Major + 2
no-action minors. Actions taken:

**M1 (Major) — string-twin state-machine coverage gap.** The data-loss-critical facts ran only through
`PublishUtf8Async`, leaving the string session's rewritten `FinalizeAsync` /
`CompleteStartedMultipartAsync` / `TryAbortMultipartAsync` / `CommitIncomplete` paths without direct
coverage (twin drift would pass CI). Fix: the 6 critical facts in `AtomicStreamedObjectsTests.cs` are now
`[Theory]` over a new `PublishMode { String, Utf8 }`, dispatched by a `Publish(mode, …)` helper (+ a
`Decode` wrapper that decodes the shared byte-record sources to strings for the string path; filler
records are ASCII JSON so normalize is content/byte-count-identical → identical multipart part
boundaries). Each now runs on BOTH twins:
  - `GrowingCall_EscalatesToMultipart_UploadsOrderedParts_NoSinglePut(PublishMode)` — escalation + atomic complete
  - `MidStreamPartFailure_AbortsExactlyOnce_NoComplete_ThenRetrySucceeds(PublishMode)` — part-failure abort-once
  - `Cancellation_MidStream_AbortsInFlightMultipart(PublishMode)` — cancellation abort
  - `DisposeWithoutComplete_AbortsInFlightMultipart(PublishMode)` — dispose-without-complete abort
  - `CompleteFailure_AbortsExactlyOnce_AndSurfacesFailure(PublishMode)` — complete-failure abort-once
  - `MultipartEndingOnPostAppendFlush_StillCompletesOnce_AndDoesNotAbort(PublishMode)` — post-append-flush
    finalization; its `Assert.Equal(expected, AssembleMultipart())` now also gives multipart byte-identity
    on the STRING path (string records → expected UTF-8 bytes). Single-PUT string byte-identity remains
    covered by `SmallCall_String_...`.
Added `using Microsoft.Extensions.Logging;` for the `ILogger?` param on the `Publish` helper. Net: 6
critical facts → 6 theories × 2 = 12 cases (+6 new string-path cases). Atomic file: 8 `[Fact]` + 6
`[Theory]` = 20 cases.

**m1 (CommitIncomplete unreachable-but-defensive)** — KEPT as-is (reviewer + source parity). No change.
**m2 (three wire-contract `storageUrl` literals)** — deliberate wire contract. No change.

**Verifier LOW doc nits** — fixed above: Emission pre-repair breakdown corrected to 15+14+14=43 (was
mis-stated 16+14+13); exempt `_multipartFinalized` history-hit count corrected to 4 lines (1 in
execution_notes.md + 3 in decisions.md).

**Post-repair verification (verbatim):**
- `dotnet test tests/IntegrationInfra.Emission.Tests -f net8.0`:
  `Passed!  - Failed: 0, Passed: 49, Skipped: 0, Total: 49` — Emission now 49 (15 pre-existing + 20 atomic
  + 14 batch-scoped). Full-solution build: 0 errors. Other 6 suites unchanged (FaultGovernance 20, Kernel
  37, Reporting 15, Conversation 11, Job 31, Conducting 31). Grand total 194/194, 0 failures.
