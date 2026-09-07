# Prompt Contract — thin ingest normalizer prototype

## Role

You are a senior .NET engineer building a data-pipeline prototype against a fixed downstream database
contract.

## Goal

In `/Users/user/Dev/Uri/localprojects/adapter-data-normalizer` (empty repo, branch `main`, no commits),
build a C# .NET 8 solution that collects from the live Falcon lab tenant without correlating, normalizes
the result through one vendor-agnostic service, writes it to a local Postgres in the shape Exposure
Analytics creates entities from, and reaches a **data ready** signal proven by SQL.

## Context

**The problem being disproven.** Exposure Analytics never computes the asset↔exposure link. It
dereferences `asset_id` with `INNER JOIN LATERAL (SELECT id FROM integration.parser_output_assets WHERE
id = be.parser_asset_id LIMIT 1)` and drops the row when it does not resolve
(`cybi-db-models/migrations/20260527124613_drop_parser_output_pk/migration.sql:239-244`). Today that
value is a random Spark uuid (`cymulate-integration-parsers/libs/packages/utilities/helpers.py:53`),
linked either by stamping a uuid before exploding a nested findings array
(`yaml_parser.py:341,460`) or by a Spark join on hostname plus type (`yaml_parser.py:488`). No vendor key
reaches Postgres. The prototype shows the Spark stage is unnecessary when the collector carries the
vendor's own parent key on both records.

**Reference sources to read, not to copy.**

- `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Collectors/FalconCollector`
  — what a collector does: Discover host inventory, Spotlight vulnerabilities, paging, batch publish.
  Its correlated envelope is the thing being removed; take the vendor mechanics, not the shape.
- `AidExtractor.ExtractRecordKey` in that collector — the parent-key rule for Falcon.
- `/Users/user/Dev/cymulate-exposure-analytics/cybi-db-models/database-schemas-up-to-date/{prod-eu,stg}/`
  — pg_catalog ground truth for the target tables and `cybi.*` types.
- `/Users/user/Dev/cymulate-exposure-analytics/cybi-db-models/prisma/schema/` — model-level view of the
  same tables.
- `s3://cybi-data/Uri-Tests/` (aws CLI works, read-only) — real collector output for shape reference.

**Credentials.** The Falcon block in
`/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Tools/Cymulate.Integration.Adapters.Tools.LocalAdapterRunner/appsettings.local.json`.
Copy it into the prototype's own gitignored local settings. The operator granted use of the live lab
tenant: ~300 hosts, ~100k findings.

**Projects.**

| Project | Responsibility |
|---|---|
| `src/Contract` | Generated label contract (columns, types, enum members, version), target row shapes, batch manifest model, manifest and row validation |
| `src/ThinFalconCollector` | Two flat lanes (assets, findings) from the live tenant, parent key present on both records, batched publish to local disk with a manifest |
| `src/Normalizer` | Labeled JSON to target rows, uuidv5 id derivation, CVE explode |
| `src/Writer.Postgres` | Docker Postgres schema for the two target tables plus a COPY-based writer |
| `src/Host` | Simulated `work available` then `data ready` events, counts payload |
| `tests/` | xUnit, including the SQL proof |

**Manifest contents.** Which file is which entity, the parent key field name, the scope ids
(`client_id`, `client_integration_id`, `instance_id`, `integration_setting_id`,
`integration_setting_flow_id`, `connector_flow_name`), and the label-contract version. No vendor name.

**Target columns.**

Asset: `id`, `instance_id`, `client_id`, `client_integration_id`, `type`, `value`, `os_type`,
`os_version`, `os_build`, `fqdn`, `group_names[]`, `site_names[]`, `tags`, `ip_address[]`, `first_seen`,
`last_seen`, `integration_setting_id`, `integration_setting_flow_id`, `connector_flow_name`,
`additional_fields`, `created_at`, `risk_score`.

Exposure: `id`, `instance_id`, `cve_id`, `client_id`, `client_integration_id`, `name`, `display_name`,
`type`, `severity`, `asset_id`, `status`, `first_seen`, `last_seen`, `integration_setting_id`,
`integration_setting_flow_id`, `connector_flow_name`, `additional_fields`, `description`, `mitigation`,
`created_at`.

## Constraints

See `constraints.md` — every entry there is binding. The load-bearing ones:

- Normalizer has no per-vendor branch and no vendor name anywhere in its code path.
- Collector does not correlate; it only guarantees the parent key on both records.
- Id derivation is in the normalizer, scoped per `(instance_id, vendor parent key)`.
- PK `(id, instance_id)`; `cve_id` NOT NULL; five `cybi.*` enums validated by membership;
  `timestamp(3) without time zone`; `additional_fields` native `jsonb`.
- Label contract generated from EA's schema, version-pinned, unknown version rejected.
- No package versions in any `.csproj`.
- No type, method, field, or file named "Legacy".
- No secret committed.
- No Spark, Glue, RabbitMQ, or ISB.

## Success Criteria

1. `dotnet build` succeeds on the solution, and `dotnet test` passes.
2. A batch whose manifest declares an unknown label-contract version is **rejected**, and a test proves
   the rejection rather than a coerced parse.
3. A live collection against the lab tenant produces both lanes on disk with a manifest, and the parent
   key is present on every record of both lanes — asserted, not eyeballed.
4. Both lanes are written to the local Postgres target tables.
5. SQL proves zero unresolved parent keys:
   `SELECT count(*) FROM exposures e WHERE NOT EXISTS (SELECT 1 FROM assets a WHERE a.id = e.asset_id AND a.instance_id = e.instance_id);`
   returns `0`, and the query output is recorded in `execution_notes.md`.
6. Re-normalizing the same batch produces byte-identical ids — a test proves reparse stability.
7. The host prints a `data ready` payload carrying asset and exposure counts that match the row counts
   in Postgres.
8. `README.md` states how to run it: docker up, collect, normalize, prove.

## Execution Rules

- Do not assume missing data. Read the referenced sources before writing the code that depends on them.
- Respect constraints strictly.
- Generate the label contract; do not hand-type column or enum lists. If a needed enum's members are not
  recoverable from the dumps or Prisma, stop and report rather than inventing them.
- Append every material finding, decision drift, and the SQL proof output to `execution_notes.md`.
- Update `state.json` when a step's status changes, a required file is created, or a blocker appears.
- Vendor calls are read-only. Never write to the Falcon tenant.
- Never commit `appsettings.local.json` or any credential; add the gitignore entry before the first
  settings file exists.
- Do not run FalconCollector suite sweeps, or ISBLoad / Dummy collector suites, in the adapters repo.

## Output Format

- Working solution in the repo, one project per concept as tabled above.
- `execution_notes.md` carrying findings, the SQL proof output, and any drift from `decisions.md`.
- `state.json` current.
- Final report: short and scannable. Lead with whether the exit bar was met. Name every success
  criterion that was not met and why. No narrative.

## Stop Conditions

- The exit bar is met and reported.
- A required source cannot be read (credentials, S3, schema dumps).
- Enum members cannot be generated from the available sources.
- Docker or Postgres cannot be started locally.
- The zero-unresolved-parent-keys proof fails, which makes the batch failure policy blocking rather
  than deferrable.
- A constraint would have to be violated to proceed.
