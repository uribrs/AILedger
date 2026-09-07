# Constraints

- 1:1 functionality with the adapters source; behavior-named symbols carry unchanged (`BatchScopedStorage`, `SoftRecordWarningBytes`, `FinalizeAsync`, `CommitIncomplete`, `TryAbortMultipartAsync`).
- D7 mechanism is REPLACED, not layered on: `_multipartFinalized` must not survive anywhere.
- Both session twins (`NdjsonBatchSession`, `NdjsonUtf8BatchSession`) change symmetrically — the changed logic must be byte-for-byte equivalent between them (documented C2 fork risk).
- Nothing in the session path may throw on record or object size after the carry.
- `MaxBytesPerBatch` semantics unchanged: part/memory discipline only.
- D-charter DAG intact: Conducting must NOT gain an Emission dependency (beyond the pre-existing `AdapterGlobalDefaults` constants edge in `CollectorTriggerParsing`); `BatchScopedStorage` lives in `Envelopes/Common`.
- FaultGovernance→Envelopes.Common edge: verify the FaultGovernance carry docs do not forbid it — STOP if they do.
- D8 naming: `Adapter*` names in Emission/FaultGovernance; `Collector*` KEPT in Conducting recovery executors (D2-conducting).
- D4-deferred reshape untouched (static `AdapterNdjsonPublisher`, findings/assets hardwiring) — no opportunistic cleanup.
- SDK types (`AdapterProgressContext`, `IAdapterDataPublisher`, `PublishResult`, `MultipartUploadSession`) are external `Cymulate.Integration.Sdk` 3.2.0 — never re-declared.
- D-C1b honored: terminal publishes keep `CancellationToken.None`; `RestoreBase` is pure metadata mutation, no token.
- Tests: plain xUnit `Assert` only — FluentAssertions must NOT be added to the package; `RecordingPublisher` extracted as one shared helper; internals via existing `InternalsVisibleTo`.
- History discipline: APPEND the D7-SUPERSEDED note to `ai/active/2026-06-30_1328_emission-carry/decisions.md`; do not rewrite existing entries.
- No changes to the adapters repo, ISB, or the SDK.
- Package `Version` → `1.0.0-preview.3` in `src/IntegrationInfra/IntegrationInfra.csproj`.
- Task-state mirror: `~/codex-state/tasks/IntegrationInfra/2026-07-07_1730_atomic-objects-and-batch-scoping-carry/`.
