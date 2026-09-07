# Carry: Atomic Streamed Objects + Batch-Scoped Storage

Carry two adapters-repo Shared changesets into the package, 1:1 functionality. Source repo: `/Users/user/Dev/cymulate-integration-adapters` (dev).

## Changeset B — atomic size-unbounded egress objects (source commit `832754a`) → `Emission/`

- **Replace D7** (interim `_multipartFinalized` single flag) with the source's superset: `_multipartCompleted`/`_multipartAborted` pair + exactly-once `TryAbortMultipartAsync` (abort on mid-stream failure, cancellation, dispose-without-complete; abort failures logged never thrown; completed uploads never aborted) — in BOTH session twins, symmetrically.
- **`FinalizeAsync`** replaces `FlushIfHasDataAsync` at end-of-stream: residual records → end flush; else started-uncompleted multipart → `CompleteStartedMultipartAsync`. Fixes the M1 empty-residual silent-loss bug the package still carries.
- **`CommitIncomplete` gate** in `ResultsBatchPublisher`: no success for an uncommitted multipart (throws `DataPipelineException`).
- **Delete** the `record-too-large` throw and `EnsureAppendWithinBatchLimit` (`batch-boundary`). Nothing in the session path throws on record/object size.
- **`SoftRecordWarningBytes`** (24MiB default, env `PublishThrottling__SoftRecordWarningBytes`): `AdapterGlobalDefaults` → `ThrottlingOptions` → `NdjsonOptions` → per-record `LogWarning`. Log-only. `MaxBytesPerBatch` unchanged (part/memory discipline).

## Changeset A — batch-scoped storage (source: adapters dev merges #265/#266)

- **`BatchScopedStorage.cs` → `Envelopes/Common`** (operator-decided home: it manipulates the run metadata envelope; `Envelopes/Common` owns the `storageUrl` wire contract; preserves the D-charter apex rule — Conducting never depends on Emission). Verbatim carry except namespace; its metadata-key constants become canonical.
- Sessions' `ApplyStorageUrlPrefix` swaps literal `"storageUrl"` for `BatchScopedStorage.StorageUrlMetadataKey`.
- **`RestoreBase` at seven run-level publication sites** (mirror source call sites exactly): `Conducting/AdapterBusEntrypointRunner` (success DONE), `Conducting/AdapterPlatformEventFactory` (incl. batch-tail strip on resume round-trip), `Conducting/Bus/Logic/AdapterBusPartialSuccessPublisher`, `Conducting/Collectors/Recovery/{CollectorResumeStrategyExecutor, CollectorResumeLegacyExecutor, CollectorResumePartialSuccessPublisher}`, `FaultGovernance/AdapterFlowFailureHandling`, `FaultGovernance/Logic/AdapterFailureDecisionExecutor`.

## Tests

Port `AtomicStreamedObjectsTests` + `BatchScopedStorageTests` into `tests/IntegrationInfra.Emission.Tests`, translated to plain xUnit `Assert` (no FluentAssertions), `RecordingPublisher` as a shared helper. First session-level tests in the package (closes review gap C3).

## Also

Emission README update; D7-SUPERSEDED note appended to `ai/active/2026-06-30_1328_emission-carry/decisions.md`; `Version` → `1.0.0-preview.3`.
