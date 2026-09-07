# Internal Recon

Recon ran read-only (Explore agent, no write tool); this file is written by the orchestrator from its
returns. Q1/Q2 below; Q3–Q6 appended when the second pass returns.

## Durable sources read

- `cymulate-integration-adapters/CLAUDE.md` — adapter kinds, substrate boundary, constants ownership.
- `cymulate-integration-adapters/ai/skills/collector-flow-patterns/SKILL.md` — the correlated-flow shape
  this prototype deliberately does not reproduce.
- `cymulate-exposure-analytics/cybi-db-models/database-schemas-up-to-date/` — pg_catalog ground truth.

## Files in scope

Greenfield: no existing files are modified. The files below are *reference only* — read, never copied.

- `Collectors/FalconCollector/Processing/Configuration/FalconCollectorConfigurationBuilder.cs` — session/auth spec, config key aliases.
- `Collectors/FalconCollector/Processing/Configuration/FalconCollectorConfiguration.cs` — endpoints, page sizes, resilience defaults.
- `Collectors/FalconCollector/Processing/Urls/FalconUrls.cs` — Discover and Spotlight URLs and facets.
- `Collectors/FalconCollector/Flows/SharedFlows/FalconHostFilters.cs` — FQL construction and timestamp format.
- `Collectors/FalconCollector/Flows/SharedFlows/FalconCursorPagination.cs` — cursor paging and depth cap.
- `Collectors/FalconCollector/Flows/Findings/Correlated/FalconDiscoverHostScroller.cs` — Discover scroll.
- `Collectors/FalconCollector/Flows/Findings/Correlated/FalconSpotlightBatchScroller.cs` — Spotlight scroll and filter.
- `Collectors/FalconCollector/Flows/Findings/Hosts/AidExtractor.cs` — the parent-key rule.

## Patterns to mirror

- OAuth2 client-credentials against `/oauth2/token`, derived from the API endpoint, never configured
  separately → `FalconCollectorConfiguration.cs:38`.
- Cursor paging on an opaque `after` token read from the response body; terminal empty string means done
  → `FalconDiscoverHostScroller.cs:60-64,119`; assets lane equivalent at `FalconAssetsScrollRunner.cs:112`.
- FQL time gating as `last_seen_timestamp:>='yyyy-MM-ddTHH:mm:ssZ'` → `FalconHostFilters.cs:7,14-36`.
- Discover page size default 1000 → `FalconCollectorConfiguration.cs:77`.
- Spotlight request: `GET /spotlight/combined/vulnerabilities/v1?limit=&facet=cve&facet=remediation&facet=evaluation_logic&sort=updated_timestamp.asc`,
  `host_info` deliberately not requested → `FalconUrls.cs:29-42`.
- Spotlight filter shape: `suppression_info.is_suppressed:!'true'+status:['open','reopen','closed']+aid:[…]`
  → `FalconSpotlightBatchScroller.cs:112-115`.
- Config keys are read case-insensitively across aliases (`ApiEndpoint|baseUrl|Host|Url`,
  `ClientId|clientID`, `ClientSecret`) → `FalconCollectorConfigurationBuilder.cs:128-135`.

## Vendor mechanics — Q1 detail

- Session is declarative (`SessionSpec` with `AuthSelection.OAuth2`), the adapter never issues the token
  request itself → `FalconCollectorConfigurationBuilder.cs:348-376`.
- Fixed User-Agent `Cymulate_Agent_v2 Cymulate-Agent/1.0` → `FalconCollectorConfigurationBuilder.cs:25,367`.
- Discover: `GET /discover/combined/hosts/v1?limit={limit}&sort=last_seen_timestamp.asc&facet=risk_factors&facet=third_party&facet=system_insights`
  plus url-escaped `&filter=` → `FalconUrls.cs:10-23`.
- Cursor depth capped at 500 pages then re-anchored from the watermark →
  `FalconCollectorConfiguration.cs:157`, `FalconCursorPagination.cs:27-34`. The prototype does not need
  re-anchoring, but must not assume unbounded cursor depth.
- Timeout 5 min; token-bucket limiter capacity 35 / refill 50 / queue 60; circuit breaker 3 failures per
  300s → `FalconCollectorConfiguration.cs:41-62`.

## Credentials — Q2

Structure only, no values. The Falcon block in the LocalAdapterRunner `appsettings.local.json` supplies
the same keys the builder reads: API endpoint, client id, client secret. The token endpoint is derived,
so it is not present. Copy the block into the prototype's own gitignored settings file.

## Shared surface to freeze

Named, not designed — phase 0 produces these before any consumer project starts:

- The two target row types (asset row, exposure row) matching the enrich column sets.
- The batch manifest model: entity-per-file mapping, parent key field name, the six scope ids, label-contract version.
- The label-contract model: column name, Postgres type, nullability, and enum member sets.
- The id-derivation signature: `(instance_id, vendor parent key) -> Guid`.
- The validation result type used by both the manifest check and the row check.

## Disjoint sets available

Once the surface above is frozen, these build independently:

- `src/ThinFalconCollector` — vendor calls plus batch/manifest emission; consumes the manifest model only.
- `src/Normalizer` — labeled JSON to row types plus id derivation.
- `src/Writer.Postgres` — DDL plus COPY; consumes the row types only.
- `src/Host` — event simulation; consumes all three but writes no logic of its own.
- `tests/` — must be owned separately from the implementation it asserts against.

Cannot be split: `src/Contract` is the surface itself, so it is phase 0 and cannot run in parallel with
its consumers.

## Landmines

- Pending Q6 (dotnet SDK list, Docker up/down, greenfield conventions).

---

# Q3–Q6 (second recon pass)

## Enum members — GO, all five recoverable

Not in the dumps (`grep -rn "AS ENUM" database-schemas-up-to-date/` = 0). Authoritative source is
`prisma/schema/enum.prisma`, corroborated by `CREATE TYPE` DDL in `migrations/`.

- `cybi.asset_type` — `enum.prisma:50-76`, 23 members. DDL `migrations/20250306140804_init/migration.sql:14`, rebuilt `20250424182444_.../migration.sql:29-47`, extended `20250626120555_.../migration.sql:12-16` and `20260729120000_add_cloud_resource_to_asset_type/migration.sql:14`.
- `cybi.asset_host_os_type` — `enum.prisma:78-96`, 15 members. **Wire labels differ from Prisma identifiers**: DDL `migrations/20250617115755_asset_host_os_type_add_enum/migration.sql:4-20` has `windows-server` and `active-directory` hyphenated; Prisma spells them `windows_server` / `active_directory` with `@map`. Generate from the `@map` value.
- `cybi.exposure_status` — `enum.prisma:98-104`: `opened, resolved, reopened`. DDL `20250306140804_init/migration.sql:17`. Not to be confused with `exposure_status_phase2` (`enum.prisma:106-113`).
- `cybi.exposure_source_severity` — `enum.prisma:191-199`: `critical, high, medium, low, info`. DDL `20250306140804_init/migration.sql:29`.
- `cybi.exposure_type` — `enum.prisma:212-227`, 12 members. DDL `20250306140804_init/migration.sql:35` plus three later migrations.

No machine-readable form of the dumps exists: `generate.mjs` emits Markdown only and needs live `PG*`
credentials to re-run. The `.md` tables are the only committed view of the live shape; `prisma/schema/`
is the committed machine-readable intended shape.

**Generator inputs decided:** column list from the dump `.md` tables, enum members from `enum.prisma`.

## Target columns — confirmed

`parser_output_assets_enrich` (`prod-eu/integration/parser_output_assets_enrich.md:11-32`, PK `(id, instance_id)` at `:36`) and
`parser_output_exposures_enrich` (`:11-30`, PK at `:34`) match the contract's column lists exactly.

Type facts that bind the writer:

- `tags` is `text` NOT NULL default `'{}'::text` — **not** an array, unlike `group_names`, `site_names`, `ip_address` which are real `text[]`.
- `cve_id` is NOT NULL on exposures.
- Six columns are `timestamp(3) without time zone`.
- Attnum gaps (assets has no 3, exposures no 4) are dropped columns. Address columns by name.

Cluster differences: stg carries `batch_id uuid` on both enrich tables (`migrations/20260630120000_add_batch_id_columns/migration.sql:9-10`) plus two `(instance_id, batch_id)` indexes; prod-eu's 2026-07-10 dump has neither. Exposure indexes also diverge. Column sets are otherwise identical.

Prisma-only, absent from both dumps: `batch_id` and six cloud-inventory columns (`sub_type`, `cloud_platform`, `region`, `cloud_account_id`, `cloud_account_name`, `cloud_provider_url`) — `integration.parser-output-assets-enrich.prisma:4,26-31`.

## Environment and conventions

- `dotnet --list-sdks`: 8.0.411, 8.0.422, 9.0.301, 10.0.301. No `global.json` in any operator repo — pin via `TargetFramework`, never `-f`.
- Docker is **up**, no containers running. Reusable compose exemplar: `IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/docker-compose.yml:2-16` — `postgres:15-alpine`, host port 15432, `postgres/postgres`, `pg_isready` healthcheck.
- csproj conventions: `net8.0`, `ImplicitUsings enable`, `Nullable enable` (`…FalconCollector.csproj:3-8`).
- Central package management on: `Directory.Packages.props:3` `ManagePackageVersionsCentrally`, `<PackageVersion Include Version>` entries; csprojs reference versionless.
- `Directory.Build.props` carries shared MSBuild props, never versions.
- Test stack xUnit + Moq + FluentAssertions, `IsPackable=false`, `IsTestProject=true`, `<Using Include="Xunit" />`.
- `InternalsVisibleTo` lives in the production csproj; production types are `internal sealed` by default.
- `Npgsql` is pinned at **10.0.3** in three separate operator repos — match it.

## Npgsql exemplars

- **No `BeginBinaryImport` / `NpgsqlBinaryImporter` / `COPY … FROM STDIN` anywhere under `/Users/user/Dev`.** The COPY path has no in-house precedent: new code, and each column type needs a round-trip test.
- Raw-SQL exemplar to mirror: `IntegrationServiceBus/.../E2EPanel/Pipeline/PipelineDbReader.cs:15-20` (one `NpgsqlDataSource` held in a field, `IAsyncDisposable`), `:27-37` (const SQL, `CreateCommand`, `AddWithValue`, `await using` reader), `:38-55` (explicit `IsDBNull` per nullable ordinal).
- Local-Postgres harness exemplar: `Cymulate.Integration.Adapters.YamlAdapter.E2E/IsbE2EFixture.cs:23,28,64,346-348` — port 15432 with a preflight port check.
- Npgsql 6+ throws on a UTC-kind `DateTime` or a `DateTimeOffset` written to `timestamp without time zone`; use `DateTimeKind.Unspecified`.

## Reference data shapes (real objects read from S3)

Both files are NDJSON, one record per line, at the run root — `batch_000001/` does not exist under
`fix-with-subfolders-001` despite the run name.

- `assets_000001.json` record 1: 65 top-level keys, a verbatim Discover host. Carries both `id` and `aid`, plus `hostname`, `platform_name`, `os_version`, `local_ip_addresses`, `first_seen_timestamp`, `last_seen_timestamp`, `criticality`, `device_policies`.
- `findings_000001.json` record 1: envelope keys `aid, chunk, isLastChunk, findingsInChunk, host, findings`. `.host` is the same Discover shape (66 keys here — vendor-optional fields make the union wider than any one record).
- **`findings[0]` carries `aid`, identical to the envelope's** — 12 keys: `id, cid, aid, vulnerability_id, vulnerability_metadata_id, data_providers, created_timestamp, updated_timestamp, status, confidence, remediation, cve`. `apps`, `suppression_info`, `host_info` are stripped by `FalconCorrelatedRecord.cs:21,35-38`.

Consequence: the Spotlight finding row is self-sufficient for the asset FK, so a non-correlating
collector needs no Discover pre-pass to know which host a finding belongs to.

Tenable.io layout was not enumerated (needs ListBucket): per code it is `assets_*.json` + `findings_*.json`
co-located in one batch dir (`TenableIoCollector.cs:218-223`) with `_staging/manifest.json`,
`_staging/spine/<assetId>.json`, `_staging/claims/chunk_<n>.ids` (`TenableIoSpinePaths.cs:52-100`).

## Landmines (replaces the pending note above)

- `tags` is `text` NOT NULL, not `text[]` — an array written there fails the COPY.
- Six `timestamp(3) without time zone` columns (first_seen, last_seen, created_at on each table) — `DateTimeKind.Unspecified` required.
- Two `asset_host_os_type` labels are hyphenated in the DB, underscored in Prisma.
- prod-eu and stg enrich tables differ by `batch_id`; the generated contract must record which cluster it came from.
- No in-house binary-COPY precedent, so no exemplar to mirror for the writer's hot path.
