# Why the Tenable.io parser overloaded STG Postgres

> **Read `rca.md` alongside this.** It carries the specific, field-level root cause and **supersedes
> every `additional_fields` size figure below**: the correct payload is **10,437 B/row ≈ 61.1 GB**, not
> the 14,803 B ≈ 86.7 GB stated in §3/§5. The figure here counted the whole `plugin` object including
> the four mandatory paths the parser excludes (`plugin.cve` alone was ~9.3 GB of it). Every conclusion
> stands; the payload is 30 % smaller and still 61 GB.
>
> Repaired after verifier pass 1 (`review/verifier-1.md`): the measured peak is **327.46 AAS**, not the
> 141.45 window-mean previously called "the true peak"; the "% of payload written" figure is withdrawn.

Diagnostic report. No code was changed. Every number carries its source; the evidence packets are in
`research/`.

---

## 1. Root cause

**One Tenable.io run turned 1,950,862 collector findings into 5,858,751 exposure rows carrying ~87 GB
of `jsonb`, and the parser tried to push that through ~36 concurrent Aurora connections in 50,000-row
single-transaction batches (~740 MB of bind data each); every write task died ~23 s into its first
flush, and 3 application retries × 4 Spark task attempts turned one failed write into 436 attempts
that held 84 % of all database load for 6½ minutes and drained the writer's freeable memory from
11.7 GB to 278 MB — without committing a single row.**

The load was excessive because the payload is oversized **and** the write pattern cannot deliver it.
Neither alone explains it, and the retry storm is what converted a failed write into an incident.

---

## 2. The run

| | |
|---|---|
| Flow / client / instance | `Tenable.io - Tenable.io Assets and Findings` / `613f3842d8ddf900117fccfa` / `21f14fe6-ca31-42a7-87aa-c343bbbb777d` |
| Collector run (Mongo `_id` `6a8593320f3d4281bd8a0df0`) | **11:27:50Z → 12:39:07Z**, status success, `Failure=null`, 71 min 17 s |
| Glue parser run `jr_36b7a68d…befb2` (`stg-cybi-parser`) | **12:41:25Z → 13:03:44Z**, 1,319 s, **FAILED** |
| Postgres load spike | **12:58Z → 13:07Z** |
| Database | Aurora PostgreSQL **16.11**, `stg-pg-cybi-aurora-cluster-instance-1` (writer), **db.r7g.2xlarge, 8 vCPUs**, behind RDS Proxy `stg-cybi-aurora-cluster-proxy` |

**Clock conversion.** The operator's screenshot axis (15:44–16:14) is **IDT = UTC+3** → 12:44–13:14 UTC.
Confirmed by matching `aws glue get-job-runs` (`StartedOn 2026-08-19T15:41:25.406000+03:00`) against the
Glue driver log's own UTC prefixes, which agree with CloudWatch `@timestamp` to the millisecond.

Note the instance is **8 vCPUs**, not the ~10 the screenshot's `Max vCPUs` line suggests.

---

## 3. Volume — the payload is real, not duplicated

| stage | count | source |
|---|---|---|
| Assets published | 17,984 | ISB `TotalAssetsCollected=17984`; 17,956 `_staging/spine/*.json` |
| Findings published | **1,950,862** | ISB `TotalFindingsCollected=1950862`; **independently** Σ`len(record.findings)` over a full read of all 2,112 files |
| NDJSON envelope records | 17,984 (one per host) | full read; `chunk` = `{0: 17984}`, `isLastChunk` = `{True: 17984}` |
| Findings with ≥1 CVE | 867,700 (44.5 %) | full read |
| Findings with 0 CVE → **dropped** | 1,083,162 (55.5 %) | `base_parser.py:291-303` drops CVE-less `type=vulnerability` |
| **Exposure rows written** | **5,858,751** | Σ`len(plugin.cve)` from S3 **and** Glue driver `DataFrame row count: 5858751` @12:57:04.041 — two independent measurements agreeing exactly |

### Multipliers, each named and sized

| multiplier | factor | verdict |
|---|---|---|
| **CVE explode** — one row per CVE (`base_parser.py:295-318`) | **3.003×** per finding (6.752× per CVE-bearing finding) | **the whole of the row multiplication** |
| Envelope chunk duplication | **1.000×** | ruled out — every host is a single chunk-0 record, 0 hosts span >1 chunk |
| Input duplication | **1.0000** | ruled out — 0 byte-identical duplicate lines, 0 repeated `finding_id` over all 1.95 M, gap-free `findings_000001..002112`, no `findings.json`, no `batch_*/` |
| Collector retry / requeue / resume | none | `Adapter declined resume from checkpoint … Starting fresh`; 0 rows for Resume/retry/requeue; exactly one Tenable.io run in stg that day |
| Join fan-out (findings→assets) | **1×** | `dropDuplicates(["_asset_correlation_id"])` at `tenableAssetsAndFindings.py:526` runs **before** the join at `:619` |
| Per-port / per-plugin explode | none in parser | Tenable's export already emits one record per (asset, plugin, port) |
| `(host, cve)` repeats via multiple plugins | +3.52 % (max 20×) | real but minor; adds rows, does not duplicate them |

CVE-per-finding is brutally long-tailed: p50 **0**, p95 10, p99 **49**, max **753**. Most volume comes
from a small minority of findings.

### The width nobody budgeted for — this is the expensive half

`additional_fields` is built by flattening the whole `plugin` object (`tenableAssetsAndFindings.py:551-579`),
and the **entire plugin object is copied into every per-CVE row** — including the full `cve[]` and
`cpe[]` arrays that made the row multiply in the first place.

| per-exposure-row, CVE-weighted | bytes | × 5,858,751 rows |
|---|---|---|
| `additional_fields` — **corrected, see `rca.md`** | **10,437** | **≈ 61.1 GB** |
| ~~whole `plugin` object (superseded: includes the 4 excluded mandatory paths)~~ | ~~14,803~~ | ~~86.7 GB~~ |
| whole finding object | 16,320 | ≈ 95.6 GB |
| `output` (Nessus plugin text) | 379 | ≈ 2.2 GB |

CVE-weighting roughly doubles the payload over its unweighted mean (10,437 vs 5,322 B/finding),
because high-CVE plugins are exactly the big ones. **Trimming `output` would save almost nothing;
trimming `additional_fields` saves ~87 GB of writes.**

Derived: 325.8 exposure rows per asset; 5,858,751 rows × 11 indexes = **64.4 M index entries**.

---

## 4. Write shape

| property | value | source |
|---|---|---|
| Mechanism | Spark `DataFrameWriter.jdbc()` — no driver-side row loop anywhere | `sparkDAL.py:225-230` |
| Statement form | **multi-row** `INSERT … VALUES ($1..$20),($21..$40),…` | measured via PI `get-dimension-key-details`: 20 binds/tuple, ≥38 tuples visible before PI's 4,095-char cap |
| Tuples per statement | **128** (390×128 + 64 + 16 per `executeBatch`) | pgjdbc **42.3.6** `PgPreparedStatement.transformQueriesAndParameters()` hard-codes `highestBlockCount = 128`; AWS documents 42.3.6 for Glue 4.0 |
| `batchsize` | **50,000** | `script.py:70`; `write_to_postgres config: … batchsize=50000` |
| `reWriteBatchedInserts` | `true`, as a JDBC property | `sparkDAL.py:209` |
| Partitions / rows each | **100 / 58,587** (coalesced from 4,224) | `DataFrame row count: 5858751, target partitions: 100` — `MAX_PARTITIONS` ceiling hit |
| **Concurrent connections** | **37 measured, 36 steady** | 9 executors × `spark.executor.cores=4`; task-replay per stage 68/69/70 |
| Transaction | one COMMIT per partition (58,587 rows) | Spark `savePartition` sets `autoCommit(false)`; no `isolationLevel` set |
| Write mode | **`append`**, no truncate, no `ON CONFLICT` | `script.py:71` |
| Retries | **3 app × 4 Spark task attempts** | `MAX_WRITE_RETRIES=3`; `Task N failed 4 times` |
| Target table | 20 columns, **11 B-tree indexes**, `additional_fields jsonb` | `cybi-db-models` prisma + migrations; no triggers |
| `stringtype` | `unspecified` → server runs `jsonb_in` per row | `sparkDAL.py:208`; required, or the bind fails |

### Does the arithmetic reproduce the screenshot?

**The ratio, yes. The absolute rates, no — and they cannot be.**

- `177.29 rows/sec ÷ 1.39 calls/sec = 127.5 rows/call` — that is the pgjdbc 128-tuple cap, i.e. **normal
  driver behavior, not a misconfiguration**. `batchsize=50000` does **not** widen the statement; it only
  buffers 50,000 rows in executor heap before flushing 392 statements.
- The absolute rates are **not** reproducible: the per-SQL rate statistics PI would need are
  unavailable on this engine, so W2 could not reproduce 1.39 calls/sec at all. Nor can the screenshot's
  rates be read as run throughput — 5,858,751 rows over a 383 s write is ~15,300 rows/s ≈ 120 calls/s,
  ~90× the screenshot's figure. Variant splitting does **not** close that gap (the dominant variant
  carries 98.9 % of the INSERT AAS) and window dilution is only ~3.3×. **Treat the screenshot's
  calls/sec and rows/sec as a sampled slice, not as throughput; only their ratio is load-bearing.**
- **A12 is refuted.** Its inferred "~499 statements in flight" is wrong: the measured concurrency is
  **37 write tasks (36 steady)**, 457 proxy connections and 570 total DB connections. Do not carry 499
  forward.
- Measured AAS by API:

| window (UTC) | exposures INSERT AAS (**window mean**) | all statements | INSERT share |
|---|---|---|---|
| 12:40–13:15 (≈ the screenshot) | 49.43 | — | — |
| **12:56–13:10 (the write)** | **126.35** (5 variants) | 150.98 | **84 %** |
| 12:55–13:06 | 141.45 | — | — |

Those are **window means**, not peaks. The measured 60-second load curve (`db.load.avg`, PI
`get-resource-metrics`) is:

| UTC | 12:55 | 12:57 | 12:58 | 12:59 | 13:00 | 13:01 | 13:02 | 13:03 | **13:04** | 13:05 | 13:06 | 13:07 | 13:08 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| AAS | 2.6 | 3.4 | 45.6 | 96.6 | 148.3 | 220.9 | 268.5 | 296.1 | **327.5** | 281.8 | 213.7 | 61.6 | 3.4 |

**Peak 327.46 AAS at 13:04Z against a ~3 AAS baseline — a ~110× amplification, and 41:1 against
8 vCPUs.** The operator's screenshot (~280 peak, 58.59 AAS on the INSERT) is **corroborated and
slightly conservative**; mean over 12:40–13:15Z is 58.62, which matches the screenshot's 58.59 almost
exactly. The parser owns 84 % of it; all EA services combined are ~24 AAS.

**The peak is *after* the job died.** Glue aborted at 13:03:44, yet load peaked at 13:04 and stayed
above 200 AAS through 13:06; PI over 13:04–13:09Z still attributes **109.54 AAS to the parser's
exposures INSERT**. `tup_inserted` actually **peaks at 13:07 (5,157 rows/s)** — after the job was
dead. The orphaned backends kept executing INSERTs their leaked connections had already sent, for
~4 minutes past the failure.

---

## 5. Attribution: volume vs shape vs per-row cost

**Volume (the necessary work).** At the screenshot's own 0.921 ms/row — itself a quotient of two
screenshot figures, one of which (calls/sec) is unreproducible per §4, so treat this as an
order-of-magnitude sanity check rather than a measurement — 5,858,751 rows is **~5,400 s of
database time = 90 minutes single-threaded, ~11 minutes of a fully saturated 8-vCPU instance**. That is the approximate floor for this payload
even with a perfect writer. Cross-check: the measured 126.35 AAS over 840 s is ~106,000 s of DB time,
~20× that floor — consistent with a retry storm on top of it, not contradictory. It is far beyond what this instance can absorb
inside a parser run.

**Per-row cost (why 0.921 ms/row) — qualitative only.** The components below are established from the
schema and the driver's behavior, but **no share is attached to any of them**: this third of the
volume/shape/per-row decomposition is not quantified, and doing so needs `EXPLAIN (ANALYZE, BUFFERS)`
or `pg_stat_statements` against the live table. Parse and plan are amortized to zero (5 statement variants,
server-prepared). The time is:
- **11 index inserts per row** — `id`, `asset_id` and the `id` tail of `(instance_id, client_id, id)` are
  **random v4 UUIDs**, so each is an arbitrary leaf-page touch with no insertion locality; five more
  indexes take a single constant value for the whole run, concentrating contention on a few pages.
- **`jsonb_in` on ~14.8 KB per row** — lex, build the binary tree, sort keys, validate encoding.
- **TOAST** — at 14.8 KB every row is ~7× the 2 KB threshold, so every row pays an LZ compression pass
  plus chunked out-of-line writes and their own WAL.
- No trigger cost (verified: no trigger on this table).

**Shape (why none of it landed).** 36 concurrent writers × a 50,000-row first batch × ~14.8 KB =
**~740 MB of bind data per task, ~26 GB in flight at once**, each inside one long transaction, against
a 64 GiB writer. Executor heap hit **0.95** the moment the write started. Every task died at a hard
floor of **22.6 s** (81 % between 22.6–30 s) — a fixed cost being hit, not progressive exhaustion.

**Ordering rules out the obvious story.** The first task failed at 12:57:31 with **11.7 GB freeable and
52 % CPU**. The memory collapse *starts* at 12:58 and bottoms at 13:03 (**278 MB**), recovering the
instant the orphaned sessions drained at 13:07. So:

> Memory exhaustion is **not** the trigger — it is what the retry storm produced.

**The retry storm.** 436 write-task attempts across 3 stages, **0 successes** (`Finished task` count = 0, 0, 0).
CloudWatch corroborates by parts, not as a sum: `numKilledTasks` **83 matches the log-derived
36+36+11 exactly**, and `numFailedTasks` 328 agrees with the log-derived 336 within 2 %; 436 is the
count of task *starts*, a third quantity. Connections rose
425 → **570** for 36 concurrent writers. The `finally`-block structure is established; attributing the
+145 surplus specifically to it is **inference**. Spark's `savePartition` `finally` calls
`conn.rollback()` before `conn.close()`; on an already-dead connection `rollback()` throws, so
**`close()` never runs and the connection leaks**.

**How much actually landed: unresolved, and it matters.** The strongest fact is that **0 of 436
write-task starts succeeded** (`Finished task` = 0 in all three stages). But rows were demonstrably
being inserted: `tup_inserted` ran 694–5,157 rows/s and peaked at 13:07, *after* the abort, while the
INSERT still held 109.54 AAS. Whether those transactions reached `COMMIT` needs a `COUNT(*)` for this
client/instance — and with `append` mode plus 3 un-guarded retries this is a **duplicate-row**
question, not just an accounting one. *(An earlier draft derived "~1.5 % of the payload was written"
from `WriteThroughput`; that is withdrawn — the 3.34 MB/s is a 35-minute mean contaminated by a
12:46 spike unrelated to this write, and Aurora's volume-bytes are not comparable to uncompressed
bind payload.)*

**Not a lock or I/O problem.** `Lock:transactionid` 0.08 AAS, `IO:DataFileRead` 2.19 AAS,
`BufferCacheHitRatio` 99.8 %, `WriteLatency` ~0.001 ms, **zero deadlocks** (two independent sources).
And not compute-bound: PI reports 152 AAS "CPU" while host CPU peaked at **68.7 %** — the sessions were
runnable-but-not-progressing, which PI buckets as CPU. *(Mechanism labelled speculation by W2: memory
pressure and allocator churn; no wait event names it and the PG error log is not exported.)*

**The reported error is an artifact — do not tune against it.**
`This connection has been closed` (SQLSTATE 08003) comes only from `checkClosed()` on an already-dead
connection, thrown by that same `finally`, which **destroys the original exception**. Recovered from
sibling runs of the same job on the same cluster: `server closed the connection unexpectedly` (×10),
`An I/O error occurred while sending to the backend` (×4), `java.io.EOFException`,
`ConnectionException: Timed-out waiting to acquire database connection` (×6). **The server or the RDS
Proxy is closing these connections**, and the borrow-timeout variant points at the proxy specifically.

---

## 6. Anomalous, or steady state?

**The load level is steady state for this payload; the payload is what is new.**

- `stg-cybi-parser` over its last 3,000 runs: **2,709 FAILED / 290 SUCCEEDED — ~90 % failure rate.**
  This run's failure is not unusual for the job.
- Tenable.io over the last 25 h: **1,347 FAILED / 8 SUCCEEDED.**
- **No successful parser run of any vendor in 25 h exceeded 524 s. Every run over ~1,100 s failed, all
  on the Postgres write.** Successful Tenable.io runs: 195–524 s. This run: 1,319 s.
- **The target instance has exactly one run in 20 days — this one.** There is no successful baseline for
  it and nothing regressed; this was its first execution.
- **No concurrency confound**: exactly one workflow started within ±5 min — itself. Connections were flat
  at 423–428 for 17 minutes before and 418–426 immediately after. A single parser, alone, was enough.
- For contrast, the 2026-08-18 17:12–17:20 IDT burst (**174 workflow runs in 8 minutes**, 125 failed) is
  a genuinely different, concurrency-driven failure mode.
- The 1,228 `MISSING_COLUMN: Column 'asset.ipv4' does not exist` failures in the same window are a
  **different bug** on instance `251f9990` and must not be conflated with this one.

Context on what changed in the platform, even though this instance has no baseline:
- The **correlated Tenable.io parser lane first reached STG 22 h before this run** — PR #214 merged to
  master 2026-08-18 13:56:46Z → Jenkins master #273 → `s3://cym-glue/stg/libs/parsers.whl` rebuilt
  14:14:37Z. `git merge-base --is-ancestor 1e8a880 a7f1c12` is false, so it was not in master before.
- The **Tenable.io collector package was replaced in stg at 2026-08-18 15:01:50Z**; this run executed git
  sha `0db32731` under version 6.3.0.7.
- This run was **incremental, not a full export** (`filters.since = 2026-08-19T00:00:00Z`, an 11.5 h
  window admitting 17,956 of the account's ~101,000 assets). The two runs the day before were **3.6×
  larger** (45.8 and 46.2 GiB). **A full run on this account would be several times worse.**

---

## 7. Ranked contributing factors

1. **Tenable's static per-plugin reference text is copied into every per-CVE row** — `xrefs` (35 %) +
   `see_also` (28 %) are 63 % of it → 10,437 B × 5,858,751 = **~61 GB** of
   `jsonb`, each row paying `jsonb_in` + TOAST compression. The single largest lever.
2. **CVE explode at 3.003×** turns 1.95 M findings into 5.86 M rows — 325.8 rows per asset.
3. **3 app retries × 4 Spark attempts = 436 attempts, 0 successes.** Multiplies a doomed write by up to
   12× and, in `append` mode with no cleanup, is a live duplicate-row hazard.
4. **11 B-tree indexes per row, 3 on random UUIDs** = 64.4 M index entries with no insertion locality.
5. **50,000-row batches** buffer ~740 MB per task (executor heap 0.95) while the driver caps the actual
   statement at 128 tuples — the setting delivers the memory cost of a huge batch and none of the benefit.
6. **~36 concurrent writers on 8 vCPUs**, one transaction per 58,587-row partition, and a `MAX_PARTITIONS`
   ceiling of 100 that ignores instance size.
7. **Leaked connections** (`rollback()` before `close()` in Spark's `finally`) → 425→570 connections and
   load that outlives the job.
8. **`MaxConcurrentRuns: 100`** with no throttle — not a factor in *this* run, but it is what produced the
   2026-08-18 burst.
9. **`DEALLOCATE ALL` at 7.61 AAS** — the 2nd-heaviest statement on the instance, pure prepared-statement
   churn from proxy session resets.

---

## 8. Fix direction

Not designed here — this is scope for a follow-up contract.

- **Shrink the row before shrinking anything else.** Stop copying the full `plugin` object (especially
  `cve[]` and `cpe[]`) into every exploded row. Highest ratio of saving to risk: ~87 GB → a small
  fraction, with no change to row count. *(`output` is not the problem — 379 B CVE-weighted.)*
- **Stop the retry storm.** Retrying a write that failed for a payload reason in `append` mode with no
  cleanup cannot succeed and risks duplicates. Fail fast, or make the write idempotent (deterministic
  `id`, or staging table + swap).
- **Bound writer concurrency and transaction size to the instance**, rather than to `MAX_PARTITIONS=100`.
- **Reconsider the load mechanism at this volume** — PostgreSQL's own guidance is that `COPY` beats
  batched `INSERT` "even if PREPARE is used and multiple insertions are batched into a single
  transaction", and it bypasses the executor entirely.
- **Drop `batchsize` toward the driver's real 128-tuple ceiling** — 50,000 buys memory pressure and nothing else.
- **Review the 11 indexes** against actual query patterns; `type` is a single constant value for all Tenable rows.
- **Read two parameters first — cheapest step toward the still-unexplained 22.6 s floor.**
  `idle_in_transaction_session_timeout` and `transaction_timeout` on `aurora-pg16-cluster` both
  *terminate* sessions and are both plausible direct causes of the observed closures. Pure reads.
  (`rds:DescribeDBClusterParameters` was AccessDenied to the investigating role — needs an identity
  that can read the parameter group.)
- **The RDS Proxy is giving no pooling benefit and is amplifying cost.** It opened **457** connections
  for a write needing 36, logged **536** session-pinning warnings — meaning it could not multiplex at
  all, so every connection cost a real Aurora backend — and each session reset issues a
  `DEALLOCATE ALL`, which is why that statement is the instance's 2nd-heaviest at 7.61 AAS. Either fix
  the pinning cause or bypass the proxy for this writer.
- **Observability, cheap and decisive:** enable the `postgresql` log export on
  `stg-pg-cybi-aurora-cluster` (it is not exported today) and guard the `finally` rollback so the real
  exception survives. Between them they would have answered this in minutes.

---

## 9. What could not be measured

| gap | risk it leaves |
|---|---|
| **The Mongo run document was never read** — Atlas rejects the operator's IAM identity (`AuthenticationFailed`, code 18). Run identity is log-derived (unambiguous: correlation id in the trigger payload and every run line). | The stored status / `partialSuccess` metadata is unverified. Low risk — S3 and ISB logs agree. |
| **STG's live catalog was not read** (read-only constraint). The 11-index list is from `cybi-db-models` HEAD (2026-05-06). | A hand-added or post-dated index would not appear. Index cost could be higher, not lower. |
| **Whether any exposure rows committed, and how many.** 0 tasks reported success, but `tup_inserted` ran 694–5,157 rows/s and the INSERT held 109.54 AAS *after* the job aborted. | With `append` + 3 retries this is a **duplicate-row** question. Needs a `COUNT(*)` for this client/instance. |
| **Postgres server parameters and the server error log.** `DescribeDBParameters` AccessDenied; no PG log group exists for this cluster. | `statement_timeout`, `idle_in_transaction_session_timeout`, `work_mem`, `max_connections` all unknown — several candidate causes of the 22.6 s floor stay open. |
| **RDS Proxy configuration.** `DescribeDBProxies` AccessDenied. | `ConnectionBorrowTimeout` / `MaxConnectionsPercent` are unknown, yet the proxy is in the failure path and one sibling error names its borrow timeout. |
| **The exact tuple count per statement.** PI truncates at 4,095 chars (≥38 tuples visible). | The 128 figure rests on pgjdbc 42.3.6 source + the screenshot's ratio, not on a direct read of the wire. |
| **The mechanism behind PI's 152 AAS "CPU" at 68 % host CPU.** | Labelled speculation. Does not change the volume or shape conclusions. |
| **The 22.6 s failure floor** is unexplained by any log line. | The trigger of the *first* failure is inferred, not proven. The retry storm and the payload size are proven independently of it. |
| Glue log retention is **3 days** — this evidence expires 2026-08-22. | Re-verification after that date is impossible. |
| **28-asset discrepancy** (17,984 reported vs 17,956 staged). | Immaterial to volume. |
