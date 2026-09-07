# W1 — actual Falcon artifact parser replay

## Result

**Parser execution: PASS. Downstream semantic contract: FAIL pending correction.**

The immutable final S3 object was replayed locally through the production
`build_default_parser_options` -> `prepare_parser_options` path and the concrete
`CrowdstrikeAssetsFindingsParser`, which selected
`CrowdstrikeAssetsFindingsCorrelated` from the live envelope shape. Product source
was mounted read-only and remained unchanged.

## Input and runtime

- Input basename: `findings_000001.json`
- Bytes: `324046289`
- SHA-256: `98dc0daf5a515e8a8d7c340c61d95bfd1e4efb029a86dc658371da55daecc48e`
- Parser source HEAD: `83626813d1b90caf0b814908c05adf1703e97062`
- Container image: `cymulate-parsers-test:latest` (`6820a2c8bfc7` when selected)
- Runtime: Python `3.10.18`, OpenJDK `11.0.31`, PySpark/Spark `3.3.0`
- Spark master: `local[4]`; driver memory: `6g`; Colima: 4 CPU / 10 GiB
- Successful parser elapsed time: `17.003` seconds
- Resolved input mode: `hydrated`; strategy type: `PathStrategy`

The successful execution was created and run with the following effective command
(the large artifact and helper were copied into the isolated container first because
Colima does not expose macOS `/private/tmp` as a bind mount):

```text
docker create --name falcon-policy-actual-parser-replay \
  -v /Users/user/Dev/cymulate-integration-parsers:/workspace:ro \
  -v /Users/user/Dev/cymulate-integration-adapters/ai/active/2026-08-20_1101_falcon-policy-s3-parser-e2e-validation/workstreams:/evidence \
  -e PYTHONPATH=/workspace/libs/packages \
  -e PYSPARK_PYTHON=/app/.venv/bin/python \
  -e PYSPARK_DRIVER_PYTHON=/app/.venv/bin/python \
  -w /workspace cymulate-parsers-test:latest \
  /app/.venv/bin/python /tmp/replay_parser.py \
  --input /tmp/findings_000001.json \
  --result /evidence/w1-parser-replay-result.json \
  --policies-parquet /tmp/policies.parquet
docker start -a falcon-policy-actual-parser-replay
```

An initial attempt reached parser post-processing but failed when Spark workers used
the system Python and could not import `elastic_transport`. Pinning both PySpark
interpreter variables to `/app/.venv/bin/python` repaired the runtime-only failure.

## Aggregate output

| Frame | Rows | Distinct business/generated ID | Result |
|---|---:|---:|---|
| Assets | 47 | 47 (`id`) | PASS |
| Findings | 124918 | 124918 (`id`) | PASS |
| Policies | 19 | 19 (`external_id`) | PASS |

The raw envelopes contained 124983 findings with non-null, unique vendor IDs.
Exactly 65 lacked `cve.id`; the remaining 124918 carried both `vulnerability_id`
and `cve.id`. The parser's 124918-row output therefore equals the complete-CVE
subset exactly. This is intentional CVE-only shaping in
`BaseParser.post_process`, which removes vulnerability findings with an empty
`cve_ids` array; it is not loss caused by replay correlation or deduplication.

The policies frame has the exact 15-column projection schema:

```text
external_id:string, name:string, source_tool:string, type:string,
platform:string, status:string, criticality:string, enabled:boolean,
is_default:boolean, precedence:int, description:string, settings:string,
rules:string, policy_edges:string, additional_fields:string
```

No nulls occurred in `external_id`, `name`, `source_tool`, `type`, `status`,
`rules`, or `policy_edges`.

Sanitized policy distributions:

- status: `untested=19`
- type: `prevention=19`
- platform: `Windows=11`, `Linux=7`, `Mac=1`
- enabled: `true=19`
- is_default: `null=19`

## Contract findings

### Policy edge identity domain — FAIL

- Distinct `policy_edges[].asset_match_key`: 47
- Distinct non-null final `assets_df.value`: 43
- Edge keys matching `assets_df.value`: **0**
- Distinct generated final `assets_df.id`: 47
- Edge keys matching `assets_df.id`: **0**
- The final asset schema does not retain an `aid` column.

The edge projection uses the collector record's CrowdStrike AID, while the final
asset `value` is hostname/current IP and `id` is parser-generated. A downstream
join from `policy_edges[].asset_match_key` to `cybi.asset.value` therefore cannot
materialize any of the 47 observed asset-policy relationships for this real batch.

### Rule value JSON shape — FAIL

All 939 observed `rules[].value` entries classified as
`json_encoded_object`: the outer `rules` JSON contains each rule value as a JSON
string whose contents encode an object. It is not a native nested JSON object.
This conflicts with a downstream `jsonb_to_recordset(... value jsonb)` contract
that expects the analytical value to be native JSON rather than a JSON string.

## Persistence handoff

The exact 19-row policy frame was written to
`/private/tmp/falcon-policy-e2e-20260820/policies.parquet`. It contains customer
policy payload and was deliberately not copied into task evidence. The sanitized
machine-readable aggregates are in `w1-parser-replay-result.json`.
