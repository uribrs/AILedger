# Execution Notes

## Commands Run

* `find /Users/user/codex-state/global -maxdepth 2 -type f`
* `find Planning -maxdepth 4 -type f`
* `find /Users/user/Dev/Uri/localprojects/RunGlue -maxdepth 3 -type f`
* `find /Users/user/Dev/cymulate-integration-parsers -maxdepth 3 -type f`
* `sed -n ...` on selected RunGlue source files and README
* `.venv/bin/python run_glue_local.py --integration tenable --flow findings --env stg`
* `.venv/bin/python run_glue_local.py --integration defender-vm --flow assets-and-findings --env stg`
* `rg "ZIP_FILE_KEY|READ_BUCKET_NAME|zip|s3" jobs/dex libs/packages -n`
* `sed -n ...` and `nl -ba ...` on parser startup, DEX, input resolver, helpers, parser preparation, and tests
* `sed -n ... /Users/user/Dev/Uri/Planning/glue-parser-s3-runbook.md`

## Notes

* Task state created before execution.
* Created `/Users/user/Dev/Uri/Planning/glue-parser-s3-runbook.md`.
* Verified that dry-run mode does not call AWS without `--execute`.
* Verified that DEX skips extraction for non-`.zip` `ZIP_FILE_KEY` values.
* Verified that parser input loaders expect `.json` file names and support NDJSON content.
* Added dev-environment artifact guidance: upload parser job script, dex job script, and rebuilt parser wheel to isolated S3 keys; configure dev Glue jobs to use those keys.
* Added explicit dev-only CI/CD bypass model and logging guidance for CloudWatch, Glue Spark UI, Spark event logs, input resolution, and DataFrame boundary diagnostics.

## Residual Risks

* AWS, S3, and Mongo were not queried.
* Local RunGlue IDs may be stale in real environments.
* Some integrations present in the parser repo are not configured in RunGlue yet.
