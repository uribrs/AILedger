# Prompt Contract — parser-connection-exhaustion-burst

Role:
You are a senior data-platform engineer specialising in PySpark on AWS Glue and PostgreSQL/Aurora
connection behaviour, working in the `cymulate-integration-parsers` repo.

Goal:
Determine what in this repo caused or amplified the 2026-08-18 stg connection-exhaustion incident,
separate repo-owned defects from infrastructure-owned ones, and deliver a plan the operator can choose
a fix layer from. Produce understanding and a plan — **no code changes this pass**.

Context:

Evidence already gathered. Each item is tagged with its evidence tier. Do not re-derive; extend.

**Tier: verified in repo (source read at `baseRef` 743293d)**
- `sparkDAL.py:56-68` — `delete_query`: line 60 is `cursor.execute(query)` inside the `try`. The
  `except psycopg2.OperationalError` at :62 logs, `_reconnect()`s at :64, and re-executes at :66.
- `sparkDAL.py:7-9` — `ROWS_PER_PARTITION = 50000`, `MIN_PARTITIONS = 4`, `MAX_PARTITIONS = 100`;
  applied at `:175` as `max(MIN, min(row_count // ROWS_PER_PARTITION, MAX))`.
- `sparkDAL.py:11-12` — `MAX_WRITE_RETRIES = 3`, `RETRY_BASE_DELAY_SECONDS = 10`; retry loop at
  `:222-247` with linear backoff `10 * attempt`, re-raising `last_exception` after attempt 3.
- `sparkDAL.py:27-32` — `_ensure_connection` tests only `self.connection.closed != 0`.
- `sparkDAL.py:213-215` — JDBC URL carries `connectTimeout=30&socketTimeout=3600&tcpKeepAlive=true`.
- `sparkDAL.py:205-211` — write properties include `reWriteBatchedInserts=true`,
  `stringtype=unspecified`, `numPartitions`, `batchsize`.
- `dbManager.py:82-92` — psycopg2 connect with `sslmode=require`, `keepalives=1`,
  `keepalives_idle=60`, `keepalives_interval=10`, `keepalives_count=5`.
- `script.py:125` — `write_mode: "append"`, `batchsize: "50000"`.
- `script.py:116-121` — `SparkDAL` constructed with
  `connection_factory=self.connection_manager.create_postgres_connection`.
- `script.py:310-327` — `_delete_output_scope`; docstring: "Delete exactly this run's scope, including
  partial JDBC retry output." Batch mode uses single-shot `delete_query`; legacy mode uses
  `delete_by_scope_batched`.
- `script.py:530` assets write and `script.py:566` exposures write pass **no** `before_retry`.
  `script.py:591` policies write **does**.
- Only `libs/packages/dal/sparkDAL.py:226` calls `.write.jdbc` in live (non-`build/lib`) code.

**Tier: verified operationally (AWS API, this session)**
- Glue job `stg-cybi-parser`: `MaxConcurrentRuns=100`, 10 workers, `G.1X`, MaxCapacity 10.0,
  Timeout 2880. Identical on `rfqa-`, `prod-us-east-` and `prod-eu-west-cybi-parser`.
- Workflow run `wr_b255af238beeb01444181c681825964e1d0ca2a5da29e580ecb5fb17b9f210ab`, 2026-08-18
  17:20:03-17:35:17 IDT, Status COMPLETED with `FailedActions: 1`. Node `stg-dex` SUCCEEDED in 37s;
  node `stg-cybi-parser` job run `jr_9e2b9c640cb1166efd9395391c39c659bf7c6c65cad6b61859dbfc1bdcbb46a2`
  FAILED after 794s with `Error Category: UNCLASSIFIED_ERROR; An error occurred while calling
  o1849.jdbc. This connection has been closed.`
- Run properties: flow "Tenable.io - Tenable.io Assets and Findings", `BATCH_KEY` ending
  `batch_002089`, `IS_BATCH=true`, `BATCH_ID=ea4f2f93-87d6-5e25-8f47-89efaf5cd92d`,
  client `613f3842d8ddf900117fccfa`, instance `251f9990-d0cb-4fa4-84e2-a2ad418c4ece`,
  `extra-py-files=s3://cym-glue/stg/libs/parsers.whl`.
- CloudWatch `AWS/RDS DatabaseConnections`, per instance, 2026-08-18 IDT, 60s Maximum:
  - instance-0: 414-432 through 17:17 → 486 @17:18 → 617 @17:19 → 843/850/837/816/810 @17:20-17:24
    → 511 @17:25 → 812 @17:26 → **967 @17:27** → **72 @17:28**.
  - instance-1: flat 228-247 through 17:27 → 607 @17:28 → 885 @17:29 → **968 @17:30** → 892 @17:32
    → **69 @17:35**. Then 324-332 @17:39-17:45, 621 @17:49, 609 @17:50, 99 @17:52.
- `stg-cybi-parser` job-run classification, last 200 runs (2026-08-18 → 2026-08-27):
  - 2026-08-18: 83 `This connection has been closed`, 10 `I/O error occurred while sending to the
    backend`, 11 `server closed the connection unexpectedly`, 6 `Max concurrent runs exceeded`,
    15 other, 47 SUCCEEDED.
  - 2026-08-19: 1 connection error. **2026-08-20 → 2026-08-27: zero.**
  - `prod-us-east-cybi-parser`: zero connection errors in 200 runs back to 2026-05-31.
    `rfqa-cybi-parser`: 2 on 2026-07-15.
  - Later stg failures are unrelated: Cortex XDR split contract, void-struct `AnalysisException`,
    `KeyError: 'parserFile'`, Mongo no-primary.
- Cluster: `stg-pg-cybi-aurora-cluster`, aurora-postgresql, 2 x `db.r7g.2xlarge`, cluster parameter
  group `aurora-pg16-cluster`, instance parameter group `default.aurora-postgresql16`.
- Glue CloudWatch log groups have **3-day** retention; the 2026-08-18 driver log is gone.

**Tier: inference, not yet evidence** — carried as assumptions A6-A10, and as
"Proceeding on unverified" lines in `decisions.md`.
- Effective `max_connections` ≈ 1000, inferred from both instances capping at 967/968.
- instance-0's 967→72 drop was a failover or restart.
- An RDS Proxy fronts the cluster (measured by the prior task, unreadable by this role).
- The Spark 3.3.0 `savePartition` rollback leak is reachable from this write path.

**Tier: prior art, stale operational evidence** — 10 seeded rows, see `assumptions.md` § Prior Art.
The load-bearing ones: per-run write concurrency is `min(partitions, task slots)` ≈ 37, not 100
(L-90bc1864); pgjdbc `08003` masks the primary exception and leaks the connection (L-9a2a1c4d);
`max_connections` is not this repo's budget to spend (L-5e32d960).

Constraints:
See `constraints.md`. The binding ones:
* No code changes, migrations, deploys or job re-triggers this pass. Stop after `orchestration_plan.md`.
* `libs/packages/dal/` and `base_parser.py` are shared by every parser in four environments — state
  blast radius explicitly for any candidate that touches them.
* The fix layer is the operator's decision. Do not presume it, and do not rank a single winner without
  showing the infrastructure alternative beside it.
* Per-run connection arithmetic uses measured task slots, not `MAX_PARTITIONS`.
* Do not plan around reading: RDS parameters, RDS events, RDS proxies, the deployed wheel, Mongo stg,
  or Glue driver logs older than 3 days. All are AccessDenied or expired.
* Prior-art rows are OPEN assumptions, not facts.

Success Criteria:
1. Every claim in Context is dispositioned as verified-in-repo, verified-operationally, or inference —
   each inference naming the exact command or access that would settle it.
2. The causal chain from batch fan-out → N concurrent Glue runs → N x per-run connections → ceiling →
   which error surfaces at which layer, written with the arithmetic shown and every unknown term named.
   Per-run connection cost must be measured or bounded, not assumed.
3. A1 (append + retry duplicate rows) confirmed or refuted from source, with the concrete row-count
   consequence and the affected lanes named.
4. A2 (`_ensure_connection` cannot see a server-side drop) confirmed or refuted, with every dependent
   call path listed.
5. Candidate changes split into repo-owned and infrastructure-owned, each with expected effect, blast
   radius, and how it would be validated. No single winner declared without its infrastructure
   counterpart shown.
6. Open questions listed with the exact command or the specific access grant needed to close each.
7. A named answer to why a handled recovery reached Slack as a "Controller Exception".

Execution Rules:
* Do not assume missing data. Where a fact cannot be obtained, say so and name what would obtain it.
* Respect constraints strictly.
* Read code before ranking options. The prior task's ranking was revised twice by measurement.
* Distinguish what was measured from what was reasoned, in every artifact.
* Do not re-open the Tenable.io `additional_fields` payload defect; note interactions only.

Output Format:
* `orchestration_plan.md` — classification, attention items, recon findings, execution path, and the
  candidate table split repo-owned vs infrastructure-owned.
* `research/*.md` for any topic requiring external behaviour (Spark/pgjdbc/psycopg2/Aurora semantics).
* `execution_notes.md` — recon findings with `file:line` citations.
* `review/verifier-N.md` — assumption disposition table covering every row in `assumptions.md`.
* A short operator-facing summary in the final response: the causal chain, the confirmed defects, the
  fix-layer choice, and the open questions.

Stop Conditions:
* Stop after the orchestration plan and review passes. Do not execute code changes.
* Stop and surface if recon contradicts a Context claim — that is a finding, not an obstacle.
* Stop and surface if a success criterion cannot be met with available access.
