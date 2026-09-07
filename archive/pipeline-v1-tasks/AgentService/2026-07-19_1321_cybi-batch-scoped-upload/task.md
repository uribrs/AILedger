# CYBI Batch-Scoped Upload (instanceBatchId mirror of adapters)

## What

Make AgentService's CYBI batch upload path produce Glue-reachable, per-batch
parseable output the same way the adapters repo does: each batch of NDJSON
rows lands under a deterministic `batch_{N:D6}/` subfolder of the run's
storage root, announced with a deterministic RFC 4122 v5 `instanceBatchId`
and an ISB-shape `metadata` object, so the already-merged consumer
(`cymulate-integrations` `connector-manager.service.ts`) can upsert the batch
doc and trigger per-batch Glue.

AgentService does not upload to S3 itself. It POSTs
`{action:"progress", stage:"batch_file", fileName, fileContent, data}` to
`POST {server}/cybi/{instanceId}` (CyAgentServer), which writes to S3. The
agent controls only the request body — the mechanism therefore lives entirely
in what the body carries: an extended `fileName` (path prefix) and new
metadata fields.

## Where

- `Source/Infrastructure/Cymulate.Agent.Infrastructure.Common/Services/CybiBatchUploader.cs`
  — all wire/mechanism changes (implementation only; interface frozen).
- `Source/Presentation/Cymulate.Agent.Executor/Actions/CyBI/CybiActionManager.cs`
  — executor-side enable hook (reads server-driven opt-in, arms the uploader).
- New static helper for the UUID port (Infrastructure.Common, mirrors
  adapters' `BatchScopedStorage.BuildBatchInstanceId`).
- Tests under `Tests/` mirroring the touched sources.

## Reference implementations / contracts

- Adapters mechanism (source of truth for UUID algorithm and semantics):
  `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/DataPipeline/Egress/BatchScopedStorage.cs`
- Consumer contract (read-only, merged, do not modify):
  `/Users/user/Dev/cymulate-integrations/apps/integration-connectors-manager/src/app/modules/connectors-manager/services/connector-manager.service.ts`
- Batch doc (unique index on `id`):
  `/Users/user/Dev/cySharedDBModels/src/models/cybi/client-integration-instance/cybiClientIntegrationInstanceBatch.model.ts`
- Field evidence that the agent already holds all metadata inputs:
  `CybiIntegrationActionParamsModel.IntegrationInstanceJson` (full
  `CybiClientIntegrationInstance` doc: `_id`, `id`, `clientID`,
  `integrationSettingId`, `integrationSettingFlowId`,
  `clientIntegrationFlowId`, `clientIntegrationId`).

## Out of scope

- CyAgentServer passthrough (separate cross-repo effort; repo not on this machine).
- cymulate-integrations consumer (merged, done).
- Glue/parsers (proven with `batch_NNNNNN/` folders).
- Per-collector opt-in wiring / collector code of any kind.
