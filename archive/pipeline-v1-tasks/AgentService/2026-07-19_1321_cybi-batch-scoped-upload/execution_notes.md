# Execution Notes

## 2026-07-19 — contract-driven-execution (initial implementation)

### Files touched

Source (allowed diff surface only):
- NEW `Source/Infrastructure/Cymulate.Agent.Infrastructure.Common/Services/CybiBatchScopedStorage.cs`
  — exact port of adapters' `BuildBatchInstanceId` (UUIDv5, frozen namespace
  `b584b489-7c3d-4caf-97eb-49d7ff6d78fb`) + `BuildBatchSegment`.
- NEW `Source/Infrastructure/Cymulate.Agent.Infrastructure.Common/Services/ICybiBatchScopeController.cs`
  — arming interface + `CybiBatchScopeOptions` (incl. `TryParse` over the action raw-parameters JSON).
- MOD `Source/Infrastructure/Cymulate.Agent.Infrastructure.Common/Services/CybiBatchUploader.cs`
  — implements `ICybiBatchScopeController`; per-run scope state (SemaphoreSlim-gated), run-global
  batch counter, scoped disk subfolders, scoped wire prefix (`sequenceId`, `itemCount`, `metadata`),
  `instanceBatchId` on folder-closing uploads only, final-partial-batch close appended to the
  pre-completion progress update (success only). `ICybiBatchUploader` UNCHANGED.
- MOD `Source/Infrastructure/Cymulate.Agent.Infrastructure.DI/Extensions/ServiceCollectionExtensions.cs`
  — one singleton behind both interfaces.
- MOD `Source/Presentation/Cymulate.Agent.Executor/Actions/CyBI/CybiActionManager.cs`
  — ctor gains `ICybiBatchScopeController` (resolved by ActivatorUtilities, no factory change);
  `TryArmBatchScope` on `CybiRunIntegration` in `initConfigurationInstance`; arming failure never
  fails the action.

Tests (all green, 205/205 project-wide):
- NEW `CybiBatchScopedStorageTests` — UUID vectors pinned against independent Python `uuid.uuid5`
  (d3070c71-5d51-55dc-a36c-a07fdef35ae1 etc.), version/variant bits, segment format.
- NEW `CybiBatchScopeOptionsTests` — opt-in bool/object forms, thresholds, rejection paths,
  consumer-shape metadata field names.
- NEW `CybiBatchScopedUploadTests` — dormant byte-identity (payload string rebuilt independently),
  run-global counter cross-flow non-collision, announce-once-per-folder, byte-threshold close,
  temp-file variant, failure keeps scoped file + counters unchanged, completion-time close exactly
  once, failure progress update never announces, foreign actionId stays dormant.

### Placeholder keys / provisional values (per contract A1/A6)
- Opt-in key: `batchScopedUpload` (bool or `{enabled, maxFilesPerBatch, maxBatchContentBytes}`),
  sibling of `instance` in the CYBI raw parameters. Rename requires touching
  `CybiBatchScopeOptions.OptInSectionName` only.
- Defaults: `maxFilesPerBatch=10`, `maxBatchContentBytes=200_000_000` — provisional (A6).
- `metadata.storageUrl` value is RELATIVE to the run's storage root: `{actionId}/batch_NNNNNN`.
  The backend, which owns the root, absolutizes it (documented in code; A2 territory).

### Wire shape when armed (batch_file POST)
`{action, stage, data, fileName: "batch_NNNNNN/<name>", sequenceId, itemCount, metadata:{instanceOid,
instanceId?, clientID?, integrationSettingId?, integrationSettingFlowId?, clientIntegrationFlowId?,
clientIntegrationId?, storageUrl, instanceBatchId(closing only)}, fileContent}`.
Final partial batch: same `sequenceId/itemCount/metadata` (with instanceBatchId) appended to the
pre-completion `action=progress` update. Stop message untouched.

### Commands run
- `dotnet build AgentService.sln` — Build succeeded, 0 errors.
- `dotnet test .../Cymulate.Agent.Infrastructure.Common.Tests.csproj` — 205 passed / 0 failed.
- `dotnet test .../Cymulate.Agent.Executor.Tests.csproj` — 6 failures, PROVEN PRE-EXISTING:
  identical failures on pristine HEAD via stash (Bas2 file-fixture + crypto env issues, unrelated).
- `dotnet format --verify-no-changes --include <touched>` — clean except ENDOFLINE noise caused by
  `core.autocrlf=input` + the stash cycle normalizing the whole working copy to LF (repo stores LF;
  commit-time normalization makes this a non-issue). BOM added to all new files.

## 2026-07-19 — repairs after verifier-1 (PASS WITH GAPS)

- Finding 1 (Medium): completion-close committed counters before the send. Fixed —
  `TryAppendFinalBatchCloseAsync` no longer resets; `CommitFinalBatchCloseAsync` runs only after
  the progress update POST succeeds. A failed send leaves the folder open; a retried update
  re-announces the same replay-stable instanceBatchId (test:
  `Armed_FinalCloseSendFails_FolderStaysOpenAndRetryReAnnouncesSameBatchId`).
- Finding 2 (Medium test gap): added `Armed_ActionIdDiffersFromInstanceOid_...` — dual-key
  registration, UUID base pinned to InstanceOid (new independent vector
  42b6f975-86cb-5e25-93b4-dcab84ae4472), shared run-global counter across both keys.
- Finding 3 (Info, cross-repo): `itemCount` semantics differ between batch_file uploads (rows in
  THIS file) and the completion-close progress update (rows in the whole final folder). The
  CyAgentServer half (A2) must map batchedAssets/batchedFindings from batch_file events only —
  mapping the completion event too would double-count the final folder. RECORDED AS SERVER-HALF
  CONTRACT NOTE; no agent-side change.
- CybiBatch tests after repairs: 36/36 green.

### Residual risks
- If a folder-closing upload fails and the collector tolerates it (none do today — they abort),
  the close is re-attempted on the next upload of that folder; the folder is never announced twice.
- Scoped uploads are serialized per run (SemaphoreSlim). Concurrency loss is negligible —
  uploads are network-bound and interleave with collection.
- Completion-time close rides `SendProgressUpdateAsync` and fires only on `isSuccessful=true`;
  a failed run leaves the final folder unannounced by design (consumer skips failed batches anyway).
