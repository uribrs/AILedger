# Execution Notes

- 2026-05-03T13:22:48Z: Created execution contract for Tenable.io pkey retry simulation harness.
- Confirmed the requested simulation should target `SparkDAL` infrastructure, not Tenable parser internals.
- Removed the uncommitted shallow `tests/test_spark_dal.py` Tenable-named unit-test addition because it did not simulate the real partial-write retry shape.
- Read Falcon recovery simulation reference files to capture the named-scenario, interrupted-attempt, retry/resume style.
- 2026-05-03T13:25:57Z: Implemented `scripts/simulate_tenable_pkey_retry.py`.
- 2026-05-03T13:25:57Z: Script defaults to `files/tenable/assets_and_findings`, accepts alternate `--input-path`, and uses the existing Tenable parser to produce exposure rows.
- 2026-05-03T13:25:57Z: Script scenarios: `fixed-retry`, `pre-fix-failure`, `clean-write`, `stage-duplicate`, `orphan-stage`, or `all`.
- 2026-05-03T13:25:57Z: Verified with `PYTHONPYCACHEPREFIX=/private/tmp/codex_pycache .venv/bin/python -m py_compile scripts/simulate_tenable_pkey_retry.py tests/test_spark_dal.py libs/packages/dal/sparkDAL.py`.
- 2026-05-03T13:25:57Z: Verified CLI with `.venv/bin/python scripts/simulate_tenable_pkey_retry.py --help`.
- 2026-05-03T13:25:57Z: Verified existing DAL tests with `PYTHONPATH=libs/packages .venv/bin/python -m pytest tests/test_spark_dal.py -q`.
- Residual: full simulation scenario execution requires Postgres env/CLI credentials in the target environment.
