# Code review — failed-checkpoint terminal status + retention TTL

## Scope reviewed

`d53e347d..worktree` — 13 modified files + 3 new test files under `src/`. Areas touched: checkpoint
lifecycle (`ProcessEventCommandHandler`, `CheckpointRecoveryHandler`), the checkpoint port and entity,
both checkpoint stores (Postgres raw SQL + in-memory), `CheckpointCleanupJob`, `ConfigurationKeys`,
`CheckpointStatus`. Docs consulted: `CLAUDE.md`, `docs/clean-architecture.md`,
`docs/coding-standards.md`, the `adapter_checkpoints` migrations and `CheckpointDbContext`.

Verified independently rather than assumed:

- `platform_event_json` is `jsonb` (`20250101000003_AddPlatformEventJson`), so the `-` key-delete is
  the right operator; `status` is `text` with `HasConversion<string>()`, so the `'Failed'` / `'Idle'`
  literals in raw SQL match what EF reads back.
- The two cleanup horizons **cannot cross**. `DeleteExpiredAsync` excludes `Failed`,
  `DeleteTerminalExpiredAsync` matches only `Failed` — status-disjoint, not time-disjoint, so a
  misconfigured retention cannot reap a live row nor strand a terminal one. Both directions are tested.
- **Stop-request deletion is unaffected.** `DeleteStoppedCheckpointsAsync` has no status predicate and
  runs first in the job; stop-request rows keep the TTL cutoff. Both are covered by tests.
- The owner guard on `MarkFailedAsync` is **satisfiable at both call sites**: the handler holds the
  claim taken by `TryAcquireExecutionLockAsync` (heartbeat-renewed, and every claim-loss path returns
  before `CompleteExecutionAsync`), and `EndUnservableRunAsync` takes its own claim at
  `CheckpointRecoveryHandler.cs:828` before marking at `:871`.
- The strip's key names are **not** stand-ins: `InMemoryCheckpointRepositoryTerminalStatusTests.cs:31`
  serializes a real `PlatformEvent` the way the handler does and asserts the pre-strip payload contains
  `CheckpointEntry.CredentialsJsonKey`, so a serializer that started emitting `credentials` would fail
  the test rather than silently skip the strip.
- I ran the 24 `API.UnitTests` checkpoint tests that do not need Docker
  (`FailedRunLeavesATerminalRowTests`, `CheckpointRecoveryCompatibilityTests`): all pass. The new
  handler test drives the real handler, the real in-memory store and the real sweep — not mocks.
- The new deletes plan fine on `ix_adapter_checkpoints_updated_at_utc`: `updated_at_utc < cutoff` is the
  selective end of the index (old rows are the minority), status is a cheap filter. No new index needed.

## Blockers

- **B1** — `Infrastructure.Postgres/Persistence/CheckpointRepository.cs:544-553` (and
  `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs:272-289`)
  - **Problem:** `TryClaimAsync` has no status predicate, and the terminal mark **releases the claim**.
    The sweep is a select-then-claim: `GetRecoverableAsync` (now excluding `Failed`) at
    `CheckpointRecoveryHandler.cs:85`, then per row a validate/deserialize/`CanReadStoredStateAsync`
    stretch, then `TryClaimAsync` at `:295`. A row marked `Failed` inside that window has
    `claimed_by_instance IS NULL`, so the claim UPDATE matches, the sweep re-dispatches a run whose
    `done` was already published, and `TryAcquireExecutionLockAsync` flips the row back to `Idle` so
    nothing downstream can tell. Before this change the same window existed but the row was **deleted**,
    so the UPDATE matched 0 rows and the sweep skipped — this is a regression, not pre-existing. Two
    replicas sweeping the same partition every few minutes is enough: replica A's dispatch fails and
    marks, replica B is still inside `CanReadStoredStateAsync` on the same row from the same tick.
  - **Suggestion:** add the status guard to the claim itself so it stays one statement:
    ```sql
    WHERE tenant_id = {tenantId} ... AND status <> 'Failed'
      AND (claimed_by_instance IS NULL OR claimed_at_utc < {staleBeforeUtc})
    ```
    Mirror it in `InMemoryCheckpointRepository.TryClaimAsync`, which today returns `true`
    unconditionally — even for a key that does not exist:
    `if (!_store.TryGetValue(key, out CheckpointEntry? existing) || existing.Status == CheckpointStatus.Failed) { return Task.FromResult(false); }`.
    Both call sites (`CheckpointRecoveryHandler.cs:295` and `:828`) only ever want a non-terminal row,
    so nothing legitimate is refused.

## Important

- **I1** — `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs:209-220` and
  `Infrastructure.Postgres/Persistence/CheckpointRepository.cs:466-468`
  - **Problem:** the strip removes the two *top-level* credential carriers, but the encrypted
    credentials blob at `Payload.credentials` is left in the retained row. It is a real shape, not a
    hypothetical: `ProcessEventCommandHandler.cs:475` reads `payload.credentials` for SIEM rules and
    `PlatformEventFactory.cs:226` writes `credentials = encryptedCredentials` into the mitigation
    payload. The blob is ciphertext, so this is not a plaintext leak — but the point of the change is
    that a row now outlives its run by seven days, and one of the two carriers was not considered.
  - **Suggestion:** strip the nested key in the same statement. Postgres:
    ```sql
    platform_event_json = (platform_event_json - CAST({credentialsKey} AS text)
                                               - CAST({headersKey} AS text))
                          #- ARRAY['Payload','credentials']
    ```
    In-memory, alongside the two `Remove` calls:
    `if (platformEvent[nameof(PlatformEvent.Payload)] is JsonObject payload) { payload.Remove("credentials"); }`.
    Name the nested key with a third const on `CheckpointEntry` so both stores and the tests share it,
    as the existing two do.

- **I2** — `Infrastructure.Postgres/Persistence/CheckpointRepository.cs:668-669` and
  `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs:340-348`
  - **Problem:** reviving `Failed` → `Idle` on lock acquisition is right (it stops a pod loss mid-retry
    stranding the run), but the ON CONFLICT deliberately does not reset progress, and
    `ExecuteWithResumeAsync` keys the resume purely on the row existing plus `CanResumeFrom` —
    `RetryCount` is logged, not consulted (`ProcessEventCommandHandler.cs:1196-1202`). So a broker
    redelivery after a reported failure now **resumes from the failed run's checkpoint** where it
    previously restarted from scratch, because the row used to be deleted. For `TransientFailure` that
    is probably what you want; for `Failure` / `ValidationFailure` the platform has already been told
    the run failed, and the retry silently skips every page the failed attempt collected.
  - **Suggestions:**
    1. Reset progress on the terminal→live transition only, keeping the resume for every other conflict —
       preferred, because it makes "the row was terminal" mean the same thing as the delete it replaced:
       ```sql
       current_page = CASE WHEN adapter_checkpoints.status = 'Failed' THEN EXCLUDED.current_page ELSE adapter_checkpoints.current_page END,
       -- same shape for processed_items, processed_findings, sequence_id, cursor_token,
       -- last_processed_id, adapter_state_json
       ```
       with the matching branch in the in-memory store (it already replaces the whole entry, so it only
       needs to stop copying the old status forward).
    2. Keep the resume and say so explicitly — add a test that pins "a redelivery after a reported
       failure resumes from page N", so the semantics are a decision rather than a side effect of not
       resetting.

- **I3** — `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs:182-207`
  - **Problem:** `Copy` is a second hand-maintained list of all 23 `CheckpointEntry` properties; the
    first is inline in `TransitionFromScheduledWaitAsync` (`:425-452`). Both are correct today (I
    checked every property), but a new property added to `CheckpointEntry` and missed here is silently
    zeroed by the terminal mark, and this store is the test double behind most of the checkpoint suite.
  - **Suggestion:** have `TransitionFromScheduledWaitAsync` call `Copy(existing)` and then set the four
    fields it actually changes (`ClaimedByInstance`, `ClaimedAtUtc`, `UpdatedAtUtc`, `Status`,
    `ScheduledResumeAtUtc = null`), leaving exactly one field list in the file.

## Nits

- **N1** — `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs:161-171`, `:213-219`,
  `:245-246`
  - **Problem:** new one-line `if` bodies and the `foreach` in `DeleteTerminalExpiredAsync` are
    unbraced. `docs/coding-standards.md` — "Always brace, even a one-line `if`" — applies to new code;
    the surrounding unbraced code is history, not licence.
  - **Suggestion:** brace the new bodies. Leave the untouched ones alone.

- **N2** — `Infrastructure.Core/Jobs/CheckpointCleanupJob.cs:37-50` vs
  `Infrastructure.Core/DependencyInjection.cs:275-292`
  - **Problem:** the job now reads its two horizons from two different places — `TtlHours` from the
    Quartz `JobDataMap` baked at registration, `FailedRetentionDays` from `IConfiguration` at fire time.
    Two mechanisms for one pair of settings is what makes the next person look in the wrong file, and
    the new `IConfiguration` constructor dependency exists only for this.
  - **Suggestion:** add `public const string FailedRetentionDaysKey = "FailedRetentionDays";` and
    `.UsingJobData(CheckpointCleanupJob.FailedRetentionDaysKey, retentionDays.ToString())` next to the
    existing `.UsingJobData(TtlHoursKey, ...)`, applying the `MinimumFailedRetentionDays` floor there;
    then drop the `IConfiguration` parameter. Keeps the parse-and-floor in one place and the job free of
    configuration lookups.

- **N3** — `Infrastructure.Postgres/Persistence/CheckpointRepository.cs:466-468`
  - **Problem:** the in-memory strip guards `JsonNode.Parse(...) is not JsonObject` and returns the
    payload untouched; the SQL does not. On a `platform_event_json` that is a JSON scalar, `jsonb - text`
    raises `cannot delete from scalar`, which fails the whole UPDATE — so status and claim release do
    not land either, and the row is left live and claimed. Unreachable today (the column is always a
    serialized object or NULL), but the two stores disagree about it.
  - **Suggestion:** wrap the assignment:
    `platform_event_json = CASE WHEN jsonb_typeof(platform_event_json) = 'object' THEN platform_event_json - ... ELSE platform_event_json END`.

- **N4** — `Domain/Enums/CheckpointStatus.cs:20-24`
  - **Problem:** the `Failed` member's doc restates the sweep exclusion, the retention sweep and the
    lock revival — three behaviours owned by three other files, each of which documents them itself.
    `CLAUDE.md` is explicit that this is the shape that rots.
  - **Suggestion:** `/// <summary>The run was reported failed. Terminal.</summary>` and leave the
    mechanics to `ICheckpointRepository` and the two stores.

## Open questions

- Does any collector put a vendor session token or refresh token into `adapter_state_json`? It is the
  one field on a retained terminal row that this change does not consider, and it now lives seven days
  rather than being deleted with the run.
- After a run has been reported failed, is resuming the intended retry semantics, or is a clean restart?
  I2 assumes restart; if resume is deliberate, say so in the PR body — it is the one behaviour a reader
  cannot infer from the diff.
- `EndUnservableRunAsync` now marks rather than deletes, and a redelivered event can revive that row to
  `Idle` via the execution lock. The sweep would then decline it again and, past the escalation bound,
  publish a *second* terminal completion for the same correlation id. Is a redelivery that late
  (hours after the row was abandoned) actually reachable in your broker configuration?
