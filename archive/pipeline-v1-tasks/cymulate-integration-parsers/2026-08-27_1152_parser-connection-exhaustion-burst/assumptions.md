# Assumptions

All entries are OPEN. This skill holds no evidence; the verifier disposes of every row against the
diff from `baseRef` and the artifacts produced.

## Prior Art

Recall tags: `cymulate-integration-parsers`, `postgres`, `spark`/`jdbc-write`, `glue`,
`connection-exhaustion`.

Matched 20 rows in `~/Dev/AILedger/lessons.md`; no row is superseded or retracted, so no
head-filtering applied. Triaged the newest 10 relevant rows and seeded them below. **10 matched rows
were dropped as belonging to a different failure class** (Falcon streaming transport, checkpoint
resume, auth breadth, JSON double-encoding). Followed 2 of the allowed 3 pointers:
`2026-08-19_1626_tenableio-parser-postgres-load` (covers 9 of the 10 seeds) and
`cymulate-integration-adapters/2026-08-18_0829_falcon-spotlight-fetch-concurrency` (covers P10).

Four `verify:` commands were re-run rather than taken on faith; results noted inline.

- **P1** OPEN — The number of concurrent Postgres connections from a Spark JDBC write equals the
  DataFrame's partition count. Refuted before: concurrency is `min(partitions, task slots)`, slots =
  executors x `spark.executor.cores`; the exposures write had 100 partitions but 37 concurrent tasks.
  source: lessons.md#L-90bc1864 (verifier, 2026-08-19).
  *verify re-run: `MAX_PARTITIONS = 100` still at `sparkDAL.py:9`, used at `:175`. Constant unchanged
  since the lesson was minted, so the lesson still applies to current code.*
- **P2** OPEN — A Glue parser failing with `An error occurred while calling oNNN.jdbc. This connection
  has been closed.` tells you why the write failed. Refuted before: SQLSTATE 08003 comes from
  pgjdbc `checkClosed()`; Spark 3.3.0 `savePartition`'s `finally` calls `conn.rollback()` unguarded
  (`JdbcUtils.scala:748`), which on a dead connection throws, discards the primary exception, and
  skips `conn.close()` — leaking the connection.
  source: lessons.md#L-9a2a1c4d (researcher, 2026-08-19).
  *verify re-run: 84 runs in the last 200 carry that message — matches this task's independent count
  of 83 on 2026-08-18 plus 1 on 2026-08-19.*
- **P3** OPEN — Dividing a statement's Performance Insights AAS by its average latency gives the number
  of concurrent writers. Refuted before; the same task measured 457 RDS Proxy connections and 570 peak
  `DatabaseConnections` against 37 actual write tasks. Carried here because it is the source of the
  claim that an RDS Proxy exists at all.
  source: lessons.md#L-1587791b (verifier, 2026-08-19).
- **P4** OPEN — Per-row index maintenance is a material share of the exposures INSERT CPU. Never tested;
  the index list came from `cybi-db-models` at HEAD, not STG's live catalog. Relevant here only because
  index cost lengthens how long each write holds its connection.
  source: lessons.md#L-0b629a04 (verifier, 2026-08-19).
  *verify re-run: 9 `@@index` in `integration.parser-output-exposures.prisma`. The lesson says
  "11 B-trees"; the difference is unexplained and the live catalog is still unread.*
- **P5** OPEN — The deployed `parsers.whl` can be downloaded and inspected to prove which code path
  shipped. Drifted before; GetObject is 403 to this role.
  source: lessons.md#L-727045fe (executor, 2026-08-19).
  *verify re-run: still Forbidden. Confirm shipped code by grepping the driver log for a
  lane-specific log string instead.*
- **P6** OPEN — A Mongo `_id` handed over as a run identifier can be resolved to its run document in
  stg. Never done; the SSO role is not a mapped Atlas user.
  source: lessons.md#L-ab924833 (verifier, 2026-08-19).
- **P7** OPEN — The Tenable.io collector has no checkpoint/resume path, so mid-run replay cannot
  duplicate published findings. Refuted before: a resume path exists and "Resume cancelled.
  Vendor=Tenable.io Flow=CollectFindings" fired four times **on 2026-08-18** — the day of this burst.
  Directly relevant: resume behaviour affects how many batches get published, hence arrival rate.
  source: lessons.md#L-57acd253 (verifier, 2026-08-19).
- **P8** OPEN — TenableIo's publish pipeline is a sequential pump with no ordered parallel-collect
  stage. Never examined. Relevant because publish shape sets the batch arrival rate that drives Glue
  concurrency.
  source: lessons.md#L-766ef02a (verifier, 2026-08-19).
- **P9** OPEN — `additional_fields` is effectively the finding's whole plugin object. Refuted before;
  measured 10,437 B per exposure row, 100% of rows over the 2 KB TOAST threshold. Relevant here only
  as connection hold time: a wider row means a longer-held writer connection.
  source: lessons.md#L-b345971b (verifier, 2026-08-19).
- **P10** OPEN — A published headroom number is ours to spend on concurrency. Refuted before in a
  different subsystem: the Falcon rate budget was per customer account and pooled across every client,
  with occupancy invisible to us. The transferable shape: **`max_connections` is not the parser's
  budget** — it is shared with Exposure Analytics, the cron services and the RDS Proxy pool. Any
  repo-side concurrency cap sized as a fraction of it repeats this error.
  source: lessons.md#L-5e32d960 (researcher, 2026-08-18).

## This task's own assumptions

### Repo behaviour — settled by reading code
- **A1** OPEN — `write_to_postgres` retrying in `append` mode without `before_retry` causes duplicate
  rows on the assets and exposures lanes, and `count_rows_by_scope` then reports the inflated number.
  Settled by: reading `sparkDAL.py:190-247` and `script.py:493-604`, then constructing the row-count
  consequence for a run whose attempt 1 partially committed.
- **A2** OPEN — `SparkDAL._ensure_connection` cannot detect a server-side or peer connection drop,
  because psycopg2 sets `.closed` only on locally-known closure. Settled by: psycopg2 documentation on
  `connection.closed` plus enumerating every call path that relies on the guard.
- **A3** OPEN — Every Postgres write in the parser goes through `SparkDAL`; there is no other JDBC or
  psycopg2 write path in the repo. Settled by: exhaustive grep for `.write.jdbc`, `psycopg2.connect`
  and `cursor.execute` outside `libs/packages/dal/`.
- **A4** OPEN — One parser run opens exactly one long-lived psycopg2 connection, created at job start
  and held across the whole Spark computation. Settled by: reading `jobBase.py:222`,
  `dbManager._init_connections`, and `script.py:116-121` for construction order.
- **A5** OPEN — The psycopg2 control connection sits idle for the entire parse phase, and the TCP
  keepalives set at `dbManager.py:82-92` are the only thing holding it open. Settled by: tracing the
  call order in `script.py` between connection construction and the first `delete_query`.

### Incident mechanics — settled operationally
- **A6** OPEN — The 2026-08-18 connection climb was caused by concurrent `stg-cybi-parser` runs and not
  by another tenant of the cluster. Settled by: counting concurrent Glue workflow runs in the window
  from the Glue API and comparing the count x per-run connection cost against the observed climb.
- **A7** OPEN — The effective `max_connections` ceiling is ~1000. Inferred from both instances capping
  at 967/968. Settled by: `rds:DescribeDBParameters` — **not available to this role**; needs someone
  with RDS read access.
- **A8** OPEN — instance-0's drop from 967 to 72 was a failover or restart, not a mass client-side
  disconnect. Settled by: `rds:DescribeEvents` — **not available to this role**.
- **A9** OPEN — The Spark 3.3.0 `savePartition` rollback leak is reachable from this repo's write path
  and did leak connections during the burst. Settled by: confirming the Spark version Glue 4.0 actually
  runs, and reading `JdbcUtils.savePartition` for that version.
- **A10** OPEN — `idle_in_transaction_session_timeout` and `transaction_timeout` on
  `aurora-pg16-cluster` are not set to values that would terminate a parser session mid-write. Settled
  by: `rds:DescribeDBClusterParameters` — **not available to this role**.

### Alerting
- **A11** OPEN — The Slack "Controller Exception" for item #4 was produced by the `logger.error` inside
  `delete_query`'s `except` block, via the Elastic tee, and not by an unhandled exception. Settled by:
  reading `elasticDAL.log_to_elastic` and the `_ElasticTeeLogger` proxy to see what escalates a
  handled error-level log, and where the stack trace on the alert comes from.
- **A12** OPEN — Nothing in this repo distinguishes a handled-and-recovered error from a fatal one when
  writing to Elastic. Settled by: grepping `libs/packages/dal/elasticDAL.py` and
  `libs/packages/job/` for a severity or recovered/fatal discriminator.
