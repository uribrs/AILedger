# Glue Parser S3 Runbook

## Scope

This explains how the local RunGlue tool triggers the EPC Glue workflow and what data must exist in S3 before running `cymulate-integration-parsers`.

This runbook does not execute AWS calls. Commands that upload or trigger are examples to run manually when you intend to affect AWS.

## How Glue Is Triggered

RunGlue is a local payload builder plus Glue workflow starter.

The practical flow is:

1. `run_glue_local.py` reads `appsettings.local.json` for the environment, AWS region, workflow name, and read bucket.
2. It resolves the integration and flow from `clients/*.json`.
3. It resolves the input S3 path from `file_paths.json`.
4. It converts that S3 path into Glue `RunProperties`.
5. With no `--execute`, it prints the payload only.
6. With `--execute`, it calls `boto3.client("glue").start_workflow_run(Name=<workflow>, RunProperties=<properties>)`.
7. With `--follow`, it polls `get_workflow_run`; with `--errors`, it queries CloudWatch logs for failed Glue jobs.

Evidence:

* `/Users/user/Dev/Uri/localprojects/RunGlue/run_glue_local.py:41` builds the payload.
* `/Users/user/Dev/Uri/localprojects/RunGlue/run_glue_local.py:104` stops after dry-run unless `--execute` is passed.
* `/Users/user/Dev/Uri/localprojects/RunGlue/run_glue_local.py:108` starts the workflow when execution is requested.
* `/Users/user/Dev/Uri/localprojects/RunGlue/run_glue/workflow.py:114` calls `start_workflow_run`.

## RunProperties That Matter

RunGlue passes these values to Glue:

```json
{
  "CLIENT_ID": "...",
  "INSTANCE_ID": "...",
  "CLIENT_INTEGRATION_ID": "...",
  "READ_BUCKET_NAME": "cybi-data",
  "ZIP_FILE_KEY": "stg/raw-data/<client-id>/<integration-setting-id>/<instance-or-run-id>/",
  "INTEGRATION_SETTING_FLOW_ID": "...",
  "FLOW_NAME": "...",
  "INTEGRATION_SETTING_ID": "...",
  "ENV": "STG"
}
```

The confusing one is `ZIP_FILE_KEY`. In this codebase it is not necessarily a zip file. The parser job uses it as the base data key.

In AWS, `cybi-parser` reconstructs the parser base path like this:

```text
s3://<READ_BUCKET_NAME>/<parent-of-ZIP_FILE_KEY>/<stem-of-ZIP_FILE_KEY>
```

For example:

```text
READ_BUCKET_NAME=cybi-data
ZIP_FILE_KEY=stg/raw-data/613f.../0c90.../04e2.../
base_file_path=s3://cybi-data/stg/raw-data/613f.../0c90.../04e2...
```

Evidence:

* `/Users/user/Dev/Uri/localprojects/RunGlue/run_glue/workflow.py:91` builds `RunProperties`.
* `/Users/user/Dev/cymulate-integration-parsers/jobs/cybi-parser/script.py:153` converts `ZIP_FILE_KEY` into the parser base path.

## What To Upload To S3

There are two supported practical modes.

### Direct-prefix mode

This is what the current RunGlue config is set up for. `ZIP_FILE_KEY` does not end with `.zip`, so DEX skips extraction and the parser reads files already present under the resolved prefix.

Upload parser input JSON files under the base path resolved from the RunGlue dry-run payload.

The default parser options point to:

```text
s3://<READ_BUCKET_NAME>/<base>/assets.json
s3://<READ_BUCKET_NAME>/<base>/findings.json
```

Most assets-and-findings parsers read `findings.json` from that folder and derive assets and findings from that one file. Asset-only parsers read `assets.json`.

The loader also supports numbered shards:

```text
assets_001.json
assets_002.json
findings_001.json
findings_000001.json
findings_000002.json
```

Files are loaded as JSON with `multiline=False`, so the safest format is NDJSON: one complete JSON object per line. The code also has a fallback path for corrupt-record parsing, but relying on that is asking Spark to do improv theater in production. Funny once, expensive twice.

Use `.json` extensions even for NDJSON content. The S3 multi-file resolver only includes keys ending in `.json`, so a file named `findings_000001.ndjson` will not be picked up by the AWS parser path.

### Zip mode

If `ZIP_FILE_KEY` ends with `.zip`, DEX downloads that zip, extracts it, and uploads the extracted contents back to S3 under a folder named after the zip basename.

Example:

```text
ZIP_FILE_KEY=stg/raw-data/<client>/<setting>/<run-id>/results.zip
DEX uploads extracted files under:
s3://cybi-data/stg/raw-data/<client>/<setting>/<run-id>/results/
Parser base path becomes:
s3://cybi-data/stg/raw-data/<client>/<setting>/<run-id>/results
```

For zip mode, the zip should contain the parser files at its root:

```text
findings.json
```

or:

```text
assets_000001.json
findings_000001.json
findings_000002.json
```

The current local RunGlue `file_paths.json` uses direct-prefix paths, not `.zip` paths.

Evidence:

* `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/preparation.py:13` creates default `assets.json` and `findings.json` paths.
* `/Users/user/Dev/cymulate-integration-parsers/libs/packages/utilities/helpers.py:14` loads JSON/NDJSON files.
* `/Users/user/Dev/cymulate-integration-parsers/libs/packages/utilities/helpers.py:182` resolves S3 single-file or numbered-file patterns.
* `/Users/user/Dev/cymulate-integration-parsers/libs/packages/utilities/helpers.py:230` sorts numbered S3 shards before loading.
* `/Users/user/Dev/cymulate-integration-parsers/jobs/dex/script.py:98` skips extraction when `ZIP_FILE_KEY` is not a `.zip`.
* `/Users/user/Dev/cymulate-integration-parsers/jobs/dex/script.py:68` extracts a real zip and uploads the extracted directory.

## Integration-Specific Input Shape

Use these practical rules:

| Flow type | Upload files |
|---|---|
| Asset-only parser | `assets.json` or `assets_*.json` |
| Standard assets-and-findings parser | `findings.json` or `findings_*.json` |
| Defender VM assets-and-findings, hydrated mode | `findings.json` or `findings_*.json` |
| Defender VM assets-and-findings, split mode | `assets.json` or `assets*.json`, plus `findings_*.json` |

Defender VM is special because it resolves an input contract before parsing:

* If both assets and split findings exist, it uses split mode.
* If only hydrated findings exist, it uses hydrated mode.
* If assets exist without findings, it fails as an incomplete split contract.

Evidence:

* `/Users/user/Dev/cymulate-integration-parsers/libs/packages/utilities/input_resolver.py:27` defines Defender VM input contract resolution.
* `/Users/user/Dev/cymulate-integration-parsers/libs/packages/utilities/input_resolver.py:77` gives split mode precedence.
* `/Users/user/Dev/cymulate-integration-parsers/libs/packages/utilities/input_resolver.py:93` rejects assets without findings.
* `/Users/user/Dev/cymulate-integration-parsers/tests/test_input_resolver.py:10` validates hydrated filenames.
* `/Users/user/Dev/cymulate-integration-parsers/tests/test_input_resolver.py:32` validates split assets/findings mode.

## Repeatable Workflow

### 1. Dry-run the Glue payload

```bash
cd /Users/user/Dev/Uri/localprojects/RunGlue
.venv/bin/python run_glue_local.py --integration tenable --flow findings --env stg
```

or:

```bash
cd /Users/user/Dev/Uri/localprojects/RunGlue
.venv/bin/python run_glue_local.py --integration defender-vm --flow assets-and-findings --env stg
```

Confirm:

* `Name` is the expected workflow, such as `stg-epc`.
* `Region` is correct.
* `READ_BUCKET_NAME` is correct.
* `ZIP_FILE_KEY` points to the exact test run prefix you intend to use.
* IDs match the integration setting and flow you expect.

### 2. Derive the S3 base path

From the dry-run output:

```text
s3://<READ_BUCKET_NAME>/<ZIP_FILE_KEY without trailing slash normalization>
```

Example dry-run output:

```text
READ_BUCKET_NAME=cybi-data
ZIP_FILE_KEY=stg/raw-data/613f3842d8ddf900117fccfa/0c90af0f-e52e-4808-a728-a62f9bce94ec/04e2fafd-728c-490c-a6b1-fc685d797225/
```

Upload under:

```text
s3://cybi-data/stg/raw-data/613f3842d8ddf900117fccfa/0c90af0f-e52e-4808-a728-a62f9bce94ec/04e2fafd-728c-490c-a6b1-fc685d797225/
```

### 3. Upload the parser input files

Single-file assets-and-findings example:

```bash
aws s3 cp /path/to/findings.json \
  s3://cybi-data/stg/raw-data/<client-id>/<integration-setting-id>/<instance-id>/findings.json
```

Asset-only example:

```bash
aws s3 cp /path/to/assets.json \
  s3://cybi-data/stg/raw-data/<client-id>/<integration-setting-id>/<instance-id>/assets.json
```

Sharded findings example:

```bash
aws s3 sync /path/to/shards/ \
  s3://cybi-data/stg/raw-data/<client-id>/<integration-setting-id>/<instance-id>/ \
  --exclude "*" \
  --include "findings_*.json"
```

Defender VM split example:

```bash
aws s3 sync /path/to/defender-vm-split/ \
  s3://cybi-data/stg/raw-data/<client-id>/<integration-setting-id>/<instance-id>/ \
  --exclude "*" \
  --include "assets*.json" \
  --include "findings_*.json"
```

Zip-mode example:

```bash
cd /path/to/input-folder
zip -r /tmp/results.zip findings.json
aws s3 cp /tmp/results.zip \
  s3://cybi-data/stg/raw-data/<client-id>/<integration-setting-id>/<instance-id>/results.zip
```

### 4. Verify S3 before triggering

```bash
aws s3 ls s3://cybi-data/stg/raw-data/<client-id>/<integration-setting-id>/<instance-id>/
```

Expected examples:

```text
findings.json
```

or:

```text
assets_000001.json
findings_000001.json
findings_000002.json
```

### 5. Trigger Glue

```bash
cd /Users/user/Dev/Uri/localprojects/RunGlue
.venv/bin/python run_glue_local.py \
  --integration tenable \
  --flow findings \
  --env stg \
  --execute
```

To wait for completion:

```bash
.venv/bin/python run_glue_local.py \
  --integration tenable \
  --flow findings \
  --env stg \
  --execute \
  --follow
```

To include failed job logs:

```bash
.venv/bin/python run_glue_local.py \
  --integration tenable \
  --flow findings \
  --env stg \
  --execute \
  --follow \
  --errors
```

### 6. Check an existing run

```bash
cd /Users/user/Dev/Uri/localprojects/RunGlue
.venv/bin/python run_glue_local.py --env stg --status <RunId> --errors
```

## Current Local Configured Paths

From `/Users/user/Dev/Uri/localprojects/RunGlue/file_paths.json`:

| Integration | Flow | Env | S3 prefix |
|---|---|---|---|
| Tenable.io | assets-and-findings | stg | `s3://cybi-data/stg/raw-data/613f3842d8ddf900117fccfa/0c90af0f-e52e-4808-a728-a62f9bce94ec/04e2fafd-728c-490c-a6b1-fc685d797225/` |
| Tenable.io | assets-and-findings | rfqa | `s3://cybi-data/rfqa/raw-data/613f3842d8ddf900117fccfa/0c90af0f-e52e-4808-a728-a62f9bce94ec/8dbe7042-163f-4cb7-bd5d-e0f195a4626c/` |
| Tenable.io | assets-and-findings | prod-eu-west | `s3://cybi-data/prod-eu-west/raw-data/5e2db79912f0b55f483f378e/0c90af0f-e52e-4808-a728-a62f9bce94ec/39f454ae-d181-465f-b4f2-0f6059469793/` |
| Defender VM | assets-and-findings | stg | `s3://cybi-data/stg/raw-data/613f3842d8ddf900117fccfa/d77ee747-93e4-4cf0-9417-a84518638b96/1a2fbf9e-b112-478c-946c-f3931fbf9886/` |
| Defender VM | assets | stg | `s3://cybi-data/stg/raw-data/613f3842d8ddf900117fccfa/d77ee747-93e4-4cf0-9417-a84518638b96/04e2fafd-728c-490c-a6b1-fc685d797228/` |
| CrowdStrike | assets | stg | `s3://cybi-data/stg/raw-data/613f3842d8ddf900117fccfa/6666d57e-4dbb-49bb-a4d8-72d433275183/04e2fafd-728c-490c-a6b1-fc685d797227/` |
| Spotlight Vulnerability Management | vulnerability-management | stg | `s3://cybi-data/stg/raw-data/613f3842d8ddf900117fccfa/6666d57e-4dbb-49bb-a4d8-72d433275183/04e2fafd-728c-490c-a6b1-fc685d797227/` |

## What To Upload For A Dev Parser Environment

Yes, conceptually this is like uploading C# `.dll`s, with Python/Glue-shaped pieces:

| Repo thing | Runtime artifact | Why Glue needs it |
|---|---|---|
| `jobs/cybi-parser/script.py` | `parser.py` in S3 | The Glue `cybi-parser` job entrypoint. |
| `jobs/dex/script.py` | `dex.py` in S3 | The Glue `dex` job entrypoint. |
| `libs/packages/**` | Python wheel, currently referenced as `parsers.whl` | Shared parser code, parser registry, DAL, job base, utilities, tenant mapping, RabbitMQ helper. |
| `jobs/*/config.json` / `workflows/epc/config.json` | Glue job/workflow configuration | Defines script S3 location, `--extra-py-files`, secrets, temp dirs, worker count, Glue version, connections, triggers. |

The configured production-like jobs point at:

```text
ScriptLocation=s3://cym-cybi-data/metadata/scripts/parser.py
ScriptLocation=s3://cym-cybi-data/metadata/scripts/dex.py
--extra-py-files=s3://cym-glue/stg/libs/utilities/parsers.whl
```

For a lower branch-specific environment, do not overwrite those shared paths. Use isolated artifact keys, for example:

```text
s3://cym-cybi-data/dev/<user-or-branch>/metadata/scripts/parser.py
s3://cym-cybi-data/dev/<user-or-branch>/metadata/scripts/dex.py
s3://cym-glue/dev/<user-or-branch>/libs/utilities/parsers.whl
```

Then create or update dev Glue jobs so:

```text
cybi-parser.Command.ScriptLocation = s3://.../parser.py
dex.Command.ScriptLocation = s3://.../dex.py
DefaultArguments["--extra-py-files"] = s3://.../parsers.whl
```

Build/upload example:

```bash
cd /Users/user/Dev/cymulate-integration-parsers/libs/packages
rm -rf build dist *.egg-info
python3 setup.py bdist_wheel
aws s3 cp dist/parsers-1.0.0-py3-none-any.whl \
  s3://cym-glue/dev/<user-or-branch>/libs/utilities/parsers.whl

cd /Users/user/Dev/cymulate-integration-parsers
aws s3 cp jobs/cybi-parser/script.py \
  s3://cym-cybi-data/dev/<user-or-branch>/metadata/scripts/parser.py
aws s3 cp jobs/dex/script.py \
  s3://cym-cybi-data/dev/<user-or-branch>/metadata/scripts/dex.py
```

Important boundary:

* Parser implementation changes under `libs/packages/parsers/**` require rebuilding and uploading the wheel.
* Shared runtime changes under `libs/packages/job`, `libs/packages/utilities`, `libs/packages/dal`, `libs/packages/tenants`, or `libs/packages/rabbitmq_api` also require rebuilding and uploading the wheel.
* Changes in `jobs/cybi-parser/script.py` require uploading `parser.py`.
* Changes in `jobs/dex/script.py` require uploading `dex.py`.
* Dependency changes require updating Glue job `--additional-python-modules` or the Glue image/config, not just uploading the wheel.
* Workflow topology changes require creating/updating the dev Glue workflow and triggers.

Recommended ownership split:

* `cymulate-integration-parsers` owns side-branch executable artifacts: wheel build, `parser.py`, `dex.py`, and dev Glue job artifact paths.
* `RunGlue` owns triggering and monitoring the already-created dev workflow with the correct `RunProperties`.
* Normal promotion still goes through the real CI/CD path after validation.

This is intentionally a dev-only CI/CD bypass for load validation. It should never overwrite shared `stg`, `rfqa`, or production artifact paths.

Minimal dev flow:

```text
parser side branch
  -> build/upload branch artifacts from cymulate-integration-parsers
  -> dev Glue jobs point at those branch artifacts
  -> RunGlue triggers dev workflow with large S3 input
  -> inspect logs/Spark behavior
  -> submit PR and promote through normal pipeline
```

Evidence:

* `/Users/user/Dev/cymulate-integration-parsers/jobs/cybi-parser/config.json` declares `ScriptLocation` for `parser.py` and `--extra-py-files`.
* `/Users/user/Dev/cymulate-integration-parsers/jobs/dex/config.json` declares `ScriptLocation` for `dex.py` and `--extra-py-files`.
* `/Users/user/Dev/cymulate-integration-parsers/workflows/epc/config.json` declares the DEX-to-parser workflow graph.
* `/Users/user/Dev/cymulate-integration-parsers/libs/packages/setup.py` packages the shared Python modules.
* `/Users/user/Dev/cymulate-integration-parsers/libs/packages/build.sh` builds the wheel.

## Dev Logging And Spark Visibility

The current Glue job config already enables several observability knobs:

```text
--enable-metrics=true
--enable-spark-ui=true
--spark-event-logs-path=s3://cym-cybi-data/metadata/sparkHistoryLogs/
--enable-observability-metrics=true
--enable-continuous-cloudwatch-log=true
```

For a dev branch environment, keep these enabled but point event logs and temp output at isolated dev paths:

```text
--spark-event-logs-path=s3://cym-cybi-data/dev/<user-or-branch>/metadata/sparkHistoryLogs/
--TempDir=s3://cym-cybi-data/dev/<user-or-branch>/metadata/temporary/
```

Practical logging checklist:

* Use `RunGlue --follow --errors` for workflow/job status plus failed-node CloudWatch tails.
* Keep parser logs around input resolution: base path, selected mode, file count, exact strategy, and resolved S3 paths.
* Log DataFrame schema and counts at parser boundaries, but avoid dumping rows unless explicitly sampling a tiny number.
* For Spark-stage visibility, use Glue Spark UI and event logs. The job config already has Spark UI enabled; the event log path should be isolated for dev.
* For “what was pushed into Spark,” log file list and byte/count metadata before `create_dynamic_frame.from_options`, then log resulting schema and row count after load.

Useful code locations for adding temporary branch logging:

* `/Users/user/Dev/cymulate-integration-parsers/jobs/cybi-parser/script.py` around parser selection and `build_default_parser_options`.
* `/Users/user/Dev/cymulate-integration-parsers/libs/packages/utilities/input_resolver.py` around input contract resolution.
* `/Users/user/Dev/cymulate-integration-parsers/libs/packages/utilities/helpers.py` around `_resolve_s3_file_paths`, `_resolve_s3_paths_from_pattern`, and DataFrame creation.
* Parser-specific `pre_process` methods when the problem is inside one vendor parser.

Keep dev logging branch-scoped. Do not promote noisy load-debug logs unless they are genuinely useful operational diagnostics.

## Practical Checklist

Before `--execute`:

* Dry-run prints the intended workflow and run properties.
* S3 prefix exists or is intentionally new.
* Expected input files exist under that prefix.
* File names match what the parser expects: `assets.json`, `findings.json`, `assets_*.json`, or `findings_*.json`.
* Sharded NDJSON files use `.json` file names, not `.ndjson`.
* Input files are JSON/NDJSON, preferably NDJSON.
* `INTEGRATION_SETTING_ID` and `INTEGRATION_SETTING_FLOW_ID` match real Mongo flow metadata in the target environment.
* AWS profile/credentials point at the account for the selected environment.

## Residual Risks

* I did not query AWS, Mongo, or S3. The runbook is based on local code and local JSON config only.
* Some local `RunGlue` IDs may be test or stale values; the Glue parser will fail if Mongo does not contain the matching integration setting or flow.
* The parser repo has more parsers than RunGlue currently configures. For unconfigured integrations, add entries in `clients/*.json` and `file_paths.json` before using this workflow.
* `ZIP_FILE_KEY` naming is historical. Treat it as the parser input prefix when it does not end with `.zip`; treat it as an actual zip object only when the configured key ends with `.zip`.
