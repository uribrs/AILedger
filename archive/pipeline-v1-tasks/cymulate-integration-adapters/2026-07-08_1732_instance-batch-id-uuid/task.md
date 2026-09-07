# Task: instanceBatchId → deterministic UUID

Make the batch-scoped storage `instanceBatchId` metadata value a deterministic RFC 4122 UUIDv5 instead of the literal folder segment (`batch_000003`), so the upstream `CybiClientIntegrationInstanceBatch.id` (string, collection-wide unique) receives a UUID-shaped, globally unique, replay-stable identifier.

- Branch: `batchful-uploads-uuid` off `origin/dev` (3dce856); `BatchScopedStorage` is already on dev.
- Storage layout is untouched: folder segment stays `batch_{page:D6}`; only the announced metadata value changes.
- Design is operator-approved; encode it, do not re-derive.
