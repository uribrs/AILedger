# Task: Why does the Tenable.io parser overload Postgres in STG?

Diagnose the excessive Postgres load produced when the Tenable.io parser (`parser_key`
`tenable-assets-findings`) writes `integration.parser_output_exposures` in STG, and report the root
cause with real numbers. This task produces a **report**, not a code change.

## The observation (operator-supplied, RDS Performance Insights, STG)

- Top SQL by load: `INSERT INTO integration.parser_output_exposures ("id","asset_id","client_id","cl...`
  - **58.59 AAS** (load by waits), **1.39 calls/sec**, **177.29 rows/sec**, **117.43 ms avg latency/call**
- DB load rises from ~5 AAS baseline to **~280 AAS** between 15:58 and 16:07 on the graph clock, then
  drops back to baseline by 16:08. `Max vCPUs` sits at ~10 — peak load is ~28x the instance's vCPUs.
- Wait profile is overwhelmingly **CPU**, with thin slices of IO:DataFileRead, IO:XactSync,
  Lock:transactionid, Client:ClientRead, LWLock:BufferContent, Timeout:VacuumDelay, LWLock:BufferMapping.
- Remaining Top SQL rows are unrelated background load: `DEALLOCATE ALL` (3.58),
  EA Cron-Service `DatabaseIntegrityRepository.upsertIntegrityRecord` (2.62 @ 63.79 calls/sec),
  EA Correlation-TP `ExposureRiskRepository.detectChanged` (1.01 @ 8939.86 ms/call).

## Run identity

- Mongo `_id`: `6a8593320f3d4281bd8a0df0` (ObjectId timestamp **2026-08-19 11:27:46 UTC**).
- Contract time: 2026-08-19 13:26 UTC / 16:26 IDT. The graph window ends ~12 minutes before contract
  time, so 15:58-16:07 is almost certainly **IDT (UTC+3) = 12:58-13:07 UTC** — confirm, do not assume.

## Questions the report must answer, with numbers

1. **Fan-out.** How many exposure rows did the parser write, vs how many findings the collector
   published? Quantify every multiplier: CVE explode (one row per CVE), per-plugin/per-port findings,
   chunk-envelope re-emission, correlated-mode duplication.
2. **Write shape.** What SQL actually reaches Postgres — single-row `INSERT` vs multi-row batch — what
   JDBC batch size, and how many concurrent Spark writer partitions? Does that arithmetic reproduce
   1.39 calls/sec at 177.29 rows/sec and 117.43 ms/call?
3. **Volume vs shape.** Separate the two causes: too many rows, versus a bad write pattern
   (no batching / excessive concurrency / per-row index, constraint or trigger cost). Attribute the
   ~280 AAS peak.
4. **Anomaly vs steady state.** Is this run unusual for its data size, or what this parser always does?

## Out of scope

- Any code change, deploy, or job re-trigger. A fix, if warranted, is a separate task.
