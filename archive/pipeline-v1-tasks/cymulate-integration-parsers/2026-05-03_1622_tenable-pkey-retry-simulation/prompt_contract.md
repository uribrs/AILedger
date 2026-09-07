Role:
You are a senior Python/Spark/Postgres engineer working in `cymulate-integration-parsers`.

Goal:
Create a local Tenable.io primary-key retry simulation harness that reproduces the `parser_output_exposures_pkey` retry shape and proves the infrastructure-level `SparkDAL` fix.

Context:
The observed issue occurred with Tenable.io data, but the fix was made in shared write infrastructure, not in the Tenable.io parser.

Relevant paths:

* `libs/packages/dal/sparkDAL.py`
* `libs/packages/parsers/tenable/tenableAssetsAndFindings.py`
* `files/tenable/assets_and_findings/findings_000001.ndjson`
* `tests/test_spark_dal.py`
* Falcon reference harness in `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Tools/Cymulate.Integration.Adapters.Tools.LocalAdapterRunner/FalconRecoverySimulation/`

Constraints:

* Do not change the Tenable.io parser to prove this fix.
* Keep the simulation routed through `SparkDAL` or a narrow test-only wrapper/subclass.
* Use Tenable.io sample data by default and allow alternate input paths.
* Simulate partial target persistence followed by an injected failure and a retry.
* The retry must use the fixed `SparkDAL` target insert path.
* Do not require services beyond local/reachable Postgres and Spark/Python dependencies.
* Do not store credentials or generated outputs in git.
* Preserve existing behavior for production code unless a minimal reusable hook is unavoidable.
* Keep output clear enough for operations to see scenario, first attempt, failure, retry, inserted/skipped counts, and final validation.
* Keep unrelated DB errors failing normally.

Success Criteria:

* A script exists for running the Tenable pkey retry simulation locally.
* The script can use `files/tenable/assets_and_findings/findings_000001.ndjson` by default.
* The script accepts alternate input path(s) for richer environments.
* The script creates/uses the parser output tables in Postgres without requiring production credentials in code.
* The script performs a partial insert into `integration.parser_output_exposures`.
* The script injects an interruption after partial target persistence.
* The script retries through the fixed `SparkDAL` path and succeeds without duplicate-key failure.
* The script validates that final target row count matches the expected unique exposure IDs.
* The script includes or documents additional scenarios: clean write, pre-fix duplicate failure, duplicate staging ID failure, and retry after orphaned staging table.
* Focused verification passes.
* Task state files are updated after implementation.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Inspect local parser/test helpers before implementing.
* Prefer existing parser preparation helpers and `SparkDAL` APIs.
* Keep implementation scoped and runnable.
* Update `state.json`, `decisions.md`, `assumptions.md`, and `execution_notes.md` as facts are validated.

Output Format:
Report:

* Whether the Tenable parser itself changed
* What script/harness was created
* How to run it
* What scenarios it covers
* What verification was run
* Any residual risk or manual dependency

Stop Conditions:

* When the harness is implemented and verified.
* When required local data, dependencies, or Postgres access assumptions are invalid.
* When implementing the harness would require unsafe changes to production parser behavior.
