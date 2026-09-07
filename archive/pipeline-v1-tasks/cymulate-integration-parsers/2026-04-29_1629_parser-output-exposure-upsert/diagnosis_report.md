# Diagnosis Report

## Origin

- `jobs/cybi-parser/script.py` prepares findings for `integration.parser_output_exposures` in `Parser._prepare_findings_df`.
- `jobs/cybi-parser/script.py` writes exposures in `Parser.run` by calling `self.spark_dal.write_to_postgres(self.postgresWriteConfig, prepared_findings_df, TABLES.PARSER_OUTPUT_EXPOSURES)`.
- `libs/packages/dal/dbManager.py` defines `TABLES.PARSER_OUTPUT_EXPOSURES = "parser_output_exposures"`.
- `libs/packages/parsers/schemas/db_schema.py` defines `integration.parser_output_exposures` with `"id" uuid PRIMARY KEY`.
- `libs/packages/dal/sparkDAL.py` owns the write mechanics:
  - `write_to_postgres` calculates partitions, builds JDBC options, and retries the whole write up to `MAX_WRITE_RETRIES = 3`.
  - current code routes `parser_output_exposures` through `_write_via_uuid_staging` because it is listed in `UUID_CAST_COLUMNS`.
  - `_write_via_uuid_staging` writes Spark rows to a random staging table with `prepared_df.write.jdbc`, then calls `_insert_staged_rows`.
  - `_insert_staged_rows` currently runs plain `INSERT INTO integration.parser_output_exposures (...) SELECT ... FROM integration.<stage>`.

## Root Cause

- The Spark retry theory holds water for a direct append JDBC writer:
  - Spark JDBC writes are distributed by partition, not one all-or-nothing transaction for the entire DataFrame.
  - a partial target-table commit followed by Spark retry, Glue retry, or this repository's whole-write retry can re-attempt already persisted rows.
  - because exposure IDs are generated before the write and then carried in the DataFrame, the retried insert can reuse the same primary keys.
  - a second append of those same IDs raises `duplicate key value violates unique constraint "parser_output_exposures_pkey"`.

- The exact observed wrapper text, `An error occurred while calling o1411.jdbc`, most strongly matches Spark's `DataFrameWriter.jdbc` call.
  - That matches the older direct writer visible in git history at `f8d80a45`, where `write_to_postgres` called `prepared_df.write.jdbc(... table="integration." + dbtable ...)` for all tables.
  - It does not perfectly match the current checked-out staged writer for `parser_output_exposures`, because current Spark `.jdbc()` writes to `integration.parser_output_exposures_stage_<suffix>`, not directly to `integration.parser_output_exposures`.

- In current checked-out code, target-table duplicate keys can still occur, but the likely point is `_insert_staged_rows`, not Spark's JDBC write to the staging table:
  - if `_insert_staged_rows` commits to the target and an exception is raised before the caller observes success, `write_to_postgres` retries the whole DataFrame and inserts the same stage rows again;
  - if the incoming DataFrame/staging table itself contains duplicate `id` values, the plain target insert fails on the primary key;
  - if production is running code before the staging change, the duplicate can occur directly inside Spark `.jdbc()`.

## Mitigation Point

- Implement the mitigation at `libs/packages/dal/sparkDAL.py` in the staged merge boundary, not in individual parsers.
- The narrowest current-code point is `_insert_staged_rows`, with table-specific conflict behavior for `parser_output_exposures`.
- If production is still on the older direct writer, the equivalent mitigation should first move `parser_output_exposures` to the staging path, then apply the same target-table merge behavior.

## Mitigation Design

- Replace the plain target insert for `parser_output_exposures` with a table-specific idempotent upsert:
  - `INSERT INTO integration.parser_output_exposures (...) SELECT ... FROM integration.<stage>`
  - `ON CONFLICT (id) DO UPDATE SET <non-id columns> = EXCLUDED.<column>`
  - use a `WHERE` clause with `IS DISTINCT FROM` across comparison columns so identical rows become a successful no-op and differing rows replace the persisted payload.

- Identical row:
  - conflict on `id`;
  - comparison columns are not distinct;
  - no update is performed;
  - object is treated as already written.

- Differing row:
  - conflict on `id`;
  - at least one comparison column differs;
  - raise a clear diagnostic error by default;
  - replace/update only if product explicitly approves last-write-wins behavior.

- Non-primary-key conflict:
  - do not handle unless investigation finds a concrete expected constraint;
  - let the database error surface.

- Unexpected database error:
  - keep existing retry/logging behavior;
  - do not classify as idempotent success.

## Equality Contract

- Primary key: `id`.
- Candidate comparison/replacement columns from the table schema:
  - `asset_id`
  - `client_id`
  - `client_integration_id`
  - `instance_id`
  - `integration_setting_id`
  - `integration_setting_flow_id`
  - `connector_flow_name`
  - `first_seen`
  - `last_seen`
  - `name`
  - `display_name`
  - `description`
  - `mitigation`
  - `type`
  - `severity`
  - `status`
  - `cve_id`
  - `additional_fields`
- `created_at` needs an explicit product decision before implementation:
  - include it if "object identity" means every persisted column must match;
  - exclude it if `created_at` is volatile write metadata and should not make an otherwise identical retry look different.

## Validation Plan

- Unit-test SQL construction for `parser_output_exposures`:
  - emits `ON CONFLICT (id)`;
  - updates non-id columns only;
  - uses `IS DISTINCT FROM` for comparison;
  - leaves other tables on their existing insert behavior unless explicitly extended.

- Integration-test with Postgres:
  - first staged insert succeeds;
  - second staged insert with identical `id` and payload succeeds without duplicate-key failure;
  - third staged insert with same `id` and changed payload fails with a clear divergent-conflict diagnostic unless product explicitly enables replacement;
  - duplicate IDs inside the same staging table are either rejected clearly or deduplicated by a deliberate rule before merge;
  - unrelated constraint/database errors still fail.

- Retry simulation:
  - force an exception after target insert commit and before `write_to_postgres` returns;
  - verify the next retry does not fail the batch on `parser_output_exposures_pkey`.

## Conclusion

- Yes, the Spark retry theory is technically sound for the direct JDBC append path and likely explains an `o1411.jdbc` duplicate-key error if production is running that path.
- For the current checked-out code, the same idempotency problem still exists, but the most likely mitigation point is the staged target insert in `_insert_staged_rows`.
- The right fix is not to catch and ignore duplicate-key errors blindly. The safer default is an idempotent target merge/check for `parser_output_exposures`: identical rows are successful no-ops, differing rows fail as a primary-key identity violation unless product explicitly approves replacement.

## Primary-Key Identity Check

- Local code does not show a legitimate business flow where the same exposure `id` should intentionally represent two different payloads.
- Exposure `id` values are generated by parser code using `helpers.uuid_udf()`, which wraps Spark SQL `uuid()`.
- Spark documents `uuid()` as non-deterministic, so generated IDs are not stable business identifiers derived from exposure content.
- The table schema defines only `id` as the primary key; there is no separate natural key or version column that would make same-`id` replacement an expected domain operation.
- Most parser flows assign `id` after selecting the exposure payload, then later normalize/shape fields. That makes `id` a surrogate row identity, not a content hash.
- Under that model, same `id` with different non-volatile payload should be treated as a primary-key contract violation unless product explicitly chooses last-write-wins semantics.
- The safer implementation shape is:
  - identical conflict: treat as idempotent retry success;
  - divergent conflict: raise a clear diagnostic error by default;
  - divergent conflict with explicit product approval: replace/update the row.
- `created_at` should not be used as proof of divergent business payload unless product decides it is part of object identity.
