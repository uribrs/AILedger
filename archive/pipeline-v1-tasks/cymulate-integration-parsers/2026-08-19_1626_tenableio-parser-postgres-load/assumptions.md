# Assumptions

Entries are OPEN unless a downstream skill resolved them with evidence (A11: VALIDATED by researcher, 2026-08-19).

## Prior Art

Recall tags: `cymulate-integration-parsers`, `tenable`, `postgres-load`, `batching`.
Matched 4 rows in `~/Dev/AILedger/lessons.md` (11 rows total); none superseded or retracted; 2 judged
relevant and seeded below. No ledger row exists for this repo or for parser-side Postgres load — this
is the first task in that space.

- **A1 — OPEN** — The ISB Tenable.io collector has no checkpoint/resume path, so mid-run replay cannot
  duplicate findings in S3 for this run. source: lessons.md#L-1e94a68a (researcher, 2026-04-29).
  Caveat carried forward: that row concerns **AgentService**, not the ISB TenableIo collector — it may
  not transfer at all. Duplication must be measured in S3, not inferred from it.
- **A2 — OPEN** — Tenable.io's publish pipeline is a sequential pump with unordered parallel fan-outs
  over order-independent sub-items; no ordered parallel-collect/serial-publish stage exists.
  source: lessons.md#L-98cb5412; TenableIoVulnPhase.cs:429/215-224 (recon, 2026-08-18).

Local prior art (previous task in this repo, `ai/active/2026-07-08_1739_tenableio-correlated-parser`) —
not ledger rows, carried as OPEN because they are stale operational observations:

- **A3 — OPEN** — The correlated lane is implemented inside the hand-written delegate
  (`parsers/deprecated/tenable/tenableAssetsAndFindingsCorrelated.py`), which derives two split lanes
  from each envelope and runs the split path's uuid-correlation machinery verbatim; the YAML spec is a
  delegate shell with `skip_post_shaping: true`. source: prior task execution_notes.md D7 (executor,
  2026-07-08).
- **A4 — OPEN** — On the 2026-07-06 real-data sample (2,168 files / 17 GB) there were **zero**
  multi-chunk envelopes, i.e. no host carried >2,000 findings on that tenant. source: prior task
  execution_notes.md "Real-data facts" (executor, 2026-07-08). Different tenant and 6 weeks stale —
  must be re-measured for this run.
- **A5 — OPEN** — `BaseParser.post_process` explodes `cve_ids` to one finding row per CVE and drops
  `type=vulnerability` findings with empty `cve_ids`, so the exposure row count is
  sum-over-findings(len(cve_ids)) for CVE-bearing findings, not the finding count.
  source: prior task execution_notes.md; CLAUDE.md "Automatic post-processing" (executor, 2026-07-08).
- **A6 — OPEN** — The correlated code path is present in the wheel STG actually runs (the correlated
  commit appears reachable from `origin/master`), so the STG run at 2026-08-19 executed the correlated
  lane rather than the split or legacy-hydrated lane. source: `git branch -a --contains 1e8a880` lists
  `remotes/origin/master` (recon, 2026-08-19). Must be confirmed against the deployed wheel and the
  Glue run's actual input shape.

## Task-specific assumptions

- **A7 — OPEN** — The PI graph's 15:44-16:14 axis is **IDT (UTC+3)**, so the load event is
  12:58-13:07 UTC on 2026-08-19, ending ~12 minutes before contract time.
- **A8 — OPEN** — Mongo `_id` `6a8593320f3d4281bd8a0df0` identifies the collector run / collection
  document in the **stg** cluster, and resolves to exactly one client + instance + integration whose
  parser Glue run is the writer behind the INSERT.
- **A9 — OPEN** — The INSERT in Top SQL is issued by the Glue parser job (the Spark JDBC writer), not
  by another service writing the same table.
- **A10 — OPEN** — `dal/dbManager.py` performs the exposures write through a Spark JDBC write whose
  parallelism equals the DataFrame's partition count, so concurrent Postgres backends = partition
  count, not 1.
- **A11 — VALIDATED** — Writes ARE multi-row batched, and 128 rows/statement is a **hard pgjdbc cap**,
  not a coincidence of `batchsize`. `PgPreparedStatement.transformQueriesAndParameters()` hard-codes
  `final int highestBlockCount = 128;` and rounds down to a power of two; `BatchedQuery.deriveForMultiBatch`
  rejects any block > 128. At 21 binds/row the 32766-parameter protocol ceiling would allow 1560 rows, so
  `min(1560, 128) = 128` — the cap binds by 12x. `executeBatch()` on 50,000 queued rows therefore emits
  392 statements (390 x 128, then 64, then 16) for a mean of 50000/392 = **127.55 rows/call**, matching the
  observed 127.5. `batchsize=50000` cannot widen the statement.
  actor: researcher. citation: pgjdbc REL42.3.6 `PgPreparedStatement.java:1678-1734` and
  `core/v3/BatchedQuery.java:47-68` (the version AWS documents for Glue 4.0 — Appendix B,
  https://docs.aws.amazon.com/glue/latest/dg/migrating-version-40.html); full derivation in
  `research/pgjdbc-batch-rewrite.md` §1.
- **A12 — OPEN** — 58.59 AAS on one statement at 117.43 ms/call implies ~499 statements in flight on
  average (58.59 / 0.11743), i.e. far more concurrent writers than the ~10 vCPUs can serve; the CPU
  wait class is then queueing on an oversubscribed instance rather than genuinely CPU-bound work.
- **A13 — OPEN** — `integration.parser_output_exposures` carries indexes and/or constraints whose
  per-row maintenance cost is a material share of the INSERT CPU; the table definition and its index
  list are readable without a DB write.
- **A14 — OPEN** — The ~280 AAS total peak is not explained by the exposures INSERT alone
  (58.59 AAS); the remainder is either other sessions of the same INSERT rolled up separately, or
  downstream consumers (EA Correlation-TP `detectChanged` at 8939.86 ms/call) reacting to the write.
- **A15 — OPEN** — Comparable prior runs of this parser (same tenant or another) are retrievable from
  Glue run history and/or PI so this run can be called anomalous or steady-state on evidence.

---

## Final Disposition

Written by the verifier pass (`review/verifier-1.md`, 2026-08-19). Compared against what actually
landed — the five evidence packets and `report.md` — not against the plan's intentions. NEVER-TESTED
is the default; VALIDATED / REJECTED only with a specific citation. The entries above are unchanged.

| id | status | citation | actor |
|----|--------|----------|-------|
| A1 | **REJECTED** (premise) / conclusion VALIDATED by another route | Premise "no checkpoint/resume path" contradicted: `evidence-isb-collector.md` §5 quotes `11:27:50.225Z Adapter declined resume from checkpoint … Page: 0. Starting fresh.` plus `Resume cancelled. Vendor=Tenable.io` on 2026-08-18 at 12:06:16Z/12:51:24Z/13:43:43Z/13:55:08Z — a resume path exists and was declined. The AgentService caveat never transferred; it was made moot. "No duplicate findings in S3" independently VALIDATED by `evidence-s3-collector.md` (0 duplicate `finding_id` over 1,950,862; 0 byte-identical duplicate lines; factor 1.0000, full read). | W3 + W1 |
| A2 | **NEVER-TESTED** | No packet examined `TenableIoVulnPhase.cs` or the publish pump's concurrency model. W3 measured publish *output*, not architecture. No citation moves it. | none |
| A3 | **VALIDATED** | `internal-recon.md`: `specs/tenable-assets-findings.yaml:29-35` (delegate shell, `skip_post_shaping: true`), `delegate_hook.py:47-48`, `tenableAssetsAndFindingsCorrelated.py:74-77`, `:96-99`. Runtime: driver log 12:47:56.166 `Tenable IO correlated parser: derived assets and findings lanes from the envelope frame`. | recon + W2 |
| A4 | **VALIDATED for this run** | `evidence-s3-collector.md`, full read of all 2,112 files: `chunk` = `{0: 17984}`, `isLastChunk` = `{True: 17984}`, hosts with >1 chunk record = **0**, `findingsInChunk` == `len(findings)` on all 17,984. Re-measured on this tenant, not carried from 2026-07-06. | W1 |
| A5 | **VALIDATED** | `base_parser.py:295-318` (explode), `:291-303` (CVE-less drop), `tenableAssetsAndFindings.py:344`, `:369-370`. Numeric closure: Σ`len(plugin.cve)` = 5,858,751 (W1) == driver `DataFrame row count: 5858751` @12:57:04.041 (W2). | recon + W1 + W2 |
| A6 | **VALIDATED — by runtime log, not by the wheel** | Wheel NOT inspectable: `GetObject` on `s3://cym-glue/stg/libs/parsers.whl` → **403** (`execution_notes.md`). Merge→build→mtime chain is circumstantial alone. Closed by driver log 12:47:55.329 `Tenable IO parser façade: resolved mode=hydrated (correlated envelope)` + 12:47:56.166 correlated-lane line, which recon maps to `tenableAssetsAndFindings.py:426-427` / `tenableAssetsAndFindingsCorrelated.py:80-82` — emitted only by the correlated lane. | W2 + recon |
| A7 | **VALIDATED** | `run-identity.md`: `get-job-runs` `StartedOn 2026-08-19T15:41:25.406000+03:00`. `evidence-glue-db.md` §Method: Glue log prefix is UTC, agrees with CloudWatch `@timestamp` to the ms. Spike = 12:58–13:07Z. | W0 + W2 |
| A8 | **SPLIT — identity VALIDATED from ISB; Mongo clause NEVER-TESTED** | `evidence-isb-collector.md` §1: trigger payload carries the correlationId with `instanceId 21f14fe6-…`, `clientID 613f3842…`, `integrationSettingId 0c90af0f-…`, `StorageUrl` == the Glue `ZIP_FILE_KEY`. **No Mongo document read** — `pymongo` → `OperationFailure: Authentication failed., code 18` (Atlas IAM auth, role not a mapped Atlas user). | W3 |
| A9 | **VALIDATED** | Proxy log client IPs = the nine Glue executor IPs + driver; PI `db.user` `cybi` 154.30 of 155.27 AAS; PI statement's 20-column list matches Spark `JdbcUtils.getInsertStatement` order; recon's `grep -rni "insert into"` finds one hit, in `elasticDAL.py`. | W2 + recon |
| A10 | **REJECTED as worded** | Partitions = 100, but measured max concurrent write tasks = **37 (36 steady)**, three independent ways (9 `Registered executor`; `spark.executor.cores=4` / `spark.default.parallelism=36`; task-start/loss replay per stage 68/69/70). Concurrency = `min(partitions, task slots)`. "Not 1" survives. | W2 |
| A11 | **VALIDATED** | pgjdbc `REL42.3.6 PgPreparedStatement.java:1678-1734` `highestBlockCount = 128` + `Integer.highestOneBit`; `BatchedQuery.java:47-68`; `PARAM_LIMIT` = 32766. Verifier re-derived the split from the quoted algorithm: 390 full blocks, `bitCount(50000 % 128) = bitCount(80) = 2` partials (64, 16), total **392**, mean **127.55**. Cap present in every released 42.x. Version grounding is AWS Glue 4.0 Appendix B, **not** a read of the running classpath (researcher's unverified item #1). Text says 21 binds/row; W2 measured **20** — `min(32766/20, 128) = 128` either way. | researcher + verifier |
| A12 | **SPLIT — first clause REJECTED, second VALIDATED; neither disposed in the report** | Arithmetic right (58.59/0.11743 = 498.9) but "≈499 concurrent writers" contradicted: **37** concurrent write tasks, **457** proxy connections, **570** peak `DatabaseConnections`. PI AAS is not a count of statements in flight. Second clause VALIDATED: PI CPU 152.10 AAS while host CPU peaked 68.71 % (CW) / 71.10 % (PI) — runnable-but-not-progressing. | W2 |
| A13 | **SPLIT — readability VALIDATED, materiality NEVER-TESTED** | `db-schema-authoritative.md`: 11 B-trees (3 on random v4 UUIDs), no triggers, from `cybi-db-models` prisma + `migrations/20260330120000_…` + `…20260505150010_…`. Two limits: (a) migration repo at local HEAD `98b95949` (2026-05-06), **not STG's live catalog** — its own Caveat says so, W2 called the catalog the largest remaining gap; (b) the "material share of INSERT CPU" claim was **never measured** — no per-row breakdown between index maintenance, `jsonb_in` and TOAST exists in any packet. | main thread (list) / nobody (cost share) |
| A14 | **VALIDATED, premise corrected upward** | Peak is **327.46 AAS at 13:04Z** (baseline ~3, mean 58.62), not ~280. Remainder is the **same INSERT**: 5 tokenized variants = **126.348 AAS** = 84 % of the 150.98 top-25 total (12:56–13:10Z), plus **109.54 AAS on that INSERT during 13:04–13:09Z after the abort**; `tup_inserted` peaks 5,157 rows/s at 13:07Z. The downstream-consumer hypothesis is **refuted** — all EA services ~24 AAS. `report.md` omits 327.46 entirely. | W2 |
| A15 | **VALIDATED, with a twist** | Retrievable: 3,000 job runs since 2026-07-29 (FAILED 2,709 / SUCCEEDED 290 / STOPPED 1), 2,900 workflow runs, 25 h graph pass, SUCCEEDED band 195–524 s. Twist: target instance `21f14fe6` has **exactly one run in 20 days**, so no same-instance baseline exists; comparison built from sibling instances and other vendors. 14-day *log* history unobtainable (stg Elastic timed out >~5 h windows); W3 substituted S3 prefix aggregation, labelled. | W2 + W3 |

**Counts:** VALIDATED 7 (A3, A4, A5, A6, A7, A9, A11) · VALIDATED-with-correction 2 (A14, A15) ·
SPLIT 3 (A8, A12, A13) · REJECTED 2 (A1 premise, A10) · NEVER-TESTED 1 (A2).

**Carried forward as still-open, for the next contract:** A2 (untested); A8's Mongo clause; A12's
disposition never reached the report; A13's index-cost share and the live-catalog read; whether any
rows committed and whether the three `append` retries duplicated them.
