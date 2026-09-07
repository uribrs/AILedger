# Decisions

- UUID is deterministic v5, not random v4 — replay/resume republish must reproduce the same id so the unique index + consumer upsert dedupe (operator-approved).
- Metadata-only change; folder names keep `batch_{page:D6}` — upstream reads the path from `storageUrl`/`s3Key`, id and path are separate fields.
- v5 name input is `"{base}/batch_{page:D6}"` — recomputable from the announced path alone.
- Namespace GUID: `b584b489-7c3d-4caf-97eb-49d7ff6d78fb`, frozen.
- Implement v5 locally (SHA-1, ~20 lines) — .NET 8 has no built-in; no new package.
- Work happens on `batchful-uploads-uuid` off dev (implementation already merged to dev); original `batchful-uploads` branch left as-is.
