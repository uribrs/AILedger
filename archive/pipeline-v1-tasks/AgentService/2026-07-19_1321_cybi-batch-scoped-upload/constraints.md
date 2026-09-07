# Constraints

- `ICybiBatchUploader` interface MUST NOT change (binary contract with dynamically-loaded collector assemblies).
- No change to any collector project, Actions.dll (`Cymulate.Agent.Application.Actions`), or any shared type consumed by dynamically-loaded assemblies.
- Allowed diff surface: `Infrastructure.Common` internals (CybiBatchUploader + new helper), executor-side hook in `CybiActionManager`, DI registration if needed, tests.
- Dormant (opt-in absent/off): wire payloads byte-identical to current behavior; disk behavior unchanged.
- UUID namespace is frozen: `b584b489-7c3d-4caf-97eb-49d7ff6d78fb` (same as adapters). Never change; changing re-mints all ids and breaks replay dedup.
- UUID algorithm: exact port of adapters' `BuildBatchInstanceId` — RFC 4122 v5, SHA-1, version/variant bit fix-up, big-endian, lowercase hyphenated string.
- UUID name input: `{instanceOid}/batch_{N:D6}` where instanceOid = integration instance `_id` (the `cybi/{instanceId}` URL id).
- Batch folder segment format: `batch_{N:D6}`, 1-based, run-global counter (not per-flow), deterministic — never random.
- One batch folder = one parse unit. The batch metadata (`instanceBatchId` + scoped `storageUrl`) must be announced EXACTLY ONCE per folder: the consumer triggers Glue on every progress event carrying `instanceBatchId` (no on-insert guard in `handleStreamingBatch`), so per-flush announcement of the same folder would multi-trigger Glue on a partially-written folder.
- Enabled path must create the local batch subdirectory before writing (today only the action folder is created; a subpath fileName throws DirectoryNotFoundException).
- The `stop`/`collection_complete` message shape stays as-is; any final open batch folder must be closed/announced before it is sent.
- Metadata object field names must match the consumer's `CollectorProgressMetadata` exactly: `instanceOid`, `instanceId`, `clientID`, `integrationSettingId`, `integrationSettingFlowId`, `clientIntegrationFlowId`, `clientIntegrationId`, `storageUrl`, `instanceBatchId`.
- `sequenceId` must be monotonically increasing per run (consumer guards on `batchingStats.lastSequenceId`).
- Thread safety: uploads may be concurrent (existing `Interlocked` usage is the precedent); counter and folder-close logic must be race-free.
- Coding standards: `ReadMEs/coding-standards.md` is authoritative; read before writing code.
- Do not commit or push without operator instruction.
