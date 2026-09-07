# Assumptions

- VALIDATED — Upstream batch id field is `CybiClientIntegrationInstanceBatch.id`: string, `unique: true` collection-wide (read from cySharedDBModels source).
- VALIDATED — Consumer (cymulate-integrations, branch CA-74750-multi-batch) upserts on `{ id: batchId }`; replay idempotency depends on deterministic ids (read from branch source).
- VALIDATED — Base storage URL is unique per run and stable across resume (rides the PlatformEvent), so v5(base, page) is globally unique and replay-stable.
- VALIDATED — Postgres `@db.Uuid` (cybi-db-models mirror) accepts any RFC 4122 UUID; version bits irrelevant.
- OPEN — ISB delivery of the `instanceBatchId` metadata key end-to-end: operator states ISB is a pass-through; local ISB clone's `MessageMetadata.FromDictionary` appeared to drop unknown keys. Not blocking this adapter-side change; flagged for the ISB/consumer owners.
