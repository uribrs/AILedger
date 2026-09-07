# Constraints

## Runtime and repo

- Node >= 22.6, TypeScript via `node --experimental-strip-types`. No dependencies, no build step.
- Strip-only mode forbids TS `enum`, `namespace`, and constructor parameter properties. Use type
  aliases, `const` objects, and explicit field assignment.
- Imports must carry the `.ts` extension.
- Must not modify `src/reader.ts` or `src/manifest.ts` behaviour that the measured baseline rests
  on. Extending the manifest type is allowed; changing `recordPath` / `parentKey` semantics is not.
- Nothing named "Legacy".
- Never commit or push without approval for that specific change.
- Do not run FalconCollector, ISBLoad, or Dummy collector suites.

## Architecture

- Per-vendor mapping is DATA applied by a generic engine. Adding a vendor must not edit any file
  under the engine's own module.
- Each mapping declares the manifest fields it reads. The engine verifies those against the lane
  manifest at load time and fails before reading any record when a field is absent.
- Exactly ONE id-derivation code path serves both lanes.
- The mapping stage must not buffer a lane. Memory stays bounded by the largest single line, as
  measured: the 474 MB Falcon lane completes under `--max-old-space-size=64`.

## Target shape (promotion boundary)

- Tables: `integration.parser_output_assets_enrich`, `integration.parser_output_exposures_enrich`.
  PK `(id, instance_id)` on both.
- `cve_id` is NOT NULL on exposures, so the CVE explode happens before the target boundary.
- `tags` is `text` NOT NULL default `'{}'::text` — NOT an array. `group_names`, `site_names` and
  `ip_address` are real `text[]`.
- Six `timestamp(3) without time zone` columns. `additional_fields` is `jsonb`.
- stg has a nullable `batch_id`; prod-eu does not. Verified this session: `batch_id` appears 3
  times in the stg dump of `parser_output_assets_enrich` and 0 times in the prod-eu dump. Any
  generated artifact must record which cluster it targets.

## Identity

- Row id: `uuidv5(frozen namespace, instanceId + parentKey)`. Instance-scoped: within-run stable
  so a reparse is byte-identical; deliberately NOT stable across runs, because EA demotes prior
  generations via `latest = false` keyed on `(value, type)` per flow.
- Exposure identity composes the record's own canonical content, not just `(host, CVE)`.
- `created_at` MUST be excluded from any content hash. It falls back to the wall clock when a
  record labels none, so including it re-derives ids on every normalization.
- The hashed set lands on exactly the twelve columns EA fingerprints. Re-verified this session at
  `cymulate-exposure-analytics/apps/create-entities-tp/src/app/create-entities-third-party/repositories/parsed-data.repository.ts:70-76`:
  `md5(ROW(asset_id, cve_id, type, severity, status, name, display_name, description, mitigation, first_seen, last_seen, additional_fields)::text)`.

## Enums

- Five enums to validate membership against: `cybi.asset_type` (23), `cybi.asset_host_os_type`
  (15), `cybi.exposure_type` (12), `cybi.exposure_source_severity` (5), `cybi.exposure_status` (3).
- Members live in `enum.prisma`, not in the pg_catalog dumps.
- Use the `@map` value, never the Prisma identifier: `windows_server @map("windows-server")` at
  `enum.prisma:79` and `active_directory @map("active-directory")` at `:89`. The database spells
  them with hyphens.

## Data

- Real input on disk at
  `/private/tmp/claude-501/-Users-user-Dev-Uri-localprojects-AdapterDataNormalizer/75719977-6fbd-4678-ab6d-c694c4efa12e/scratchpad/s3/`:
  `falcon-assets.json` (298 rows), `falcon-findings-big.json` (497 MB, 151 lines, 246,212 records),
  `falcon-prod-findings.json` (44 MB, separate prod client), `tenable-findings.json` (13 MB).
  Re-fetchable from `s3://cybi-data/`.
- Derived manifests already exist under `manifests/`.

## Established facts — do not re-derive

- Every collector publishes NDJSON, one minified JSON value per line. Enforced by shared infra:
  `IntegrationInfra/src/IntegrationInfra/Emission/NdjsonBatchEmitter.cs` — "normalizes each record
  into a single-line/minified JSON string (global invariant)".
- Per-line content is not uniform. Two shapes across 18 collectors: flat vendor row
  (`recordPath '$'`), and the correlated per-asset envelope
  `{<key>, chunk, isLastChunk, findingsInChunk, host:{…}, findings:[…]}`
  (`recordPath '$.findings[*]'` + `parentPath '$.host'`), emitted only by FalconCollector and
  TenableIoCollector.
- The manifest is per (vendor, lane), so lane shape never needs a global decision.
- EA never correlates. It dereferences `asset_id` with an inner join and drops what does not
  resolve: `cybi-db-models/migrations/20260527124613_drop_parser_output_pk/migration.sql:239-244`.
- EA's exposure dedup default is off (`dedupMode` defaults false, `parsed-data.repository.ts:408`);
  the content dedup is a safety net gated on byte-identical duplicates. No unique constraint on
  `(asset_id, cve_id)` exists anywhere.

## Out of scope for this slice

- Postgres write and create-entities invocation, unless the plan shows it is cheap.
- Quarantine lane and synthetic-asset lane. No genuinely asset-less vendor has been named with
  evidence.
- The 16 collectors other than Falcon and TenableIo.

## Operator constraints added mid-execution (2026-09-03, after phase 1 started)

- **Do NOT run the TenableIo collector.** It is large and a new run is expensive. Operator's words:
  "tenable.io collector is huge, so be careful to run a new". Use the Tenable data already on disk.
  This extends the existing prohibition, which named FalconCollector, ISBLoad and Dummy.
  Prohibited collector runs are now: TenableIoCollector, FalconCollector, IsbLoadTestCollector,
  DummyCollector.
- **The prod dataset the operator supplied is recent and real, and is the reference.**
  `s3://cybi-data/prod-eu-west/raw-data/68a4aae33628dcd89bdaa974/6666d57e-4dbb-49bb-a4d8-72d433275183/e8d681e8-0c63-49f2-850f-ee4d9c747fff/`
  — 12,716 findings files, 946.9 GB, avg 74.5 MB, max 165 MB, one sample already on disk as
  `falcon-prod-findings.json`. Prefer it over lab data when the two disagree.
- **Stability over completeness.** Operator: "I want it stable, it doesn't have to be perfect yet."
  A working, measured, honestly-bounded stage beats a complete one. Do not widen scope to close
  gaps; record them instead.
- Collectors will eventually be run locally to produce real input, but not in this slice.
