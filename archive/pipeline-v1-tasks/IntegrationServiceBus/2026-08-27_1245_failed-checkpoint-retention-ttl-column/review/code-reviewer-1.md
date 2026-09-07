# Code review — failed-checkpoint retention TTL column

## Scope reviewed

`d53e347d48b71d7befa9bce6b9086b909a547093` → working tree (`feat/failed-checkpoint-retention-ttl`),
`src/` only, including the three untracked files. 17 files.

Areas touched: `Domain` (`CheckpointEntry`, `ICheckpointRepository`, `ConfigurationKeys`),
`Application` (`ProcessEventCommandHandler`, `CheckpointRecoveryHandler` doc), `Infrastructure.Core`
(`InMemoryCheckpointRepository`, `CheckpointCleanupJob`), `Infrastructure.Postgres`
(`CheckpointRepository`, `CheckpointDbContext`, migration + Designer + snapshot), tests in
`API.UnitTests/Checkpoints`, `Application.UnitTests`, and the new
`Infrastructure.Core.UnitTests/InMemoryCheckpointRetentionTests.cs`.

Docs consulted: `CLAUDE.md`, `docs/coding-standards.md`, `docs/clean-architecture.md`.

Baseline verification: every behaviour claim below was checked against
`git show d53e347d:<file>` for the affected lines, and against the surrounding call graph
(`IsbPlatformEventDispatcher`, `RabbitMqConsumerService`, `AdapterResult` in the
`Cymulate.Integration.Client` source at `/Users/user/Dev/IntegrationInfra`).

### Things I checked and found correct — no findings

- **The three migration artifacts are mutually consistent.**
  `20260827124500_AddCheckpointRetainUntil.Designer.cs` and `CheckpointDbContextModelSnapshot.cs`
  differ only in the four lines EF itself varies (the `Migration` attribute, the class declaration,
  `BuildTargetModel` vs `BuildModel`, the extra `using`). The Designer's model differs from the
  previous migration's Designer by exactly the added `RetainUntilUtc` property. The migration id
  `20260827124500` sorts after `20260610120000`. `MigrateAsync()` (called from
  `Infrastructure.Postgres/DependencyInjection.cs:57`) will apply
  `ALTER TABLE adapter_checkpoints ADD retain_until_utc timestamptz` and nothing else — a nullable
  column with no default, so PostgreSQL takes a brief metadata-only ACCESS EXCLUSIVE lock and does
  not rewrite the table. Nothing here misbehaves at `MigrateAsync()`.
- **SQL and index behaviour.** `SetRetentionAsync`'s `WHERE` is an exact match on
  `ix_adapter_checkpoints_tenant_correlation_platform_category` (unique) — index scan, one row.
  `GetRecoverableAsync`'s added `retain_until_utc IS NULL` and `DeleteExpiredAsync`'s added
  `(retain_until_utc IS NULL OR retain_until_utc <= @now)` are post-filters over the existing
  `ix_adapter_checkpoints_tenant_claim` / `ix_adapter_checkpoints_updated_at_utc` scans. Retained
  rows are the rare minority, so neither changes the plan or the selectivity in a way worth an index.
- **`retain_until_utc` genuinely survives the two whole-row write paths in Postgres.** The
  `UpsertAsync` CTE names the column in neither its INSERT list nor its `DO UPDATE SET` list, and
  `TryAcquireExecutionLockAsync`'s `DO UPDATE SET` names only claim/event/updated columns. The
  in-memory store, which does replace the whole entry, now patches the hold across both. That part
  of the design is sound and the tests pin it in both stores.
- **Conventions.** Explicit types throughout the new code (no `var`), `_camelCase` fields,
  `PascalCase` private constants, structured log templates with named placeholders, braces on every
  branch, no `#region`, no stale `TODO`. `SetRetentionAsync`'s new `retainUntilUtc` goes at the end
  of the real parameter list, before `cancellationToken` — correct per `docs/coding-standards.md`.
  `Checkpoint:FailedRetentionDays` has a code default and is reachable from Secrets Manager as
  `checkpoint:failed_retention_days`; the `Checkpoint:` section has no `appsettings.json` presence
  today, so nothing was skipped there.
- **`CheckpointCleanupJob`'s new comment is accurate.** `DeleteStoppedCheckpointsAsync` does run
  before `DeleteExpiredAsync` and does carry no retention predicate, so a stopped run's row is
  removed within one 30-minute tick regardless of a hold — well inside the 24h stop-request TTL.

---

## Blockers

- **B1** — `Applications/…Application/Commands/ProcessEventCommandHandler.cs:1302-1308` and
  `Infrastructure/…Postgres/Persistence/CheckpointRepository.cs:769-796`

  - **Problem:** The failure branch retains the row but never releases the execution claim, and
    nothing else on that path does either. On `d53e347d` the row was deleted, which took
    `claimed_by_instance` with it; now the row survives owned by the pod that just finished.
    That breaks the broker retry loop, because `TransientFailure` is exactly the status
    `IsbPlatformEventDispatcher.cs:258-268` turns into `MessageOutcome.Retry` — the consumer
    republishes the *same* body with the *same* `correlation_id`. The redelivery calls
    `TryAcquireExecutionLockAsync`, whose `DO UPDATE ... WHERE` requires
    `claimed_by_instance IS NULL OR claimed_at_utc < staleBefore OR claimed_by_instance = {instanceId}`.
    `claimed_at_utc` was renewed by the heartbeat right up to the end of the run, so on any *other*
    replica the lock is denied for the full `StaleClaimThreshold` (10 minutes by default).
    `Handle` then returns `FailureResult(..., "EXECUTION_LOCKED")`, and because `EXECUTION_LOCKED`
    is not in `AdapterResult.NonTransientErrorCodes` it is itself classified transient — so the
    redelivery is republished again. With the shipped defaults (`MaxRetryAttempts` 5,
    `InitialRetryDelayMs` 1000, multiplier 2.0) the whole budget is spent in roughly 31 seconds,
    far short of the 10-minute claim expiry, and the message dead-letters. A transient collector
    failure that used to retry cleanly now DLQs on any multi-replica deployment.
  - **Suggestion:** Release the claim in the same statement that stamps the hold, so there is no
    window and no second round trip:

    ```sql
    UPDATE adapter_checkpoints
    SET retain_until_utc = {retainUntilUtc},
        claimed_by_instance = NULL,
        claimed_at_utc = NULL,
        updated_at_utc = {nowUtc}
    WHERE ...
    ```

    and mirror it in `InMemoryCheckpointRepository.SetRetentionAsync`
    (`Infrastructure.Core/Services/InMemoryCheckpointRepository.cs:334-349`) by also setting
    `existing.ClaimedByInstance = null; existing.ClaimedAtUtc = null;`. Calling
    `TryReleaseClaimAsync` after `TryRetainCheckpointAsync` in the handler would also work but
    leaves a two-statement window and an extra round trip, so I would not take it.

    Note this must land together with **B2**, not on its own: releasing the claim while
    `GetRecoverableAsync` still excludes on `IS NULL` leaves the row unclaimed *and* invisible to
    the sweep, which is the worst of both.

- **B2** — `Infrastructure/…Postgres/Persistence/CheckpointRepository.cs:675` and
  `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs:291`

  - **Problem:** Nothing ever clears `retain_until_utc`, and the recovery sweep excludes on
    `RetainUntilUtc == null` rather than on the deadline. `UpsertAsync` deliberately does not touch
    the column, `TryAcquireExecutionLockAsync` deliberately preserves it (pinned by two of the new
    tests), and `TransitionFromScheduledWaitAsync` copies it forward
    (`InMemoryCheckpointRepository.cs:387`). So once a row is stamped it is invisible to
    `GetRecoverableAsync` for the rest of its life, including after a *new* execution takes it over.
    That is reachable: a redelivered message reuses the correlation id, `TryAcquireExecutionLockAsync`
    grants the lock on the same pod (or on any pod once the claim goes stale), and
    `ExecuteWithResumeAsync` resumes from the retained checkpoint — a live, progressing run on a row
    the sweep will never offer. If that pod dies, nothing resumes the run and no done message is ever
    published; `DeleteExpiredAsync` will not remove it either, because `updated_at_utc` keeps being
    renewed. That is precisely the stranded-run outcome `CheckpointRecoveryHandler` exists to
    prevent. The two predicates also disagree with each other: `DeleteExpiredAsync` treats
    `retain_until_utc <= now` as "hold over", `GetRecoverableAsync` treats any non-null as "held".
  - **Suggestions:**
    1. Do both of the following — preferred, because (a) alone still strands a taken-over row for
       up to seven days, and (b) alone leaves an expired hold hiding a row indefinitely.
       - (a) Make the sweep agree with the cleanup job. In `CheckpointRepository.GetRecoverableAsync`
         capture `DateTime nowUtc = DateTime.UtcNow;` and use
         `(c.RetainUntilUtc == null || c.RetainUntilUtc <= nowUtc)`; same change in
         `InMemoryCheckpointRepository.GetRecoverableAsync`.
       - (b) Let a new execution supersede the artifact. Add `retain_until_utc = NULL` to
         `TryAcquireExecutionLockAsync`'s `DO UPDATE SET` list
         (`CheckpointRepository.cs:614-618`), and in `InMemoryCheckpointRepository.cs:262-269`
         replace the preserve-the-hold branch with `entry.RetainUntilUtc = null;`. A run starting
         again on this row means the failed run's inspection window is over — and it keeps the hold
         from outliving the failure it describes. This contradicts
         `TryAcquireExecutionLockAsync_does_not_clear_an_existing_retention_hold` in both test files,
         which would need to invert.
    2. Keep the hold across a re-trigger (as written) and take only (a), if preserving the artifact
       across a retry is a deliberate product requirement. Trade-off: a re-triggered run then runs
       for up to seven days with no recovery sweep behind it, which I would not accept without an
       explicit decision recorded in the PR body.

## Important

- **I1** — `Infrastructure/…Postgres/Persistence/CheckpointRepository.cs:769-796`

  - **Problem:** `SetRetentionAsync` has no ownership guard. Every other targeted write on this table
    that a *running execution* issues carries one — `TransitionToScheduledWaitAsync:763`
    (`AND claimed_by_instance = {instanceId}`), `RenewClaimAsync:571`, `ReleaseClaimAsync:540`, and
    the upsert's four-clause `DO UPDATE ... WHERE`. A superseded leg that reaches
    `CompleteExecutionAsync` before its heartbeat notices the claim moved will stamp a hold on a row
    a successor now owns and is actively writing to. `R3_RefusedWriteTakesPrecedenceOverRetention`
    covers the case where the store *detected* the loss; it does not cover the window before
    detection. Combined with **B2** this hides the successor's live run from the sweep permanently.
    (`DeleteAsync` on the success path is likewise unguarded — that is pre-existing on `dev` and I am
    not filing it against this PR — but retention is strictly worse than delete here, because delete
    lets the successor's next upsert re-INSERT a clean row while a stamped row silently persists.)
  - **Suggestion:** Add an `instanceId` parameter to `ICheckpointRepository.SetRetentionAsync`, placed
    after `retainUntilUtc` and before `cancellationToken`, and append
    `AND claimed_by_instance = {instanceId}` to the `WHERE`. `TryRetainCheckpointAsync`
    (`ProcessEventCommandHandler.cs:1149`) passes `InstanceId`, which it already has. The `false`
    return then also means "not ours", which that method already logs at Debug — reword that message
    to `"No owned checkpoint row to hold for ..."`. Mirror the guard in
    `InMemoryCheckpointRepository.SetRetentionAsync` with the same
    `string.Equals(existing.ClaimedByInstance, instanceId, StringComparison.Ordinal)` check that
    `TransitionToScheduledWaitAsync:325` uses.

- **I2** — `Applications/…Application/Commands/ProcessEventCommandHandler.cs:884` and `:1115-1146`

  - **Problem:** The unhandled-exception path deletes rather than retains.
    `HandleUnhandledExceptionAsync` builds `AdapterResult.FailureResult(..., "UNHANDLED_EXCEPTION", ex)`
    — a genuine failure by the same definition the retention branch at `:1302` uses — but calls
    `TryDeleteCheckpointUnlessRetainedAsync`, which only honours a hold that some *earlier* run
    already stamped. So a pod killed or a vendor connection dropped mid-collection, the failure most
    worth having the row for, leaves nothing behind, while a tidy collector-reported failure does.
    The doc comment on `TryRetainCheckpointAsync` and the class comment on
    `InMemoryCheckpointRetentionTests` both describe the crash case as the thing retention is for,
    so the code and its own stated intent disagree. Secondarily, the read-then-delete in
    `TryDeleteCheckpointUnlessRetainedAsync` is not atomic — a concurrent `SetRetentionAsync` between
    the `GetAsync` and the `DeleteAsync` loses the hold.
  - **Suggestions:**
    1. Replace the call at `:884` with `await TryRetainCheckpointAsync(tenantId, platformEvent, category);`
       and delete `TryDeleteCheckpointUnlessRetainedAsync` entirely — preferred, because it makes the
       two failure paths agree, removes the extra `GetAsync` round trip, and closes the race for
       free (`SetRetentionAsync` already returns `false` when there is no row, which is the only
       thing the read was establishing).
    2. Keep the delete and say so explicitly: rename to
       `TryDeleteCheckpointPreservingHoldAsync`, and state in the PR body that crash-path
       checkpoints are deliberately not retained. Only worth taking if there is a reason the
       exception path must not hold rows that I cannot see from the diff.

- **I3** — `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs:69-70` and
  `UnitTests/…Infrastructure.Core.UnitTests/InMemoryCheckpointRetentionTests.cs:55-63`

  - **Problem:** The in-memory guard only *preserves* a hold when the incoming entry's is null; it
    does not stop an incoming entry from **setting or changing** one. Postgres can never do either,
    because the upsert names the column nowhere. So `UpsertAsync` with a non-null `RetainUntilUtc`
    writes the hold in memory and silently drops it in Postgres — the two stores disagree on the
    exact operation this change exists to keep in step. Worse, `SeedHeldAsync` seeds every held row
    in the new test class through precisely that divergent path, so the parity tests are built on
    the gap they are meant to pin: if the Postgres store later started writing the column on upsert,
    these tests would not notice. Latent in production today only because the handler never sets
    `RetainUntilUtc` on an entry it upserts.
  - **Suggestion:** Make the in-memory store mirror "this write never touches the column",
    unconditionally rather than conditionally. On the update arm replace lines 69-70 with
    `entry.RetainUntilUtc = existing.RetainUntilUtc;`, and on the `TryAdd` arm (line 41) set
    `entry.RetainUntilUtc = null;` before the add, matching the Postgres INSERT list. Then change
    `SeedHeldAsync` to seed through the real path — `UpsertAsync` → `SetRetentionAsync(hold)` →
    a second `UpsertAsync` carrying the desired `updatedAtUtc`, which now survives because the hold
    is preserved by the store rather than carried on the entry. That also removes the helper's
    comment about `SetRetentionAsync` stamping `UpdatedAtUtc` as a side effect.

- **I4** — `UnitTests/…Application.UnitTests/ProcessEventCommandHandlerTests.cs:2585` and
  `Tests/…API.UnitTests/Checkpoints/CheckpointRepositoryTests.cs:973`

  - **Problem:** No test anywhere asserts what happens to the *claim* when a hold is stamped, or that
    the next delivery of the same correlation id can still acquire the lock. `R2` verifies
    `SetRetentionAsync` was called and `DeleteAsync` was not; `SetRetentionAsync_sets_the_hold_and_leaves_the_rest_of_the_row_alone`
    positively asserts `persisted.ClaimedByInstance.Should().Be("pod-a")`, i.e. it *pins* the
    behaviour that causes **B1**. That is the coverage gap that let B1 through, and it is cheap to
    close.
  - **Suggestion:** Add two tests alongside the B1 fix.
    - In `CheckpointRepositoryTests`: seed a row claimed by `pod-a`, call `SetRetentionAsync`, then
      assert `await Sut.TryAcquireExecutionLockAsync(OwnedWrite(correlationId, owner: "pod-b", page: 0), "pod-b", TimeSpan.FromMinutes(10))`
      returns `true` immediately — no waiting out the stale threshold — and that the hold survives.
      Update the existing `ClaimedByInstance.Should().Be("pod-a")` assertion to
      `.Should().BeNull()`.
    - In `ProcessEventCommandHandlerTests`, extend `R2` so the `stampsRetention` cases also
      `_checkpointRepository.Verify(r => r.ReleaseClaimAsync(...), Times.Once)` (or verify the
      `instanceId`/null-claim argument on `SetRetentionAsync`, depending on which B1 shape you take).

- **I5** — rolling-deploy window; `Infrastructure/…Postgres/Persistence/CheckpointRepository.cs:672-680`

  - **Problem:** During the rollout, old pods run `d53e347d` code against the new schema. Their
    `GetRecoverableAsync` has no retention predicate, so an old replica's sweep will happily offer a
    row that a new replica just stamped and hand it to `CheckpointRecoveryHandler` for re-dispatch —
    for a run that has already published its failure done. On `d53e347d` there was nothing to pick
    up, because the row was deleted. Today the held claim (B1) buys ~10 minutes of accidental cover;
    once B1 is fixed and the claim is released, an old pod's sweep can take the row on its next tick.
    Confidence: the divergent predicate is certain from the diff; whether a re-dispatch actually
    produces a second done depends on `CheckpointRecoveryHandler` internals I did not trace end to
    end, so treat the consequence as likely rather than confirmed.
  - **Suggestions:**
    1. Ship the schema migration in one release and the handler change in the next — preferred. The
       column is purely additive and old code ignores it, so a migration-only release is free, and
       by the time any pod stamps a hold every replica knows to skip held rows.
    2. Accept the window and record it in the PR body plus the deploy runbook, with the mitigation
       that the sweep only offers rows whose claim is stale, so a fast rollout mostly closes it.
       Cheaper, but it trades a duplicate done message against a deploy step.

## Nits

- **N1** — `Infrastructure/…Postgres/Persistence/CheckpointRepository.cs:787`
  - **Problem:** `retainUntilUtc` is interpolated straight into `ExecuteSqlAsync` without going
    through the class's own `AsUtc` helper (`:392`). The current caller passes
    `DateTime.UtcNow + FailedRetention`, so `Kind` is `Utc` and it works — but
    `SetRetentionAsync` is public interface surface, and a caller passing a `DateTimeKind.Unspecified`
    value gets an Npgsql `ArgumentException` at run time instead of the normalisation the helper
    exists to provide.
  - **Suggestion:** `SET retain_until_utc = {AsUtc(retainUntilUtc)}`. One call, the helper is already
    in the file, and it matches what the raw-ADO upsert does for every other timestamptz value.

- **N2** — `Applications/…Application/Commands/ProcessEventCommandHandler.cs:59-61`
  - **Problem:** `FailedRetention` does not guard a non-positive configured value. A
    `checkpoint:failed_retention_days` of `0` from Secrets Manager stamps `retain_until_utc = now`,
    which reads as "held" to `GetRecoverableAsync` (which tests `IS NULL`) but "expired" to
    `DeleteExpiredAsync` — so a config typo silently turns retention into "hide this row from
    recovery and delete it on the ordinary TTL", which is strictly worse than either intended
    behaviour. Mostly moot once **B2(a)** lands, but the clamp is one line and stops the typo from
    being invisible.
  - **Suggestion:**
    ```csharp
    private TimeSpan FailedRetention => TimeSpan.FromDays(
        Math.Max(1, configuration.GetValue(
            ConfigurationKeys.Checkpoint.FailedRetentionDays,
            ConfigurationKeys.Checkpoint.DefaultFailedRetentionDays)));
    ```

- **N3** — `Infrastructure/…Postgres/Migrations/20260827124500_AddCheckpointRetainUntil.Designer.cs:1`
  - **Problem:** The hand-written Designer carries a UTF-8 BOM; every EF-generated Designer already
    in this folder (`20260610120000_AddAdapterStopRequests.Designer.cs` and the rest) does not. When
    someone next runs `dotnet ef migrations add` on a machine that has the tool, the regenerated
    file will differ from this one by an invisible first byte, which reads as a spurious change.
  - **Suggestion:** Strip the BOM —
    `perl -i -pe 's/^\xEF\xBB\xBF//ums' 20260827124500_AddCheckpointRetainUntil.Designer.cs`.
    `CheckpointDbContextModelSnapshot.cs` already had one on `dev`, so leave that file alone.

## Open questions

- Is a failed run's checkpoint *meant* to be resumed from? Retaining the row makes
  `ExecuteWithResumeAsync` (`ProcessEventCommandHandler.cs:1191`, "the resume decision is made
  on CHECKPOINT PRESENCE") resume the next delivery of the same correlation id from the failed leg's
  page, where `d53e347d` deleted the row and restarted from the beginning. Given that method's
  `<remarks>` treats restart-from-scratch as the bug it was written to fix, resuming may well be the
  improvement — but it is an unstated semantic change on the retry path and it is what decides
  between the two options under **B2**. Which is intended?
- `HandleAdapterNotFoundAsync` (`:794`) still deletes unconditionally, and
  `Handle_deletes_the_checkpoint_without_a_hold_when_no_adapter_is_found` pins that as deliberate.
  Is the distinction "an execution actually ran and left something to read" the intended rule? If so
  it argues for **I2** option 1, since the exception path *did* run.
- `AdapterResultStatus.Cancelled` is excluded from the retention set, so a run cancelled by anything
  other than the stop API takes the delete branch at `:1310`. Is a cancellation never worth an
  artifact, or is that just following the stop-request precedent?
