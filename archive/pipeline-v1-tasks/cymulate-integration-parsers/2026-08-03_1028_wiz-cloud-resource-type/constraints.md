# Constraints

## Scope

- Modify ONLY:
  - `libs/packages/parsers/deprecated/wiz/wizAssetsAndFindings.py`
  - `libs/packages/parsers/schemas/db_schema.py`
  - `tests/test_wiz_assets_findings_split.py`
- **`libs/packages/parsers/common/base_parser.py` MUST NOT be modified** (decision D-C).
- **No `*.expected_assets.json` / `*.expected_findings.json` snapshot may be regenerated or edited.** If any snapshot changes, the implementation has leaked into shared behaviour — stop and report.
- Never read or write `/Users/user/Dev/cymulate-exposure-analytics`. All EA facts are in this contract.
- No git operations: no branch creation, no commits, no pushes.

## Behaviour

- Wiz asset `type` = `cloud_resource` (const `cloud_resource`; BaseParser lowercases anyway).
- Six columns emitted on the Wiz asset frame: `sub_type`, `cloud_platform`, `region`, `cloud_account_id`, `cloud_account_name`, `cloud_provider_url`.
- Four populated from source: `cloud_platform` ← `cloud_platform`, `sub_type` ← `type`, `cloud_account_id` ← `cloud_account_id`, `cloud_account_name` ← `cloud_account_name`.
- `region` and `cloud_provider_url` emit NULL (D-D).
- Do not change asset `value`.
- Do not regress commit `f230eee` behaviour.
- The cloud columns must NOT be declared in `asset_mandatory_fields` — that machinery silently drops unknown keys (predecessor D8).

## Implementation

- Pure DataFrame transforms. Only the Glue job touches Postgres. No SQL execution, no DB connections, no new dependencies, no new files.
- Match the surrounding code style of the Wiz parser and its sibling split parsers.
- The Wiz `create_asset_source` override must call `super()` and append — it must not re-implement or copy the base select body.

## Verification

- Spark runs locally and tests MUST actually be executed:
  - `JAVA_HOME=/opt/homebrew/opt/openjdk@11/libexec/openjdk.jdk/Contents/Home`
  - `PYSPARK_PYTHON=/Users/user/Dev/cymulate-integration-parsers/.venv/bin/python`
  - `PYSPARK_DRIVER_PYTHON` same, `PYTHONPATH=libs/packages`
- Run `tests/test_wiz_assets_findings_split.py` (all cases must pass).
- Run ONE non-Wiz parser's golden-snapshot case to prove the shared path is untouched, e.g. `pytest tests/test_run_parsers.py -k qualys`. It MUST pass without regenerating snapshots.
- NEVER run the full suite.
- Never claim a test passed that did not run.

## Real-data proof (preferred over assertion)

- Harness: `/private/tmp/claude-501/-Users-user-Dev-cymulate-integration-adapters/c230b3e6-70d0-4eef-91ab-5182b903b2c6/scratchpad/run_wiz_parser.py`
- Real lanes already downloaded to `.../scratchpad/sim` — 4,809 assets, 107,653 findings.
- Must reproduce the predecessor invariants: 4,809 asset rows, 107,653 exposure rows, 0 duplicate asset ids, 0 null/dangling `asset_id`.

## EA contract (verified — do not re-derive, do not open the EA repo)

- `cybi.asset_type` contains `cloud_resource` (exact spelling).
- EA populates `cybi.asset_cloud_resource` from six `parser_output_assets` columns:
  `cloud_platform`→cloud_provider, `sub_type`→cloud_resource_sub_type, `cloud_account_id`,
  `cloud_account_name` (EA applies `NULLIF(x,'')`), `region`, `cloud_provider_url`.
- EA gates the satellite INSERT on DATA, not on `asset.type` — a row is written when at least one of the six is non-null.
