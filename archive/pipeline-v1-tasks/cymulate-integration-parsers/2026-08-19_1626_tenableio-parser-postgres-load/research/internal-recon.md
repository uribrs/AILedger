# Internal Recon

## Durable sources read
- `CLAUDE.md` (repo root) — already settles: Glue 4.0 / Python 3.10 / PySpark 3.3.0; the two output tables; that the parser is a wheel loaded by zipimport; the **Glue logger gotcha** (`self.options.logger` is log4j: `info`/`warn`/`error` only, single string, no `%s`); that `post_process` centrally normalizes, sanitizes control chars, explodes `cve_ids` and drops CVE-less vulnerability findings. Not restated below except where the code adds detail.
- `.claude/skills/yaml-parser-reference/` — not consulted for the write path; it documents spec keys and field primitives only, and `tenable-assets-findings` is a Tier-3 delegate spec (`source: {}`, all logic in the deprecated class), so it settles nothing about DB load.
- `libs/packages/parsers/yaml_engine/specs/tenable-assets-findings.yaml:29-35` — confirms the spec is metadata-only: `process_hook` → `parsers.yaml_engine.delegate_hook.delegate_to_original`, `skip_post_shaping: true`, delegate `parsers.deprecated.tenable.tenableAssetsAndFindings.TenableAssetsAndFindings`. `libs/packages/parsers/yaml_engine/delegate_hook.py:47-48` just calls `original.run()` — the engine adds no extra pass, no extra action, no second post_process.

## The DB write path

**It is a Spark JDBC `DataFrameWriter.jdbc()`. There is no driver-side row loop anywhere.**
`grep -rni "insert into" libs/ jobs/` returns exactly one hit, in `elasticDAL.py` — nothing psycopg2-side inserts into either parser_output table. psycopg2 in this path is used only for DDL, DELETE and COUNT.

`libs/packages/dal/sparkDAL.py:225-230`:
```python
prepared_df.write.jdbc(
    url=jdbc_url,
    table="integration." + dbtable,
    mode=postgresWriteConfig["write_mode"],
    properties=properties,
)
```

### Connection properties and URL
`libs/packages/dal/sparkDAL.py:202-215`:
```python
properties = {
    "user": self.connection.info.user,
    "password": self.connection.info.password,
    "numPartitions": str(target_partitions),
    "batchsize": postgresWriteConfig["batchsize"],
    "driver": "org.postgresql.Driver",
    "stringtype": "unspecified",
    "reWriteBatchedInserts": "true",
}

jdbc_url = (
    f"jdbc:postgresql://{self.env_vars['postgresHostname']}:{self.env_vars['postgresPort']}"
    f"/{database_name}?connectTimeout=30&socketTimeout=3600&tcpKeepAlive=true"
)
```
- **`reWriteBatchedInserts=true` IS set** — as a JDBC *property*, not on the URL. Spark's `JDBCOptions.asConnectionProperties` forwards every property whose key is not a known Spark JDBC option to `DriverManager.getConnection`, and `reWriteBatchedInserts` is not a Spark option name, so it reaches pgjdbc with its casing preserved. So a batch is rewritten into multi-row `INSERT ... VALUES (...),(...),...`, **not** N single-row statements.
- `stringtype=unspecified` is what lets the `additional_fields` string bind into the `jsonb` column — meaning **Postgres parses the JSON server-side for every row** (see row width below).
- **No `isolationLevel`** is set → Spark's default `READ_COMMITTED`. Spark's `savePartition` sets `autoCommit(false)`, `addBatch` per row, `executeBatch()` every `batchsize` rows, and **one `commit()` per partition** — so each write task is one transaction spanning its whole partition.
- **No `truncate`, no upsert / `ON CONFLICT`, no staging table, no `sessionInitStatement`.**

### batchsize
`jobs/cybi-parser/script.py:69-72`:
```python
self.postgresWriteConfig: PostgresWriteConfig = {
    "batchsize": "50000",
    "write_mode": "append",
}
```
`batchsize = 50000` (not the JDBC default of 1000). Default constant `DEFAULT_BATCH_SIZE = "50000"` at `sparkDAL.py:10` is unused by this path. Note `ROWS_PER_PARTITION = 50000` (`sparkDAL.py:7`) equals batchsize, so a typical partition flushes in a **single** `executeBatch()` of up to 50 000 rows — one very large client-side batch, one transaction, one commit.

### Write parallelism / concurrent Postgres connections
`libs/packages/dal/sparkDAL.py:7-9,173-200`:
```python
ROWS_PER_PARTITION = 50000
MIN_PARTITIONS = 4
MAX_PARTITIONS = 100
...
def _calculate_partitions(self, df):
    row_count = df.count()
    target = max(MIN_PARTITIONS, min(row_count // ROWS_PER_PARTITION, MAX_PARTITIONS))
```
- `_prepare_df` (`sparkDAL.py:179-188`) then `coalesce`s down or `repartition`s up to that number, so **the partition count reaching the writer is exactly `target_partitions`**, and `numPartitions` in the properties is set to the same value (a no-op second coalesce).
- **Concurrent Postgres connections from one job run = min(target_partitions, available Spark task slots).** Nothing derives from DPU count in code; the ceiling is the fixed `MAX_PARTITIONS = 100`.
- Job allocation, `jobs/cybi-parser/config.json:39-44`: `AllocatedCapacity: 10`, `WorkerType: G.1X`, `NumberOfWorkers: 10`, `GlueVersion: 4.0` → 1 driver + 9 executors × 4 cores = **36 concurrent task slots**, so one run sustains up to ~36 concurrent writing connections, in waves, until all partitions are done.
- **`jobs/cybi-parser/config.json:8-10`: `"ExecutionProperty": {"MaxConcurrentRuns": 100}`.** Nothing in this repo throttles below that. In per-batch mode each batch is its own Glue run (`script.py:309-322`, `jobBase.py:199-218`), so N concurrent batch runs of the same instance each contribute their own ~36 writers plus one psycopg2 driver connection. This is the largest multiplier the code permits and it is not visible from the parser code alone.

### Write mode and the delete that precedes it
`write_mode = "append"` — always. Refresh is delete-then-insert, `jobs/cybi-parser/script.py:309-334`:
- **Per-batch mode** (`batch_id` set): one single-shot `DELETE FROM integration.{table} WHERE client_id=… AND instance_id=… AND batch_id=…` per table (`script.py:317-322`).
- **Legacy mode**: `delete_by_scope_batched` per table (`script.py:327-334`, implementation `sparkDAL.py:70-128`). Each iteration runs
  ```sql
  WITH victims AS (SELECT ctid FROM integration.<table> WHERE client_id = %s AND (instance_id = %s OR client_integration_id = %s) LIMIT 10000)
  DELETE FROM integration.<table> t USING victims v WHERE t.ctid = v.ctid
  ```
  committing per 10 000-row batch and looping until a short batch. This repeats the same `WHERE` scan once per batch; whether that is an index scan or a heap scan depends on indexes that this repo does not own (see Open questions). It is a plausible competing source of sustained DB load and will appear in Performance Insights as a *separate* statement from the INSERT.
- `script.py:335`: `time.sleep(3)` between the delete and the write.
- Order is **assets first, then exposures** (`script.py:361-363` then `script.py:397-401`), each followed by a `count_rows_by_scope` (`sparkDAL.py:130-171`) — one `SELECT COUNT(*) … WHERE client_id/instance_id[/batch_id]` per table.

### Retry that can re-send rows
`libs/packages/dal/sparkDAL.py:11-12,222-247`:
```python
MAX_WRITE_RETRIES = 3
RETRY_BASE_DELAY_SECONDS = 10
...
for attempt in range(1, MAX_WRITE_RETRIES + 1):
    try:
        prepared_df.write.jdbc(... mode=postgresWriteConfig["write_mode"] ...)
```
The whole `write.jdbc` is retried up to **3 times** with 10 s / 20 s backoff, in `append` mode, with **no cleanup between attempts**. A Spark JDBC write is not atomic across partitions — partitions that committed before the failure stay committed — so **any retry re-sends every row of the DataFrame**, producing both duplicate rows and up to 3× the insert load. Same non-idempotency applies at Spark task level: `spark.stage.maxConsecutiveAttempts` is raised to `10` (`jobBase.py:414`), and a task that fails after its `commit()` but before Spark records success is re-run, re-inserting that partition.

### Caching / recomputation
`jobs/cybi-parser/script.py:289-292` deliberately guards against this:
```python
if assets_df is not None:
    assets_df = assets_df.persist(StorageLevel.DISK_ONLY)
if findings_df is not None:
    findings_df = findings_df.persist(StorageLevel.DISK_ONLY)
```
The parser output is persisted DISK_ONLY before any action, so the source parse / explode / join lineage runs once. Downstream there are still **three full scans of the cached frame plus the write**, on the exposures side:
1. `script.py:298` `findings_df.count()` (raw count),
2. `script.py:392` `prepared_findings_df.count()` (pre-save count),
3. `sparkDAL.py:174` `df.count()` inside `_calculate_partitions`,
4. the `write.jdbc`.

These are Spark cost, not DB cost. Additionally `TenableAssetsAndFindings.process` caches the assets frame (`tenableAssetsAndFindings.py:526` / `:533`, `.cache()` = MEMORY_AND_DISK) because it is both an output and the join lookup.

## Row fan-out

### The CVE explode
`libs/packages/parsers/common/base_parser.py:295-318`:
```python
with_cve = self.findings_df.filter(F.col("cve_ids").isNotNull() & (F.size(F.col("cve_ids")) > 0))
without_cve = self.findings_df.filter(~(F.col("cve_ids").isNotNull() & (F.size(F.col("cve_ids")) > 0)))
exploded = (
    with_cve.withColumn("_cve", F.explode("cve_ids"))
    .drop("cve_ids")
    .withColumn("cve_ids", F.array(F.col("_cve")).cast(ArrayType(StringType())))
    .drop("_cve")
    .withColumn("id", helpers.uuid_udf())
)
...
self.findings_df = exploded.unionByName(without_cve)
```
A finding with N CVEs becomes **exactly N output rows**, each with a fresh `id` UUID and a single-element `cve_ids`. `create_finding_source` then flattens it to the scalar `cve_id` column (`base_parser.py:527-530`: `F.col("cve_ids").getItem(0)`). A finding with 0 CVEs stays one row.

**Confirmed: vulnerability findings with no usable CVE are dropped** — `base_parser.py:291-303`:
```python
self.findings_df = self.findings_df.filter(
    ~((F.col("type") == "vulnerability") &
      (F.size(F.expr("filter(cve_ids, x -> x is not null AND x != '')")) == 0))
)
```
For Tenable every finding is hard-coded `type = "vulnerability"` (`tenableAssetsAndFindings.py:344`) and `cve_ids` comes from `plugin.cve` (`:369-370`), so **the Tenable exposure row count is exactly `sum over findings of len(plugin.cve)`** — informational/no-CVE plugins contribute zero rows, multi-CVE plugins contribute one row each. This is the dominant volume term.

### Other fan-out in the Tenable correlated path
- `tenableAssetsAndFindingsCorrelated.py:96-99` — `explode(findings)`: one row per finding inside each envelope. This is a flattening of the envelope, not a duplication: total findings rows = total findings across all chunk envelopes.
- No per-port or per-plugin explode in the parser. Tenable's `/vulns/export` already emits one record per (asset, plugin, port); `port` survives only inside `additional_fields`.
- `finding_ids` on the asset side **cannot explode**: `tenableAssetsAndFindings.py:54-58` defines it as `path=None, default_value=F.array()` — a constant empty array. `BaseParser.add_finding_ids_to_asset_df` (`base_parser.py:449`) and `add_cve_ids_to_assets` (`base_parser.py:474-491`, which does `groupBy … collect_set`) are **never called** by the Tenable parser — its `post_process` (`tenableAssetsAndFindings.py:652-678`) calls only `super().post_process()`, `create_asset_tags`, `add_unified_finding_name_column_for_vulnerability`, `create_asset_source`, `create_finding_source`.

### Join fan-out — checked, and the dedup does happen *before* the join
The concern is well founded in shape but the code guards it.

Dedup key, `tenableAssetsAndFindingsNotHydrated.py:33` and `:200-202`:
```python
_ASSET_CORRELATION_ID = "id"        # the raw /assets/export row's `id`
...
return assets_lane_df.select(opt(_ASSET_CORRELATION_ID).alias("_asset_correlation_id"), asset_struct)
```
In correlated mode `assets_lane_df` is `full_df.select("host.*")` (`tenableAssetsAndFindingsCorrelated.py:74-77`), so `_asset_correlation_id = host.id` — one row per **envelope**, i.e. one row per (host, chunk).

Dedup, `tenableAssetsAndFindings.py:502-533` — note the ordering:
```python
correlates_by_uuid = is_split or self._correlated_input          # :459
...
self.assets_df = asset_source_df.select(*asset_columns_for_selection)   # :508
...
self.assets_df = self.assets_df.withColumn("id", id_col_asset)   # :519  fresh UUID per row
if correlates_by_uuid:
    self.assets_df = self.assets_df.dropDuplicates(["_asset_correlation_id"]).cache()   # :526
```
Then, and only then, the join, `tenableAssetsAndFindings.py:611-626`:
```python
asset_id_lookup = self.assets_df.select(
    F.col("id").alias("asset_id"),
    F.col("_asset_correlation_id").alias("_lk_corr_id"),
)
self.findings_df = self.findings_df.join(
    asset_id_lookup,
    F.col("_finding_asset_uuid") == asset_id_lookup["_lk_corr_id"],
    "left",
).drop("_lk_corr_id", "_finding_asset_uuid")
```
- **`dropDuplicates(["_asset_correlation_id"])` runs at line 526, the join at line 619 — dedup is before the join**, and the lookup is projected off the already-deduped frame. So a host appearing in chunks 0..N collapses to one asset row and each finding matches exactly one lookup row. **No join fan-out from multi-chunk envelopes.**
- Multiple NULL `_asset_correlation_id` rows also collapse — Spark's `dropDuplicates` treats NULLs as equal — and NULL-keyed findings match nothing anyway (`NULL = NULL` is false in a Spark join condition), so `asset_id` is simply null for them.
- Residual risk, **not** fan-out: the dedup makes the surviving asset row an *arbitrary* chunk's host snapshot. The façade documents this as a collector invariant (`tenableAssetsAndFindingsCorrelated.py:22-26`: every chunk carries the same host snapshot verbatim). That is a comment, not a runtime guarantee — but it affects field values, never row counts.
- The `uuid_udf()` at `:519` is applied **before** the dedup, so asset `id` is not deterministic across runs; irrelevant to volume, relevant to any hope of idempotent re-writes.
- One more join, `base_parser.py:213-217`: `_enforce_asset_output_contract` joins findings to surviving asset ids with `"left_semi"` — a semi-join cannot fan out.

### Row width — this is the other half of the cost
`test_files/tenable/assets_and_findings/findings_correlated.expected_findings.json`, measured over its 8 rows:

| field | min | median | max |
|---|---|---|---|
| `additional_fields` | 3 904 B | 4 210 B | 4 822 B |
| `description` | 198 B | 198 B | 198 B |
| whole row | — | 4 812 B | 5 420 B |

`additional_fields` carries 53 keys including **`output`** (Nessus plugin output — unbounded in real data), `synopsis`, `see_also`, `xrefs`, `vpr`, `scan`, `cvss*_vector` (each nested struct re-serialized with `to_json`). It is built dynamically from every `plugin.*` sub-field plus every non-excluded root field (`tenableAssetsAndFindings.py:551-579`) and JSON-serialized at `base_parser.py:515-516`. In the fixture it is **~87 % of the row**, and the fixture's `output` values are small; production Tenable `output` blobs are routinely multi-KB. Every one of these is parsed into `jsonb` server-side (because of `stringtype=unspecified`) and TOAST-compressed on the way in.

## Input file resolution

Chain: `jobs/cybi-parser/script.py:267-273` → `parsers/preparation.py:54-111` (`tenable-assets-findings` is registered in `DUAL_MODE_PARSERS` at `preparation.py:21`) → `utilities/input_resolver.py:27-109`, and then the actual read at `tenableAssetsAndFindings.py:418-421` → `utilities/helpers.py:14-64` → `helpers._resolve_file_paths` (`:134-158`) → `_resolve_s3_file_paths` (`:182-238`).

- The resolver builds three strategies over `<base>` (`input_resolver.py:44-60`): assets `assets.json` / `assets*.json` (`selection="auto"`), split findings `findings*.json` (`selection="pattern"`), hydrated `findings.json` / `findings*.json` (`selection="auto"`).
- Correlated input has **no assets lane**, so `has_assets` is false and it falls through to the hydrated branch (`input_resolver.py:96-107`). `preparation.py:104` then sets `findings_file_path = hydrated_path or findings_strategy.single_path` — which is `<base>/findings.json` in both branches.
- **No double-read in the correlated/hydrated path.** `_resolve_s3_file_paths` is strictly single-file-first-and-exclusive (`helpers.py:200-233`): `head_object(<base>/findings.json)`; on success it returns **only** that one path; on `ClientError` it lists prefix `<base>/findings_` — which by construction cannot match `findings.json`. So `findings.json` and `findings_000001.json` are never both read here.
- **There IS a double-read hazard in the *split* path** (not the one under investigation): `selection="pattern"` goes to `_resolve_s3_paths_from_pattern` (`helpers.py:282-300`), which strips at the `*` and lists prefix `<base>/findings`, collecting **every** `.json` under it. If a directory ever held both `findings.json` and `findings_*.json`, split mode would read both and duplicate the overlap. Correlated mode does not take this path.
- **Both S3 resolvers list recursively by prefix**, so a `batch_NNNNNN/` subfolder layout under `<base>` would be picked up by the *pattern* path only if the keys start with `<base>/findings…`; a `<base>/batch_000001/findings_*.json` layout matches **neither** the `head_object` nor the `findings_` prefix, and `_resolve_s3_file_paths` then falls back to the non-existent `<base>/findings.json` (`helpers.py:235-238`), yielding a 0-column frame rather than an error. (This matches the recorded `batch_*/ layout breaks the reader` behavior.)
- `get_base_file_path` (`script.py:163-172`) is `s3://{READ_BUCKET_NAME}/{parent(ZIP_FILE_KEY)}/{stem(ZIP_FILE_KEY)}`; in batch mode ICM points `ZIP_FILE_KEY` at the per-batch prefix, so each batch run reads only its own folder.

## Output table

`libs/packages/parsers/schemas/db_schema.py:43-66` — `integration.parser_output_exposures`, **21 columns**:

`id` uuid **PRIMARY KEY**, `asset_id` uuid, `client_id` text, `client_integration_id` uuid, `instance_id` uuid, `batch_id` uuid, `integration_setting_id` uuid, `integration_setting_flow_id` uuid, `connector_flow_name` text, `created_at` timestamptz, `first_seen` timestamptz, `last_seen` timestamptz, `name` text, `display_name` text, `description` text, `mitigation` text, `type` text, `severity` text, `status` text, `cve_id` text, `additional_fields` **jsonb DEFAULT '{}'::jsonb**.

- 7 of 21 columns are `uuid`, and the primary key is a **random v4 UUID** minted per row (`base_parser.py:313` for exploded rows, `tenableAssetsAndFindings.py:637` for the base row) — every insert is a random-position B-tree leaf touch, which is the expensive pattern for a large table.
- The **wide column is `additional_fields` (jsonb)**, plus `description` and `mitigation` (Tenable plugin description/solution text). Measured median row ≈ 4.8 KB in the fixture; see Row fan-out above.
- Binds per row = 21 in batch mode (`batch_id` added at `script.py:158-159`), 20 in legacy mode (the fixture's shaped rows carry 20 keys). The Performance Insights digest `INSERT INTO integration.parser_output_exposures ("id","asset_id","client_id","cl...` matches Spark's `JdbcUtils.getInsertStatement`, which emits the quoted column list in exactly this order — confirming the write is Spark JDBC, not psycopg2.
- Assets table (`db_schema.py:4-41`) has **33 columns** including three jsonb (`additional_fields`, `agent_metadata`, `device_metadata`).
- Table-name constants, `libs/packages/dal/dbManager.py:15-25`:
  ```python
  class TABLES(str, Enum):
      DATA_TRANSFORMATION_ASSETS = "data_transformation_assets"
      DATA_TRANSFORMATION_FINDINGS = "data_transformation_findings"
      PARSER_OUTPUT_ASSETS    = "parser_output_assets"
      PARSER_OUTPUT_EXPOSURES = "parser_output_exposures"
  ```
  Schema `integration.` is prepended at `sparkDAL.py:227` and in every SQL string in `script.py` / `sparkDAL.py`.
- `db_schema.py:68-81` states explicitly that `CREATE TABLE IF NOT EXISTS` is a **no-op on a database where the table already exists** and that deployed environments get their columns from the `cybi-db-models` migrations that own the table. **So this DDL is not authoritative for STG's actual column set or index set.**

## Log lines that report counts
Grep these in the Glue CloudWatch log for the run. All are single-string f-strings (Glue-logger-safe).

- `Raw parser output counts (unfiltered)` — Elastic running-status doc carrying `assets_count` + `exposures_count` **straight out of the parser, before the delete and before any prepare step**. This is the true fan-out result. — `jobs/cybi-parser/script.py:300-304`
- `Assets count before postgres write` — `assets_count` on the prepared frame — `jobs/cybi-parser/script.py:357-360`
- `Assets count after postgres write` — `assets_count` **read back from Postgres** via `SELECT COUNT(*)`; compare against the "before" number to detect duplicate rows from a write retry — `jobs/cybi-parser/script.py:372-375` (and `:340-343` for the zero-asset case)
- `Exposures count before postgres write` — `exposures_count` on the prepared frame; **this is the number of rows the INSERT will send** — `jobs/cybi-parser/script.py:393-396`
- `Exposures count after postgres write` — `exposures_count` read back from Postgres; **before ≠ after is the duplicate-insert smoking gun** — `jobs/cybi-parser/script.py:410-413` (and `:381-384` for the zero-findings case)
- `DataFrame row count: {row_count}, target partitions: {target}` — logged **twice per run**, once per table, immediately before each write; `target` is the concurrent-connection ceiling — `libs/packages/dal/sparkDAL.py:176`
- `Coalescing from {current_partitions} to {target_partitions} partitions` — `libs/packages/dal/sparkDAL.py:182`
- `Repartitioning from {current_partitions} to {target_partitions} partitions` — `libs/packages/dal/sparkDAL.py:185`
- `Keeping current {current_partitions} partitions` — the shuffle-free case; `current_partitions` is then whatever AQE left, which can exceed `target` — `libs/packages/dal/sparkDAL.py:187`
- `write_to_postgres config: partitions={n}, batchsize={n}, table={t}` — the definitive per-table write parameters — `libs/packages/dal/sparkDAL.py:217-220`
- `DataFrame successfully written to {dbtable} PostgreSQL` — one per successful attempt; **appearing once means no retry** — `libs/packages/dal/sparkDAL.py:231`
- `Write to {dbtable} failed on attempt {attempt}/3: {e}. Retrying in {delay}s...` — **the row-duplication / 3× load signal** — `libs/packages/dal/sparkDAL.py:237-240`
- `Write to {dbtable} failed after 3 attempts: {e}` — `libs/packages/dal/sparkDAL.py:243-245`
- `[delete_by_scope] integration.{table} batch {n}: deleted {n} (total {t}, client_id={id})` — one line per 10 000-row delete batch; the **number of these lines is the number of repeated scans** — `libs/packages/dal/sparkDAL.py:118-121`
- `[delete_by_scope] integration.{table} DONE: deleted {total} row(s) in {batch} batch(es) (client_id=..., instance_id=...)` — `libs/packages/dal/sparkDAL.py:124-127`
- `Rows successfully deleted using query: <full SQL>` — batch-mode single-shot delete; echoes the whole `DELETE … batch_id = …` — `libs/packages/dal/sparkDAL.py:68`
- `Rows successfully executed using query: <full SQL>` — DDL / `CREATE SCHEMA` — `libs/packages/dal/sparkDAL.py:54`
- `[{integration_name}] output-contract dropped {dropped}/{before} asset(s) with null/blank/control-char value` — only emitted when `dropped > 0`; gives asset count **before** the contract filter — `libs/packages/parsers/common/base_parser.py:200-203`
- `Tenable IO split parser: loaded assets lane rows={n}` — `libs/packages/parsers/deprecated/tenable/tenableAssetsAndFindingsNotHydrated.py:112`
- `Tenable IO split parser: loaded findings lane rows={n}` — **split mode only; the correlated path emits NO row count** — `.../tenableAssetsAndFindingsNotHydrated.py:119`

Non-count lines that identify which path ran (grep these first to confirm the mode):
- `Tenable IO input resolver: inspecting base path={p} parser_key={k} flow_id={f} environment={AWS|LOCAL}` — `libs/packages/utilities/input_resolver.py:39-42`
- `Tenable IO input resolver: discovered inventory={...has_assets/has_split_findings/has_hydrated...}` — `libs/packages/utilities/input_resolver.py:75`
- `Tenable IO input resolver: selected hydrated contract with findings={path}` — `libs/packages/utilities/input_resolver.py:99`
- `Tenable IO input resolver: parser config mode={m} assets_strategy={...} findings_strategy={...}` — `libs/packages/parsers/preparation.py:107-110`
- `Tenable IO parser façade: resolved mode=hydrated (correlated envelope)` — proves the correlated branch — `libs/packages/parsers/deprecated/tenable/tenableAssetsAndFindings.py:426-427`
- `Tenable IO correlated parser: derived assets and findings lanes from the envelope frame` — `.../tenableAssetsAndFindingsCorrelated.py:80-82`
- `Tenable IO correlated parser: findings arrays are empty across the batch; findings lane is empty (no-vuln assets survive in the assets lane)` — `.../tenableAssetsAndFindingsCorrelated.py:90-93`

## Landmines
- **`MaxConcurrentRuns: 100`** (`jobs/cybi-parser/config.json:9`) with per-batch mode means the AAS you are looking at may be **many Glue runs at once**, not one. Any arithmetic that assumes a single run will be off by the concurrency factor. Check how many `cybi-parser` runs overlapped the Performance Insights window before dividing anything.
- **The file-resolution log messages are silent for Tenable.** `helpers.fetch_df_from_file` only emits `Found {n} multi-files: [...]`, `Loading {n} file(s) into DataFrame`, `Checking if single file exists: …` etc. when an `elastic_job_status` is passed, and the Tenable façade calls it with three positional args only (`tenableAssetsAndFindings.py:418-421`). Their **absence proves nothing** about how many shards were read. Use the input-resolver lines above instead.
- **The correlated path logs no lane row count** — only the split path does (`tenableAssetsAndFindingsNotHydrated.py:112,119`). `Raw parser output counts (unfiltered)` is the earliest count available for a correlated run.
- **`tests/test_run_parsers.py:216`** sets `_POSTGRES_WRITE_CONFIG = {"numPartitions": "5", "batchsize": "200000", "write_mode": "append"}`. The `numPartitions` key is **dead** — `PostgresWriteConfig` (`sparkDAL.py:15-17`) declares only `batchsize` and `write_mode`, and `write_to_postgres` always computes its own. Do not read production parallelism off that dict. The `batchsize` there also differs from production's `50000`.
- **`reWriteBatchedInserts` lives in the properties dict, not the URL.** Grepping the connection URL alone (`sparkDAL.py:212-215`) makes it look absent. It is set at `sparkDAL.py:209`.
- **`retry ≠ idempotent.**` `mode="append"` + 3 outer retries + `spark.stage.maxConsecutiveAttempts=10` means both a whole-DataFrame retry and a single-task retry re-insert rows with brand-new random `id` UUIDs, so the PRIMARY KEY does not deduplicate them. `count_rows_by_scope` after the write is the only thing that would expose it, and only if you compare it to the "before" count.
- **The DDL in `db_schema.py` is not STG's schema.** `db_schema.py:68-81` says so outright: `CREATE TABLE IF NOT EXISTS` no-ops, and the real table is owned by `cybi-db-models` migrations. STG may have extra columns and — crucially for insert cost — **extra indexes** this repo cannot see.
- **`delete_by_scope_batched` is a second, separate DB load source** (`sparkDAL.py:70-128`), re-running the same `WHERE client_id/instance_id/client_integration_id` predicate once per 10 000-row batch. It will show up in Performance Insights as its own statement digest, distinct from the INSERT. Do not attribute all the AAS to the INSERT without checking it.
- **PI's counters may not share a window.** `1.39 calls/sec × 117.43 ms = 0.163 AAS`, which cannot be reconciled with the reported 58.59 AAS for that statement. Either the AAS is a peak while calls/sec and rows/sec are averaged over the whole (mostly idle) window, or the numbers come from different windows. Resolve this before doing arithmetic on them.
- **`rows/call ≈ 127.5` (177.29 / 1.39).** Given `batchsize=50000` and `reWriteBatchedInserts=true`, the driver must be splitting each `executeBatch()` into multi-VALUES statements of ~128 rows. *Speculation on the mechanism:* pgjdbc caps the number of VALUES blocks in a rewritten statement to a power of two bounded by the 32767 bind-parameter limit; with 20–21 binds per row that arithmetic gives 1024, not 128, so either the cap is a fixed driver constant or the `rows` counter means something other than rows-affected here. **Do not treat the 128 as evidence of a specific driver setting without checking the pgjdbc version on the Glue classpath.**
- Glue logger gotcha (per `CLAUDE.md`) — relevant only if you add logging: `logger.warning()` does not exist and `%s` lazy args are not interpolated. Every line listed above is already an f-string.
- `--job-bookmark-option: job-bookmark-disable` (`jobs/cybi-parser/config.json:26`) — no bookmark, so a re-run always reprocesses the whole input. Not a bug, but it means "why did it write those rows twice" is never answered by bookmarks.

## Open questions the code alone cannot answer
- **How many `cybi-parser` Glue runs were concurrent during the Performance Insights window?** With `MaxConcurrentRuns: 100` and per-batch dispatch this is the single biggest unknown multiplier. Needs `glue get-job-runs` / the workflow run list for the window.
- **Was this run in per-batch mode or legacy mode?** Grep the log for `Rows successfully deleted using query: … batch_id` (batch) vs `[delete_by_scope] … DONE` (legacy). This changes both the delete cost and whether many sibling runs were writing at once.
- **What are STG's actual indexes on `integration.parser_output_exposures`?** The repo's DDL declares only the `id` primary key, and `db_schema.py:68-81` says the deployed schema is owned by `cybi-db-models`. Every extra index multiplies the per-row insert cost, and the absence of an index on `(client_id, instance_id)` would make `delete_by_scope_batched` a repeated sequential scan. Needs `\d+` against the STG database.
- **What was `target_partitions` for the exposures write?** Grep `write_to_postgres config: partitions=… table=parser_output_exposures`. This is the concurrent-connection count for that run and the code cannot predict it without the row count.
- **Did the write retry?** Grep `Write to parser_output_exposures failed on attempt`. If it did, rows were re-sent and the load is 2–3×.
- **Do `Exposures count before postgres write` and `Exposures count after postgres write` agree?** A larger "after" is direct proof of duplicate inserts.
- **What is the real median `additional_fields` size for this client?** The 4.2 KB median is from an 8-row fixture whose `output` values are small. Production Nessus plugin output dominates the row and therefore the WAL/TOAST cost. Needs a `SELECT avg(pg_column_size(additional_fields))` against the STG table or a sample of the S3 input.
- **Which pgjdbc version is on the Glue 4.0 classpath?** Determines whether the rewritten-INSERT VALUES-block cap explains the observed ~128 rows/call.
- **What else was hitting that RDS instance during the window?** `parser_output_assets` is written first from the same job, `create-entities` consumes both tables downstream, and the batched deletes run against both. 280 AAS total vs 58.59 AAS for the exposures INSERT means **~78 % of the load was something else** — the remaining top statements are needed.
