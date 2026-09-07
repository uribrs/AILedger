# Lessons — 2026-08-19_1626_tenableio-parser-postgres-load (2026-08-19)

## L-b345971b — A13 — additional_fields is not the plugin object
belief:  The Tenable parser's additional_fields column is effectively the finding's whole plugin object, so plugin size measures the written payload.
counter: The plugin walk skips every mandatory path: additional_fields excludes plugin.{name,solution,cve,description} and root {asset,plugin,severity_id,state,first_found,last_found}. Measured against the real exclusion set the payload is 10,437 B/exposure-row (61.1 GB/run), not the 14,803 B (86.7 GB) the whole-plugin figure gave; plugin.cve alone was ~9.3 GB of the error.
source:  tenableAssetsAndFindings.py:343-370 (mandatory map), :560, :569, :573 (exclusion set); rca.md field table (verifier, 2026-08-19)
verify:  git show a9f6631:libs/packages/parsers/deprecated/tenable/tenableAssetsAndFindings.py | grep -nE 'full_path not in mandatory|excluded_root_fields'
do not:  Do not size a parser's additional_fields from the source object; reconstruct it against the mandatory-path exclusion set first.

## L-1587791b — A12 — PI AAS is not a count of statements in flight
belief:  Dividing a statement's Performance Insights AAS by its average latency gives the number of concurrent writers (58.59 / 0.11743 = ~499).
counter: Measured concurrency was 37 write tasks (36 steady, = 9 executors x spark.executor.cores 4), 457 RDS Proxy connections and 570 peak DatabaseConnections. PI AAS counts active sessions in any state including runnable-but-not-progressing, not parser statements in flight. Separately PI reported 152 AAS 'CPU' while host CPU peaked at 68.7%.
source:  evidence-glue-db.md write-stage task replay (stages 68/69/70) + SparkListenerJobStart config; PI db.load.avg by wait_event (verifier, 2026-08-19)
verify:  aws glue get-job-run --job-name stg-cybi-parser --run-id <jr> --query 'JobRun.[NumberOfWorkers,WorkerType]'
do not:  Do not derive writer concurrency from AAS/latency; measure it from Spark task starts or DatabaseConnections.

## L-90bc1864 — A10 — Spark JDBC concurrency is the partition count
belief:  The number of concurrent Postgres connections from a Spark JDBC write equals the DataFrame's partition count.
counter: The exposures write had 100 partitions but only 37 concurrent tasks: concurrency is min(partitions, task slots), and slots = executors x spark.executor.cores. The remaining partitions queue. Glue 10 x G.1X = 9 executors x 4 cores = 36 slots.
source:  sparkDAL.py:173-200 (_calculate_partitions, MAX_PARTITIONS=100); driver log 'Adding task set 68.0 with 100 tasks' vs measured 37 concurrent (verifier, 2026-08-19)
verify:  grep -n 'MAX_PARTITIONS\|ROWS_PER_PARTITION' libs/packages/dal/sparkDAL.py
do not:  Do not equate numPartitions with concurrent DB connections; bound it by executor task slots.

## L-57acd253 — A1 — The ISB Tenable.io collector has no resume path
belief:  The Tenable.io collector has no checkpoint/resume path, so mid-run replay cannot duplicate published findings.
counter: A resume path exists and is exercised: 'Adapter declined resume from checkpoint ... Page: 0. Starting fresh.' on the run under study, and 'Resume cancelled. Vendor=Tenable.io Flow=CollectFindings' four times on 2026-08-18. It was declined here, not absent. The no-duplication conclusion held only because it was measured directly (0 duplicate finding_id over 1,950,862; factor 1.0000, full read).
source:  evidence-isb-collector.md section 5; evidence-s3-collector.md duplication census (verifier, 2026-08-19)
verify:  none - citation is an Elastic log line in stg under short retention, not a repo artifact
do not:  Do not carry an AgentService collector property over to the ISB collector, and do not infer absence of duplication from absence of a resume path - count finding ids.

## L-9a2a1c4d — A9 — The reported JDBC error names the cause
belief:  A Glue parser failing with 'An error occurred while calling oNNN.jdbc. This connection has been closed.' tells you why the write failed.
counter: SQLSTATE 08003 is thrown only by checkClosed() on an already-dead connection. Spark 3.3.0 savePartition's finally block calls conn.rollback() unguarded (JdbcUtils.scala:748), so on a dead connection it throws and DISCARDS the primary exception - and conn.close() on the next line never runs, leaking the connection. Sibling runs where rollback succeeded report the real causes: 'server closed the connection unexpectedly', EOFException, 'I/O error occurred while sending to the backend', 'Timed-out waiting to acquire database connection'.
source:  pgjdbc REL42.3.6 PgConnection.java:881-885,889-890; Spark v3.3.0 JdbcUtils.scala:741-752; evidence-glue-db.md section C error census (researcher, 2026-08-19)
verify:  aws glue get-job-runs --job-name stg-cybi-parser --region us-east-1 --query 'JobRuns[].ErrorMessage' | grep -c 'connection has been closed'
do not:  Do not tune against the 08003 message; find the FIRST executor-side exception, or recover the cause from sibling runs.

## L-0b629a04 — A13 — Per-row index share of INSERT cost never measured
belief:  Per-row index maintenance is a material share of the exposures INSERT CPU.
counter: Never measured. The index LIST was established (11 B-trees, 3 keyed on random v4 UUIDs, no triggers) from cybi-db-models, but no evidence sizes index maintenance against jsonb_in or TOAST compression, and the index list is the migration repo at HEAD 98b95949 (2026-05-06), not STG's live catalog. rds:DescribeDBParameters was AccessDenied and no PG log group exists for the cluster.
source:  db-schema-authoritative.md; evidence-glue-db.md section B4 (verifier, 2026-08-19)
verify:  grep -c '@@index' ~/Dev/cybi-db-models/prisma/schema/integration.parser-output-exposures.prisma
do not:  Do not claim an index-cost share without EXPLAIN (ANALYZE, BUFFERS) or pg_stat_statements against the live table.

## L-ab924833 — A8 — Mongo stg is unreachable from an SSO role
belief:  A Mongo _id handed over as a run identifier can be resolved to its run document in stg.
counter: Never done. Atlas eks-stg authenticates with MONGODB-AWS and the SSO role AAD-Platform/<user> is not a mapped Atlas user: pymongo returns OperationFailure code 18 AuthenticationFailed, and the mongodb MCP connect fails the same way. The secret mongo-atlas-eks-stg-yH52mK is readable and yields a mongodb+srv string with no credentials. Run identity had to be established from the ISB RabbitMQ trigger payload instead, which carries correlationId together with instanceId, clientID and storageUrl.
source:  evidence-isb-collector.md Method; secret mongo-atlas-eks-stg-yH52mK (verifier, 2026-08-19)
verify:  aws secretsmanager get-secret-value --secret-id mongo-atlas-eks-stg-yH52mK --query SecretString --output text | head -c 80
do not:  Do not plan a stg investigation around reading Mongo from an SSO identity; use the ISB trigger payload for run identity, or get an Atlas-mapped user first.

## L-727045fe — D7 — Deployed-wheel confirmation was substituted
belief:  The deployed parsers.whl can be downloaded and inspected to prove which code path shipped to an environment.
counter: s3://cym-glue/stg/libs/parsers.whl GetObject returns 403 Forbidden to the operator's role (ListBucket is allowed, so the listing and mtime ARE readable). D7's plan to confirm by inspection was replaced by runtime proof from the Glue driver log's lane-specific log strings, which is stronger evidence - what ran, not what shipped.
source:  execution_notes.md blocked note; driver log 12:47:55.329 / 12:47:56.166 (executor, 2026-08-19)
verify:  aws s3 cp s3://cym-glue/stg/libs/parsers.whl /dev/null 2>&1 | grep -c Forbidden
do not:  Do not plan to verify a deploy by reading the wheel; grep the driver log for a log string unique to the code path in question.

## L-766ef02a — A2 — TenableIo publish concurrency shape never examined
belief:  TenableIo's publish pipeline is a sequential pump with unordered parallel fan-outs over order-independent sub-items, and no ordered parallel-collect/serial-publish stage.
counter: Never examined in this task. It was seeded as prior art from lessons.md#L-98cb5412 and carried through to close, but no packet opened TenableIoVulnPhase.cs or any adapter source. What WAS measured is publish OUTPUT (2,112 contiguous findings pages over a single 71-minute write window, 0 gaps, 0 duplicates) - that constrains the result, not the architecture.
source:  verifier-1.md disposition A2; prior art lessons.md#L-98cb5412 (verifier, 2026-08-19)
verify:  grep -rn 'Parallel.ForEachAsync' ~/Dev/cymulate-integration-adapters --include=TenableIoVulnPhase.cs
do not:  Do not treat a measured publish result as evidence about the publisher's concurrency model; open the phase source.
