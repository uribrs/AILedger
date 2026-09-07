# Code review — adapter-data-normalizer

Independent implementation review. Judged on its own merits: correctness, runtime behaviour,
streaming, data structures, idiomatic .NET 8, failure modes, resource lifetime, maintainability, and
whether the complexity is proportional.

## Findings by severity

| Severity | Count |
|---|---|
| Blocker | 0 |
| Major | 3 |
| Minor | 10 |
| Nit | 6 |
| Observation | 6 |

Nothing here is dangerous to build on. The three Major findings are two wrong operational claims and
one missing failure path, all with small local fixes.

## Calibration

- **Change type:** feature logic plus infrastructure/orchestration, with one generated public
  contract surface (`src/Contract/Generated`).
- **Risk level:** High. Persistence boundary, bulk load inside transactions, derived primary keys,
  a network-bound paging loop, 122k rows / 496 MB per run.
- **Depth applied:** failure semantics, recovery, scale behaviour, idempotency, long-term coupling.

Measured, not assumed (Debug build, this machine):

- One full normalization pass over `out/full-001` (496,102,580 bytes, 122,359 records):
  **max RSS ~152 MB**, so the streaming design holds — memory does not track lane size.
- Two full passes: **~14.5 s wall**, `user` ~2x `real`, i.e. GC threads are doing real work but the
  absolute cost at target scale is fine.
- Clean `--no-incremental` rebuild: **0 errors, 0 compiler warnings, 0 analyzer warnings.** The only
  build noise is 14x `NU1507` (see m10).

## Major

### M1 — The host and the generator exit `0` on bad input

`src/Host/Program.cs:22-28` returns `ExitOk` whenever `HostOptions.Usage` is non-null. `Usage` is
non-null for three different things: `-h`, a rejected argument, and *no batch folder found*
(`src/Host/HostOptions.cs:53-61`). `README.md:123` states "Exit code `0` means every parent key
resolved". So `--btach out/full-001`, `--cluster prod-eau`, and an empty `out/` all print to
**stdout** and exit `0`.

`src/Contract/Generator/Program.cs:21-26` has the same shape, and there it is worse: `--chek` (or any
typo) prints usage and exits `0`, so the contract drift gate the README sells at `README.md:159-163`
passes without checking anything.

- **Impact:** a wrapper script or CI step reads success from a run that did nothing.
- **Fix (local patch):** separate "help was requested" from "input was rejected". Give
  `GeneratorOptions`/`HostOptions` a distinct rejected state, write it to `Console.Error`, return
  `ExitBadInput`. The collector already does this correctly
  (`src/ThinFalconCollector/Program.cs:10-14` returns `2`) — make the other two match it.

### M2 — `EnsureSchemaAsync` cannot repair a diverged database, but two documents say it does

`SchemaDdl.Statements` emits only `CREATE SCHEMA IF NOT EXISTS`, a `DO $$ … IF NOT EXISTS … CREATE
TYPE` guard, and `CREATE TABLE IF NOT EXISTS`. There is no `ALTER` anywhere in
`src/Writer.Postgres/Schema/SchemaDdl.cs`.

Consequently, on a data volume created before a contract change, every statement is a no-op: the
table keeps its old column set and the enum keeps its old member set. Both of these say otherwise:

- `README.md:69-70` — "The writer's `EnsureSchemaAsync` applies the same DDL idempotently, so an
  existing container is repaired by running the host."
- `docker-compose.yml:14-17` — "a container whose volume predates a contract change is repaired by
  running the writer".

An operator who believes that gets a COPY failure (`CopyPlan.For` throws only on contract-vs-cell
mismatch, not on database-vs-contract mismatch) or a failed enum cast, one full collection later.

The same gap has a second face. `db/schema.prod-eu.sql` and `db/schema.stg.sql` are declared
generated (`db/README.md:1-7`) but there is no command that regenerates them — `db/README.md:20-21`
describes it in prose only — and unlike `src/Contract/Generated` they are **not covered by the
generator's `--check`**. They can silently stop matching the contract they claim to mirror, and the
first symptom is a load failure.

- **Impact:** the one stated repair path does not exist, and the generated-not-hand-written invariant
  that the whole design leans on is unenforced for two of the four generated artifacts.
- **Fix (local patch, two parts):**
  1. Either emit `ALTER TABLE … ADD COLUMN IF NOT EXISTS` per column and
     `ALTER TYPE … ADD VALUE IF NOT EXISTS` per member next to the creates (both are supported on the
     pinned `postgres:15-alpine`, and `EnsureSchemaAsync` runs statements outside an explicit
     transaction, which `ALTER TYPE … ADD VALUE` requires) — or correct both documents to say the
     repair is `docker-compose down -v`. Do not leave the claim standing.
  2. Fold the two `db/*.sql` snapshots into the generator's artifact list so `--check` covers them.

### M3 — The collector's retry loop does not cover transport failures, and ignores the vendor's rate-limit header

`FalconApiClient.GetPageAsync` (`src/ThinFalconCollector/Vendor/FalconApiClient.cs:24-56`) retries on
`IsTransient(response.StatusCode)`. It never sees a failure that has no status code:
`SendOnceAsync` throwing `HttpRequestException` (connection reset, DNS blip, TLS failure) or
`TaskCanceledException` (the 5-minute `HttpClient.Timeout` at
`ThinFalconCollectorRunner.cs:22`) propagates straight out of the loop.

There is no resume. `FalconCursorScroll` holds the cursor in a local (`Vendor/FalconCursorScroll.cs:19`)
and nothing is checkpointed, so a scroll that dies on page 60 of 62 restarts from page 1, and the
batch folder is left with two partial lanes and no manifest — which is at least honest
(`Emission/BatchFolder.cs:22-25`), but it is a whole four-minute collection lost to one reset packet.

Separately, `429` is retried on a blind 2/5/15 s ladder with no attention to `Retry-After` or Falcon's
`X-RateLimit-RetryAfter`. A throttled tenant burns the three attempts inside 22 s and then fails hard,
where the header would have said how long to wait.

- **Impact:** the failure most likely to actually happen during a 62-page live scroll is the one not
  handled. This is the finding I would fix first if this code were to run unattended.
- **Fix (local patch):** move the `SendAsync` call inside the `try` and add
  `catch (HttpRequestException) when (attempt < RetryDelays.Length)` plus the same for
  `TaskCanceledException` that is not the caller's cancellation (`!cancellationToken.IsCancellationRequested`);
  and when the response carries a rate-limit header, prefer it over `RetryDelays[attempt]`.
- **Not required now:** cursor checkpointing. That is the resumability machinery this prototype
  deliberately does without, and adding it here would be scope the problem does not demand.

## Minor

### m1 — `RowValidator` is O(columns²) per row, on the 122k-row path

`TableContract.Column(name)` is `Columns.FirstOrDefault(...)` (`src/Contract/Labels/LabelContract.cs:77-78`).
`RowValidator.CheckColumns` calls it once per binding entry, so each row costs ~21 linear scans over
~22 columns (~450 ordinal string comparisons), plus `LabelContract.IsEnumMember` doing two more linear
scans (`Enums.FirstOrDefault` then `Members.Contains`) for each of the three enum columns. Per batch
that is ~55M string comparisons.

Measured impact is acceptable today (see Calibration). Flagging it because the fix is one line of
state and it removes the quadratic term entirely.

- **Fix (local patch):** give `TableContract` a lazily built
  `Dictionary<string, ColumnContract>` and back `Column`/`HasColumn` with it; same for
  `LabelContract.Enums` and `EnumContract.Members` (a `HashSet<string>`).

### m2 — The vendor payload is parsed and re-serialized about five times per finding

For one finding record, the same ~4 KB of vendor JSON goes through:

1. `LabeledRecord.SetVendorRecord` — `record.GetRawText()` then `JsonNode.Parse` (collector,
   `src/ThinFalconCollector/Labels/LabeledRecord.cs:64-68`);
2. `NdjsonLane.WriteAsync` — `record.ToJsonString()` back to a string
   (`src/ThinFalconCollector/Emission/NdjsonLane.cs:41-48`);
3. `LabeledRecord.TryParse` + `Clone` on read (`src/Normalizer/Labels/LabeledRecord.cs:43-59`);
4. `LabelBinding.Carry` — `declared.ToNode()` parses it again, moves every top-level member, then
   `JsonbValue.FromNode` serializes it again (`src/Normalizer/Rows/LabelBinding.cs:63-84`);
5. `ExposureContent.Encode` — `json.ToNode()` parses it a *third* time for
   `CanonicalJson.Write` (`src/Normalizer/Rows/ExposureContent.cs:70`).

Inside step 5, `CanonicalJson.WriteObject` calls `JsonSerializer.Serialize(member.Key)` once per
member (`src/Normalizer/Rows/CanonicalJson.cs:56`) — roughly 10M `JsonSerializer` invocations across
the batch, each spinning up a writer and a pooled buffer, to quote a string.

At the target scale this costs seconds, not minutes, so it is not urgent. The single line worth
changing is the per-key `JsonSerializer.Serialize`.

- **Fix (local patch):** replace it with a small `AppendQuoted` helper (or `JsonEncodedText.Encode`
  cached per name). If the batch ever grows an order of magnitude, collapse steps 4 and 5 by having
  `Carry` return the canonical text alongside the `JsonbValue` so the tree is walked once.

### m3 — `FalconTokenSource` advertises thread-safety its fast path does not provide

`GetAsync` (`src/ThinFalconCollector/Vendor/FalconTokenSource.cs:20-28`) reads `_token` and `_renewAt`
outside `_gate`. `_renewAt` is a `DateTimeOffset` — a multi-word struct with no atomic read — so a
concurrent refresh could in principle be observed torn. Today the two lanes run strictly in sequence
(`ThinFalconCollectorRunner.cs:38-40`), so there is no live defect; the `SemaphoreSlim` just implies a
guarantee the fast path does not keep. `_gate` is also never disposed (the class is not
`IDisposable`).

- **Fix (local patch):** hold a single immutable `record Token(string Value, DateTimeOffset RenewAt)`
  in a field and read it once with `Volatile.Read`, or state in the doc-comment that the type is
  single-consumer.

### m4 — `BatchValidationException`'s doc-comment states an atomicity the code does not have

`src/Normalizer/BatchValidationException.cs:12-14`: "which the writer turns into a rolled-back
transaction — the batch fails as a whole either way, and nothing is partially written."

`NormalizeAndLoadStage.LoadAsync` (`src/Host/NormalizeAndLoadStage.cs:69-72`) calls
`WriteAssetsAsync` and then `WriteExposuresAsync`; `EnrichWriter.RunAsync`
(`src/Writer.Postgres/EnrichWriter.cs:101-106`) opens a **new connection per call** and
`EnrichCopyRunner.RunAsync` begins its own transaction. The asset lane is therefore committed before
the exposure lane starts streaming, so an exposure-lane rejection leaves that batch's asset rows in
the table.

`README.md:197-205` documents this correctly and argues it is benign in the direction that matters
(assets without exposures produce no unresolved parent keys, and the next run truncates). I agree
with that reasoning — an outer transaction spanning a 122k-row COPY would be the wrong trade. The
class comment is simply the wrong document.

- **Fix (nit-sized patch):** correct the comment to match `README.md:197-205`. Do not change the
  behaviour.

### m5 — The host crashes with a stack trace when Postgres misbehaves

`src/Host/Program.cs:39` catches `InvalidOperationException or IOException`. `NpgsqlException` /
`PostgresException` derive from `DbException`, not either of those, so a container that is down, a
failed enum cast, or a COPY error escapes as an unhandled exception — from a tool whose whole output
contract is a one-line verdict plus an exit code.

- **Fix (local patch):** add `DbException` to the filter, or catch `Exception` at the top level and
  print `ex.Message` with a distinct exit code.

### m6 — The generator's default input path is one developer's home directory

`ContractSources.DefaultModelsRoot = "/Users/user/Dev/cymulate-exposure-analytics/cybi-db-models"`
(`src/Contract/Generation/ContractSources.cs:11-12`). `--models-root` overrides it and
`MissingInputs()` + the failure message name it well, so this degrades gracefully — but the generator
is unrunnable out of the box for anyone else, and `GeneratorOptions`'s own doc-comment
(`Generator/Program.cs:164`) admits it.

- **Fix (local patch):** probe for a sibling checkout of `cybi-db-models` next to the repo root
  (`RepoLayout`-style walk-up), fall back to an env var, keep `--models-root` as the override.

### m7 — `ExposuresAsync` is documented enumerable-once but does not enforce it

`NormalizedBatch.ExposuresAsync` (`src/Normalizer/NormalizedBatch.cs:51-66`) says "Enumerable once:
it reads the lane's files as it goes and the counters advance with it", and `Counts` is shared mutable
state. A second enumeration silently re-reads 496 MB and doubles `Exposures`,
`FindingsWithoutCve` and `DuplicateExposuresCollapsed`. Given
`NormalizeAndLoadStage.ReportAsync` compares those counters against `count(*)`
(`src/Host/NormalizeAndLoadStage.cs:101-102`), the consequence of an accidental double-drain is a
confusing "Postgres holds N but the normalizer produced 2N" failure.

- **Fix (local patch):** one `bool _drained` guarded with `InvalidOperationException`.

### m8 — Three of the nine identity columns are not covered by the per-column theory

`ExposureContentCoverageTests.a_column_inside_the_identity_separates_two_otherwise_equal_records`
enumerates `name`, `display_name`, `severity`, `status`, `description`, `mitigation`. The plan also
admits `first_seen`, `last_seen` and `additional_fields`. The test's own doc-comment explains exactly
why per-column coverage is needed — a getter reading the wrong field compiles and still passes the
coverage check — and `additional_fields` is the column whose presence in the identity has the largest
consequence (it is what makes two findings on one host+CVE two rows).

- **Fix (local patch):** add the three `[InlineData]` cases; `additional_fields` needs a JSON-object
  label rather than a string, and `first_seen`/`last_seen` need ISO-8601 values.

### m9 — Most of the suite cannot run on a fresh clone or in CI

`RealBatch.Directory()` requires `out/full-001`, and `.gitignore` excludes `out/`. `PipelineFixture`,
`RealBatchFixture`, `CollectedBatchTests`, `ReparseStabilityTests` and `DuplicateKeyTests` therefore
all hard-fail without a live Falcon tenant and a local container, by design —
`PostgresFixture`'s comment argues explicitly that skipping would be worse, and I agree with that for
the Postgres half.

The cost is worth stating: there is no green-on-clean-clone tier, and `CollectedBatchTests` asserts
exact live counts (`294`, `122_359`) that a re-collection under the same `--run-id` invalidates.

- **Fix (defer, not now):** commit a trimmed lane subset (a few hundred findings, real vendor JSON)
  as a second fixture so the shape and stability assertions have a CI-runnable tier, and keep the
  full-batch tier as the local-only one.

### m10 — `NU1507` on every project

All 7 projects warn: two package sources (`nuget.org`, `cym-dom/cym-repo-nuget`, inherited from a
parent-level config) under central package management with no source mapping. 14 warnings on every
build train the eye to ignore build output.

- **Fix (local patch):** add a repo-root `nuget.config` with `<packageSourceMapping>` — `Npgsql`,
  `xunit*`, `Microsoft.*`, `FluentAssertions`, `Moq`, `coverlet.*` to `nuget.org`. Nothing here
  consumes a `Cymulate.*` package, so the internal feed may not be needed at all.

## Nits

- **n1** `CopyColumnTypeTests.ReadAsync` creates an `NpgsqlCommand` with `connection.CreateCommand()`
  and never disposes it; only the reader is disposed by the caller.
- **n2** `LabeledRecord.Build()` (collector) returns the internal `JsonObject` by reference, so a
  caller can mutate a "built" record.
- **n3** `BatchManifestJson.TryDeserialize` catches only `JsonException`; `System.Text.Json` also
  throws `NotSupportedException` for some shapes, which would escape a `Try…` method.
- **n4** `LabelContractEmitter.Literal` escapes `\` and `"` only. A newline or control character in a
  column default would emit C# that does not compile. Unreachable from a Markdown table cell today.
- **n5** `PostgresTypeMap.Quote` does not double an embedded `"`. Inputs are generated-contract
  identifiers, so unreachable today; worth one `Replace` for the same reason `Literal` has one.
- **n6** `NdjsonLane.Create` uses `StreamWriter`'s default 1 KB char buffer for a 496 MB lane. A
  64 KB buffer on the `StreamWriter` and the `FileStream` is one argument each.

## Observations — no action

- **o1 The streaming claim is real.** Max RSS ~152 MB across a pass over a 496 MB lane, with the
  asset lane held (bounded by host count, and needed as the parent-key set) and the exposure lane
  streamed. `NormalizedBatch._assetIds` as a `HashSet<Guid>` and `seen` as a `HashSet<Guid>` are the
  right structures; the only unbounded-in-row-count allocations are those two id sets, ~6 MB at this
  scale.
- **o2 The writer's shape is the right answer.** COPY into a transaction-scoped
  `ON COMMIT DROP` temp table, then `INSERT … SELECT DISTINCT ON (pk) … ON CONFLICT DO UPDATE`, with
  enum columns staged as `text` and cast on the way in so an out-of-contract label fails loudly at
  the cast. `CopyPlan.For` rejecting a contract column with no cell, a surplus cell, and a
  CLR-type/Postgres-type mismatch *before* a row streams is exactly where that check belongs.
  `DuplicateKeyTests` correctly notices that `DISTINCT ON` without a tiebreaker picks an arbitrary
  survivor and asserts the writer never has to.
- **o3 `ColumnCoverage` is the best idea in the codebase.** Refusing at construction to walk past a
  contract column that is neither accessed nor *declared excluded with a reason* closes a failure
  that would otherwise be invisible: an unaccessed column drops out of the exposure identity, two
  distinct rows derive one id, and one disappears as a "duplicate collapse" with a counter
  incrementing and no error. Pairing getter and setter in one `ColumnAccessor` and then testing per
  column anyway — because a getter reading the wrong field still compiles — is the correct depth.
- **o4 The uuidv5 implementation is correct**, including the RFC 4122 big-endian field swap on both
  sides, and is checked against the published `python.org` vector rather than only against itself.
  `KeySeparator = U+001F` as a join character that cannot occur in the inputs is the right way to
  make the composed name injective.
- **o5 House rules hold.** No `Version=` in any `.csproj` (all versions in `Directory.Packages.props`).
  No identifier, type, or file named "Legacy" anywhere in code — the only two hits in the tree are
  the rule statements under `ai/`. `Nullable` and `ImplicitUsings` enabled in
  `Directory.Build.props` and repeated per project. No secret committed: `appsettings.local.json` is
  gitignored by three separate patterns and the tree is entirely untracked so far; the only
  credentials in source are `postgres/postgres` for a local throwaway container. Clean rebuild is
  warning-free apart from `NU1507`.
- **o6 Complexity is proportional.** ~6,550 hand-written lines of source (plus 262 generated) and
  2,010 of tests, for a five-stage pipeline with a generated schema contract, two cluster shapes, and
  a bulk loader. Methods are short, helpers are named for behaviour, and the doc-comments carry the
  *why* rather than restating the code — the comment on `AssetRow.Tags` explaining that it is scalar
  `text` among three `text[]` neighbours is worth more than the test that proves it. The only place
  the abstraction sits slightly ahead of the need is the
  `LabeledValue` / `ColumnAccessor` / `ColumnCoverage.Plan` triple, and that pays for itself in o3.
  I would want the `IRowIdDerivation` interface's stated invariant ("stable within a run, not across
  runs") reconciled with `PrototypeScope` pinning `instance_id` — but `README.md:175-189` already
  states that gap and names what it leaves unexercised, which is the honest handling of it.

## What I would fix before building on this

In order: **M3** (the failure that will actually happen), **M1** (a gate that reports success
without checking), **M2** (a repair path that does not exist and is documented as if it does). Then
**m4** and **m1**, which are a five-line comment correction and a five-line data-structure change.

Everything else can wait for the code to earn it.
