# Internal recon: Falcon policy S3 -> parser -> Postgres -> consumer

Recon date: 2026-08-20. Product repositories were inspected read-only. No payload values, credentials, or customer identifiers are recorded here.

## 1. Collector and object contract

- The completed local run is `logs/20260820-135639/local-adapter-runner.log`, correlation `a2d4279b-38dd-4293-a66f-7496d402f012`.
- Terminal proof is present:
  - line 846: `run.receipt ... completed`, one page, 47 staged/emitted hosts, 124,983 findings;
  - line 860: `Publish Completion: Success=true`;
  - line 870: `Adapter final outcome. Success=true`.
  - The line-827 50 MiB soft-threshold warning is explicitly non-fatal; the 324,046,289-byte object was published atomically and acknowledged at line 845.
- Staging keys are `{run-prefix}/_staging/{generation}/hosts_{page:D6}.json` and `manifest.json`; see `FalconStagingPaths.cs:30-36,113-135`. The manifest is written last and is Phase-1 completion proof (`FalconPhase1Manifest.cs:8-22`).
- Actual Phase 1 first freezes the Discover scroll, then enriches and atomically rewrites staged pages, then writes the manifest (`FalconHostSpooler.cs:198-220`). This supersedes the older README shorthand saying Phase 1 has no enrichment: policy calls are outside the mutable scroll, but still occur before the Phase-1 manifest.
- The final parser-visible object is `{run-prefix}/findings_000001.json` (log line 845). Its NDJSON records are `{aid, chunk, isLastChunk, findingsInChunk, host, findings[]}` (`FalconCorrelatedRecord.cs:7-17,50-73`); `host` carries `device_policies`.
- The envelope is always explicit: schema version 1; collection status `complete`, `partial`, or `disabled`; and `prevention` is null or contains assignment, definition status, and definition (`FalconDevicePoliciesEnvelope.cs:18-25,29-40,47-73,88-100`).
- Parser discovery is deliberately blind to `_staging`: the collector guard documents that the parser matches only `assets*`/`findings*` (`FalconStagingPaths.cs:9-21,67-105`).
- Actual downloaded final artifact for replay: `/private/tmp/falcon-policy-e2e-20260820/findings_000001.json`.

## 2. Faithful parser invocation

Checkout evidence: `/Users/user/Dev/cymulate-integration-parsers` is on `feature/crowdstrike-prevention-policy-parsing` at `83626813d1b90caf0b814908c05adf1703e97062`; recorded `origin/master` is `37bdd02991ba4305fcc12aed2295e8e51d571670`. The feature revision contains two commits not present in that recorded master ref, so the operator should fetch before treating this local ref comparison as publication proof.

Do **not** use `tests/test_run_parsers.py -k crowdstrike-assets-findings` as the live-artifact runner: the YAML spec deliberately has `run: false` and routes coverage to the dedicated test (`crowdstrike-assets-findings.yaml:27-38`). The exact production-equivalent transformation path is:

1. Put the downloaded object alone in a disposable directory, preserving the name `findings_000001.json`.
2. Create Spark using the repository test fixture settings (`tests/conftest.py:87-128`) without logging rows.
3. Call `build_default_parser_options(...)`, then `prepare_parser_options("crowdstrike-assets-findings", ..., base_file_path=<disposable-dir>, flow_id=<synthetic uuid>)`, then `get_parser_class("crowdstrike-assets-findings")(options, None).run()`.
4. This exercises the real dual-mode resolver (`parsers/preparation.py:54-111`; `utilities/input_resolver.py:27-109`), selects hydrated/correlated input from `findings*.json`, performs marker detection (`CrowdstrikeAssetsFindingsCorrelated.py:76-120`), and returns assets, findings, and policies.
5. Record only schemas, counts, distinct counts, envelope-state counts, stable hashes/IDs, and cross-frame match counts. Never call the generic harness's `_log_top_rows`, which prints customer rows (`tests/test_run_parsers.py:55-71,376-378`).

Direct local Spark reading of the `s3://` prefix with `is_glue_env=True` is closer to Glue input discovery, but depends on the local Hadoop S3 connector/credential chain. Replaying the exact downloaded bytes through the same resolver and parser is the safest faithful boundary; S3 byte identity should be proved separately with object size/ETag or SHA-256.

For a non-production Postgres write, use only localhost/container credentials and assert the resolved host before connecting. `docker-compose.core.yml:43-55` exposes a local Postgres at `localhost:5432`, database/user/password `postgres`. Important: `tests/conftest.py:48` defaults `POSTGRES_DB` to `cymulate`, which conflicts with compose/README; set `POSTGRES_DB=postgres` explicitly. Create the integration tables from `parsers/schemas/db_schema.py`, prepare policies with the production job's `_prepare_policies_df` and `_policies_df_for_write` (`jobs/cybi-parser/script.py:218-308`), and write through `SparkDAL`. The production write/count boundary is `script.py:584-610`; the test harness demonstrates loading those preparation methods without constructing all of `JobBase` at `tests/test_run_parsers.py:419-443`. Use synthetic run-scope UUIDs and query back only that scope.

## 3. Parser policy contract

- Projection is one distinct row per prevention policy and retains assignment-only policies with edges (`crowdstrike_policy_projection.py:228-283`).
- Projection-owned fields are `external_id, name, source_tool, type, platform, status, criticality, enabled, is_default, precedence, description, settings, rules, policy_edges, additional_fields` (`:87-108`). Job preparation adds the run-scope/identity fields and selects the exact 26-column staging shape (`script.py:56-83,218-290`).
- `source_tool=crowdstrike`, `type=prevention`, ingest status `untested`; rules/settings and deterministic policy edges are built at `crowdstrike_policy_projection.py:321-373`.
- Parser DDL for `integration.parser_output_policies` (`parsers/schemas/db_schema.py:69-104`) matches the migration column-for-column, including JSONB defaults and both indexes.

## 4. Receiving repositories (source readiness only)

### cybi-db-models

- Local checkout is `development` at `98b9594`; inspected source-of-truth was recorded `origin/master` `128234cfb78c9e90845889e54302a3e89f370679` via `git show`.
- `origin/master:migrations/20260802120000_add_parser_output_policies_tables/migration.sql:35-70` creates the keyless staging table with the same 26 columns and the instance/client/id plus instance/batch indexes.
- `origin/master:migrations/20260802120100_create_security_policy_tables/migration.sql:27-72,84-143` creates `cybi.security_policy`, `cybi.security_policy_rule`, and `cybi.asset_policy` with the natural key, child keys, and indexes.

### cymulate-exposure-analytics

- Local checkout is `master` at `8bdfdf5` and is 39 commits behind recorded `origin/master` `d3c3eddb7bbbbde3a3705b23641c24785033d935`; inspection used `git show origin/master:...`. This proves source availability on the recorded remote ref, not what is deployed.
- `policy.repository.ts:83-99` declares the path from `integration.parser_output_policies` to `cybi.security_policy`, `cybi.security_policy_rule`, and `cybi.asset_policy`.
- It gates policy work by run scope (`:125-153`), keyset-pages staging IDs (`:156-191`), and ingests each policy/rule/edge page transactionally (`:194-239`). It reads/coalesces `settings`, `rules`, and `policy_edges` at `:347-380`, upserts policies on the natural key at `:489-560`, expands rules around `:612-710`, resolves and upserts edges around `:724-866`, and snapshots disappeared policies (`:258-331,901-976`).
- The service executes the page loop and sweeps at `create-entities-third-party.service.ts:2140-2289`, with explicit rejected/unmatched-edge warnings and a summary at `:2221-2258,2292-2320`.

## 5. Critical cross-repo verification gate

The parser emits each policy edge's `asset_match_key` from Falcon `aid` (`crowdstrike_policy_projection.py:67-70,261-270,349-373`; tests explicitly expect `aid-*` keys). Exposure Analytics currently resolves that edge with `cybi.asset.value = asset_match_key` (`origin/master:policy.repository.ts:785-800`). However, the CrowdStrike asset parser sets normalized asset `value` to hostname, falling back to local IP—not AID (`crowdstrikeAssets.py:49-63`). It separately maps source `id` to an `aid` field (`:48`).

Therefore source files alone do **not** prove edge resolution. The actual-artifact replay must calculate `policy_edges[].asset_match_key` intersection with parsed asset `value`; if the intersection is zero/partial, policies and rules can persist but `cybi.asset_policy` will report unmatched edges. This is a likely contract defect unless another deployed normalization step demonstrably changes `cybi.asset.value`. Treat it as a staging gate, not a cosmetic warning.

## Recommended bounded workstreams

1. **Collector/S3 evidence**: finalize log receipt, inventory the run prefix, hash/download representative final objects, compare manifest counts, and inspect only redacted envelope/state counts. Shared artifact: sanitized object inventory + SHA-256 + `/private/tmp/.../findings_000001.json`.
2. **Parser/Postgres replay**: execute the exact registry/preparer/parser path above against the downloaded bytes; write prepared frames only to explicitly-local Postgres; query back scoped counts/schema; compute edge-key-to-asset-value and edge-key-to-source-AID reconciliation. Shared artifact: synthetic scope UUIDs, schema/count/hash summary, DB query summary, no rows.
3. **Downstream contract audit**: verify recorded remote-master migration and consumer fields/joins/sweeps read-only; compare every parser column and the actual replay's identifier semantics. Shared artifact: commit refs, file/line matrix, and explicit source-vs-deployed readiness statement.
