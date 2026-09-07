# W2 — Glue run + Postgres evidence

## Method & clock (state UTC for everything)

**All timestamps in this document are UTC** unless explicitly suffixed IDT. Conversions applied:
IDT = UTC+3. Two AWS surfaces render local time and were normalised:

- `aws cloudwatch get-metric-statistics` returns `+03:00` timestamps. Where a CloudWatch table
  below shows `15:41`–`16:14`, that is **IDT**; subtract 3h for UTC. Column headers say which.
- CloudWatch Logs Insights `@timestamp` is UTC. Glue's own log line prefix (`26/08/19 12:57:31`)
  is also UTC and agrees with `@timestamp` to the millisecond, so no clock skew inside Glue.
- RDS Proxy log records carry their own embedded event time (`...Z`, UTC) which runs **up to ~5 s
  behind** the CloudWatch ingest `@timestamp` for the same record. All proxy times quoted are the
  embedded event time, and proxy events were re-sorted on it.

Credentials: `aws sts get-caller-identity` → account `118330362824`, role `AAD-Platform/urib@cymulate.com`.
No `ExpiredToken` at any point. Every call was read-only (`get-*`, `describe-*`, `filter/start-query`).

Log-retrieval constraint worth recording: `/aws-glue/jobs/logs-v2` and `/aws-glue/jobs/error` are
**`INFREQUENT_ACCESS`** log class (`aws logs describe-log-groups --log-group-name-prefix /aws-glue/jobs
--query 'logGroups[].{n:logGroupName,class:logGroupClass,retention:retentionInDays}'`), so
`GetLogEvents` and `FilterLogEvents` both fail with `ValidationException: This operation is only
supported on the Standard log class`. All Glue log evidence was therefore obtained via CloudWatch
Logs **Insights** (`start-query`/`get-query-results`). Retention on all three groups is **3 days** —
this evidence expires 2026-08-22.

Job run facts re-confirmed, not re-derived (`aws glue get-job-run --job-name stg-cybi-parser --run-id
jr_36b7a68d...befb2`): `FAILED`, `StartedOn` 2026-08-19T15:41:25.406+03:00, `CompletedOn`
16:03:44.989+03:00, `ExecutionTime` 1319, `WorkerType` G.1X, `NumberOfWorkers` 10, GlueVersion 4.0,
`DPUSeconds` 13199, `TriggerName` `stg-epc-parser-start`, `Attempt` 0 (no Glue-level retry).

---

## A. Glue driver log findings

Driver stream: `jr_36b7a68d...befb2-driver` in `/aws-glue/jobs/logs-v2`. **59,885 events** matched
the run window (Insights `recordsMatched`), so the log was mined with targeted filters rather than
dumped whole (a full sort/limit query truncates at 10,000 events, reaching only 12:46:39).

The parser's own application logging surfaces as **`GlueLogger`** (52 INFO + 5 ERROR lines in the
noise-filtered view). Those lines carry every count below.

### Resolved parser / spec / input mode

```
12:42:32.064  INFO GlueLogger: Running cybi-parser: Tenable.io Assets and Findings with key of: tenable-assets-findings
12:42:35.197  INFO GlueLogger: Tenable IO input resolver: discovered inventory={... 'has_assets': False,
                               'has_split_findings': True, 'has_hydrated': True}
12:42:36.010  INFO GlueLogger: Tenable IO input resolver: selected hydrated contract with
                               findings=s3://cybi-data/stg/raw-data/613f3842d8ddf900117fccfa/
                               0c90af0f-e52e-4808-a728-a62f9bce94ec/21f14fe6-ca31-42a7-87aa-c343bbbb777d/findings*.json
12:42:36.011  INFO GlueLogger: Tenable IO input resolver: parser config mode=hydrated assets_strategy=None
12:47:55.329  INFO GlueLogger: Tenable IO parser façade: resolved mode=hydrated (correlated envelope)
12:47:56.166  INFO GlueLogger: Tenable IO correlated parser: derived assets and findings lanes from the envelope frame
```

So: `parser_key=tenable-assets-findings`, **mode = hydrated (correlated envelope)**, assets lane
derived from the findings envelope (`has_assets: False` — there is no `assets*.json`).

Phase timeline (UTC):

| time | event |
|---|---|
| 12:42:31.7 | Elasticsearch + Postgres connections opened (tenant `default`, db `cymulate`) |
| 12:42:36.0 | pre-processing starts |
| 12:47:55.3 | façade resolves hydrated/correlated mode (≈5 min 20 s of S3 read/inference first) |
| 12:50:28.2 | processing done; asset + finding schemas logged |
| 12:55:07.9 | `delete_by_scope` on `parser_output_assets` done |
| 12:56:33.6 | `delete_by_scope` on `parser_output_exposures` done |
| 12:56:37.8 | assets write starts |
| 12:56:46.8 | **assets write SUCCEEDS** |
| 12:57:04.2 | exposures write starts (attempt 1) |
| 12:59:20.9 | exposures attempt 1/3 fails |
| 13:01:18.3 | exposures attempt 2/3 fails |
| 13:03:26.8 | exposures attempt 3/3 fails → job fails |

### Row counts logged

Verbatim, with UTC timestamps:

```
12:55:07.825  INFO GlueLogger: [delete_by_scope] integration.parser_output_assets batch 1: deleted 6485 (total 6485, client_id=613f3842d8ddf900117fccfa)
12:55:07.884  INFO GlueLogger: [delete_by_scope] integration.parser_output_assets DONE: deleted 6485 row(s) in 1 batch(es) (client_id=613f3842d8ddf900117fccfa, instance_id=21f14fe6-ca31-42a7-87aa-c343bbbb777d)
12:55:36.469  INFO GlueLogger: [delete_by_scope] integration.parser_output_exposures batch 1: deleted 10000 (total 10000, ...)
     ... batches 2-6, 10000 each ...
12:56:33.577  INFO GlueLogger: [delete_by_scope] integration.parser_output_exposures batch 7: deleted 7553 (total 67553, ...)
12:56:33.596  INFO GlueLogger: [delete_by_scope] integration.parser_output_exposures DONE: deleted 67553 row(s) in 7 batch(es) (client_id=613f3842d8ddf900117fccfa, instance_id=21f14fe6-ca31-42a7-87aa-c343bbbb777d)

12:56:37.608  INFO GlueLogger: DataFrame row count: 17984, target partitions: 4
12:56:37.748  INFO GlueLogger: Coalescing from 36 to 4 partitions
12:56:37.760  INFO GlueLogger: write_to_postgres config: partitions=4, batchsize=50000, table=parser_output_assets
12:56:46.834  INFO GlueLogger: DataFrame successfully written to parser_output_assets PostgreSQL

12:57:04.041  INFO GlueLogger: DataFrame row count: 5858751, target partitions: 100
12:57:04.143  INFO GlueLogger: Coalescing from 4224 to 100 partitions
12:57:04.164  INFO GlueLogger: write_to_postgres config: partitions=100, batchsize=50000, table=parser_output_exposures
```

**The headline pair of numbers:**

- **assets: 17,984 rows** → 4 partitions → written in 7.5 s, no error.
- **exposures: 5,858,751 rows** → 100 partitions → 100 % task failure.
- The scope this run replaced held **67,553 exposures and 6,485 assets**. The new payload is
  **86.7× the exposure rows** that were previously there, and **2.8× the assets**.

Two derived numbers that matter for the write shape:

- rows per write partition = 5,858,751 / 100 = **58,588**, i.e. **above** `batchsize=50000`, so each
  exposures task issues **2** `executeBatch` calls (one of 50,000 rows, one of ~8,588).
- rows per assets partition = 17,984 / 4 = **4,496**, i.e. **one** batch of 4,496 rows per task.

That is an in-run controlled comparison: same JDBC options, same driver, same target cluster, same
minute — the write that used ~4.5 k-row batches succeeded, the write that used 50 k-row batches
failed on every single task. **Batch size, not total volume alone, is the variable that changed.**

### Write stage: task count = concurrent JDBC connections

The three exposures attempts are three separate Spark jobs, each with its own `ResultStage`:

```
12:56:39.308  INFO DAGScheduler: Got job 32 (jdbc at NativeMethodAccessorImpl.java:0) with 4 output partitions
12:56:39.520  INFO TaskSchedulerImpl: Adding task set 61.0 with 4 tasks resource profile 0
12:56:46.773  INFO DAGScheduler: ResultStage 61 (jdbc ...) finished in 7.463 s          <- ASSETS, OK

12:57:04.641  INFO DAGScheduler: Got job 37 (jdbc ...) with 100 output partitions
12:57:04.764  INFO TaskSchedulerImpl: Adding task set 68.0 with 100 tasks resource profile 0
12:59:20.626  INFO DAGScheduler: ResultStage 68 (jdbc ...) failed in 135.958 s

12:59:31.896  INFO DAGScheduler: Got job 38 (jdbc ...) with 100 output partitions
12:59:32.024  INFO TaskSchedulerImpl: Adding task set 69.0 with 100 tasks resource profile 0
13:01:18.073  INFO DAGScheduler: ResultStage 69 (jdbc ...) failed in 106.154 s

13:01:40.739  INFO DAGScheduler: Got job 39 (jdbc ...) with 100 output partitions
13:01:40.894  INFO TaskSchedulerImpl: Adding task set 70.0 with 100 tasks resource profile 0
13:03:26.590  INFO DAGScheduler: ResultStage 70 (jdbc ...) failed in 105.828 s
```

**100 write tasks were *submitted*, but only ~36 could run at once — so the concurrent JDBC
connection count from the write is ~36, not 100.** Evidence, three independent ways:

1. Cluster shape: `Registered executor` lines in the driver log → **9 executors** (IDs 1–9;
   the 10th G.1X worker is the driver, `spark.driver.host=10.103.41.13`).
2. Config dumped in the `SparkListenerJobStart` event: **`spark.executor.cores=4`**,
   `spark.executor.memory=10g`, `spark.default.parallelism=36`, `spark.sql.shuffle.partitions=36`,
   `spark.dynamicAllocation.enabled=false`. 9 × 4 = **36 task slots**.
3. Measured directly by replaying `Starting task` / `Lost task` / `Finished task` for each write
   stage and tracking the running count:

   | stage | max concurrent write tasks | at (UTC) |
   |---|---|---|
   | 68.0 | **37** | 12:57:31.489 |
   | 69.0 | **37** | 12:59:58.621 |
   | 70.0 | **37** | 13:02:05.335 |

   (37 rather than 36 because one slot's start is logged before the previous occupant's loss is
   accounted; the steady-state ceiling is 36.)

Confirmed on the executor side: one executor log shows exactly four stage-68 tasks launched in the
same millisecond — `task 4.0`, `25.0`, `28.0`, `33.0` all "Running ... in stage 68.0" at
12:57:04.825–.827.

So the write presented **~36 simultaneous sessions**, each of which the RDS Proxy pinned to a
dedicated Aurora backend (see §B2/§A2).

### Retries / failed tasks / re-sent rows

There are **two nested retry layers**, and both re-send rows.

Layer 1 — application: `dal/sparkDAL.py` `write_to_postgres` wraps `df.write.jdbc` in
`for attempt in range(1, MAX_WRITE_RETRIES + 1)` with `MAX_WRITE_RETRIES = 3`,
`RETRY_BASE_DELAY_SECONDS = 10` (delay = 10 s × attempt). Observed:

```
12:59:20.852  ERROR GlueLogger: Write to parser_output_exposures failed on attempt 1/3: An error occurred while calling o1835.jdbc.
13:01:18.320  ERROR GlueLogger: Write to parser_output_exposures failed on attempt 2/3: An error occurred while calling o1840.jdbc.
13:03:26.848  ERROR GlueLogger: Write to parser_output_exposures failed after 3 attempts: An error occurred while calling o1845.jdbc.
```

Layer 2 — Spark task retry, 4 attempts per task (`Task N in stage X failed 4 times; aborting job`).

Counted from the driver log (`Starting task A.B in stage S.0`, grouped by attempt index B):

| stage | attempt 0 | attempt 1 | attempt 2 | attempt 3 | total task starts |
|---|---|---|---|---|---|
| 61.0 (assets) | 4 | – | – | – | **4** |
| 68.0 | 45 | 43 | 34 | 8 | **130** |
| 69.0 | 45 | 45 | 44 | 21 | **155** |
| 70.0 | 45 | 45 | 42 | 19 | **151** |

**Zero successful tasks in any exposures stage.** `grep -c "Finished task .* in stage S.0"`:
stage 61 → **4**; stages 68, 69, 70 → **0, 0, 0**. Every exposures task attempt ended in
`Lost task` (132 / 157 / 130) or `TaskKilled (Stage cancelled)` (36 / 36 / 11).

Cross-validated against CloudWatch `Glue` namespace, dimensions
`JobName=stg-cybi-parser, JobRunId=jr_36b7a68d...befb2, Type=count`, 60 s period (times **IDT**):

| metric | 15:58 | 15:59 | 16:00 | 16:01 | 16:02 | 16:03 | Sum |
|---|---|---|---|---|---|---|---|
| `glue.driver.aggregate.numFailedTasks` (Sum) | 48 | 46 | 66 | 53 | 37 | 78 | **328** |
| `glue.driver.aggregate.numKilledTasks` (Sum) | 0 | 22 | 14 | 25 | 11 | 11 | **83** |

The killed count **83 matches the log-derived 36+36+11 = 83 exactly**, which validates the method.
`numFailedTasks` 328 vs my log-derived 419 `Lost task` minus 83 killed = 336 — agreement within 2 %.
`numCompletedTasks` is 0 in every bucket from 15:58 IDT onward, and the last non-zero bucket is
15:57 IDT (8,528), i.e. the read/shuffle work, not the exposures write.

`Type=gauge` returns nothing for the `aggregate.*` metrics — those are emitted with `Type=count`.
`shuffleBytesWritten` reported 0.0 and `recordsRead` 8,051; both are unreliable here and I do not
rely on them.

**Rows re-sent — the number that matters for DB load:** 331 distinct failed write-task attempts each
began streaming its partition to Postgres. At 58,588 rows per partition, and given the measured
task-lifetime floor of 22.6 s (below), the work sent to Aurora was of order **3× the whole 5.86 M-row
payload** in the 6.5 minutes between 12:57:04 and 13:03:27 — plus whatever fraction of each aborted
partition had already been transmitted. Because `write_mode` is **`"append"`** (`jobs/cybi-parser/
script.py:71`), nothing between the three attempts truncates or de-duplicates: any partition that
*did* commit on attempt 1 would be re-inserted whole by attempts 2 and 3. That is a live duplication
hazard, not a hypothetical one.

**A timing fact that reframes the diagnosis.** Lifetime of every failed write task, computed as
`Lost task` timestamp minus its `Starting task` timestamp (TaskKilled excluded), n = 331:

| stage | n | min | median | max |
|---|---|---|---|---|
| 68.0 | 95 | 22.6 s | 38.1 s | 112.9 s |
| 69.0 | 120 | 22.7 s | 26.3 s | 64.8 s |
| 70.0 | 116 | 22.8 s | 27.5 s | 46.7 s |
| **all** | **331** | **22.6 s** | **27.1 s** | **112.9 s** |

Histogram in 10 s buckets: `{20s: 269, 30s: 12, 40s: 22, 50s: 9, 60s: 7, 70s: 2, 80s: 9, 110s: 1}`.

**No task ever failed faster than 22.6 s, and 81 % failed between 22.6 and 30 s.** A hard floor with
a tight cluster just above it is the signature of a fixed cost being reached, not of progressive
resource exhaustion. Combined with the first failure landing at 12:57:31 — 27 s into the very first
attempt, when (see §B3) the DB still had 11.7 GB free memory and 54 % CPU — **the initial trigger is
not DB saturation.** The DB saturation is what the retry storm then produced.

The floor is *not* the JDBC socket timeout: the URL sets `socketTimeout=3600` (§B, from source).
The most economical reading consistent with the evidence is that ~22–23 s is how long a write task
needs to pull ~42 cached RDD partitions and marshal its first 50,000-row batch, so **every task is
dying on its first `executeBatch`** — the first time the oversized statement hits the wire. I label
that last step **speculation**: I have no log line that times the first `executeBatch`.

### The failure traceback (verbatim, with UTC time)

Emitted `2026-08-19 13:03:28.477 UTC`, `ERROR ProcessLauncher: Error from Python`. Reproduced
verbatim; the Java frames are trimmed only where marked, and **never inside the causal chain**.

```
Traceback (most recent call last):
  File "/tmp/script.py", line 463, in <module>
    parser.execute()
  File "/tmp/parsers.whl/job/jobBase.py", line 251, in execute
    self.run()
  File "/tmp/script.py", line 397, in run
    self.spark_dal.write_to_postgres(
  File "/tmp/parsers.whl/dal/sparkDAL.py", line 247, in write_to_postgres
    raise last_exception
  File "/tmp/parsers.whl/dal/sparkDAL.py", line 225, in write_to_postgres
    prepared_df.write.jdbc(
  File "/opt/amazon/spark/python/lib/pyspark.zip/pyspark/sql/readwriter.py", line 1340, in jdbc
    self.mode(mode)._jwrite.jdbc(url, table, jprop)
  File "/opt/amazon/spark/python/lib/py4j-0.10.9.5-src.zip/py4j/java_gateway.py", line 1321, in __call__
    return_value = get_return_value(
  File "/opt/amazon/spark/python/lib/pyspark.zip/pyspark/sql/utils.py", line 190, in deco
    return f(*a, **kw)
  File "/opt/amazon/spark/python/lib/py4j-0.10.9.5-src.zip/py4j/protocol.py", line 326, in get_return_value
    raise Py4JJavaError(
py4j.protocol.Py4JJavaError: An error occurred while calling o1845.jdbc.
: org.apache.spark.SparkException: Job aborted due to stage failure: Task 20 in stage 70.0 failed 4 times, most recent failure: Lost task 20.3 in stage 70.0 (TID 17935) (10.103.33.161 executor 3): org.postgresql.util.PSQLException: This connection has been closed.
	at org.postgresql.jdbc.PgConnection.checkClosed(PgConnection.java:883)
	at org.postgresql.jdbc.PgConnection.rollback(PgConnection.java:890)
	at org.apache.spark.sql.execution.datasources.jdbc.JdbcUtils$.savePartition(JdbcUtils.scala:748)
	at org.apache.spark.sql.execution.datasources.jdbc.JdbcUtils$.$anonfun$saveTable$1(JdbcUtils.scala:868)
	at org.apache.spark.sql.execution.datasources.jdbc.JdbcUtils$.$anonfun$saveTable$1$adapted(JdbcUtils.scala:867)
	at org.apache.spark.rdd.RDD.$anonfun$foreachPartition$2(RDD.scala:1011)
	at org.apache.spark.rdd.RDD.$anonfun$foreachPartition$2$adapted(RDD.scala:1011)
	at org.apache.spark.SparkContext.$anonfun$runJob$5(SparkContext.scala:2269)
	at org.apache.spark.scheduler.ResultTask.runTask(ResultTask.scala:90)
	at org.apache.spark.scheduler.Task.run(Task.scala:138)
	at org.apache.spark.executor.Executor$TaskRunner.$anonfun$run$3(Executor.scala:548)
	at org.apache.spark.util.Utils$.tryWithSafeFinally(Utils.scala:1516)
	at org.apache.spark.executor.Executor$TaskRunner.run(Executor.scala:551)
	at java.util.concurrent.ThreadPoolExecutor.runWorker(ThreadPoolExecutor.java:1149)
	at java.util.concurrent.ThreadPoolExecutor$Worker.run(ThreadPoolExecutor.java:624)
	at java.lang.Thread.run(Thread.java:750)

Driver stacktrace:
	at org.apache.spark.scheduler.DAGScheduler.failJobAndIndependentStages(DAGScheduler.scala:2863)
	... [Spark scheduler frames elided] ...
	at org.apache.spark.sql.execution.datasources.jdbc.JdbcUtils$.saveTable(JdbcUtils.scala:867)
	at org.apache.spark.sql.execution.datasources.jdbc.JdbcRelationProvider.createRelation(JdbcRelationProvider.scala:70)
	at org.apache.spark.sql.execution.datasources.SaveIntoDataSourceCommand.run(SaveIntoDataSourceCommand.scala:45)
	... [Catalyst / QueryExecution frames elided] ...
	at org.apache.spark.sql.DataFrameWriter.jdbc(DataFrameWriter.scala:757)
	at sun.reflect.NativeMethodAccessorImpl.invoke0(Native Method)
	... [py4j frames elided] ...
	at java.lang.Thread.run(Thread.java:750)
Caused by: org.postgresql.util.PSQLException: This connection has been closed.
	at org.postgresql.jdbc.PgConnection.checkClosed(PgConnection.java:883)
	at org.postgresql.jdbc.PgConnection.rollback(PgConnection.java:890)
	at org.apache.spark.sql.execution.datasources.jdbc.JdbcUtils$.savePartition(JdbcUtils.scala:748)
	... [same 12 frames] ...
	... 1 more
```

**Which write failed, and when:** `parser_output_exposures`, final abort at
**13:03:26.588 UTC** (`ERROR TaskSetManager: Task 20 in stage 70.0 failed 4 times; aborting job`),
Python traceback at 13:03:27.214, Glue post-processing at 13:03:28.6, `SparkContext` stopped
13:03:29.229. The **assets** write succeeded at 12:56:46.834 and is not implicated.

**The reported error is a masked secondary exception — the real cause is destroyed.** The innermost
frame is `PgConnection.rollback` called from `JdbcUtils.savePartition:748`. In Spark 3.3.0 that line
is inside `savePartition`'s `finally` block, whose own source comment reads *"The stage must fail.
We got here through an exception path, so let the exception through unless rollback() or close()
want to tell the user about another problem."* The connection was **already** closed when the
`finally` ran, so `rollback()` threw and replaced the original exception. Two consequences:

1. `Error Category: UNCLASSIFIED_ERROR; ... This connection has been closed.` is **not** the failure
   cause — it is the failure *cleanup* failing. Anyone tuning against that string is tuning against
   an artefact.
2. In the same `finally`, `conn.close()` is on the line *after* `conn.rollback()`, so when
   `rollback()` throws, **`conn.close()` never executes** and the connection is leaked on the
   executor JVM. This is consistent with the connection-count and post-abort-load behaviour in
   §B3/§B2.

I recovered the masked primary cause by a different route — sibling runs of the same job where
`rollback()` happened to succeed. See §C.

---

## A2. Executor log findings

Executor streams live in `/aws-glue/jobs/error` as `<jr_id>_g-<hash>` (10 streams: 1 driver-side +
9 executors, matching the 9 registered executors).

Insights query over all of them for `PSQLException|connection has been closed|too many clients|
terminating connection|out of memory|canceling statement|deadlock|SocketTimeout|EOFException|
Broken pipe|Connection reset|IOException|SSLException|FATAL|server closed the connection|
OutOfMemory|Container killed|spilling` → 677 matches. Classified:

| pattern | count |
|---|---|
| `This connection has been closed` | 326 |
| `org.postgresql.util.PSQLException` | 323 |
| `javax.net.ssl.trustStore*` | 10 (JVM command line, not an error) |
| `OutOfMemoryError` | 10 (**JVM flag only** — see below) |

**Everything the executors logged is the same masked secondary.** There is not one occurrence, in any
executor stream, of `too many clients`, `terminating connection`, `canceling statement`, `deadlock`,
`Broken pipe`, `Connection reset`, `server closed the connection`, `SocketTimeout`, or
`Container killed`.

**The 10 "OutOfMemoryError" hits are not OOM events.** They are the Glue-injected JVM argument
`-XX:OnOutOfMemoryError=/opt/exception_catch/onOOMError.sh`, present once in each executor's
process-launch command line (`grep -o 'OnOutOfMemoryError=[^ ]*' → /opt/exception_catch/onOOMError.sh`).
A second Insights pass for all `ERROR|WARN` lines *excluding* `This connection has been closed`
returned only 130 rows, all scheduler bookkeeping: 83 `WARN scheduler.TaskSetManager`,
3 `ERROR scheduler.TaskSetManager`, 1 `WARN utils.OOMExceptionHandler`, 1 `WARN util.package`,
1 `WARN sparkmeasure.FlightRecorderStageMetrics`, 1 `ERROR log.GlueLogger`. **No executor OOM,
no container kill, no shuffle-fetch failure, no disk spill error.**

Earliest executor-side failure, verbatim:

```
2026-08-19 12:57:31,333 ERROR [Executor task launch worker for task 25.0 in stage 68.0 (TID 17549)]
executor.Executor (Logging.scala:logError(98)): Exception in task 25.0 in stage 68.0 (TID 17549)
org.postgresql.util.PSQLException: This connection has been closed.
```

Task 25.0 started at 12:57:04.827 → **26.5 s to failure**, matching the 22.6 s floor.

**Direction-of-close evidence.** The executor threw at **12:57:31.333**. The RDS Proxy's first
`The client connection closed. Reason: The TCP channel was closed by either the client or the proxy.`
on the write endpoint is at **12:57:38.435** — 7 s *later*. On the proxy's own timeline the earliest
close of any kind after the write began is 12:57:13 and those are `Reason: The client requested that
the connection close` from unrelated app clients. So **the client-side JDBC connection was already
closed before the proxy recorded closing anything**, i.e. the close was observed at the executor
first. That is what makes the masked primary exception load-bearing: I cannot tell from this run
alone whether the server/proxy sent a reset that pgjdbc converted into a local close, or something
on the executor closed it. §C resolves this from sibling runs.

---

## A3. Glue CloudWatch metrics

Namespace `Glue`, dimensions `JobName=stg-cybi-parser, JobRunId=jr_36b7a68d...befb2`, period 60 s.
**Times in this section are IDT (+03:00) as returned by the API.**

Task counters (`Type=count`) are in §A. Executor heap (`Type=gauge`):

| IDT | `glue.ALL.jvm.heap.usage` Max | Avg | `glue.driver.jvm.heap.usage` Max |
|---|---|---|---|
| 15:47 | 0.211 | 0.077 | 0.122 |
| 15:52 | 0.459 | 0.235 | 0.156 |
| 15:56 | 0.395 | 0.230 | 0.137 |
| **15:57** | **0.872** | 0.702 | 0.123 |
| **15:58** | **0.950** | **0.802** | 0.124 |
| 15:59 | 0.835 | 0.696 | 0.117 |
| 16:00 | 0.845 | 0.733 | 0.155 |
| 16:01 | 0.881 | 0.673 | 0.122 |
| 16:02 | 0.713 | 0.622 | 0.141 |
| 16:03 | 0.757 | 0.668 | 0.156 |

**Executor heap jumps from ~0.39 to 0.87 the moment the exposures write starts (15:57 IDT =
12:57 UTC) and peaks at 0.95.** Driver heap stays flat at ~0.12–0.16, so this is entirely the
executors. Mechanically that is expected: with `reWriteBatchedInserts=true` pgjdbc must hold an
entire 50,000-row batch plus the rewritten multi-row statement in heap, and there are 4 such tasks
per 10 GB executor. 95 % heap did not produce an OOM (§A2) but it is the same pressure on the
client side that the DB was seeing on its side.

`glue.driver.ExecutorAllocationManager.executors.numberAllExecutors` returned no datapoints for
this run; `aws cloudwatch list-metrics --namespace Glue --dimensions Name=JobRunId,Value=<jr>` shows
only per-executor `glue.<N>.jvm.heap.*`, `glue.<N>.s3.filesystem.*`, `glue.<N>.system.cpuSystemLoad`
plus the `aggregate.*`/`ALL` families — the executor-allocation gauge is not emitted (consistent with
`spark.dynamicAllocation.enabled=false`). Executor count is established from the driver log instead
(9 × `Registered executor`).

---

## B. The Postgres instance (class, vCPUs, max_connections, key params)

`aws rds describe-db-instances` + `aws rds describe-db-clusters` (us-east-1). The STG cybi target is
**not a plain RDS instance — it is an Aurora PostgreSQL cluster**:

| property | value |
|---|---|
| cluster | **`stg-pg-cybi-aurora-cluster`** |
| engine | **aurora-postgresql 16.11** |
| writer endpoint | `stg-pg-cybi-aurora-cluster.cluster-cwjkumtjdvpj.us-east-1.rds.amazonaws.com` |
| reader endpoint | `stg-pg-cybi-aurora-cluster.cluster-ro-cwjkumtjdvpj.us-east-1.rds.amazonaws.com` |
| **writer instance** | **`stg-pg-cybi-aurora-cluster-instance-1`** (`IsClusterWriter: true`), `DbiResourceId` `db-3VVWVZHWZVBQD4LXTN4AOBPJKI` |
| reader instance | `stg-pg-cybi-aurora-cluster-instance-0` (`IsClusterWriter: false`), `db-36PI2FQXDGY5U7PTM7PHTHMFWY` |
| instance class | **db.r7g.2xlarge** (both) |
| **vCPUs** | **8** — measured, not assumed: PI `os.general.numVCPUs.avg` = **8.0** at every 60 s point across 12:40–13:15Z |
| storage | `aurora-iopt1` (I/O-Optimized) |
| Performance Insights | **enabled**, retention 7 days |
| cluster parameter group | `aurora-pg16-cluster` (**custom**) |
| instance parameter group | `default.aurora-postgresql16` |
| Multi-AZ flag | false per instance; HA is the 2-instance cluster |
| Backtrack | not enabled |

Also present and relevant: **two RDS Proxies** front this account's STG clusters, and the Glue job
goes through one of them (§B2). Their CloudWatch log groups are `/aws/rds/proxy/
stg-cybi-aurora-cluster-proxy` (1.88 GB retained) and `/aws/rds/proxy/stg-cybi-cluster-proxy`.

**JDBC options — read from source, since they are not logged.** The driver log never prints the URL
(`grep -oE 'jdbc:postgresql://[^ ",\\]*'` over all JDBC-matching driver lines → no match; the only
option echoed is `batchsize=50000`). From `libs/packages/dal/sparkDAL.py` `write_to_postgres`
(Evidence tier 1, repo source at the branch that produced this wheel):

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

and the constants that produced the partition counts:

```python
ROWS_PER_PARTITION = 50000
MIN_PARTITIONS = 4
MAX_PARTITIONS = 100
MAX_WRITE_RETRIES = 3
RETRY_BASE_DELAY_SECONDS = 10
...
target = max(MIN_PARTITIONS, min(row_count // ROWS_PER_PARTITION, MAX_PARTITIONS))
```

with `batchsize: "50000"`, `write_mode: "append"` from `jobs/cybi-parser/script.py:69-71`.

Three things fall out of that:

- **`reWriteBatchedInserts=true` is the reason the top statement is multi-row** (§B2). This is the
  single most consequential option in the config.
- **`socketTimeout=3600`** (1 hour) — so the 22.6 s failure floor is *not* a JDBC read timeout,
  and no client-side statement timeout exists.
- **`MAX_PARTITIONS = 100` breaks the design invariant above 5 M rows.** `ROWS_PER_PARTITION` and
  `batchsize` are both 50,000, so the intent is clearly one batch per write task. The cap holds
  that only while `row_count ≤ 5,000,000`. At 5,858,751 rows the partition size is forced to
  58,588 — past the cap, extra volume lands as *more rows per connection*, never as more
  connections. Concurrency is capped at 36 anyway (§A), so beyond 5 M rows this job can only get
  slower and heavier per session, never wider.

**Parameters I could not read.** `rds:DescribeDBClusterParameters` and `rds:DescribeDBParameters` are
both **AccessDenied** for this role:

```
User: arn:aws:sts::118330362824:assumed-role/AAD-Platform/urib@cymulate.com is not authorized to
perform: rds:DescribeDBClusterParameters on resource: .../cluster-pg:aurora-pg16-cluster
```

So I have **no measured value** for `max_connections`, `shared_buffers`, `work_mem`,
`maintenance_work_mem`, `synchronous_commit`, `wal_*`, `checkpoint_*`, `autovacuum*`,
`statement_timeout`, `idle_in_transaction_session_timeout`, or `tcp_keepalives_*`. I am not going to
guess a custom group's contents. For scale only, and clearly labelled: Aurora PostgreSQL's
*documented default* `max_connections` formula is `LEAST({DBInstanceClassMemory/9531392}, 5000)`,
which on a 64 GiB r7g.2xlarge evaluates to the 5000 ceiling — but `aurora-pg16-cluster` is a custom
group and may override it. **Treat `max_connections` as unknown.** What I can say from measurement is
that peak `DatabaseConnections` was 570 (§B3), and no `too many clients` error appears anywhere in
Glue or proxy logs — so whatever the limit is, it was not reached.

---

## B2. Performance Insights — real numbers

All PI calls against the **writer**, `--identifier db-3VVWVZHWZVBQD4LXTN4AOBPJKI`.
Note `AlignedStartTime`/`AlignedEndTime` come back in +03:00; the `--start-time`/`--end-time` I
passed were UTC and are what I quote.

`db.application_name` is **not a valid group** for this engine
(`InvalidArgumentException: The specified group is not a known group`), so §B2's application
attribution is done via `db.user` + `db.sql_tokenized` + the proxy log's client IPs instead.

### The FULL top INSERT statement text ← single-row or multi-row VALUES? how many tuples?

**It is MULTI-ROW.** This is the decisive fact and it is directly measured, not inferred:

```
INSERT INTO integration.parser_output_exposures ("id","asset_id","client_id","client_integration_id",
"instance_id","integration_setting_id","integration_setting_flow_id","connector_flow_name",
"created_at","first_seen","last_seen","name","display_name","description","mitigation","type",
"severity","status","cve_id","additional_fields") VALUES
($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20),
($21,$22,$23,$24,$25,$26,$27,$28,$29,$30,$31,$32,$33,$34,$35,$36,$37,$38,$39,$40),
($41,...
```

**20 columns → 20 bind parameters per value tuple**, tuples chained with `),(`. This is exactly what
the pgjdbc `reWriteBatchedInserts=true` option produces: it rewrites a JDBC batch of single-row
inserts into one multi-row `INSERT`. Spark's own JDBC writer emits single-row `VALUES (?,...)`; the
multi-row form can only come from the driver-side rewrite. So §B's source reading and §B2's measured
statement text corroborate each other.

**Exact tuple count: I could not measure it.** PI truncates statement text:

- `db.sql_tokenized.statement` from `describe-dimension-keys` → truncated at **500 chars**
  (ends `...,($`).
- `get-dimension-key-details --group db.sql --group-identifier
  57718E45045EDE7A7D38EB1B5264D9A900BC7797 --requested-dimensions statement` → `Status: AVAILABLE`,
  length **4095 chars**, i.e. PI's hard cap. It ends mid-tuple at `$757,`. Parsed from that prefix:
  **38 complete value tuples visible, highest parameter `$757`**, and the statement demonstrably
  continues past the cap.
- `get-dimension-key-details` rejects the tokenized group outright:
  `The specified group isn't supported. GetDimensionKeyDetails for engine manfred supports only the
  db.execution_plan and db.lock_snapshot and db.sql dimension groups.`
- The per-SQL rate statistics that would give rows-per-call are unavailable on this engine: every
  spelling I tried (`db.sql_tokenized.stats.{sum_,}{calls_per_sec,rows_per_sec,avg_latency_per_call,
  total_time_per_sec}`) fails with `InvalidArgumentException: The specified statistic is not a known
  statistic`, in both `describe-dimension-keys --additional-metrics` and `get-resource-metrics`.

So: **≥38 tuples, measured; true count not obtainable via the PI API.** The only quantitative
handle on tuples-per-call is the **operator's screenshot** — 177.29 rows/sec ÷ 1.39 calls/sec =
**~127.5 rows per call**, suggesting ~128 value tuples per rewritten statement (a power of two,
consistent with pgjdbc bucketing the rewrite block count). I did not measure that and it is *not*
my number; flagged so the main thread does not promote it.

What that ratio *would* explain if correct: 1.39 calls/sec at 117.43 ms average latency carrying
58.59 AAS looks contradictory (1.39 × 0.117 ≈ 0.16 AAS) until you note there are **5 distinct
tokenized variants** (below) and ~36 concurrent sessions; the screenshot's row is one variant's slice
of the calls but PI's AAS for that token is the aggregate time. I flag the arithmetic tension rather
than resolve it, since I could not reproduce the calls/sec figure.

### AAS by tokenized statement (all inserts to parser_output_exposures, summed)

Window **12:56:00Z–13:10:00Z** (the write window), `--group-by '{"Group":"db.sql_tokenized"}'
--max-results 25`, `NextToken` absent so this is the complete top-25:

| AAS | tokenized id | statement |
|---|---|---|
| **124.970** | `004D79500B77DB35721698F2775A6902D906A48E` | `INSERT INTO integration.parser_output_exposures ...` |
| 0.477 | `0E727820E6A4DA70FD9DC712C6D529BE3898FAB8` | same INSERT, different tuple count |
| 0.320 | `5896A4284C0C6A6B673AB8066070845D15D614DE` | same INSERT, different tuple count |
| 0.296 | `DB1A6CE05884D4721874726EA9C751C030B668B8` | same INSERT, different tuple count |
| 0.285 | `DD810C57C4E82D77107FF3D19EFBDC0678E082FA` | same INSERT, different tuple count |
| | | **COUNT = 5 distinct variants, SUM = 126.348 AAS** |
| 7.61 | `4BE79A86...` | `DEALLOCATE ALL` |
| 5.21 | `08B719A0...` | `[EA Cron-Service \| DatabaseIntegrityRepository.upsertIntegrityRecord]` |
| 1.99 | `-6428386...` | `[EA Correlation-Shared \| CorrelationProgressRepository.saveCheckpoint]` |
| 1.89 | `70B8C53F...` | `SELECT vulnerability FROM cybi.client_risk_profile WHERE client_id = $1` |
| 0.91 | `52722D4B...` | `[EA Correlation-TP \| ExposureRiskRepository.detectChangedExposureIds]` |
| 0.87 | `2032866527...` | `[EA Cron-Service \| DatabaseIntegrityRepository.getDispatchCandidates]` |
| | | **sum of all 25 listed keys = 150.98 AAS** |

**The parser's exposures INSERT is 126.35 of 150.98 AAS — 84 % of all database load** in the write
window. Everything else, all EA services combined, is ~24 AAS.

Two observations on the shape:

- **Only 5 distinct variants**, not hundreds. pgjdbc buckets the rewrite block count, so batch-size
  jitter does not explode the statement-text space. The dominant variant carries 98.9 % of the
  INSERT AAS, so the batches were near-uniform.
- **`DEALLOCATE ALL` at 7.61 AAS is the 2nd-heaviest statement on the instance.** That is
  server-side prepared-statement churn: with `reWriteBatchedInserts` each block-count variant is a
  distinct statement, pgjdbc server-prepares past `prepareThreshold`, and the proxy's session reset
  issues `DEALLOCATE ALL`. 7.61 AAS spent purely on discarding plans is pure overhead.

For the wider window **12:40:00Z–13:15:00Z** (closest to the operator's screenshot): top INSERT
= **49.428 AAS**, `DEALLOCATE ALL` = **3.023**, `upsertIntegrityRecord` = 2.253,
`detectChangedExposureIds` = 1.006, `getDispatchCandidates` = 0.992.

Narrower still, **12:55:00Z–13:06:00Z**, `--group-by db.sql` (full statements): the INSERT
(`db.sql.id 57718E45045EDE7A7D38EB1B5264D9A900BC7797`, tokenized-id `004D7950...`) = **141.452 AAS**,
`DEALLOCATE ALL` = 8.395. So the peak-window intensity is ~141 AAS on 8 vCPUs.

### AAS by wait event / application_name / user

Window **12:56:00Z–13:10:00Z**:

| group | key | AAS |
|---|---|---|
| wait_event | **CPU** | **152.10** |
| wait_event | IO:DataFileRead | 2.19 |
| wait_event | IO:XactSync | 0.82 |
| wait_event | Lock:transactionid | 0.08 |
| wait_event | LWLock:BufferContent | 0.04 |
| wait_event | Timeout:VacuumDelay / Client:ClientRead / LWLock:BufferMapping / IPC:BgWorkerShutdown / Client:ClientWrite | 0.01 each |
| | **total** | **155.27** |
| user | **cybi** | **154.30** |
| user | integration_service_bas | 0.50 |
| user | rdsproxyadmin | 0.46 |
| user | Unknown | 0.01 |
| session_type (12:40–13:15Z) | client backend | 63.61 |
| session_type (12:40–13:15Z) | autovacuum worker | 0.05 |

**Almost no lock contention (0.08 AAS on `Lock:transactionid`), almost no I/O wait, zero deadlocks.**
The load is 98 % classified "CPU" and 99 % attributable to the **`cybi`** database user — which is
the role the Glue parser authenticates as. `db.application_name` is unavailable on this engine, so
user-level attribution cannot separate Glue from the EA services on its own; the tokenized-statement
breakdown does that instead, and it puts 84 % of load on the parser's INSERT.

**Important caveat on reading "CPU" here — this is the one place the numbers fight each other.**
PI reports 152 AAS in the CPU state, but the host was **never CPU-saturated**: PI
`os.cpuUtilization.total.avg` peaks at **71.1 %** (mean 55.5 %) and CloudWatch `CPUUtilization`
peaks at **68.7 %** (mean 55.0 %) across the same window. 152 active sessions on 8 vCPUs at 60 % CPU
means the sessions were overwhelmingly **runnable-but-not-progressing**, not burning cycles. PI
classifies "on CPU or waiting on something untracked" as CPU, so this is a bucket of sessions
Postgres could not attribute. Given §B3's memory collapse, the coherent reading is memory pressure
and allocator/context churn rather than compute — but I mark the mechanism **speculation**, because
neither PI nor CloudWatch exposes a wait event that names it, and I could not read the PG error log.

### The real load curve (60 s) and real peak

`aws pi get-resource-metrics --period-in-seconds 60`, 12:40:00Z–13:15:00Z. Times below are **IDT**
as returned; subtract 3 h for UTC. Job window = 15:41:25–16:03:44 IDT; exposures write =
15:57:04–16:03:27 IDT.

| IDT | UTC | db.load.avg | tup_inserted/s | tup_deleted/s | xact_commit/s | deadlocks | cpuUtil % |
|---|---|---|---|---|---|---|---|
| 15:41 | 12:41 | 6.3 | 37 | 34 | 486 | 0 | 49.3 |
| 15:45 | 12:45 | 2.8 | 38 | 33 | 529 | 0 | 49.3 |
| 15:50 | 12:50 | 6.4 | 39 | 33 | 467 | 0 | 46.8 |
| 15:55 | 12:55 | 2.6 | 28 | 23 | 469 | 0 | 41.0 |
| 15:56 | 12:56 | 4.0 | 19 | **124** | 467 | 0 | 50.2 |
| 15:57 | 12:57 | 3.4 | 7 | **2482** | 482 | 0 | 52.3 |
| **15:58** | 12:58 | **45.6** | 335 | 1746 | 540 | 0 | 51.6 |
| 15:59 | 12:59 | **96.6** | 15 | 11 | 459 | 0 | 62.7 |
| 16:00 | 13:00 | **148.3** | 694 | 2 | 413 | 0 | 58.5 |
| 16:01 | 13:01 | **220.9** | 1139 | 2 | 383 | 0 | 60.0 |
| 16:02 | 13:02 | **268.5** | 2436 | 14 | 332 | 0 | 71.1 |
| 16:03 | 13:03 | **296.1** | 2114 | 24 | 341 | 0 | 57.1 |
| **16:04** | **13:04** | **327.5** ← peak | 2085 | 12 | 310 | 0 | 65.0 |
| 16:05 | 13:05 | 281.8 | 2102 | 178 | 281 | 0 | 68.4 |
| 16:06 | 13:06 | 213.7 | 1917 | 2 | 328 | 0 | 68.5 |
| 16:07 | 13:07 | 61.6 | **5157** ← peak | 3 | 367 | 0 | 57.9 |
| 16:08 | 13:08 | 3.4 | 3577 | 1 | 451 | 0 | 58.5 |
| 16:09 | 13:09 | 4.1 | 1536 | 2 | 447 | 0 | 59.1 |
| 16:13 | 13:13 | 3.5 | 8 | 2 | 487 | 0 | 53.6 |

Summary over 12:40–13:15Z: `db.load.avg` **peak 327.46, mean 58.62**; `tup_inserted` peak 5156.78,
mean 862.60; `xact_commit` peak 539.78, mean 442.39; `xact_rollback` peak 2.92, mean 1.29;
**`deadlocks` 0.00 at every point**; `numVCPUs` 8.0 flat; `cpuUtilization` peak 71.10, mean 55.53.

**Three findings from this curve that change the story:**

1. **Baseline load is ~3 AAS and the run drove it to 327 — a ~110× amplification on an 8-vCPU
   writer.** Sustained AAS/vCPU ratio at peak is **41:1**.
2. **The peak (327.5 AAS at 13:04) is AFTER the Glue job aborted at 13:03:44**, and load stays
   above 200 AAS through 13:06. Confirmed by attribution: PI over **13:04:00Z–13:09:00Z** still puts
   **109.54 AAS on the parser's exposures INSERT** and 7.00 on `DEALLOCATE ALL`, with 132.07 AAS in
   the CPU wait state. **The abandoned Aurora backends kept executing the INSERTs their orphaned
   connections had already sent for ~4 minutes after Glue reported failure.** `tup_inserted` in fact
   *peaks* at 13:07 (5,157 rows/s) — after the job was dead. This is consistent with §A's finding
   that `conn.close()` was skipped because `conn.rollback()` threw.
3. **The deletes are visible and cheap**: `tup_deleted` 2,482/s at 12:57 is the tail of the
   67,553-row `delete_by_scope` (logged complete at 12:56:33.6). They cost essentially no AAS.

`db.SQL.tup_inserted.avg` returns null at 13:10, 13:12, 13:14 (PI gap), which is why those rows are
absent above; `db.load.avg` is continuous.

---

## B3. RDS CloudWatch metrics (peak + mean)

Namespace `AWS/RDS`, `DBInstanceIdentifier=stg-pg-cybi-aurora-cluster-instance-1` (the writer),
period 60 s, 12:40:00Z–13:15:00Z. Statistic column notes which. **Times IDT.**

| metric | peak | mean | when peak (IDT / UTC) |
|---|---|---|---|
| `CPUUtilization` (%) | **68.71** | 55.01 | 16:01 / 13:01 |
| `DatabaseConnections` | **570** | 443.6 | 16:03 / 13:03 |
| `WriteIOPS` | 33,804.7 | 8,625.3 | 15:46 / 12:46 |
| `WriteThroughput` (B/s) | 12,380,024 | 3,339,086 | 15:46 / 12:46 |
| `CommitLatency` (ms) | **4.85** | 1.31 | 16:04 / 13:04 |
| `DiskQueueDepth` | 39 | 3.6 | 15:50 / 12:50 |
| **`FreeableMemory`** (bytes) | 12,553,924,608 | 9,858,498,560 | **min 278,224,896 at 16:03 / 13:03** |
| `WriteLatency` (ms) | 0.001 | ~0 | — |
| `BufferCacheHitRatio` (%) | 99.91 | 99.76 | — |
| `DeadlockCount` | **no datapoints** | — | — |

**The single most striking number in the whole investigation is FreeableMemory.** Trajectory:

| IDT | UTC | FreeableMemory |
|---|---|---|
| 15:57 | 12:57 | 11.73 GB |
| 15:58 | 12:58 | 8.76 GB |
| 15:59 | 12:59 | 6.63 GB |
| 16:00 | 13:00 | 4.20 GB |
| 16:01 | 13:01 | 1.94 GB |
| 16:02 | 13:02 | 1.70 GB |
| **16:03** | **13:03** | **0.278 GB** |
| 16:04 | 13:04 | 0.82 GB |
| 16:05 | 13:05 | 2.55 GB |
| 16:06 | 13:06 | 7.18 GB |
| 16:07 | 13:07 | 12.36 GB |

**A 64 GiB writer went from 11.7 GB freeable to 278 MB in six minutes and recovered the instant the
orphaned sessions drained.** The recovery at 13:07 coincides exactly with load collapsing from
213 → 61 AAS and with the `tup_inserted` peak — i.e. the backends finished and released.

Reading the rest against that:

- **`DatabaseConnections` 425 → 570 (+145).** The exposures write only ever ran ~36 concurrent
  tasks (§A). +145 connections for 36 concurrent writers is ~4× over-count, and it tracks the
  leaked-connection mechanism in §A: `conn.close()` skipped, so dead connections linger. Baseline
  425 is the steady EA-service population — it is flat at 423–428 for the 17 minutes before the
  write and returns to 418–426 immediately after.
- **CPU is a non-story.** 68.7 % peak on 8 vCPUs while PI reports 152–327 AAS. The writer was not
  compute-bound at any point.
- **Storage is a non-story.** `WriteLatency` ~0.001 ms, `BufferCacheHitRatio` 99.8 %,
  `IO:DataFileRead` only 2.19 AAS. The `WriteIOPS`/`WriteThroughput` peak at 12:46 predates the
  exposures write by 11 minutes and is not attributable to it.
- **`CommitLatency` rises 0.6 → 4.85 ms** during the write — a 8× degradation, but in absolute terms
  still small, and it is a *symptom* of the memory squeeze, not a cause.
- **Zero deadlocks**, from two independent sources: `AWS/RDS DeadlockCount` emits no datapoints at
  all, and PI `db.Concurrency.deadlocks.avg` = 0.00 at all 32 points. `Lock:transactionid` is
  0.08 AAS. **Lock contention played no part in this failure.**

**Ordering matters for causation, and it cuts against the obvious story.** The first executor
failure is 12:57:31, when FreeableMemory was still 11.73 GB and CPU 52 %. The memory collapse
begins at 12:58 and bottoms at 13:03. So **memory exhaustion is not the initial trigger — it is what
the retry storm produced.** The sequence is: oversized batches fail from the first attempt → Spark
retries 4× per task and the app retries 3× more → 331 failed attempts and 457 new connections pile
pinned, un-closed sessions onto the writer → memory drains to 278 MB → everything after 12:58 gets
worse for everyone. Two distinct phases, and conflating them would send a fix in the wrong direction.

---

## B4. What I could not read without a DB connection

Stated plainly, no guessing:

1. **The exposures table's row count.** Not obtainable. `delete_by_scope` tells me the *scope* held
   **67,553** rows before this run, but that is one client/instance, not the table. No CloudWatch or
   PI metric exposes table cardinality.
2. **The index list on `integration.parser_output_exposures`.** Not obtainable without connecting.
   I will not guess it. This matters: index maintenance cost per inserted row is a prime suspect for
   why 5.86 M inserts hurt so much, and it is exactly what I cannot see. **Recommend W1/main obtain
   `\d+ integration.parser_output_exposures` from a psql session** — it is the largest remaining gap.
3. **Whether any exposure rows committed, and how many.** Unresolved and genuinely ambiguous.
   Against: zero tasks reported success to the driver (§A), and Spark's `savePartition` commits only
   at end of partition, so an aborted partition should roll back. For: `tup_inserted` was 694–5,157
   rows/s from 13:00 to 13:09 and the parser INSERT held 109.54 AAS *after* the job aborted — the
   orphaned backends were demonstrably still inserting. Whether those transactions reached `COMMIT`
   requires querying the table. **With `write_mode="append"` and 3 un-guarded retries, this is a
   duplicate-row question, not just an accounting one.**
4. **All Postgres server parameters** — `rds:DescribeDBClusterParameters` /
   `rds:DescribeDBParameters` AccessDenied (§B). `max_connections`, `work_mem`, `shared_buffers`,
   `autovacuum*`, `statement_timeout`, `idle_in_transaction_session_timeout` all unknown.
5. **The Postgres server error log** — the definitive record of *why* backends died. Three routes,
   all closed: `rds:DescribeDBLogFiles` AccessDenied; no
   `/aws/rds/cluster/stg-pg-cybi-aurora-cluster/postgresql` log group exists
   (`aws logs describe-log-groups --log-group-name-prefix /aws/rds` lists a group for the *old*
   `stg-postrgres-cybi-cluster` but **not** for `stg-pg-cybi-aurora-cluster`), so PG logs are not
   exported to CloudWatch for this cluster. **Enabling the `postgresql` log export on this cluster is
   the highest-value observability change available** — it would have answered the whole question
   directly.
6. **RDS Proxy configuration** — `rds:DescribeDBProxies`, `DescribeDBProxyTargets`,
   `DescribeDBProxyTargetGroups` all AccessDenied. So `MaxConnectionsPercent`,
   `MaxIdleConnectionsPercent`, `ConnectionBorrowTimeout`, and `IdleClientTimeout` are **unknown**,
   despite the proxy being squarely in the failure path (§B2 note, §C). Its *logs* are readable and
   are what §C leans on.

No connection to Postgres was attempted at any point.

### The RDS Proxy is in the write path — measured, and it was not in the brief

This was not anticipated by the task and it changes the topology, so it is called out separately.

`/aws/rds/proxy/stg-cybi-aurora-cluster-proxy`, Insights over 12:53:00Z–13:06:00Z, 3,595 events
(3,035 INFO / 560 WARN). The client IPs connecting to the proxy's `default` (writer) endpoint are
**exactly the nine Glue executor IPs** from the driver log — 10.103.32.87, 10.103.55.36,
10.103.33.161, 10.103.55.82, 10.103.59.53, 10.103.45.149, 10.103.33.192, 10.103.42.64,
10.103.49.145 — plus the driver 10.103.41.13. The proxy's own target is 10.103.58.157:5432.
**The Glue parser writes through the RDS Proxy, not direct to the cluster endpoint.**

Normalised event histogram (IDs and IP:port collapsed):

| count | event |
|---|---|
| 661 | `[INFO] [default] A new client connected from IP:PORT.` |
| **536** | `[WARN] [default] The client session was pinned to the database connection [X] for the remainder of the session. The proxy can't reuse this connection until the session ends. Reason: Running a prepared statement...` |
| 442 | `[INFO] [default] If this prepared statement runs, the client session will be pinned... Reason: SQL changed session settings that the proxy doesn't track.` |
| 421 | `[INFO] The database connection closed. Reason: The client connection that borrowed this database connection from the connection pool was closed in the middle of a request.` |
| 420 | `[INFO] [default] The client connection closed. Reason: The TCP channel was closed by either the client or the proxy.` |
| 201 | `[INFO] [default] The client connection closed. Reason: The client requested that the connection close.` |
| 419 | `[INFO] A TCP connection was established from the proxy at IP:PORT to the database at IP:PORT.` |
| 134 | `[INFO] The database connection was released to the connection pool upon session reset.` |
| 170 / 167 / 24 | same three shapes on the `-read-only` endpoint (other clients) |

New client connections **from Glue executor IPs only**, per UTC minute:

| 12:53 | 12:56 | 12:57 | 12:58 | 12:59 | 13:00 | 13:01 | 13:02 | 13:03 | total |
|---|---|---|---|---|---|---|---|---|---|
| 3 | 6 | 60 | 59 | 74 | 83 | 54 | 79 | 39 | **457** |

**457 proxy connections established for a write that needed 36.** That is the retry storm measured
from the third side, and it agrees with the 331 failed + 83 killed task attempts from §A.

**The proxy provided no pooling benefit and actively amplified cost.** 536 pinning warnings mean
essentially every parser session was pinned to a dedicated backend for its whole life — pinning is
triggered by prepared statements and untracked session settings, and this workload is nothing but
server-prepared statements (`reWriteBatchedInserts` + `stringtype=unspecified`). So the proxy could
not multiplex, every connection cost a real Aurora backend, and each session reset cost a
`DEALLOCATE ALL` — which is why that statement is the instance's 2nd-heaviest at 7.61 AAS.

On direction of close, the proxy's messages are deliberately non-committal
(`closed by either the client or the proxy`) and the 421 `...closed in the middle of a request`
events say the *client* went away. Combined with the executor throwing 7 s before the proxy's first
write-endpoint close (§A2), this run cannot settle who closed first. §C settles it.

---

## C. Baseline: Tenable.io runs for this instance, and other-vendor runs

Method: `aws glue get-workflow-runs --name stg-epc` paginated (run properties carry
`FLOW_NAME`/`CLIENT_ID`/`INSTANCE_ID`), joined to the parser outcome via
`--include-graph` → node `stg-cybi-parser` → `JobDetails.JobRuns[]`. Two passes:

- **light pass** (no graph): 2,900 workflow runs back to 2026-07-30T11:10 IDT.
- **graph pass**: 1,500 workflow runs, 2026-08-18T14:57 → 2026-08-19T15:39 IDT (~25 h). Pagination
  with `--include-graph` is slow (25/page); I stopped at 25 h of coverage, which is sufficient
  because of the first finding below.

Separately, `aws glue get-job-runs --job-name stg-cybi-parser` paginated: **3,000 runs** back to
2026-07-29T17:25 IDT — `FAILED: 2709, SUCCEEDED: 290, STOPPED: 1`. **The stg parser job fails ~90 %
of the time as a baseline.** Whatever else is true, this run's failure is not anomalous for the job.

### Was there ever a SUCCEEDED run for this instance?

**No — and there was never any run at all before this one.** Of 2,900 stg-epc workflow runs since
2026-07-30, **2,146 are `Tenable.io - Tenable.io Assets and Findings`**, and the instance breakdown is:

| INSTANCE_ID | workflow runs |
|---|---|
| `251f9990-d0cb-4fa4-84e2-a2ad418c4ece` | **2,130** |
| **`21f14fe6-ca31-42a7-87aa-c343bbbb777d`** (target) | **1** |
| `72205515-…`, `b89a408d-…`, `2e78446c-…`, `61cea440-…`, `7076cb6b-…`, `eeb71134-…` | 1 each |

**The target instance has exactly one run in 20 days — the failing one.** There is no
SUCCEEDED baseline for it and nothing "changed": this is its first execution. Client
`613f3842d8ddf900117fccfa` is the same client behind the 2,130-run instance and has 2,389 workflow
runs total across Qualys, InsightVM Cloud, CrowdStrike Falcon, Defender VM and Tenable.io.

(Note: workflow `Status` for Tenable.io is `COMPLETED` 2,145 / `STOPPED` 1 — the *workflow* completes
even when the parser node fails, so workflow status is not a proxy for parser success. All outcome
figures below come from the parser job run inside the graph.)

### Tenable.io parser outcomes, last ~25 h (graph pass)

| flow | runs | parser outcome | max exec | median exec |
|---|---|---|---|---|
| **Tenable.io — Assets and Findings** | 1,355 | **FAILED 1,347 / SUCCEEDED 8** | 1,442 s | 65 s |
| Crowdstrike Falcon — Spotlight VM | 138 | FAILED 73 / SUCCEEDED 61 / none 4 | 1,403 s | 146 s |
| Qualys — Assets and Findings | 3 | FAILED 1 / SUCCEEDED 2 | 237 s | 130 s |
| Active Directory — Assets | 2 | FAILED 1 / SUCCEEDED 1 | 324 s | 324 s |
| Nessus — Assets and Findings | 1 | SUCCEEDED 1 | 228 s | 228 s |

Distinct Tenable.io parser error messages in that window:

| count | error |
|---|---|
| **1,228** | `Error Category: QUERY_ERROR; Spark Error Class: MISSING_COLUMN; AnalysisException: Column 'asset.ipv4' does no...` |
| 36 | `UNCLASSIFIED_ERROR; An error occurred while calling o1849.jdbc. This connection has been close...` |
| 18 | `UNCLASSIFIED_ERROR; An error occurred while calling o1851.jdbc. This connection has been close...` |
| 14 | `UNCLASSIFIED_ERROR; An error occurred while calling o1850.jdbc. This connection has been close...` |
| **10** | `UNCLASSIFIED_ERROR; DatabaseError: server closed the connection unexpectedly` |
| 9 | `Max concurrent runs exceeded` |
| **6** | `Error Category: CONNECTION_ERROR; ConnectionException: Timed-out waiting to acquire database connection.` |
| **4** | `UNCLASSIFIED_ERROR; An error occurred while calling o1849.jdbc. An I/O error occurred while se...` |

**This table recovers the primary exception that our run masked.** Sibling runs of the same job,
hitting the same Aurora cluster through the same proxy, failing the same JDBC write, report:

- `server closed the connection unexpectedly` (×10)
- `An I/O error occurred while sending to the backend` (×4)
- `java.io.EOFException` (seen in the longest-runs table below)
- `ConnectionException: Timed-out waiting to acquire database connection` (×6)

**The server/proxy side is closing these connections.** `EOFException` and "server closed the
connection unexpectedly" are unambiguous: the peer went away mid-request. In our run, pgjdbc
converted that peer-close into a local `close()`, and `savePartition`'s `finally` then overwrote the
diagnosis with `This connection has been closed.` The masked cause is a **server- or proxy-initiated
connection reset**, and the `Timed-out waiting to acquire database connection` variant points at the
proxy's connection-borrow path specifically — which is exactly the config I could not read (§B4.6).

Note the 1,228 `MISSING_COLUMN: Column 'asset.ipv4' does not exist` failures are a **different bug**
on the other instance (`251f9990`), and they explain the 2,130-run count as a retry loop. They are
not this failure and should not be conflated with it.

### Comparison: 1,319 s and this load level are not normal for a *successful* run

Longest parser runs in the 25 h window, all flows:

| exec | state | started (IDT) | instance | flow | error |
|---|---|---|---|---|---|
| 1,442 s | FAILED | 2026-08-18 17:13:28 | 251f9990 | Tenable.io | `DatabaseError: server closed the connection unexpectedly` |
| 1,403 s | FAILED | 2026-08-18 17:13:08 | b71f40ca | CrowdStrike Falcon | `DatabaseError: server closed the connection unexpectedly` |
| 1,327 s | FAILED | 2026-08-18 17:14:43 | 251f9990 | Tenable.io | `DatabaseError: server closed the connection unexpectedly` |
| **1,319 s** | **FAILED** | **2026-08-19 15:39:07** | **21f14fe6** | **Tenable.io** | **`o1845.jdbc. This connection has been close…`** ← target |
| 1,202 s | FAILED | 2026-08-18 17:16:33 | 251f9990 | Tenable.io | `server closed the connection unexpectedly` |
| 1,188 s | FAILED | 2026-08-18 17:12:48 | 251f9990 | Tenable.io | `o1849.jdbc. This connection has been close…` |
| 1,150 s | FAILED | 2026-08-18 17:16:48 | 251f9990 | Tenable.io | `server closed the connection unexpectedly` |
| 1,145 s | FAILED | 2026-08-18 17:16:08 | 251f9990 | Tenable.io | `server closed the connection unexpectedly` |
| 1,106 s | FAILED | 2026-08-18 17:13:53 | 251f9990 | Tenable.io | `o1849.jdbc. java.io.EOFException` |
| 1,104 s | FAILED | 2026-08-18 17:13:33 | b71f40ca | CrowdStrike Falcon | `o2015.jdbc. This connection has been close…` |

**Every single run over ~1,100 s failed, and every one failed on the Postgres write.** By contrast:

| | duration range |
|---|---|
| SUCCEEDED Tenable.io runs (8, all instance 251f9990) | **195, 195, 212, 239, 252, 263, 272, 524 s** |
| SUCCEEDED CrowdStrike runs (longest 8, instance c0ad4f10) | **240–275 s** |

**No successful parser run in 25 h exceeded 524 s.** There is a clean separation: ≤524 s succeeds,
≥1,104 s fails. Our 1,319 s sits solidly in the failing band. ~1,300 s is *not* a normal successful
duration for any vendor here.

### The one comparison that rules out a confound

**Our run was alone.** Workflows started within ±5 minutes of it (15:36:25–15:46:25 IDT): **1** —
itself. No sibling parser was competing for the cluster.

Contrast the 2026-08-18 17:12–17:20 IDT burst: **174 workflow runs in ~8 minutes**, 125 FAILED /
49 SUCCEEDED. That burst is a genuine concurrency-driven failure mode, and it is *not* what happened
to us. The 425-connection baseline during our run was steady EA-service traffic (flat 423–428 for
17 minutes beforehand, back to 418–426 immediately after).

**So a single parser, running alone, writing 5.86 M rows in 100 partitions with 50,000-row rewritten
multi-row batches, was sufficient on its own to take the writer's freeable memory from 11.7 GB to
278 MB and drive 327 AAS on 8 vCPUs.** No concurrency confound.

---

## Numbers table (quantity | value | how measured)

| quantity | value | how measured |
|---|---|---|
| Exposure rows the parser tried to write | **5,858,751** | driver `GlueLogger: DataFrame row count: 5858751` @12:57:04.041 |
| Asset rows written (succeeded) | **17,984** | driver `GlueLogger: DataFrame row count: 17984` @12:56:37.608 |
| Exposure rows previously in scope (deleted) | **67,553** in 7 batches | `[delete_by_scope] … DONE` @12:56:33.596 |
| Asset rows previously in scope (deleted) | **6,485** in 1 batch | `[delete_by_scope] … DONE` @12:55:07.884 |
| Exposure-row growth factor | **86.7×** | 5,858,751 / 67,553 |
| Exposures write partitions / rows per partition | **100 / 58,588** | `target partitions: 100`; 5,858,751/100 |
| Assets write partitions / rows per partition | **4 / 4,496** | `target partitions: 4`; 17,984/4 |
| JDBC batchsize | **50,000** | `write_to_postgres config: … batchsize=50000`; `script.py:70` |
| Executors / cores each / task slots | **9 / 4 / 36** | 9× `Registered executor`; `spark.executor.cores=4` in SparkListenerJobStart |
| **Max concurrent JDBC write connections** | **37 measured (36 steady)** | replay of `Starting`/`Lost`/`Finished task` per stage 68/69/70 |
| Write stages / tasks each | 68.0, 69.0, 70.0 / **100 each** | `Adding task set N.0 with 100 tasks` |
| Application-level write retries | **3** (`MAX_WRITE_RETRIES`) | 3× `Write to parser_output_exposures failed on attempt N/3` |
| Spark task attempts per task | **4** | `Task N in stage X failed 4 times; aborting job` |
| Write-task attempts started | **130 + 155 + 151 = 436** | count of `Starting task … in stage 68/69/70.0` |
| Write tasks that SUCCEEDED | **0** | `grep -c "Finished task .* in stage 68/69/70.0"` = 0,0,0 |
| Failed task attempts | **328** (CW) / **336** (log-derived) | `numFailedTasks` Sum; 419 `Lost task` − 83 killed |
| Killed task attempts | **83** (both sources agree exactly) | `numKilledTasks` Sum = 22+14+25+11+11; log 36+36+11 |
| Failed write-task lifetime | **min 22.6 s, median 27.1 s, max 112.9 s** (n=331) | `Lost task` ts − `Starting task` ts |
| Share of failures in 20–30 s bucket | **269/331 = 81 %** | 10 s histogram |
| Assets write duration | **7.463 s** | `ResultStage 61 … finished in 7.463 s` |
| Exposures attempt durations | **135.958 / 106.154 / 105.828 s** | `ResultStage 68/69/70 … failed in … s` |
| First executor JDBC failure | **12:57:31.333** (26.5 s into attempt 1) | executor `Exception in task 25.0 in stage 68.0` |
| Final abort | **13:03:26.588** | `Task 20 in stage 70.0 failed 4 times; aborting job` |
| Executor heap usage peak | **0.950** (avg 0.802) | `glue.ALL.jvm.heap.usage`, 15:58 IDT |
| Driver heap usage peak | 0.156 | `glue.driver.jvm.heap.usage` |
| Real executor OOM events | **0** | 10 "OutOfMemoryError" hits are the `-XX:OnOutOfMemoryError=` flag |
| — | | |
| Target cluster | **`stg-pg-cybi-aurora-cluster`**, Aurora PostgreSQL **16.11** | `describe-db-clusters` |
| Writer instance | **`…-instance-1`**, `db-3VVWVZHWZVBQD4LXTN4AOBPJKI` | `IsClusterWriter: true` |
| Instance class / **vCPUs** | db.r7g.2xlarge / **8** | `describe-db-instances`; PI `os.general.numVCPUs.avg`=8.0 |
| `reWriteBatchedInserts` | **true** | `sparkDAL.py` properties dict; corroborated by multi-row VALUES in PI |
| `socketTimeout` / `connectTimeout` | **3600 s / 30 s** | `sparkDAL.py` jdbc_url |
| `write_mode` | **`append`** (×3 retries, no idempotency) | `script.py:71` |
| `ROWS_PER_PARTITION` / `MAX_PARTITIONS` | **50,000 / 100** | `sparkDAL.py:7-9` |
| Top INSERT form | **MULTI-ROW** `VALUES ($1..$20),($21..$40),…` | PI `db.sql.statement`, tokenized id `004D7950…` |
| Value tuples per statement | **≥38 measured; exact count unobtainable** | PI text capped at 4095 chars, ends at `$757` |
| Bind params per tuple | **20** | 20 columns in the INSERT column list |
| Distinct tokenized exposures INSERTs | **5**, summing **126.348 AAS** | `describe-dimension-keys` 12:56–13:10Z, no NextToken |
| Dominant INSERT variant AAS | **124.970** (12:56–13:10Z) / **141.452** (12:55–13:06Z) | PI `db.sql_tokenized` / `db.sql` |
| Parser INSERT share of all DB load | **126.348 / 150.98 = 84 %** | sum of top-25 tokenized keys, same window |
| `DEALLOCATE ALL` | **7.61 AAS** — 2nd heaviest statement | PI `db.sql_tokenized` 12:56–13:10Z |
| **db.load.avg peak** | **327.46 AAS at 13:04Z** (baseline ~3, mean 58.62) | PI `get-resource-metrics` 60 s |
| AAS-to-vCPU ratio at peak | **41:1** | 327.46 / 8 |
| Wait-event split (12:56–13:10Z) | **CPU 152.10**, IO:DataFileRead 2.19, Lock:transactionid 0.08 | PI `db.wait_event` |
| AAS by user | **cybi 154.30**, others <0.5 | PI `db.user` |
| **Host CPU utilisation peak** | **68.71 %** (CW) / **71.10 %** (PI), mean ~55 % | `CPUUtilization`; `os.cpuUtilization.total.avg` |
| **FreeableMemory: before → floor** | **11.73 GB (12:57) → 0.278 GB (13:03)** | `AWS/RDS FreeableMemory` 60 s |
| DatabaseConnections: baseline → peak | **425 → 570** (+145) | `AWS/RDS DatabaseConnections` |
| Deadlocks | **0** (two sources) | `DeadlockCount` no datapoints; PI `db.Concurrency.deadlocks.avg`=0.00 |
| CommitLatency: baseline → peak | 0.6 ms → **4.85 ms** | `AWS/RDS CommitLatency` |
| WriteLatency / BufferCacheHitRatio | 0.001 ms / 99.76 % mean | `AWS/RDS` |
| tup_inserted peak | **5,157 rows/s at 13:07Z** — *after* the job died | PI `db.SQL.tup_inserted.avg` |
| **Parser INSERT AAS after job abort** | **109.54 (13:04–13:09Z)** | PI `db.sql_tokenized`, post-abort window |
| — | | |
| Glue → proxy path | **executors connect via RDS Proxy**, target 10.103.58.157:5432 | proxy log client IPs = 9 executor IPs |
| New proxy connections from Glue IPs | **457** (12:53–13:03Z) | proxy log `A new client connected from <exec IP>` |
| Proxy session-pinning warnings | **536** | proxy log normalised histogram |
| Proxy "closed in the middle of a request" | **421** | same |
| Proxy "TCP channel was closed by either the client or the proxy" | **420** | same |
| — | | |
| stg-cybi-parser runs since 2026-07-29 | **3,000**: FAILED 2,709 / SUCCEEDED 290 / STOPPED 1 | `get-job-runs` paginated |
| Tenable.io workflow runs since 2026-07-30 | **2,146** of 2,900 total | `get-workflow-runs` light pass |
| **Runs ever for target instance `21f14fe6`** | **1** — this one | instance histogram over 2,146 Tenable.io runs |
| Tenable.io parser outcomes, last 25 h | **FAILED 1,347 / SUCCEEDED 8** | graph pass |
| Dominant Tenable.io error (other instance) | **1,228× `MISSING_COLUMN: Column 'asset.ipv4'`** | graph pass, distinct errors |
| Server-initiated-close errors in siblings | **10× `server closed the connection unexpectedly`, 4× `An I/O error occurred`, 6× `Timed-out waiting to acquire database connection`, ≥1× `EOFException`** | graph pass |
| SUCCEEDED Tenable.io durations | **195–524 s** (n=8) | graph pass |
| SUCCEEDED CrowdStrike durations (longest 8) | **240–275 s** | graph pass |
| All runs ≥1,104 s | **100 % FAILED**, all on the Postgres write | graph pass longest-runs table |
| **Workflows started within ±5 min of our run** | **1 (itself)** — no concurrency confound | graph pass |
| 2026-08-18 17:12–17:20 IDT burst (contrast) | **174 runs**, 125 FAILED / 49 SUCCEEDED | graph pass |

---

## Caveats

**Separation of sources.** Every number in the tables above is **measured via API** by me except
where a row explicitly says otherwise. The **operator's screenshot** figures — INSERT 58.59 AAS,
1.39 calls/sec, 177.29 rows/sec, 117.43 ms/call, `DEALLOCATE ALL` 3.58, EA Cron-Service 2.62 @
63.79 calls/sec, EA Correlation-TP 1.01 @ 8,939.86 ms/call, baseline ~5 AAS, peak ~280 AAS,
Max vCPUs ~10 — are **not my measurements** and are not carried into my conclusions. Where they
overlap mine they broadly agree and the screenshot's window looks close to 12:40–13:15Z: my measured
INSERT AAS for that window is 49.43 (screenshot 58.59) and `DEALLOCATE ALL` 3.02 (screenshot 3.58).
**Two screenshot figures I contradict with measurement:** vCPUs is **8**, not ~10
(PI `os.general.numVCPUs.avg` = 8.0 at every point; db.r7g.2xlarge), and peak AAS is **327.5**,
not ~280. The rows-per-call ratio (~127.5) exists *only* in the screenshot; I could not reproduce it
because the per-SQL rate statistics are unavailable on this engine.

**Labelled speculation** — flagged inline, restated here so nothing leaks into the record as fact:

1. That the 22.6 s failure floor is "time to marshal the first 50,000-row batch", so every task dies
   on its first `executeBatch`. The floor and its tightness are measured; the *reason* is inference.
   No log line times an `executeBatch`.
2. That the mechanism behind PI's 152–327 "CPU" AAS at only ~60 % host CPU is memory-pressure /
   allocator churn. The AAS/CPU divergence and the FreeableMemory collapse are measured; the causal
   link is inference. No wait event names it and the PG log is unreadable.
3. That the connection-count overshoot (+145 for 36 writers) and the 4-minute post-abort load are
   caused by `conn.close()` being skipped when `conn.rollback()` throws. The Spark 3.3 `finally`
   structure and the thrown `rollback()` are established from the traceback and Spark source; the
   attribution of the specific connection surplus to that path is inference.

**What is NOT speculation and should carry weight:** the multi-row INSERT form; 5,858,751 rows;
100 partitions against 36 slots; zero successful write tasks; 3×4 nested retries; 331 failed
attempts; 457 proxy connections; 536 pinnings; FreeableMemory 11.7 GB → 278 MB; 327 AAS on 8 vCPUs;
CPU never above 71 %; zero deadlocks; the run being alone; the 524 s-vs-1,104 s success/failure
separation; and `reWriteBatchedInserts=true` / `socketTimeout=3600` / `write_mode="append"` from
source.

**On the reported error string.** `Error Category: UNCLASSIFIED_ERROR; An error occurred while calling
o1845.jdbc. This connection has been closed.` is a **cleanup artefact**, not the cause — it is
`conn.rollback()` failing inside `savePartition`'s `finally` and overwriting the real exception.
Do not tune against it. The masked primary cause, recovered from sibling runs in §C, is a
**server- or proxy-initiated connection close** (`server closed the connection unexpectedly`,
`An I/O error occurred while sending to the backend`, `java.io.EOFException`,
`Timed-out waiting to acquire database connection`).

**On causal ordering** — the single most important interpretive point. Two phases, do not merge them:

- **Phase 1, trigger (12:57:04–12:57:31):** oversized rewritten batches fail on the *first* attempt,
  when the DB had 11.7 GB freeable memory, 52 % CPU, zero deadlocks, and the parser was the only job
  running. **DB saturation did not cause the first failure.**
- **Phase 2, amplification (12:58–13:07):** 3×4 nested retries turn one failure into 331 failed
  attempts and 457 pinned, un-closed connections; *that* drains the writer to 278 MB and drives
  327 AAS, degrading the DB for every other tenant service — and continuing for ~4 minutes after
  Glue reported failure.

A fix aimed only at Phase 2 (bigger instance, more memory) would not prevent Phase 1.

**Coverage limits.** The graph-joined baseline covers ~25 h (2026-08-18T14:57 → 2026-08-19T15:39
IDT), not the full 14 days requested — `--include-graph` paginates 25/page and I capped the cost.
This is not load-bearing: the target instance has exactly **one** run in the full 20-day light pass,
so no longer graph window could produce a prior baseline for it. The 20-day aggregate
(3,000 job runs, 2,900 workflow runs) is reported separately and does cover the period.

**Evidence expiry.** All three `/aws-glue/jobs*` log groups have **3-day retention** and the two
richest are `INFREQUENT_ACCESS` (Insights-only access). The driver, executor and proxy extracts
behind this document are saved under
`/private/tmp/claude-501/-Users-user-Dev-cymulate-integration-parsers/adbf1114-0abc-4883-8523-fb6b6af5f1ab/scratchpad/`
(`driver_clean.log`, `driver_stages.log`, `driver_tasks.log`, `driver_jdbc.log`, `exec_err.log`,
`proxy_full.log`, `wfruns.json`, `wf_light.json`, `allruns.json`). **Anything not captured there is
gone after 2026-08-22.** PI retention is 7 days.

**Open items for whoever owns the next step**, in order of value:
1. Enable the `postgresql` log export on `stg-pg-cybi-aurora-cluster` — there is no
   `/aws/rds/cluster/stg-pg-cybi-aurora-cluster/postgresql` log group, which is why the primary
   cause had to be recovered indirectly.
2. Get the index list on `integration.parser_output_exposures` from a psql session (§B4.2).
3. Determine whether rows committed and whether the 3 `append` retries duplicated any (§B4.3).
4. Obtain the RDS Proxy connection-pool config and the cluster parameter group — both AccessDenied
   for the `AAD-Platform` role (§B4.4, §B4.6).
