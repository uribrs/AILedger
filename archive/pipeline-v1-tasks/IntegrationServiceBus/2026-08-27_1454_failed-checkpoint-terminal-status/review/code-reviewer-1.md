# Code review — failed-checkpoint terminal status + retention

## Scope reviewed

`d53e347d` (= current `HEAD`, so the whole change is uncommitted working tree) — 11 modified files,
3 new test files. Areas touched: `ProcessEventCommandHandler` completion path, `ICheckpointRepository`
+ both implementations, `CheckpointCleanupJob`, `ConfigurationKeys`, `CheckpointEntry`.
Docs consulted: `CLAUDE.md`, `docs/coding-standards.md`, `docs/clean-architecture.md`.
Baseline checks: `git show` of the base for every predicate I call a regression; `git log` confirms
no commits on the branch, so "base" is `d53e347d` throughout.

Verified rather than assumed:

- `PlatformEvent.Credentials` (`~/Dev/IntegrationInfra/src/Cymulate.Integration.Client/Models/PlatformEvent.cs`)
  carries no `[JsonPropertyName]`, and both write sites
  (`ProcessEventCommandHandler.cs:134`, `AdapterExecutionContext.cs:105`) use default
  `JsonSerializer.Serialize`. **The JSON key `Credentials` is correct.**
- `platform_event_json` is `jsonb` (`CheckpointDbContext.cs:73`), so `jsonb - text` is the right
  operator, and the `CAST(... AS text)` is needed to disambiguate it. Correct as written.
- `status` is `HasConversion<string>()`, so both changed LINQ predicates emit `status = 'Failed'` /
  `status <> 'Failed'` against a text column. Correct.
- The ownership guard **is** satisfiable at the call site: `TryAcquireExecutionLockAsync` inserts/updates
  the row with `claimed_by_instance = InstanceId` (`CheckpointRepository.cs:643,647`) and every
  claim-loss path returns before `CompleteExecutionAsync`. The mark is not a silent no-op in the
  ordinary case.
- Releasing the claim inside the same UPDATE opens no window: it is a single statement, and the row
  becomes unclaimed and terminal atomically. The recovery exclusion is what keeps the unclaimed row
  from being re-offered, and `GetRecoverableAsync` has it in both stores.

## Blockers

- **B1** — `Infrastructure.Postgres/Persistence/CheckpointRepository.cs:483`,
  `Infrastructure.Core/Jobs/CheckpointCleanupJob.cs:70`,
  `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs:191`
  - **Problem:** the TTL backstop was *repurposed*, not extended, so **nothing deletes a non-terminal
    checkpoint row any more**. Tracing every remaining delete: `DeleteAsync` on a non-failure outcome,
    `DeleteByCorrelationIdAsync` on an explicit stop, `DeleteStoppedCheckpointsAsync`, and
    `CheckpointRecoveryHandler.EndUnservableRunAsync` (which only fires for rows the compatibility
    probe *declines*). A row outside all four now lives forever, and such rows demonstrably exist —
    `ProcessEventCommandHandler.cs:750-754` documents that a row keyed under a client id "belongs to no
    pod's partition (`CheckpointPartition`): crash recovery and the scheduled-wait resume never see it".
    A tenant-scoped row with no dedicated pod is in the same position: the shared sweeper matches only
    `null`/`""`/`default`/`none`. Add to that every row whose delete or mark failed — both are swallowed
    (`ProcessEventCommandHandler.cs:1126,1268`) with the log line "TTL job will handle it", which is now
    false. `adapter_checkpoints` grows without bound and the recovery sweep's partition scan degrades
    with it.
  - **Suggestion:** keep two horizons in two methods rather than overloading one. Restore
    `DeleteExpiredAsync`'s original predicate (`UpdatedAtUtc < cutoffUtc && Status != ScheduledWait`)
    at the TTL cutoff, and add a sibling for the new window:

    ```csharp
    Task<int> DeleteTerminalExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
    // Postgres: .Where(c => c.Status == CheckpointStatus.Failed && c.UpdatedAtUtc < cutoffUtc)
    ```

    `CheckpointCleanupJob.Execute` then calls `DeleteExpiredAsync(cutoff)` **and**
    `DeleteTerminalExpiredAsync(failedCutoff)`. Terminal rows survive the 24h TTL because their
    retention is longer, so the two do not fight; the `ScheduledWait` exemption is preserved; and the
    "TTL job will handle it" logs become true again. This also removes B1's dependency on I1.

- **B2** — `Infrastructure.Postgres/Persistence/CheckpointRepository.cs:646-654` (with `:707`)
  - **Problem:** a redelivery over a retained `Failed` row runs while the row is still marked terminal,
    which makes the run invisible to crash recovery for the whole window. `TryAcquireExecutionLockAsync`'s
    `ON CONFLICT DO UPDATE` sets claim, payload and `updated_at_utc` but **not `status`** (deliberately —
    "progress fields are NOT reset on conflict"). Status is only reset later, by the first adapter
    `UpsertAsync` (`status = EXCLUDED.status`, line 90) — which many collectors reach only at the end of
    their first page. And this is not a rare path: `IsReportedFailure` includes `TransientFailure`, and
    `IsbPlatformEventDispatcher.cs:96` redelivers exactly the transient results, so mark-then-retry is
    the *normal* transient sequence. If the pod dies in that window, `GetRecoverableAsync` skips the row
    (`:707`), no other execution can find it, no done is ever published, and the row is deleted silently
    at the retention horizon. At base the row was deleted on failure, so the retry created a fresh
    `Idle` row and stayed recoverable throughout — this is a regression, not a pre-existing hole.
  - **Suggestion:** clear the terminal state when the row is taken again, in the same statement that
    takes it. Add to the `DO UPDATE SET` list:

    ```sql
    status = CASE WHEN adapter_checkpoints.status = 'Failed' THEN 'Idle' ELSE adapter_checkpoints.status END,
    ```

    A `CASE` rather than a bare assignment because `ScheduledWait` must survive the same clause. The
    in-memory store already satisfies this (`TryAcquireExecutionLockAsync` replaces the whole entry), so
    only the Postgres side needs the change. A defensible alternative — narrowing `IsReportedFailure` to
    the results `SiemRulesPageRejection.AllowsRetry` will *not* retry — leaves the max-retries-exceeded
    case deleting the row instead of retaining the evidence you wanted, so I would take the `CASE`.

## Important

- **I1** — `Applications/…Application/Commands/ProcessEventCommandHandler.cs:1098-1111`
  - **Problem:** `TryMarkCheckpointFailedAsync` discards `MarkFailedAsync`'s return value. `false` means
    the row is gone or the claim has moved — the exact case where the feature does nothing — and it is
    now unobservable and (until B1 is fixed) leaves a row nothing will ever delete. The new test seeds
    the mock to return `true` (`ProcessEventCommandHandlerTests.cs:66-71`), so no test drives it either.
  - **Suggestion:** capture the result and log it; do **not** fall back to `DeleteAsync`, because a moved
    claim means another execution owns the row.

    ```csharp
    bool marked = await checkpointRepository.MarkFailedAsync(...);
    if (!marked)
    {
        logger.LogWarning(
            "Checkpoint not marked terminal for {TenantId}/{CorrelationId}/{Platform}/{Category} — no row, or the claim moved.",
            tenantId, platformEvent.CorrelationId, platformEvent.ProductType, category);
    }
    ```

    Add a handler test that sets the mock to `false` and asserts the row is not deleted.

- **I2** — `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs:160-174`
  - **Problem:** the mark mutates the stored `CheckpointEntry` in place, while every other write in this
    class swaps the reference under a CAS (`TryUpdate(key, entry, existing)` at `:68`, `_store[key] =
    updated` at `:393`). A concurrent `UpsertAsync` that read `existing` before the mark will win its
    `TryUpdate` and silently erase both the `Failed` status and the credential strip. Separately, the
    field order means a `JsonNode.Parse` throw on a malformed payload leaves the entry half-marked —
    `Failed`, unclaimed, credentials still present — where the Postgres store is atomic. The two stores
    therefore disagree on the predicate this change is built around.
  - **Suggestion:** compute the stripped JSON first, build a copy the way
    `TransitionFromScheduledWaitAsync` does, and CAS it in a loop:

    ```csharp
    while (_store.TryGetValue(key, out CheckpointEntry? existing))
    {
        if (!string.Equals(existing.ClaimedByInstance, instanceId, StringComparison.Ordinal))
            { return Task.FromResult(false); }
        CheckpointEntry updated = CloneWith(existing, CheckpointStatus.Failed, StripCredentials(existing.PlatformEventJson));
        if (_store.TryUpdate(key, updated, existing)) { return Task.FromResult(true); }
    }
    return Task.FromResult(false);
    ```

    and wrap the parse in `try { … } catch (JsonException) { return platformEventJson; }` so a malformed
    payload is left alone rather than throwing mid-update.

- **I3** — `Infrastructure.Postgres/Persistence/CheckpointRepository.cs:465`
  - **Problem:** the strip is undone within seconds on the common path, and it does not cover every
    secret the row can carry. `TryAcquireExecutionLockAsync` writes `platform_event_json =
    EXCLUDED.platform_event_json` on conflict (`:649`), so the next redelivery — which a transient
    failure guarantees — puts the credentials straight back. The strip is therefore a property of the
    *last* attempt, not an invariant of `Failed` rows. And `PlatformEvent.Headers` is not stripped, which
    is where an `Authorization` header for an HTTP integration lives; `Payload` can also carry the
    encrypted `credentials` blob that `PopulateCredentialsFromEncryptedBlob` reads.
  - **Suggestions:**
    1. Strip `Headers` in the same statement — preferred, it costs one operator and nothing resumes from
       a terminal row: `platform_event_json - CAST({credentialsKey} AS text) - CAST({headersKey} AS text)`,
       with `HeadersJsonKey = nameof(PlatformEvent.Headers)` beside the existing constant, and the
       matching `Remove` in the in-memory `StripCredentials`. Then say in the PR body that the guarantee
       is "a row in `Failed` state carries no secrets", which is now true.
    2. If `Headers` is deliberately out of scope, say so in the PR body and state the weaker guarantee
       explicitly, so nobody later reads the retained row as secret-free.

- **I4** — `Applications/…Application/Services/CheckpointRecoveryHandler.cs:770,867,874` (also `:694,748,794`
  and `ProcessEventCommandHandler.cs:416,1126,1268`)
  - **Problem:** eight places assert a backstop that no longer exists. Two of them are operator-facing
    log text acted on during an incident: `:770` tells the operator the unservable checkpoint "will be
    deleted by the cleanup job" and `:874` says "the cleanup job will remove it" — both false now, since
    those rows are non-terminal. The `<remarks>` at `:694`/`:748`/`:794` derive `UnservableTerminalAge`
    from a deletion horizon that has gone.
  - **Suggestion:** make the claim true instead of just editing the prose — swap the delete at `:867` for
    the new mark. `EndUnservableRunAsync` claims the row with `InstanceId` at `:824`, so the ownership
    guard is already satisfied:

    ```csharp
    await checkpointRepository.MarkFailedAsync(
        checkpoint.TenantId, checkpoint.CorrelationId, checkpoint.PlatformType, checkpoint.Category,
        InstanceId, cancellationToken);
    ```

    The unservable run *is* a reported failure, so it gets the same terminal row, credential strip and
    retention reap; the sweep stops re-offering it (today a failed delete leaves it claimed, stale, and
    re-declined on the next sweep); and `:874` becomes accurate. Then correct the three `<remarks>` and
    the three "TTL job will handle it" lines to name whichever backstop survives B1.

- **I5** — `Tests/…API.UnitTests/Checkpoints/CheckpointRepositoryTests.cs:316-490`
  - **Problem:** every test of the new raw SQL is in the Testcontainers project, which does not execute
    without Docker, so the `jsonb - text` UPDATE, the SQL ownership guard and both changed predicates
    have not run against a real Postgres. The rest of the suite is genuinely good — the handler tests
    drive the real handler, and `FailedRunLeavesATerminalRowTests` runs the real sweep over the real
    in-memory store rather than asserting against a mock — but none of that exercises the SQL.
  - **Suggestion:** before merge, start Docker once and run
    `dotnet test <API.UnitTests.csproj> --filter "FullyQualifiedName~CheckpointRepositoryTests"`,
    and paste the result into the PR. Add one case there for a `platform_event_json` that is not a JSON
    object (see N4).

## Nits

- **N1** — `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs:163,166,179,182`
  - **Problem:** four new single-line `if`s with no braces; `docs/coding-standards.md` says always brace,
    and the surrounding unbraced code is legacy, not licence.
  - **Suggestion:** brace all four. (The rest of the new code follows the explicit-type rule correctly.)

- **N2** — `UnitTests/…Infrastructure.Core.UnitTests/CheckpointCleanupJobTests.cs`
  - **Problem:** `MinimumFailedRetentionDays` (`ConfigurationKeys.cs`) exists precisely to stop a
    misconfigured `0` deleting terminal rows on the next sweep, and nothing exercises it.
  - **Suggestion:** add `[Theory] [InlineData("0")] [InlineData("-3")]` over the existing
    `RunAndCaptureCutoffsAsync` shape, asserting the cutoff lands one day back.

- **N3** — `Infrastructure.Postgres/Persistence/CheckpointRepository.cs:466-471`
  - **Problem:** the `WHERE` has no `status <> 'ScheduledWait'`, unlike every other surgical write here
    (`UpsertAsync` at `:97`). No path reaches it today — the handler returns at `:111` on a
    `ScheduledWait` row — but `CheckpointStatus`'s own doc says a parked row "MUST NOT be touched".
  - **Suggestion:** add `AND status <> 'ScheduledWait'` to the `WHERE`, and the matching
    `if (existing.Status == CheckpointStatus.ScheduledWait) { return Task.FromResult(false); }` in the
    in-memory store, so the two keep agreeing.

- **N4** — `Infrastructure.Postgres/Persistence/CheckpointRepository.cs:465`
  - **Problem:** `jsonb - text` throws `cannot delete from scalar` if `platform_event_json` is ever a JSON
    scalar, where the in-memory store returns the text unchanged (`:181`). The exception is swallowed by
    `TryMarkCheckpointFailedAsync`, so the row silently stays non-terminal — and after B1, undeletable.
  - **Suggestion:** guard the expression:
    `platform_event_json = CASE WHEN jsonb_typeof(platform_event_json) = 'object' THEN platform_event_json - CAST({credentialsKey} AS text) ELSE platform_event_json END`.

- **N5** — `Infrastructure.Core/Jobs/CheckpointCleanupJob.cs:70`
  - **Problem:** the modified line keeps `var deletedCount`, and the name now means something else — it
    counts terminal rows reaped on retention, not expired ones.
  - **Suggestion:** `int deletedTerminalCount = await …` (explicit type per the standards, which apply to
    modified lines).

## Open questions

- Is `TransientFailure` meant to be terminal? `CheckpointStatus.Failed` is documented as "Terminal
  non-retryable failure", but the dispatcher redelivers exactly those results — so the row is marked
  terminal for a run that is about to be retried. Was that considered, or should `IsReportedFailure`
  track `SiemRulesPageRejection.AllowsRetry`?
- Was dropping the non-terminal TTL deliberate (B1)? If some other process — a DBA job, partition
  rotation — reaps `adapter_checkpoints`, B1 is moot and I would rather know than guess.
- Should the number of `Failed` rows be exposed (log line count, metric)? With the TTL gone, nothing
  else tells an operator the table is growing, and the retention window is the only thing bounding it.
