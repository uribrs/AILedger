# Execution Notes

- 2026-04-29T13:29:45Z: Created documentation-only task state and execution contract.
- No product code has been read for this task yet.
- No product code has been modified.
- Verification is not started.
- 2026-04-29T13:35:46Z: Ran diagnosis by reading `state.json`, `prompt_contract.md`, constraints, assumptions, then searching with `rg` for `parser_output_exposures`, Spark JDBC, and write paths.
- 2026-04-29T13:35:46Z: Identified origin in `jobs/cybi-parser/script.py` exposure preparation/write path and `libs/packages/dal/sparkDAL.py` staged Postgres writer.
- 2026-04-29T13:35:46Z: Found current table schema in `libs/packages/parsers/schemas/db_schema.py`; `parser_output_exposures.id` is the primary key.
- 2026-04-29T13:35:46Z: Found current code writes `parser_output_exposures` through a staging table before inserting into the target table.
- 2026-04-29T13:35:46Z: Found git-history evidence that commit `f8d80a45` used direct `prepared_df.write.jdbc(... table="integration." + dbtable ...)`, which aligns closely with the observed `o1411.jdbc` error wrapper.
- 2026-04-29T13:35:46Z: Diagnosis conclusion: Spark retry/whole-write retry after partial persistence is plausible. In current code the target duplicate is most likely at `_insert_staged_rows`; in older/direct code it can occur inside Spark `.jdbc()`.
- 2026-04-29T13:35:46Z: Recommended mitigation point is a table-specific idempotent merge/upsert for `parser_output_exposures` in `libs/packages/dal/sparkDAL.py`.
- 2026-04-29T13:47:46Z: Checked whether same exposure `id` can legitimately carry different data. Code evidence shows exposure IDs are generated surrogate row IDs (`helpers.uuid_udf()` -> Spark SQL `uuid()`), not natural business keys or versioned identifiers.
- 2026-04-29T13:47:46Z: Updated mitigation recommendation: identical conflicts are idempotent retry success; divergent conflicts should raise a clear diagnostic error by default, with replacement only if product explicitly approves last-write-wins.
- 2026-04-29T14:04:36Z: User chose transactional row-level defense while keeping batches. Updated execution contract to implement `ON CONFLICT (id) DO NOTHING` for `parser_output_exposures` staged target insert, with staging duplicate detection and no replacement/update behavior.
- 2026-04-29T14:06:19Z: Implemented table-specific staged target insert handling in `libs/packages/dal/sparkDAL.py`.
- 2026-04-29T14:06:19Z: Added `tests/test_spark_dal.py` covering exposure conflict SQL, duplicate staging IDs, required `id`, and routing behavior.
- 2026-04-29T14:06:19Z: Verification passed: `PYTHONPYCACHEPREFIX=/private/tmp/codex_pycache .venv/bin/python -m py_compile libs/packages/dal/sparkDAL.py tests/test_spark_dal.py`.
- 2026-04-29T14:06:19Z: Verification passed: `PYTHONPATH=libs/packages .venv/bin/python -m pytest tests/test_spark_dal.py -q` returned 4 passed.
