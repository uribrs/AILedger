# Authoritative schema for `integration.parser_output_exposures` (main thread)

`libs/packages/parsers/schemas/db_schema.py:68-81` states its own `CREATE TABLE IF NOT EXISTS` is a
no-op in deployed environments and that the table is owned by the `cybi-db-models` migrations. That
repo is checked out locally at `/Users/user/Dev/cybi-db-models` (HEAD `98b95949`, 2026-05-06,
"Merge pull request #382 from cymulate-rnd/more_indexes"), so the real index set is readable without
touching the database.

## Indexes on the INSERT target — 11 B-trees per row

Source: `prisma/schema/integration.parser-output-exposures.prisma` (the model's `@@index` list) and
`migrations/20260330120000_add_parser_output_tables/migration.sql` +
`migrations/20260505150010_index_parser_output_exposures_instance_id_client_id_idx/migration.sql`.

| # | index | columns | key locality |
|---|---|---|---|
| 1 | `parser_output_exposures_pkey` (PRIMARY KEY) | `id` | **random v4 UUID** |
| 2 | `parser_output_exposures_asset_id_idx` | `asset_id` | **random v4 UUID** |
| 3 | `parser_output_exposures_instance_id_idx` | `instance_id` | one constant value per run |
| 4 | `parser_output_exposures_client_id_idx` | `client_id` | one constant value per run |
| 5 | `parser_output_exposures_client_integration_id_idx` | `client_integration_id` | constant |
| 6 | `parser_output_exposures_type_idx` | `type` | constant (`vulnerability` for all Tenable rows) |
| 7 | `parser_output_exposures_cve_id_idx` | `cve_id` | scattered |
| 8 | `parser_output_exposures_first_seen_idx` | `first_seen` | scattered |
| 9 | `parser_output_exposures_last_seen_idx` | `last_seen` | scattered |
| 10 | `parser_output_exposures_client_id_instance_id_idx` | `client_id, instance_id` | constant prefix |
| 11 | `parser_output_exposures_instance_id_client_id_id_idx` | `instance_id, client_id, id` | constant prefix + **random UUID** tail |

Consequences for the INSERT, all per row:

- **11 index entries inserted per row.** Every one is WAL-logged in addition to the heap tuple.
- **Three of them are keyed on a random v4 UUID** (`id`, `asset_id`, and the `id` tail of #11). A
  random key has no insertion locality, so each of those touches an arbitrary leaf page: a buffer
  lookup, a pin, an exclusive content lock, and a page read when the index no longer fits in
  `shared_buffers`. That is exactly the observed wait mix — dominant **CPU** with
  **LWLock:BufferContent**, **LWLock:BufferMapping** and **IO:DataFileRead** alongside it.
- Indexes #3, #4, #5, #6 and the prefix of #10/#11 take a **single constant value for the entire
  run**, so every row of the run appends to the same small set of leaf pages — heavy contention on
  those specific pages across concurrent writers, again `LWLock:BufferContent`.
- Index #6 on `type` is effectively useless for Tenable: `tenableAssetsAndFindings.py:344` hard-codes
  `type = "vulnerability"` on every finding, so all rows share one key.

## No per-row trigger cost

`grep -rn 'CREATE TRIGGER' migrations/` finds no trigger on `integration.parser_output_exposures`.
The `trigger_insert_exposure_latest_from_*` / `trigger_sync_exposure_latest_from_subtable_safe`
triggers are on the `cybi.exposure_*` tables, not on the parser output table. So the INSERT's cost is
heap + 11 indexes + the `jsonb` input function, with no trigger amplification.

## Columns — 20, matching the parser (legacy mode)

The Prisma model lists exactly the 20 columns `db_schema.py:43-66` declares **minus `batch_id`**:
`id, asset_id, client_id, client_integration_id, instance_id, integration_setting_id,
integration_setting_flow_id, connector_flow_name, created_at, first_seen, last_seen, name,
display_name, description, mitigation, type, severity, status, cve_id, additional_fields`.
`additional_fields` is `Json @default("{}")` → **`jsonb`**, so with the writer's
`stringtype=unspecified` the server runs the `jsonb` input function on every row's ~4 KB payload.

## Downstream consumer of the same rows

`migrations/20260424130000_create_entities_remove_distinct_and_fix_pk/migration.sql:709-745`
(`create_entities_tp_insert_into_parser_output_exposures_enrich`) copies
`integration.parser_output_exposures` into `integration.parser_output_exposures_enrich`, which
carries **17 further indexes** of its own (grep of the migration index names). Lines 484-488 and
604 also `DELETE FROM integration.parser_output_exposures` / `..._enrich`. So the rows this INSERT
writes are re-read, re-inserted and re-indexed by create-entities afterwards — relevant to the
`EA Correlation-TP | ExposureRiskRepository.detectChanged` (8939.86 ms/call) entry in the operator's
Top SQL, and to any load that outlives the Glue run.

## Caveat

This is the migration repo at its local HEAD (2026-05-06). It is authoritative for what the
migrations declare, **not** proof of what STG's catalog currently holds — an index added or dropped in
STG by hand, or a migration merged after 2026-05-06 and not pulled locally, would not appear here.
Confirming against `\d+ integration.parser_output_exposures` needs a DB connection, which this task's
constraints forbid.
