# Code review — failed-checkpoint retention TTL column

## Scope reviewed

`d53e347d48b71d7befa9bce6b9086b909a547093`…working tree — 14 modified files + 3 untracked, under `src/`.
Areas touched: `Application/Commands/ProcessEventCommandHandler`, `Application/Services/CheckpointRecoveryHandler`
(doc only), `Domain/Constants/ConfigurationKeys`, `Domain/Interfaces/ICheckpointRepository`,
`Domain/Models/CheckpointEntry`, `Infrastructure.Core/Jobs/CheckpointCleanupJob`,
`Infrastructure.Core/Services/InMemoryCheckpointRepository`, `Infrastructure.Postgres` (DbContext,
CheckpointRepository, one new migration + Designer + model snapshot), four test files.

Docs consulted: `CLAUDE.md`, `docs/clean-architecture.md`, `docs/coding-standards.md`.

Verified clean, no findings raised on:

- **Migration artifacts are mutually consistent.** `20260827124500_AddCheckpointRetainUntil.Designer.cs`
  differs from `CheckpointDbContextModelSnapshot.cs` only in the method name (`BuildTargetModel` vs
  `BuildModel`), and from the previous migration's Designer only by the four added `RetainUntilUtc`
  lines. Column type, name and nullability match `CheckpointDbContext.cs:95`. `MigrateAsync()`
  (`Infrastructure.Postgres/DependencyInjection.cs:57`) will apply a single `ADD COLUMN`; the timestamp
  sorts after `20260610120000`. `API.SiemRules` reaches the same `MigrateAsync` through
  `InitializePostgresCheckpointsAsync` — its `PostgresDatabaseInitializer.EnsureCreatedAsync` only
  creates the database, it does not bypass migrations.
- **The owner guard is satisfiable where it fires.** `TryAcquireExecutionLockAsync` sets
  `claimed_by_instance = InstanceId` at `ProcessEventCommandHandler.cs:145`, nothing on the completion
  path releases it before `FlushAndCleanupCheckpointAsync`, and owned `UpsertAsync` writes do not
  change the owner column. `SetRetentionAsync` therefore matches its row on the reported-failure path.
- **The preserve-on-write / clear-on-takeover split holds on every Postgres write path.**
  `UpsertAsync` names `retain_until_utc` in neither its INSERT list nor its `DO UPDATE SET` list
  (preserve); `TryAcquireExecutionLockAsync` sets it to `NULL` (clear); `TryClaimAsync`,
  `ReleaseClaimAsync`, `RenewClaimAsync`, `TransitionToScheduledWaitAsync`,
  `TransitionFromScheduledWaitAsync` and `ReleaseAllClaimsForInstanceAsync` are all targeted UPDATEs
  that leave the column alone. The in-memory store mirrors each of these deliberately.
- **No unowned-and-invisible window on the stamp itself.** Hold and claim release are one UPDATE, so a
  row is never both owned and held, and the pod-death window between stamping and publishing the done
  is covered by broker redelivery — collections are consumed `autoAck: false` and the message is not
  settled until `Handle` returns. That window is no worse than the delete it replaces.
- **SQL and planning.** `DeleteExpiredAsync` still leads with `updated_at_utc < @cutoff`, served by the
  `updated_at_utc` index; `GetRecoverableAsync` still leads with the
  `(tenant_id, claimed_by_instance, claimed_at_utc)` index. The new `retain_until_utc IS NULL OR
  retain_until_utc <= @now` is a residual filter in both. No new index needed.
- **Layering.** Port on `ICheckpointRepository` in `Domain`, adapters in Infrastructure. No new
  application → Infrastructure reference.

---

## Blockers

- **B1** — `Infrastructure.Postgres/Persistence/CheckpointRepository.cs:676-678` (and
  `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs:287-288`)
  - **Problem:** A hold hides a row from the recovery sweep only *while it is unexpired*. Once
    `retain_until_utc` passes, the row satisfies every clause of `GetRecoverableAsync`: status `Idle`,
    `claimed_by_instance IS NULL` (the stamp released it), and now `retain_until_utc <= now`. The
    sweep will pick it up, `TryClaimAsync` it, and re-dispatch a `ProcessEventCommand` built from the
    row's `platform_event_json` — resuming, seven days later, a collection that already published its
    failure done. `RecoverAsync` (`CheckpointRecoveryHandler.cs:77-115`) applies no age ceiling.
    This is not a narrow race: `CheckpointRecoveryJob.DefaultIntervalMinutes` is 5 and
    `CheckpointCleanupJob.DefaultIntervalMinutes` is 30, so after the hold expires the sweep almost
    always ticks before the cleanup that would delete the row. The downstream result is a duplicate
    collection and a second done message for a correlation id the platform closed a week earlier —
    and it happens to *every* retained row on schedule, not to an unlucky one. The prior behaviour
    (delete on failure) made this impossible.
  - **Suggestions:**
    1. Make a hold terminal rather than a timed hide — preferred, because it removes the resurrection
       entirely rather than shrinking the window. In `GetRecoverableAsync` (both stores) exclude *any*
       row carrying a hold: `c.RetainUntilUtc == null`. In `DeleteExpiredAsync` make an expired hold
       its own deletion trigger instead of an additional permission:
       ```csharp
       .Where(c => c.Status != CheckpointStatus.ScheduledWait
                   && (c.RetainUntilUtc == null
                       ? c.UpdatedAtUtc < cutoffUtc
                       : c.RetainUntilUtc <= nowUtc))
       ```
       A held row is then invisible to the sweep for its whole life and deleted the moment the hold
       lapses. `TryAcquireExecutionLockAsync` still clears the hold, so a redelivery or an explicit
       re-trigger resumes the row normally — the only path that loses is the sweep, which is exactly
       the one that must not resurrect a run that already reported done.
    2. If a retained row must stay sweep-eligible after expiry, park it in a status the sweep already
       skips and flip it back on takeover — more moving parts, and it collides with `ScheduledWait`'s
       own meaning, so I would not take this.
  - Note the two tests that currently pin the wrong behaviour and would need to invert with the fix:
    `CheckpointRepositoryTests.cs:1145` (`GetRecoverableAsync_offers_a_row_whose_hold_has_expired`) and
    `InMemoryCheckpointRetentionTests.cs:228`. Their stated rationale — "hiding it would strand it in
    the window between the hold expiring and the TTL cutoff" — does not hold: a stamped row's
    `updated_at_utc` is set at stamp time, so by expiry it is already days past the 24h cutoff and
    the very next cleanup tick deletes it. There is no stranding window to protect against.

## Important

- **I1** — `Application/Commands/ProcessEventCommandHandler.cs:1116-1145`, asserted by
  `Application.UnitTests/ProcessEventCommandHandlerTests.cs:2670`
  - **Problem:** `TryDeleteCheckpointUnlessRetainedAsync` can only see a hold that was stamped *later
    in the same `Handle` call*, because `TryAcquireExecutionLockAsync` at line 145 sets
    `retain_until_utc = NULL` before the adapter ever runs (`CheckpointRepository.cs:618`). So the
    scenario the guard is written for — an adapter that throws mid-collection leaving "a row worth
    reading" — still deletes the row outright: no hold was ever stamped on that path, and any hold
    from a previous run was cleared at lock acquisition. Its only reachable purpose is the narrow case
    where `TryRetainCheckpointAsync` already ran and `PublishCompletionEventAsync` then threw.
    The test asserting otherwise passes only because `ArrangeStampingResume`
    (`ProcessEventCommandHandlerTests.cs:1800-1819`) mocks `TryAcquireExecutionLockAsync` as a no-op
    returning `true`, so the seeded `held.RetainUntilUtc` survives into `GetAsync`. Against either real
    store it would be `null`. This is a test that would keep passing if production diverged, and its
    docstring states the opposite of what production does.
  - **Suggestions:**
    1. If an exception-terminated run should be retained (I think it should — it is the failure most
       worth inspecting), call `TryRetainCheckpointAsync(tenantId, platformEvent, category)` in
       `HandleUnhandledExceptionAsync` at line 885 instead of the delete wrapper. The claim is still
       held there: the `catch` at line 379 has already taken every claim-loss exception, so line 396
       is reached only by an execution that still owns the row, and `SetRetentionAsync` will match.
       The result is reported as `UNHANDLED_EXCEPTION` failure anyway, so the two paths agree.
    2. If exception-path retention is deliberately out of scope, keep the wrapper (it is needed for
       the publish-throws case) but rewrite the test's docstring to name that case, and re-point the
       test at a fake store whose `TryAcquireExecutionLockAsync` clears `RetainUntilUtc` the way both
       real stores do — otherwise the mock keeps certifying an unreachable state.

- **I2** — `Application/Commands/ProcessEventCommandHandler.cs:1304-1310`
  - **Problem:** Keeping the row on a failure silently changes what a retry does. Before this change a
    `Failure`/`TransientFailure` deleted the row, so the broker's redelivery found no checkpoint and
    started the collection fresh. Now the row survives with its progress intact, the redelivery's
    `TryAcquireExecutionLockAsync` preserves those progress fields, and `ExecuteWithResumeAsync`
    resumes — the resume decision is made on checkpoint presence
    (`ProcessEventCommandHandler.cs:1193`). That is arguably the better behaviour, but it is a
    behaviour change on the retry path that the diff does not mention and no test covers, and for a
    `ValidationFailure` or a failure caused by corrupt adapter state it means every retry replays the
    same broken position instead of starting clean.
  - **Suggestions:**
    1. Keep resume-on-retry, say so explicitly in the PR body, and add a handler test: stamp a hold
       on a `TransientFailure`, redeliver the same correlation id, assert the adapter is invoked
       through `ResumeAsync` with the stored page rather than `ProcessAsync`. Preferred — resuming is
       the point of a checkpoint, and the change is only dangerous while it is undocumented.
    2. If a failed run must restart clean, exclude `ValidationFailure` from the stamped set (nothing
       useful was collected) and stamp the other two, so the resume-vs-restart split follows the
       status rather than falling out of the retention decision.

- **I3** — `Application/Commands/ProcessEventCommandHandler.cs:139` read together with
  `CheckpointRepository.cs:793`
  - **Problem:** `platform_event_json` is `JsonSerializer.Serialize(platformEvent)`, and
    `PlatformEvent.Credentials` is a public read/write dictionary holding *decrypted* vendor
    credentials by that point — `PopulateCredentialsFromEncryptedBlob` (line 663) decrypts SIEM-rules
    credentials at line 80, before the lock, and the collector path arrives with
    `GetCredentials()` output already flattened (`API/Controllers/EventsController.cs:481`). Until now
    that row was deleted at the end of every run and TTL-bounded to 24h. This change extends the
    lifetime of stored plaintext vendor credentials to 7 days by default and up to 30 by
    configuration, for exactly the runs most likely to be looked at by a human. Nothing in the diff
    flags it.
  - **Suggestions:**
    1. Strip the credentials as part of the stamp — the held row exists to be read, not to be
       resumed, so it does not need them. In `SetRetentionAsync`'s UPDATE add
       `platform_event_json = platform_event_json - 'credentials'` (confirm the serialized key casing
       first; the client package carries a lowercase `credentials` wire name). Mirror it in
       `InMemoryCheckpointRepository.SetRetentionAsync`. This composes with B1's fix: once a held row
       is never sweep-eligible, nothing needs the credentials back.
    2. If the credentials must stay for a manual re-drive, say so in the PR body and get the retention
       window signed off by whoever owns credential handling — 7 days of plaintext is a decision, not
       an implementation detail.

## Nits

- **N1** — `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs:69`, `:341`, `:344`
  - **Problem:** Three new one-line `if`s without braces; `docs/coding-standards.md` says always brace.
  - **Suggestion:** Add the braces. (The surrounding legacy is unbraced too — leave that alone.)

- **N2** — `Domain/Interfaces/ICheckpointRepository.cs:53` vs
  `API.UnitTests/Checkpoints/CheckpointRepositoryTests.cs:975` and
  `Infrastructure.Core.UnitTests/InMemoryCheckpointRetentionTests.cs:142`
  - **Problem:** The same change adds two comments giving opposite accounts of the same fact: the
    interface says "`EXECUTION_LOCKED` is transient, so it would re-retry to dead-letter", both test
    docstrings say "`EXECUTION_LOCKED` is not treated as transient, so the message burns its retry
    budget". One is wrong, and both are load-bearing rationale for why the stamp releases the claim.
  - **Suggestion:** Check how `RabbitMqConsumerService` classifies the result and keep one wording;
    delete the other two rather than restating it in three places. The claim release is correct
    either way, so the fix is editorial — but a wrong rationale is what the next reader will act on.

- **N3** — `Domain/Constants/ConfigurationKeys.cs:380`
  - **Problem:** `NormalizeFailedRetentionDays` is a pure function with three branches (clamp low,
    pass through, clamp high) and no test anywhere in the solution.
  - **Suggestion:** Add an `[InlineData]` theory to `Application.UnitTests` covering `-1`, `0`, `7`,
    `31` → `7`, `7`, `7`, `30`. Three lines, and it is the only part of this change that can be
    executed without Docker.

- **N4** — `CheckpointRepositoryTests.cs:1199` and `InMemoryCheckpointRetentionTests.cs:267`
  - **Problem:** `DeleteExpiredAsync_keeps_a_held_row_whose_deadline_passed_while_it_is_still_progressing`
    pins a combination production cannot reach: a fresh `updated_at_utc` on a still-held row. Every
    writer that refreshes `updated_at_utc` either goes through `TryAcquireExecutionLockAsync`, which
    nulls the hold, or is an owned `UpsertAsync` — and a held row is unowned, so an owned upsert cannot
    match it. The test encodes a rule nothing depends on.
  - **Suggestion:** Delete both, or keep one and reword the docstring to say it guards the invariant
    rather than describing a run "resumed on day eight", which cannot happen with a hold still set.

- **N5** — `Infrastructure.Postgres/Persistence/CheckpointRepository.cs:671-672`
  - **Problem:** Two separate `DateTime.UtcNow` reads in one method, so `nowUtc` and the base of
    `staleBeforeUtc` differ by a few microseconds for no reason.
  - **Suggestion:** `DateTime nowUtc = DateTime.UtcNow; DateTime staleBeforeUtc = nowUtc - staleThreshold;`
    — and take the `var` off `staleBeforeUtc` while the line is being touched.

- **N6** — no `Checkpoint` section in any host `appsettings.json`
  - **Problem:** `Checkpoint:FailedRetentionDays` has a code default but appears nowhere an operator
    would look, and the secret-store override name (`checkpoint:failed_retention_days`) is
    undiscoverable from the repo.
  - **Suggestion:** Consistent with the existing `Checkpoint:*` keys, which are also absent — so this
    is optional. If you add it, add the whole section with its current defaults
    (`StaleClaimThresholdMinutes: 10`, `HeartbeatIntervalMinutes: 5`, `FailedRetentionDays: 7`) rather
    than the one new key on its own.

- **N7** — `Domain/Constants/ConfigurationKeys.cs:358-379`, `Domain/Interfaces/ICheckpointRepository.cs:43-55`,
  and the test docstrings throughout
  - **Problem:** The change adds roughly 60 lines of explanatory prose for about 90 lines of code —
    seven-line `<remarks>` narrating why a clamp clamps, four-line docstrings above single-assert
    tests. `CLAUDE.md` and `docs/coding-standards.md` both call this out as the shape that keeps
    getting deleted here, and it is the prose that has already gone wrong twice in this diff (N2, and
    the rationale under B1).
  - **Suggestion:** Keep the two that state a real invariant — "the hold and the claim release are one
    statement" on `SetRetentionAsync`, and "the upsert names no `retain_until_utc` column" on the
    in-memory preserve branch. Move the rest to the PR description.

- **N8** — `Infrastructure.Core/Jobs/CheckpointCleanupJob.cs:60`
  - **Problem:** The table's steady-state size changes from "rows for in-flight runs" to "rows for
    every failed run in the last seven days", and nothing reports that. A collector failing on a
    frequent schedule now leaves a row per run per correlation id, and the first sign of a problem
    would be table size.
  - **Suggestion:** Have the cleanup job log the held-row count alongside the deleted count — a
    `COUNT(*) WHERE retain_until_utc > now()` on the same tick — so growth is visible before it
    matters.

## Open questions

- Is the retention window meant to survive a re-trigger of the same correlation id? As written, any
  new run over the row clears the hold, so a run that fails, is retried an hour later, and fails again
  keeps only the second failure's state. Intended, or should a hold extend rather than reset?
- Should `ValidationFailure` retain at all? It typically fails before any collection, so the stamped
  row carries zeroed progress and nothing to inspect — while still holding 7 days of the credential
  blob covered in I3.
- What is meant to *read* these rows? Nothing in this change exposes them (no API, no query), so the
  intended workflow is presumably direct SQL. If a read path is planned, its shape decides whether
  B1's fix (holds are terminal) is acceptable or whether a held row must stay re-drivable.

## Untested and materially risky

`Tests/…API.UnitTests/Checkpoints/CheckpointRepositoryTests.cs` is Testcontainers-backed and did not
run in this environment, so **every one of the new raw-SQL behaviours is unexecuted**: the
`SetRetentionAsync` UPDATE and its owner guard, `retain_until_utc = NULL` in the lock upsert, the two
new predicates, and the claim-release-with-stamp. The suites that did run (`Application.UnitTests`
93/93, `Infrastructure.Core.UnitTests` 417/417) are Moq-driven or in-memory and structurally cannot
catch I1 — the in-memory store is the one place the interaction is exercised end to end, and its
`TryAcquireExecutionLockAsync` is a stub that always succeeds. Run the Postgres suite with Docker
available before merging; it is where B1 and I1 become visible.
