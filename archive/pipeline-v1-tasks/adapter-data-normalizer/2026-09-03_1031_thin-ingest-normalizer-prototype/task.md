# Thin ingest: normalizer prototype

Prove a `collector → normalizer → create-entities` path that removes the Glue/Spark parser stage from
third-party ingest. The prototype ends at the **data ready** stage.

## What it must show

A collector that does no correlation, only guaranteeing the vendor's parent key is present on both
the asset record and every finding record, can be followed by one generic, vendor-agnostic normalizer
that produces rows Exposure Analytics can create entities from directly — with no Spark, no staging
reshape, and no per-vendor code path in the normalizer.

## Shape

Repo: `/Users/user/Dev/Uri/localprojects/adapter-data-normalizer` (empty, branch `main`, no commits).
C# .NET 8, one project per concept.

| Project | Concept |
|---|---|
| `src/ThinFalconCollector` | Uncorrelated, batched publish from the live Falcon lab tenant. Two flat lanes; parent key on both. |
| `src/Contract` | Target row shapes, enum sets, batch manifest, validation. Label contract generated from EA's schema. |
| `src/Normalizer` | Labeled JSON to target rows. Owns id derivation. |
| `src/Writer.Postgres` | COPY into two tables shaped as the EA enrich tables. |
| `src/Host` | Simulated events: work available, then data ready. |
| `tests/` | xUnit, including the SQL proof. |

## Status of this work

Prototype. The operator expects the shape to change as it is built, and has said so explicitly.
Nothing here is a production commitment.
