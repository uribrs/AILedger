# Research — pgjdbc batched-insert rewrite and its cost model

Task type: **bug investigation** (external-behavior resolution for assumption A11).
Documentation status: **public official docs available** — plus full driver and Spark source. Every
load-bearing claim below rests on driver source at the exact version AWS documents for Glue 4.0.

---

## Summary (the answers, one line each)

1. **The 128 cap is real and it is the binding limit.** `PgPreparedStatement.transformQueriesAndParameters()`
   hard-codes `final int highestBlockCount = 128;` and rounds down to a power of two, so a rewritten
   `INSERT` carries **at most 128 `VALUES` tuples** regardless of `batchsize`. Your prior is confirmed
   verbatim, including the power-of-two rounding and the prepared-statement-cache rationale.
2. **The 32767/65535 bind-parameter limit never engages here.** At 21 binds/row it would permit 1560
   rows; `min(1560, 128) = 128`. The cap wins by a factor of 12.
3. **`executeBatch()` on 50,000 rows emits 392 SQL statements**: 390 × 128 tuples, then one 64-tuple
   and one 16-tuple statement. Mean = 50000/392 = **127.55 rows per call** — which is your observed
   127.5 to three significant figures. A11 is confirmed by construction, not by coincidence.
4. **Yes, Spark forwards the property.** `JDBCOptions.asConnectionProperties` reads
   `parameters.originalMap`, which preserves the author's casing; only Spark's *own* option names are
   lowercased, and only for the filter test. `reWriteBatchedInserts` reaches `Driver.connect` intact —
   and it must, because `PGProperty` looks it up with a case-sensitive `Properties.getProperty(name)`.
5. **Glue 4.0 ships pgjdbc 42.3.6** — documented by AWS in Appendix B of the 4.0 migration guide. Not
   a guess. That version contains the 128 cap.
6. **`batchsize=50000` buys nothing on the wire and costs real memory.** It does not widen the SQL
   statement. What it does control: how much bind data the executor buffers before any flush (50,000 ×
   21 encoded values), the transient doubling of that buffer while the rewrite copies parameters into
   392 new `ParameterList`s, and the length of the single longest *idle-in-transaction* gap.
7. **"This connection has been closed." is almost never the primary failure.** It is thrown only by
   `checkClosed()` on an *already-dead* connection — and Spark's `savePartition` `finally` block calls
   `conn.rollback()` **unguarded**, so that secondary exception replaces and destroys the original
   cause. To find the real reason you must look at the *first* exception in the executor log, not the
   one Spark reports.

The most defensible next action is at the bottom.

---

## 1. reWriteBatchedInserts mechanics and the VALUES-tuple cap

### Where the rewrite happens

`Statement.executeBatch()` → `PgStatement.executeBatch()` → `transformQueriesAndParameters()`. In
**pgjdbc 42.3.6** — the version on Glue 4.0 — that method is at
`pgjdbc/src/main/java/org/postgresql/jdbc/PgPreparedStatement.java:1678`:

```java
  protected void transformQueriesAndParameters() throws SQLException {
    ArrayList<@Nullable ParameterList> batchParameters = this.batchParameters;
    if (batchParameters == null || batchParameters.size() <= 1
        || !(preparedQuery.query instanceof BatchedQuery)) {
      return;
    }
    BatchedQuery originalQuery = (BatchedQuery) preparedQuery.query;
    // Single query cannot have more than {@link Short#MAX_VALUE} binds, thus
    // the number of multi-values blocks should be capped.
    // Typically, it does not make much sense to batch more than 128 rows: performance
    // does not improve much after updating 128 statements with 1 multi-valued one, thus
    // we cap maximum batch size and split there.
    final int bindCount = originalQuery.getBindCount();
    final int highestBlockCount = 128;
    final int maxValueBlocks = bindCount == 0 ? 1024 /* if no binds, use 1024 rows */
        : Integer.highestOneBit( // deriveForMultiBatch supports powers of two only
            Math.min(Math.max(1, (Short.MAX_VALUE - 1) / bindCount), highestBlockCount));
    int unprocessedBatchCount = batchParameters.size();
    final int fullValueBlocksCount = unprocessedBatchCount / maxValueBlocks;
    final int partialValueBlocksCount = Integer.bitCount(unprocessedBatchCount % maxValueBlocks);
    final int count = fullValueBlocksCount + partialValueBlocksCount;
    ...
    for (int i = 0; i < count; i++) {
      int valueBlock;
      if (unprocessedBatchCount >= maxValueBlocks) {
        valueBlock = maxValueBlocks;
      } else {
        valueBlock = Integer.highestOneBit(unprocessedBatchCount);
      }
      // Find appropriate batch for block count.
      BatchedQuery bq = originalQuery.deriveForMultiBatch(valueBlock);
      ParameterList newPl = bq.createParameterList();
      for (int j = 0; j < valueBlock; j++) {
        ParameterList pl = batchParameters.get(offset++);
        if (pl != null) {
          newPl.appendAll(pl);
        }
      }
      newBatchStatements.add(bq);
      newBatchParameters.add(newPl);
      unprocessedBatchCount -= valueBlock;
    }
    this.batchStatements = newBatchStatements;
    this.batchParameters = newBatchParameters;
  }
```

Permalink: <https://github.com/pgjdbc/pgjdbc/blob/REL42.3.6/pgjdbc/src/main/java/org/postgresql/jdbc/PgPreparedStatement.java#L1678-L1734>

### Is there a cap? Yes — 128, and it is enforced twice

**Your prior is confirmed, verbatim and in both places you predicted.** The formula is:

```
maxValueBlocks = highestOneBit( min( max(1, PARAM_LIMIT / bindCount), 128 ) )
```

where `PARAM_LIMIT` is `Short.MAX_VALUE - 1` = **32766** in 42.3.6. (In 42.5.0+ it became
`maximumNumberOfParameters()`, which returns **65535** in extended mode and `Integer.MAX_VALUE` in
`preferQueryMode=simple` — see §"Version drift" below. The 128 ceiling is unchanged in every released
42.x.)

`Integer.highestOneBit` is the power-of-two round-down you predicted, and the comment says why:
`deriveForMultiBatch supports powers of two only`. The second enforcement point is
`pgjdbc/src/main/java/org/postgresql/core/v3/BatchedQuery.java:47-68`:

```java
  public BatchedQuery deriveForMultiBatch(int valueBlock) {
    if (getBatchSize() != 1) {
      throw new IllegalStateException("Only the original decorator can be derived.");
    }
    if (valueBlock == 1) {
      return this;
    }
    int index = Integer.numberOfTrailingZeros(valueBlock) - 1;
    if (valueBlock > 128 || valueBlock != (1 << (index + 1))) {
      throw new IllegalArgumentException(
          "Expected value block should be a power of 2 smaller or equal to 128. Actual block is "
              + valueBlock);
    }
    if (blocks == null) {
      blocks = new BatchedQuery[7];
    }
```

Permalink: <https://github.com/pgjdbc/pgjdbc/blob/REL42.3.6/pgjdbc/src/main/java/org/postgresql/core/v3/BatchedQuery.java#L47-L68>

**The prepared-statement-cache rationale you predicted is also confirmed** — `blocks` is a
fixed-size `BatchedQuery[7]` array, one slot per power of two from 2 to 128. So a given original
`INSERT` can only ever produce **7 derived SQL texts plus the 1-row original = 8 distinct statements**
per connection. pgjdbc `master` now states this explicitly in the field's javadoc, having raised the
ceiling to `1 << 15`:

```java
  /**
   * ... distinct derived statements at {@code log2(MAX_VALUE_BLOCK)}.
   */
  public static final int MAX_VALUE_BLOCK = 1 << 15;
```
(`BatchedQuery.java:29-31` on `master`.) That javadoc is the maintainers stating your inferred reason
in their own words. It is **not** in any released 42.x — see §"Version drift".

### Interaction with the 32767 / 65535 protocol limit

The wire protocol encodes the parameter count as an `int16` in the `Bind` message, so a single
extended-protocol statement cannot carry more than 65535 (pre-42.5: pgjdbc conservatively used
`Short.MAX_VALUE - 1` = 32766) bind values. The code takes `min()` of the two ceilings, so:

| bindCount/row | protocol-derived row limit (42.3.6) | `min(.., 128)` | after `highestOneBit` |
|---|---|---|---|
| 20 | 32766/20 = 1638 | 128 | **128** |
| 21 | 32766/21 = 1560 | 128 | **128** |

Your arithmetic (~1560 rows) is right, and it is exactly why the protocol limit is irrelevant here:
**the 128 cap binds by ~12×.** The protocol limit only becomes the operative constraint above 256
binds per row (32766/256 = 127 → `highestOneBit` = 64), i.e. for tables far wider than 21 columns.

Actual parameter count per emitted statement: 21 × 128 = **2688** — 8% of the protocol limit.

### The arithmetic: 50,000 queued rows, 21 binds

```
bindCount              = 21
maxValueBlocks         = highestOneBit(min(max(1, 32766/21), 128))
                       = highestOneBit(min(1560, 128)) = highestOneBit(128) = 128

fullValueBlocksCount   = 50000 / 128            = 390
50000 % 128            = 50000 - 49920          = 80
partialValueBlocksCount= Integer.bitCount(80)   = bitCount(0b1010000) = 2
count                  = 390 + 2                = 392 statements

loop emits: 390 x 128 tuples  = 49920 rows
            then unprocessed=80  -> highestOneBit(80) = 64 tuples
            then unprocessed=16  -> highestOneBit(16) = 16 tuples
            total                                     = 50000 rows  (check)

mean rows per INSERT call = 50000 / 392 = 127.551
```

**Observed: ~127.5 rows per call. Predicted: 127.551.** A11 is confirmed — and confirmed as a *cap*,
not as a coincidence. Note also what it rules out: `batchsize=50000` did *not* produce large multi-row
statements, and it never could have.

One measurement nuance worth stating: the 128-, 64- and 16-tuple statements have **different SQL text**
and therefore different `pg_stat_statements` queryids. A digest showing exactly 127.5 is consistent
either with Performance Insights rolling several tuple-widths into one row, or with partitions whose
row counts differ from 50,000 so the tail blocks vary. Either way the conclusion is the same: the
statement width is pinned at 128 and cannot be raised by configuration on 42.3.6.

### Network round trips are *not* 392

This matters for the cost model and is easy to get wrong. pgjdbc pipelines the batch — it writes many
`Bind`/`Execute` pairs before reading anything — and forces a `Sync` only when it estimates the
server→client buffer is filling. `QueryExecutorImpl.java:502-503` (42.3.6):

```java
  private static final int MAX_BUFFERED_RECV_BYTES = 64000;
  private static final int NODATA_QUERY_RESPONSE_SIZE_BYTES = 250;
```

and `flushIfDeadlockRisk()`:

```java
    estimatedReceiveBufferBytes += NODATA_QUERY_RESPONSE_SIZE_BYTES;
    ...
    if (disallowBatching || estimatedReceiveBufferBytes >= MAX_BUFFERED_RECV_BYTES) {
      LOGGER.log(Level.FINEST, "Forcing Sync, receive buffer full or batching disallowed");
      sendSync();
      processResults(resultHandler, flags);
      estimatedReceiveBufferBytes = 0;
```

64000 / 250 = **256 statements per forced `Sync`**. So 392 statements ⇒ one forced round-trip stall
after #256, plus the final `sendSync()` at the end of `execute()` = **2 client/server stalls per
`executeBatch()`**, not 392. Per-statement latency (your 117.43 ms) is therefore *server execution
time as pg_stat_statements measures it*, and is largely overlapped with the driver's writing; it is
**not** 117 ms of round-trip.

### Eligibility: is Spark's INSERT even rewritable?

Yes. `Parser.java` sets `isCurrentReWriteCompatible = keyWordCount == 0` only when the statement
*starts* with `INSERT` ("Only allow rewrite for insert command starting with the insert keyword. Else,
too many risks of wrong interpretation."), requires a top-level `VALUES` with a located closing paren,
and disqualifies anything where a bind sits after the `VALUES` close paren (which excludes
`ON CONFLICT ... = ?`). Spark generates exactly the eligible shape —
`JdbcUtils.getInsertStatement` (Spark 3.3.0, `JdbcUtils.scala`):

```scala
    val placeholders = rddSchema.fields.map(_ => "?").mkString(",")
    s"INSERT INTO $table ($columns) VALUES ($placeholders)"
```

No `RETURNING`, no CTE, no upsert. Rewrite applies. One side effect: with a rewritten batch the
per-row update counts become `Statement.SUCCESS_NO_INFO` (`BatchResultHandler.java:249`). Spark
discards `executeBatch()`'s return value, so this is harmless here — but it would break any code that
audits per-row affected counts.

### Version drift (do not apply `master` to this job)

| | released 42.2.x – 42.7.x | pgjdbc `master` (unreleased as of this writing) |
|---|---|---|
| tuple cap | `highestBlockCount = 128` | `BatchedQuery.MAX_VALUE_BLOCK = 1 << 15` (32768) |
| tunable | none | `reWriteBatchedInsertsSize` |
| param limit | 32766 (≤42.3), 65535 (≥42.5) | `maximumNumberOfParameters()` |

Verified by fetching `PgPreparedStatement.java` and `BatchedQuery.java` at tags `REL42.2.27`,
`REL42.3.6`, `REL42.5.4`, `REL42.7.7` and `master`: all four release tags contain
`final int highestBlockCount = 128;`. **There is no released pgjdbc in which the cap is
configurable.** Raising rows-per-statement is not a config change; it is a driver upgrade to a version
that does not exist yet, or a different write mechanism (`COPY`).

---

## 2. Does Spark forward the property?

Yes, with camelCase preserved, and the source is unambiguous at every hop. Spark 3.3.0:

**Hop 1 — `DataFrameWriter.jdbc`** (`sql/core/.../DataFrameWriter.scala:750-758`):
```scala
  def jdbc(url: String, table: String, connectionProperties: Properties): Unit = {
    ...
    // connectionProperties should override settings in extraOptions.
    this.extraOptions ++= connectionProperties.asScala
    this.extraOptions ++= Seq("url" -> url, "dbtable" -> table)
    format("jdbc").save()
  }
```

**Hop 2 — `CaseInsensitiveMap` keeps the original key.** `extraOptions` is a `CaseInsensitiveMap`, and
its `+` retains the supplied pair verbatim
(`sql/catalyst/src/main/scala-2.12/.../CaseInsensitiveMap.scala`):
```scala
class CaseInsensitiveMap[T] private (val originalMap: Map[String, T]) extends Map[String, T] {
  val keyLowerCasedMap = originalMap.map(kv => kv.copy(_1 = kv._1.toLowerCase(Locale.ROOT)))
  override def get(k: String): Option[T] = keyLowerCasedMap.get(k.toLowerCase(Locale.ROOT))
  override def +[B1 >: T](kv: (String, B1)): CaseInsensitiveMap[B1] = {
    new CaseInsensitiveMap(originalMap.filter(!_._1.equalsIgnoreCase(kv._1)) + kv)
  }
```
Lookups lowercase the *query*, not the stored key. (Note `override def iterator` **does** yield
lowercased keys — which is exactly why the next hop avoids it.)

**Hop 3 — `saveToV1Source` passes `originalMap`, explicitly** (`DataFrameWriter.scala:384-391`):
```scala
        options = optionsWithPath.originalMap).planForWriting(mode, df.logicalPlan)
```

**Hop 4 — `JDBCOptions.asConnectionProperties`** (`JDBCOptions.scala:61-66`):
```scala
  val asConnectionProperties: Properties = {
    val properties = new Properties()
    parameters.originalMap.filterKeys(key => !jdbcOptionNames(key.toLowerCase(Locale.ROOT)))
      .foreach { case (k, v) => properties.setProperty(k, v) }
    properties
  }
```
This is the precise answer to your sub-question. **Lowercasing is applied only to the key used for the
`jdbcOptionNames` membership test**; the key actually written into the `Properties` is `k` from
`originalMap`, unmodified. `jdbcOptionNames` is populated by `newOption(...)`
(`JDBCOptions.scala:251-254`) and contains only Spark's own options.

Consequences for your exact config:
- `batchsize`, `driver` → **are** Spark option names (`JDBC_BATCH_INSERT_SIZE = newOption("batchsize")`,
  `JDBC_DRIVER_CLASS`), so they are filtered *out* of the connection properties and consumed by Spark.
- `reWriteBatchedInserts`, `stringtype`, `socketTimeout`, `connectTimeout`, `tcpKeepAlive` → not Spark
  options, so they are forwarded as-is.

**Hop 5 — `BasicConnectionProvider.getConnection`** (`connection/BasicConnectionProvider.scala:43-50`):
```scala
    jdbcOptions.asConnectionProperties.asScala.foreach { case(k, v) => properties.put(k, v) }
    logDebug(s"JDBC connection initiated with URL: ${jdbcOptions.url} and properties: $properties")
    driver.connect(jdbcOptions.url, properties)
```
(`Driver.connect`, not `DriverManager.getConnection` — irrelevant to casing, but worth being precise.)

**Why exact casing matters.** `PGProperty` does a case-sensitive lookup
(`PGProperty.java:924-926` and `:1084-1090`):
```java
  public @Nullable String getOrDefault(Properties properties) {
    return properties.getProperty(name, defaultValue);
  }
```
with `REWRITE_BATCHED_INSERTS("reWriteBatchedInserts", "false", ...)` at `PGProperty.java:591-594`.
A key spelled `rewritebatchedinserts` would be **silently ignored** and default to `false` — no
warning, no error, just row-at-a-time inserts. Since the observed 127.5 rows/call proves the rewrite
*is* active, the property is demonstrably arriving correctly; but this is the failure mode to check
first if anyone ever "cleans up" the option map.

Passing it as a *property* rather than on the URL is equivalent: `Driver.parseURL` merges URL query
parameters into the same `Properties` object.

---

## 3. pgjdbc version on Glue 4.0

**AWS documents it: 42.3.6.** From *Migrating AWS Glue for Spark jobs to AWS Glue version 4.0*,
"Appendix B: JDBC driver upgrades":

| Driver | JDBC driver version in past AWS Glue versions | JDBC driver version in AWS Glue 3.0 | JDBC driver version in AWS Glue 4.0 |
|---|---|---|---|
| PostgreSQL | 42.1.0 | 42.2.18 | **42.3.6** |

Same page, Appendix A confirms the rest of the environment: Spark `3.3.0-amzn-1`, Python `3.10`,
Scala `2.12`. Source: <https://docs.aws.amazon.com/glue/latest/dg/migrating-version-40.html>
(official AWS documentation, tier 1).

Two caveats before treating this as settled for *your* job:

1. Glue 4.0 documents `--user-jars-first` behavior; if the job attaches its own `postgresql-*.jar`
   via `--extra-jars`, that jar can win the classpath race and the effective version is whatever was
   attached. The bundled version is the default, not a guarantee.
2. 42.3.6 uses the `Short.MAX_VALUE - 1` = 32766 parameter ceiling (not 65535). Immaterial at 21
   binds/row, but relevant if the table ever widens past ~256 columns.

**How to determine it from the job itself** (cheapest first):

```python
# in the Glue script — prints e.g. "PostgreSQL JDBC Driver 42.3.6"
print(spark.sparkContext._jvm.org.postgresql.Driver.getVersion())
```
`Driver.getVersion()` is a public static returning `DriverInfo.DRIVER_FULL_NAME`
(`pgjdbc/src/main/java/org/postgresql/Driver.java:466-468`). It appears in the Glue driver log
(`/aws-glue/jobs/output`). Equivalents: `conn.getMetaData().getDriverVersion()` on an open
connection, or locating the jar with
`print(spark.sparkContext._jvm.java.lang.Class.forName("org.postgresql.Driver").getProtectionDomain().getCodeSource().getLocation())`.
The `BasicConnectionProvider` line that echoes the properties is `logDebug`, so it will not appear at
Glue's default log level.

---

## 4. Cost model: 128-row statements vs single-row vs one huge statement

First, the per-row cost that is **identical in all three shapes** — and it is the dominant one:

- **WAL**: PostgreSQL emits a WAL record per inserted heap tuple plus records for index inserts.
  Statement shape does not change WAL *volume*. It changes only the number of commit records, and
  Spark commits once per partition regardless (see §6).
- **Index maintenance**: one index-tuple insert per index per row, with page splits and possible
  buffer reads. `populate.html` §14.4.3: *"If you are loading a freshly created table, the fastest
  method is to create the table, bulk load the table's data using COPY, then create any indexes needed
  for the table."* This is per-row work that no batching shape avoids.
- **jsonb input parsing**: once per row per jsonb column (see §5).

What the shapes actually change:

| | (a) 392 × 128-row statements | (b) 50,000 single-row INSERTs | (c) one 50,000-row statement |
|---|---|---|---|
| Parse/plan on the server | 8 distinct SQL texts per connection, max; then reused as named prepared statements once `prepareThreshold=5` is passed. Effectively **zero** steady-state parse cost. | 1 SQL text, also server-prepared after 5 executions → also ~zero parse cost. | 1 parse, but of a statement with 1,050,000 parameters and a 50,000-element `VALUES` list — a very large parse tree and a one-off plan. |
| Executor invocations | 392 `Bind`+`Execute` pairs | 50,000 `Bind`+`Execute` pairs | 1 |
| Client/server stalls | 2 (`64000/250 = 256` statements per forced `Sync`, + final `Sync`) | ~196 (50000/256) | 1 |
| `pg_stat_statements` calls | 392 | 50,000 | 1 |
| Protocol legality at 21 binds | yes (2688 params) | yes | **no** — 1,050,000 > 65535. And unreachable anyway: pgjdbc caps at 128. |
| WAL volume | same | same | same |
| Index maintenance | same | same | same |

So (a) vs (b) is a **~127× reduction in per-statement server overhead** (executor start/stop, snapshot
handling, `pg_stat_statements` accounting, `Bind` parameter decode dispatch) and a ~98× reduction in
protocol stalls. The pgjdbc docs claim *"this provides 2-3x performance improvement"* for exactly this
transition, which is the honest magnitude once you account for the per-row work that does not shrink.

(c) is not an option and would not obviously be better if it were. PostgreSQL's own guidance stops
short of recommending giant statements — it recommends `COPY` instead. `populate.html` §14.4.2:

> *"Use COPY to load all the rows in one command, instead of using a series of INSERT commands. The
> COPY command is optimized for loading large numbers of rows; it is less flexible than INSERT, but
> incurs significantly less overhead for large data loads."*

and, decisively for this investigation:

> *"loading a large number of rows using COPY is almost always faster than using INSERT, even if
> PREPARE is used and multiple insertions are batched into a single transaction."*

**The practical reading of your numbers.** 117.43 ms per 128-row call = **0.917 ms per row**. Parse and
plan are already amortized to nothing, so that 0.917 ms is index maintenance + jsonb parsing + heap
insert + WAL + buffer I/O — the per-row costs that the 128-cap does not touch and that a larger
statement would not touch either. Widening the statement is therefore the *least* promising lever
available; `COPY` (which bypasses the executor entirely) and index/constraint reduction are the levers
with headroom.

**A consistency problem in the metrics, flagged not resolved.** `1.39 calls/sec × 0.11743 s` =
**0.163 average active sessions** for this digest — i.e. essentially an idle database with respect to
this INSERT. That contradicts `assumptions.md` A12, which records **58.59 AAS** on one statement at the
same 117.43 ms/call (which would require ~499 calls/sec, not 1.39). The two figures cannot both
describe the same statement over the same window. Before anyone tunes anything, reconcile which metric
covers which window — the answer decides whether the database is the bottleneck at all, or whether the
writer is idle 84% of the time waiting on something upstream.

---

## 5. `stringtype=unspecified` into `jsonb` — server-side parse cost

### What the setting does at the protocol level

`stringtype=unspecified` makes **every** `setString()` bind go out with OID 0 rather than
`Oid.VARCHAR` — not just the jsonb one. `PgConnection.java:327-340` sets `bindStringAsVarchar = false`
for `"unspecified"`, and `PgPreparedStatement.java:354-356`:

```java
  private int getStringType() {
    return (connection.getStringVarcharFlag() ? Oid.VARCHAR : Oid.UNSPECIFIED);
  }
```

Official description (pgjdbc `docs/content/documentation/use.md:274-279`):

> *"If `stringtype` is set to `VARCHAR` (the default), such parameters will be sent to the server as
> varchar parameters. If `stringtype` is set to `unspecified`, parameters will be sent to the server as
> untyped values, and the server will attempt to infer an appropriate type."*

This setting is **load-bearing, not incidental**: with the default `VARCHAR`, binding a JSON string
into a `jsonb` column fails outright (`column is of type jsonb but expression is of type character
varying`). So it cannot simply be removed.

### The per-row server cost

Type *resolution* happens at `Parse` time — once per SQL text, cached in the prepared statement. That
part is cheap and does not scale with rows. The **input function** is what runs per row:

> *"The `json` data type stores an exact copy of the input text, which processing functions must
> reparse on each execution; while `jsonb` data is stored in a decomposed binary format that makes it
> **slightly slower to input due to added conversion overhead**, but significantly faster to process,
> since no reparsing is needed."* — PostgreSQL docs, `datatype-json.html`

Plus validation work that `text` would not do:

> *"the input function for `jsonb` is stricter: it disallows Unicode escapes for characters that
> cannot be represented in the database encoding. The `jsonb` type also rejects ` ` ... and it
> insists that any use of Unicode surrogate pairs ... be correct."*

So per 128-tuple statement: **128 `jsonb_in` invocations** — lex the text, build the binary
`jsonb` tree, sort object keys, validate encoding. This is CPU on the database server, and it is
attributed to the INSERT statement in `pg_stat_statements`, i.e. it is inside your 117.43 ms.

### TOAST, if the jsonb values are large

`jsonb` has default storage `EXTENDED`. From `storage-toast.html`:

> *"The TOAST management code is triggered when a row value to be stored exceeds
> TOAST_TUPLE_THRESHOLD bytes (normally 2 kB). The code then compresses and/or moves field values
> out-of-line until the row is shorter than TOAST_TUPLE_TARGET bytes (also normally 2 kB, adjustable)
> or no further gains can be achieved."*

> *"**EXTENDED** — Allows both compression and out-of-line storage. This is the default for most
> TOAST-able data types. Compression will be attempted first, then out-of-line storage if the row is
> still too big."*

So for any row wider than ~2 kB you additionally pay, **per row**: an LZ-family compression pass over
the jsonb value, and if still oversized, chunked writes into the TOAST relation plus its index — each
chunk generating its own WAL. This is the single most likely explanation for a 0.917 ms/row insert
cost, and it is entirely invisible in the statement text. Measuring the mean serialized length of the
jsonb column is a cheap, decisive test (`SELECT avg(pg_column_size(<jsonb_col>)) ...`, or
`SELECT count(*) FROM pg_class WHERE relname = <the table's reltoastrelid name>` for whether TOAST is
being used at all).

Setting `ALTER TABLE ... ALTER COLUMN <col> SET STORAGE EXTERNAL` trades storage for CPU
(*"Use of EXTERNAL will make substring operations on wide `text` and `bytea` columns faster (at the
penalty of increased storage space)"*) — worth considering only if compression is shown to be the cost,
and it increases WAL volume, so it is not a free win.

---

## 6. One transaction per partition — consequences

### Confirming the shape

`isolationLevel` is not set, but Spark's default is **not** `NONE` (`JDBCOptions.scala:175-184`):

```scala
  val isolationLevel =
    parameters.getOrElse(JDBC_TXN_ISOLATION_LEVEL, "READ_UNCOMMITTED") match {
      case "NONE" => Connection.TRANSACTION_NONE
      case "READ_UNCOMMITTED" => Connection.TRANSACTION_READ_UNCOMMITTED
```

pgjdbc reports `supportsTransactions() == true` and accepts `READ_UNCOMMITTED`
(`PgDatabaseMetaData.java:1117-1127` returns `true` for all four JDBC levels), so
`finalIsolationLevel != TRANSACTION_NONE` and `savePartition` does
(`JdbcUtils.scala:679-681, 718-721`):

```scala
      if (supportsTransactions) {
        conn.setAutoCommit(false) // Everything in the same db transaction.
        conn.setTransactionIsolation(finalIsolationLevel)
      }
      ...
      if (supportsTransactions) {
        conn.commit()
      }
```

**One transaction per partition, committed once at the end.** PostgreSQL maps `READ UNCOMMITTED` onto
`READ COMMITTED`, so the isolation request is a no-op; only the transaction *shape* matters.

### Consequences, ranked by how much they should worry you

**Mostly good: WAL and commit flushes.** Batching commits is what PostgreSQL and AWS both recommend.
`populate.html` §14.4.1:

> *"When using multiple INSERTs, turn off autocommit and just do one commit at the end. ... Running
> all insertions in one transaction ensures that if the insertion of one row were to fail then the
> insertion of all rows inserted up to that point would be rolled back, so you won't be stuck with
> partially loaded data."*

Row-level WAL volume is unchanged; you save 50,000-ish commit records and, more importantly, the
`fsync`/replica-ack wait per commit. On RDS for PostgreSQL that wait surfaces as **`IO:WALWrite`**
(*"This event occurs when RDS for PostgreSQL is waiting for the write-ahead log (WAL) buffers to be
written to a WAL file"*). On **Aurora** PostgreSQL it is **`IO:XactSync`**, whose documented remedy is
literally *"To reduce the number of commits, combine statements into transaction blocks"* — which this
code already does. A correction to the brief's framing: `IO:XactSync` appears in the **Aurora**
PostgreSQL wait-event reference, not in the RDS for PostgreSQL list; if the instance is plain RDS
PostgreSQL, the event to look for is `IO:WALWrite`. **The commit shape is not the problem here.**

**Neutral: subtransactions.** pgjdbc's `autosave` defaults to `never` (`PGProperty.java:101-109`), and
`QueryExecutorImpl.sendAutomaticSavepoint()` returns immediately when
`getAutoSave() == AutoSave.NEVER`. So there are **no** `SAVEPOINT`s, no subtransaction XIDs, and no
exposure to `LWLock:SubtransSLRU` (the RDS wait event for subtransaction SLRU contention). Had anyone
set `autosave=always`, each of the 392 statements would get a savepoint and this would become a real
problem. Worth knowing; not currently active.

**Neutral: visibility map.** Rows are not marked all-visible until a `VACUUM`/autovacuum pass; this is
true regardless of transaction shape and does not make the load slower. It does mean immediately
following index-only scans will not be index-only.

**Genuinely concerning: lock retention and horizon retention.** The transaction holds
`RowExclusiveLock` on the table and its indexes from first insert until commit — for the entire
partition, potentially many minutes. That blocks `ALTER TABLE`, `TRUNCATE`, non-concurrent
`CREATE INDEX`, and `VACUUM FULL` for the whole duration, and it pins the global xmin horizon.
`routine-vacuuming.html` names this as a cleanup blocker to hunt down:

> *"End long-running open transactions. You can find these by checking `pg_stat_activity` for rows
> where `age(backend_xid)` or `age(backend_xmin)` is large. Such transactions should be committed or
> rolled back, or the session can be terminated using `pg_terminate_backend`."*

For an insert-only load this does not create dead tuples of its own, but it does prevent vacuum from
reclaiming dead tuples **anywhere in the cluster** for as long as the transaction runs.

**The failure mode that actually kills jobs: idle-in-transaction.** Between `executeBatch()` calls the
transaction is open and the backend is `idle in transaction` while the driver pulls the next 50,000
rows out of the Spark iterator (S3 reads, shuffle, deserialization, the CVE explode). From
`runtime-config-client.html`:

> *"**idle_in_transaction_session_timeout** — Terminate any session that has been idle (that is,
> waiting for a client query) within an open transaction for longer than the specified amount of time.
> ... Even when no significant locks are held, an open transaction prevents vacuuming away
> recently-dead tuples that may be visible only to this transaction; so remaining idle for a long time
> can contribute to table bloat."*

Effect: **the session is terminated.** This is a direct path to a dead connection mid-write — and
`batchsize=50000` maximizes the length of each such gap. `batchsize=1000` would leave the same total
idle time but make the longest contiguous idle-in-transaction interval ~50× shorter. That is the one
concrete argument for lowering `batchsize`, and it costs nothing on the wire because rows-per-statement
stays pinned at 128 either way.

**A cost of `batchsize=50000` nobody usually accounts for: executor memory.** All 50,000 rows' encoded
bind values are buffered in `ParameterList` objects before the first flush. Then
`transformQueriesAndParameters()` allocates 392 *new* `ParameterList`s and copies everything into them
via `newPl.appendAll(pl)` **before executing anything** — so peak bind-data memory is roughly
**doubled** at the moment of the rewrite, per concurrent writer connection per executor. With a jsonb
column of any size this is tens to hundreds of MB per writer. If the executors are showing GC pressure
or OOM near the write stage, this is a prime suspect, and it is a direct consequence of combining a
large `batchsize` with `reWriteBatchedInserts=true`.

---

## 7. "This connection has been closed." — documented causes and how to tell them apart

### The critical structural point first

This message is thrown from exactly one place — `checkClosed()`, on a connection that is **already**
marked closed (`PgConnection.java:881-885`, 42.3.6):

```java
  protected void checkClosed() throws SQLException {
    if (isClosed()) {
      throw new PSQLException(GT.tr("This connection has been closed."),
          PSQLState.CONNECTION_DOES_NOT_EXIST);
    }
  }
```

`PSQLState.CONNECTION_DOES_NOT_EXIST` = SQLSTATE **08003**. It is therefore *always* a secondary
symptom: something already killed the connection and pgjdbc called `abort()`. In the batch path
(`QueryExecutorImpl.java:562-567`, 42.3.6):

```java
    } catch (IOException e) {
      abort();
      handler.handleError(
          new PSQLException(GT.tr("An I/O error occurred while sending to the backend."),
              PSQLState.CONNECTION_FAILURE, e));
    }
```

**And here is why the real cause usually never reaches your logs.** Spark's `savePartition` `finally`
block calls `conn.rollback()` **without a try/catch** on the failure path (`JdbcUtils.scala:741-752`):

```scala
    } finally {
      if (!committed) {
        // The stage must fail. ...
        if (supportsTransactions) {
          conn.rollback()
        } else {
```

`PgConnection.rollback()` starts with `checkClosed()` (`PgConnection.java:889-890`). So on a dead
connection the `finally` throws 08003 "This connection has been closed.", and a `finally` that throws
**discards the in-flight exception** — the genuine `SQLException` from `executeBatch()` (with its
`SocketTimeoutException`/`EOFException` cause, or the server's FATAL message) is destroyed. Note the
asymmetry in Spark's own code: the *success* path wraps `conn.close()` in a try/catch precisely to
avoid masking, but the failure path does not wrap `conn.rollback()`.

**Diagnostic consequence:** do not investigate the reported exception. Find the *first*
`SQLException`/`PSQLException` in the executor's stderr for that task, and enable pgjdbc `FINEST`
logging (`loggerLevel=TRACE`) or check the PostgreSQL server log for the same timestamp. Without one of
those, several of the causes below are genuinely indistinguishable.

### Enumerated causes

| # | Cause | What the server does | Client-side signature | Distinguishable from client log alone? |
|---|---|---|---|---|
| 1 | **`socketTimeout` exceeded** (job sets `socketTimeout=3600`, i.e. 1 h) | nothing — the driver gives up | `SocketTimeoutException: Read timed out` as the cause of "An I/O error occurred while sending to the backend" (08006) | **Yes**, if the primary exception survives. Documented: *"The timeout value used for socket read operations. If reading from the server takes longer than this value, the connection is closed."* |
| 2 | **`idle_in_transaction_session_timeout`** fires during the gap between `executeBatch()` calls | *"Terminate any session"* — backend exits, sends FATAL 25P03 | FATAL `terminating connection due to idle-in-transaction timeout`, or a bare `EOFException` if the socket died first | **Yes if the FATAL is read**; otherwise indistinguishable from #4/#5. Server log settles it. |
| 3 | **`statement_timeout`** exceeded | *"Abort any statement"* — statement only | `PSQLException` SQLSTATE **57014** `canceling statement due to statement timeout`; **connection stays open** | **Yes, and it excludes this cause**: 57014 never produces 08003. If you see "connection has been closed", `statement_timeout` was not it. |
| 4 | **`pg_terminate_backend` / admin shutdown / RDS reboot** | backend terminated | FATAL SQLSTATE **57P01** `terminating connection due to administrator command` | Partly — 57P01 is explicit if read; correlate with RDS events. |
| 5 | **Backend crash or host OOM-killer on the DB** | crash-shutdown of the whole cluster | FATAL **57P02** `terminating connection because of crash of another server process`, on *all* sessions at once | **Yes** — simultaneity across every executor plus RDS event/log entries. |
| 6 | **RDS/Aurora failover** | all connections dropped | `EOFException` / `Connection reset` on every writer at the same instant | **Yes** — correlate with the RDS event stream; the pattern is simultaneous, not per-task. |
| 7 | **NAT gateway / NLB / firewall idle-flow timeout** | nothing — the middlebox drops the flow | `Connection reset by peer`, or a silent hang until `socketTimeout` fires (then looks like #1) | **No** — needs network-side evidence. `tcpKeepAlive=true` is set, but Linux's default `tcp_keepalive_time` is 7200 s, longer than a typical NAT idle timeout, so the flag alone does not guarantee protection. |
| 8 | **`max_connections` exhaustion** | refuses new connections | SQLSTATE **53300** `sorry, too many clients already`, **at connect time** | **Yes, and it excludes this cause**: it is a connect failure, not a mid-batch closure. Relevant only as a reason a *retry* fails. |
| 9 | **Executor JVM OOM / Spark task kill / speculative-execution kill** | nothing | Spark reports `ExecutorLostFailure` / container killed *before* any JDBC error | **Yes** — the Spark-side event precedes the JDBC one. Given §6's note on doubled bind-buffer memory at `batchsize=50000`, this deserves a real look. |
| 10 | **The masking artifact itself** (`finally { conn.rollback() }` on an already-aborted connection) | n/a | 08003 "This connection has been closed." with no other JDBC error in the log | **This is what you are most likely looking at.** It means causes 1–9 are all still open, and the log has been stripped of the evidence needed to choose. |

Ruled out by construction, so you can stop considering them: `statement_timeout` (#3, wrong SQLSTATE
and the connection survives) and `max_connections` (#8, wrong lifecycle phase).

---

## Sources (ranked: official source/docs first)

**Tier 1 — driver source at the exact version AWS documents for Glue 4.0 (pgjdbc `REL42.3.6`)**
- `pgjdbc/src/main/java/org/postgresql/jdbc/PgPreparedStatement.java:1678-1734` — `transformQueriesAndParameters()`, `highestBlockCount = 128`.
  <https://github.com/pgjdbc/pgjdbc/blob/REL42.3.6/pgjdbc/src/main/java/org/postgresql/jdbc/PgPreparedStatement.java#L1678-L1734>
- `pgjdbc/src/main/java/org/postgresql/core/v3/BatchedQuery.java:47-68` — `deriveForMultiBatch`, second 128 enforcement, `new BatchedQuery[7]`.
- `pgjdbc/src/main/java/org/postgresql/jdbc/PgPreparedStatement.java:349-356` — `getStringType()` → `Oid.UNSPECIFIED`.
- `pgjdbc/src/main/java/org/postgresql/jdbc/PgConnection.java:881-885, 889-895` — `checkClosed()`, `rollback()`.
- `pgjdbc/src/main/java/org/postgresql/core/v3/QueryExecutorImpl.java:502-503, 556-567` — `MAX_BUFFERED_RECV_BYTES`, `NODATA_QUERY_RESPONSE_SIZE_BYTES`, the `abort()` path.
- `pgjdbc/src/main/java/org/postgresql/Driver.java:466-468` — `getVersion()`.

**Tier 1 — driver source, other versions (for the drift table and version-independent facts)**
- `REL42.2.27`, `REL42.5.4`, `REL42.7.7` `PgPreparedStatement.java` — all contain `highestBlockCount = 128`.
- `master` `BatchedQuery.java:29-31` — `MAX_VALUE_BLOCK = 1 << 15` and the prepared-statement-count javadoc (unreleased).
- `REL42.7.7` `PGProperty.java:591-594, 924-926, 1084-1090`; `:101-109` (`autosave` default `never`).
- `REL42.7.7` `Parser.java:238-246, 290-295` — rewrite eligibility.
- `REL42.7.7` `PgDatabaseMetaData.java:1117-1127`; `BatchResultHandler.java:249`.
- `REL42.7.7` `PgPreparedStatement.java:118-120` — `maximumNumberOfParameters()` = 65535 / `Integer.MAX_VALUE`.

**Tier 1 — Apache Spark source (`v3.3.0`, the version in Glue 4.0)**
- `sql/core/src/main/scala/org/apache/spark/sql/DataFrameWriter.scala:750-758, 367-391`
- `sql/core/src/main/scala/org/apache/spark/sql/execution/datasources/jdbc/JDBCOptions.scala:50-66, 169-184, 251-285`
- `sql/core/src/main/scala/org/apache/spark/sql/execution/datasources/jdbc/JdbcUtils.scala:634-763` (`savePartition`), `getInsertStatement`
- `sql/core/src/main/scala/org/apache/spark/sql/execution/datasources/jdbc/connection/BasicConnectionProvider.scala:43-50`
- `sql/catalyst/src/main/scala-2.12/org/apache/spark/sql/catalyst/util/CaseInsensitiveMap.scala`

**Tier 1 — official documentation**
- AWS Glue: *Migrating AWS Glue for Spark jobs to AWS Glue version 4.0*, Appendices A & B — <https://docs.aws.amazon.com/glue/latest/dg/migrating-version-40.html>
- pgjdbc connection parameters — <https://jdbc.postgresql.org/documentation/use/> (and its source `docs/content/documentation/use.md:274-279, 404-405`)
- PostgreSQL *Populating a Database* — <https://www.postgresql.org/docs/17/populate.html>
- PostgreSQL *JSON Types* — <https://www.postgresql.org/docs/17/datatype-json.html>
- PostgreSQL *TOAST* — <https://www.postgresql.org/docs/17/storage-toast.html>
- PostgreSQL *Client Connection Defaults* (timeouts) — <https://www.postgresql.org/docs/17/runtime-config-client.html>
- PostgreSQL *Routine Vacuuming* — <https://www.postgresql.org/docs/17/routine-vacuuming.html>
- AWS *RDS for PostgreSQL wait events* — <https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/PostgreSQL.Tuning.concepts.summary.html>
- AWS *Aurora `IO:XactSync`* — <https://docs.aws.amazon.com/AmazonRDS/latest/AuroraUserGuide/apg-waits.xactsync.html>

**Community tier — not used for any load-bearing claim.** No blog or Stack Overflow evidence was
needed; every finding above is source- or vendor-documented. The commonly repeated "2-3x improvement"
figure is quoted from pgjdbc's own docs, not from community reports.

---

## What remains unverified

1. **That this job actually loads 42.3.6.** AWS documents it as the Glue 4.0 bundle, but `--extra-jars`
   plus `--user-jars-first` can override it. One `print(spark.sparkContext._jvm.org.postgresql.Driver.getVersion())`
   settles it. Low risk (the 128 cap holds in every released 42.x), but it is a one-line check.
2. **Which `pg_stat_statements` digest the 127.5 figure came from**, and whether Performance Insights
   rolled the 128/64/16-tuple variants together. Does not change the conclusion; does change how you
   read the rows/call number in future.
3. **The 0.163-AAS vs 58.59-AAS contradiction** between the brief and `assumptions.md` A12. Unresolved
   here by design — it is a question about your metric windows, not about pgjdbc. It determines whether
   the database is the bottleneck at all, so it should be resolved before any tuning.
4. **Whether `idle_in_transaction_session_timeout` / `transaction_timeout` are non-zero on this RDS
   parameter group.** Both terminate sessions and both are plausible causes of the observed closure.
   Readable with `SHOW idle_in_transaction_session_timeout;` — a read, no write required.
5. **The mean `pg_column_size()` of the jsonb column**, hence whether TOAST compression is running per
   row. This is the most likely explanation for 0.917 ms/row and the cheapest thing left to measure.
6. **The actual index and constraint list on `integration.parser_output_exposures`** (this is
   `assumptions.md` A13 and is internal, not external research).
7. **Whether a middlebox idle timeout sits between the Glue ENI and RDS**, and the executor host's
   `net.ipv4.tcp_keepalive_time`. Needed to rule cause #7 in or out; not answerable from any log.

---

## Most defensible next action

**Stop treating `batchsize` as the lever.** It cannot widen the INSERT — 128 tuples is a compile-time
constant in every released pgjdbc, enforced in two places, and 42.3.6 is what Glue 4.0 ships. A11 is
confirmed as a hard driver cap, so close it and redirect the investigation.

Then, in this order:

1. **Reconcile the AAS contradiction (item 3 above)** before tuning anything. If the exposures INSERT
   is genuinely ~0.16 AAS, the database is nearly idle on it and the whole write-path tuning thread is
   the wrong thread — the time is going somewhere upstream in Spark.
2. **Read `idle_in_transaction_session_timeout` and `transaction_timeout`** from the parameter group,
   and hunt the *first* exception in the failing executor's stderr rather than the 08003 that Spark
   reports. Given `JdbcUtils.scala:741-752`, the reported message is a masking artifact and carries
   no diagnostic information.
3. **Measure `avg(pg_column_size(<jsonb col>))`** and count the table's indexes. At 0.917 ms/row with
   parse cost already amortized to zero, TOAST compression and index maintenance are where the time
   must be.
4. **Only if the database is confirmed as the bottleneck**, evaluate `COPY` (via `CopyManager`, in a
   `foreachPartition`) rather than any INSERT-shape tuning. PostgreSQL's own documentation says COPY
   beats batched prepared INSERTs *"almost always"*, and it is the only change that escapes the 128
   cap without waiting for an unreleased driver.
5. **Independently of the above, consider lowering `batchsize` to ~1000.** It costs nothing on the wire
   (rows-per-statement is pinned at 128 regardless), it shortens the longest idle-in-transaction gap by
   ~50×, and it removes the doubled bind-buffer memory spike that `transformQueriesAndParameters()`
   creates at 50,000 queued rows. This is a strict improvement with no downside I can find in the
   source.
