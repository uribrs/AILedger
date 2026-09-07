# Constraints

## Architecture

- The normalizer is vendor-agnostic: no per-vendor branch, no vendor name in the manifest or in any code path.
- The collector never correlates. It guarantees the vendor parent key on both records and nothing more.
- Id derivation lives in the normalizer, never in the collector.
- Derived ids are scoped per `(instance, asset)`: within-run stable, not stable across runs.
- Target shape is the EA promotion boundary (`parser_output_assets_enrich` / `parser_output_exposures_enrich` column sets), not today's staging shape.
- No Spark, no Glue, no RabbitMQ, no ISB. Events are simulated in-process.

## Target shape (non-negotiable, derived from EA)

- PK is `(id, instance_id)` on both row types.
- `cve_id` is NOT NULL on exposures: the CVE explode happens before the boundary, one row per CVE.
- These are Postgres enums, and validation must check membership: `cybi.asset_type`, `cybi.asset_host_os_type`, `cybi.exposure_type`, `cybi.exposure_source_severity`, `cybi.exposure_status`.
- Timestamps are `timestamp(3) without time zone`.
- `additional_fields` is `jsonb` and must land as native JSON, not a JSON-encoded string.

## Label contract

- Generated from EA's schema, never hand-typed. Sources: `cybi-db-models/database-schemas-up-to-date/{prod-eu,stg}/`, `cybi-db-models/prisma/schema/`.
- Version-pinned. A batch declaring an unknown or mismatched version is rejected, not coerced.

## Repo and code

- No package versions in any `.csproj`. Central package management via `Directory.Packages.props`; defaults in `Directory.Build.props`.
- Never name a type, method, field, or file "Legacy".
- Secrets never committed. Local settings file is gitignored from the first commit.
- Small methods, helpers over long procedural bodies; mirror the shape of neighbouring code.

## Environment and safety

- Falcon lab tenant use is granted for a short real-time collection (~300 hosts, ~100k findings). Read-only vendor calls.
- Local Docker Postgres is granted.
- `s3://cybi-data/Uri-Tests/` is read-only reference. Do not model the new contract on its correlated envelope shape.
- Do not run FalconCollector suite sweeps, or ISBLoad / Dummy collector suites, in the adapters repo.

## Reporting

- Short, scannable output. Lead with the answer. Tool and test output must lead with its conclusion.
