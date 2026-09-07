# Decisions

- **D1**: Deliverable is a diagnostic report. No code change, no deploy, no job trigger in this task.
- **D2**: Investigate from four independent surfaces and reconcile them, rather than reasoning from the
  screenshot: (a) Mongo run identity, (b) S3/collector published volume, (c) Glue job run + logs,
  (d) repo write path. A conclusion supported by only one surface is reported as unconfirmed.
- **D3**: Separate **volume** (row count reaching Postgres) from **shape** (statement form, batch size,
  writer concurrency, per-row index cost) and attribute the observed AAS to each. Reporting a single
  undifferentiated "too much load" is a failed deliverable.
- **D4**: Anchor every timestamp to UTC before any two clocks are compared, and state the conversion
  in the report.
- **D5**: Duplication in S3 is measured by strided sampling across the full key space plus at least one
  fully-read shard — never from a contiguous prefix.
- **D6**: Proceeding on unverified: the PI axis is IDT, so the event is 12:58-13:07 UTC. If wrong, the
  Glue run correlation is wrong and every number attributed to this run is wrong — so this is
  confirmed first, in S1, before any counting.
- **D7**: Proceeding on unverified: STG's deployed wheel contains the correlated Tenable lane. If wrong,
  the code path analysed is not the code path that ran, and the fan-out arithmetic is void. Confirmed
  in S3/S4 against the deployed wheel and the run's input shape.
- **D8**: If AWS credentials are expired, stop and ask the operator; do not substitute repo-only
  reasoning for live evidence and do not present the result as measured.
- **D9**: Report the fix direction (what a fix would target and its rough cost) but do not design or
  implement it — that is the next task's contract.
- **D10**: `batchsize` is NOT a lever on statement width. pgjdbc caps a rewritten multi-row `INSERT` at
  **128 `VALUES` tuples** (`highestBlockCount = 128` in `PgPreparedStatement.transformQueriesAndParameters()`,
  re-enforced in `BatchedQuery.deriveForMultiBatch`), in every released 42.x including the 42.3.6 that
  AWS documents for Glue 4.0. Any statement of the form "raise/lower batchsize to change rows per INSERT"
  is void. `batchsize` controls only flush frequency, buffered bind memory, and idle-in-transaction gap
  length. Escaping 128 requires `COPY`, not configuration. Source: `research/pgjdbc-batch-rewrite.md` §1.
- **D11**: Never diagnose from a "This connection has been closed." (SQLSTATE 08003). It is thrown only by
  `checkClosed()` on an already-dead connection, and Spark's `JdbcUtils.savePartition` `finally` calls
  `conn.rollback()` unguarded — so that 08003 *replaces and destroys* the real exception. Diagnosis must
  use the FIRST exception in the executor stderr, plus the PostgreSQL server log for the same instant.
  Source: `research/pgjdbc-batch-rewrite.md` §7.
