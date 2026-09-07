# Code review — failed-checkpoint terminal status + retention TTL

## Scope reviewed

`d53e347d...working tree` — 12 modified files + 3 new test files under `src/`. Areas touched:
`ProcessEventCommandHandler` completion path, `CheckpointRecoveryHandler.EndUnservableRunAsync`,
`ICheckpointRepository` (+2 members), `CheckpointEntry` strip-key consts, `CheckpointStatus.Failed`
docs, `ConfigurationKeys.Checkpoint`, `CheckpointCleanupJob`, both repository implementations, and
four test suites. Docs consulted: `CLAUDE.md`, `docs/clean-architecture.md`,
`docs/coding-standards.md`, `ReadMEs/evaluate_query_flow.md` (not on this path), plus the
`Cymulate.Integration.Client` source in `~/Dev/IntegrationInfra` for `PlatformEvent` and
`AdapterResultStatus`.

## What is correct (verified, not assumed)

- **Strip keys.** `PlatformEvent` (IntegrationInfra `src/Cymulate.Integration.Client/Models/PlatformEvent.cs`)
  carries **no** `[JsonPropertyName]`, and both write sites — `ProcessEventCommandHandler.cs:134` and
  `AdapterExecutionContext.cs:105` — use `JsonSerializer.Serialize(platformEvent)` with default options.
  So `Credentials`/`Headers`/`Payload` are the keys actually written, and the `nameof` derivation is right.
  The nested `payload.credentials` matches both producers: `IoaDeletePayload.Credentials` carries
  `[JsonPropertyName("credentials")]`, and the wrapped `siemrules.run` shape is read back as the same
  case-sensitive literal at `ProcessEventCommandHandler.cs:475`.
- **Strip SQL.** `jsonb_typeof` guards handle NULL and non-object columns; `#-` then `-` then `-` is
  left-associative at equal precedence, so the path delete happens before the two key deletes and all
  three land. `CAST(... AS text)` resolves the `jsonb - text` operator that untyped EF parameters would
  otherwise leave ambiguous. `JsonNode`-based in-memory strip agrees key-for-key.
- **Ownership guard is satisfiable at both call sites.** The handler holds the claim from
  `TryAcquireExecutionLockAsync` (`:140`) through `CompleteExecutionAsync`, renewed by the heartbeat; the
  sweep takes a fresh claim at `CheckpointRecoveryHandler.cs:828` immediately before marking at `:871`.
- **The TOCTOU the status guard closes is real and correctly closed.** The mark releases the claim, so a
  row marked between `GetRecoverableAsync` and `TryClaimAsync` would otherwise look claimable;
  `CheckpointRepository.cs:565` and `InMemoryCheckpointRepository.cs:311` both refuse it.
- **The two cutoffs cannot cross destructively.** `DeleteExpiredAsync` excludes `Failed` and
  `DeleteTerminalExpiredAsync` requires it — the two predicates are disjoint, so any TTL/retention
  ordering (including `TtlHours` > retention) deletes each row exactly once, on its own horizon.
- **Stop is unaffected.** `DeleteByCorrelationIdAsync` and `DeleteStoppedCheckpointsAsync` have no status
  filter, so a `Failed` row is still removed by a stop; stop-request rows keep the TTL cutoff.
- **A straggler checkpoint write cannot revive a marked row.** `UpsertAsync`'s ON CONFLICT requires
  `adapter_checkpoints.claimed_by_instance = EXCLUDED.claimed_by_instance`, and the mark nulls the claim
  while every production write carries a non-null owner (`SetExecutionOwner` is called on the one path,
  `ProcessEventCommandHandler.cs:206`). This is strictly better than the old delete, which let a straggler
  INSERT a fresh live row.
- **Config.** `Checkpoint:FailedRetentionDays` follows the existing `Checkpoint:*` pattern and normalizes
  from `failed_retention_days`; there is no `Checkpoint` section in `appsettings.json` today, so the
  code-side default matches `TtlHours`'s precedent rather than breaking it.
- **Conventions.** New code names its types, braces, tail-orders parameters (`instanceId` before
  `CancellationToken`), appends `Failed` rather than renumbering, and logs structured templates. No
  layering change: the two new members sit on the existing Domain port.

## Blockers

None. Nothing here loses data, breaks a wire contract, or leaks a secret on a path I could reach.

## Important

- **I1** — `Applications/…Application/Commands/ProcessEventCommandHandler.cs:1259` together with
  `Infrastructure/…Postgres/Persistence/CheckpointRepository.cs:682`
  - **Problem:** Retention silently converts a failed run's retry from *restart* into *resume*, and can
    poison it. Previously the row was deleted on a reported failure, so a redelivery found no checkpoint
    and ran `ProcessAsync` from scratch (`ExecuteWithResumeAsync:1191`). Now the row survives, the
    execution-lock ON CONFLICT revives it to `Idle` **without resetting progress** (`current_page`,
    `cursor_token`, `adapter_state_json` are deliberately not in the SET list), so the retry resumes from
    the failed leg. Where the adapter *declines* the resume — `CanResumeFrom` returns false on a state
    version bump or an unknown flow, e.g.
    `cymulate-integration-adapters/.../CortexXdrCheckpointHelper.cs:11` — the retry starts at page 1
    against a stored high-water mark of, say, 57, and `UpsertAsync`'s monotonic guard
    (`current_page <= EXCLUDED.current_page`) refuses **every** checkpoint write of the retry. That trips
    `HasRejectedCheckpointWrite` and ends the execution through `HandleRejectedCheckpointWriteAsync`. It is
    the exact shape of the 2026-08-11 incident documented at `ProcessEventCommandHandler.cs:1160`, newly
    reachable on the ordinary "run failed, broker redelivered" path rather than only on pod loss.
  - **Suggestions:**
    1. Reset the progress columns when — and only when — the ON CONFLICT revives a `Failed` row, so the
       retry starts exactly as it did before this change and the retained counts are overwritten only by
       the retry that supersedes them. Preferred: it restores the baseline semantics instead of inventing
       new ones, and the CASE is already there. Sketch, alongside the existing `status` CASE:
       ```sql
       current_page = CASE WHEN adapter_checkpoints.status = 'Failed'
                           THEN EXCLUDED.current_page ELSE adapter_checkpoints.current_page END,
       -- same for processed_items, processed_findings, sequence_id,
       -- cursor_token, last_processed_id, adapter_state_json
       ```
    2. Keep the resume, but state it in the PR body as an intended behaviour change and add a Postgres test
       next to `TryAcquireExecutionLockAsync_returns_a_terminal_row_to_Idle` asserting the surviving page —
       plus a guard so a declined resume clears the stored high-water mark before running fresh. More code,
       and it leaves the monotonic-refusal case to be handled somewhere new.

- **I2** — `Infrastructure/…Core/Services/InMemoryCheckpointRepository.cs:357`
  - **Problem:** `TryAcquireExecutionLockAsync` replaces the row wholesale (`_store[key] = entry`), so the
    in-memory store zeroes `CurrentPage`/`CursorToken`/`AdapterStateJson` on revive while Postgres
    preserves them. The new `entry.Status` carry-over makes this divergence load-bearing: the store that
    backs most of this suite (and `CheckpointRecoveryCompatibilityTests`) shows *restart* semantics for the
    `Failed → Idle` revive, which is the opposite of what Postgres does, so no test in the suite can
    surface I1. The comment at `:368` claims "same status rule as the Postgres ON CONFLICT" while the
    surrounding statement is not the same rule at all.
  - **Suggestion:** Mirror the ON CONFLICT column set — on an existing row, copy the progress fields from
    `existing` into `entry` (reuse the new `Copy` helper and then overwrite only claim, status,
    `PlatformEventJson`, `UpdatedAtUtc`), so the double preserves progress exactly as Postgres does. Then
    add a test asserting the revived row keeps page 14.

- **I3** — `Applications/…Application/Commands/ProcessEventCommandHandler.cs:1105`
  - **Problem:** When `MarkFailedAsync` returns false the handler logs a warning and leaves the row. The
    old code called `DeleteAsync`, which had **no** owner guard and therefore always removed the row. So a
    mark that misses its guard now leaves a claimed-or-idle, non-terminal row carrying full credentials and
    progress — visible to `GetRecoverableAsync` once the claim goes stale, which re-dispatches a run whose
    done has already been published. The narrow window (claim stolen after a failed heartbeat renew, or a
    row deleted underneath) is exactly the window the old delete covered.
  - **Suggestion:** Fall back to the previous behaviour when the mark misses:
    ```csharp
    if (!marked)
    {
        logger.LogWarning(...);
        await TryDeleteCheckpointAsync(tenantId, platformEvent, category);
    }
    ```
    Losing the inspection row is the cheaper failure; a re-dispatched run that already reported failed is not.

- **I4** — `Tests/…API.UnitTests/Checkpoints/CheckpointRepositoryTests.cs` (13 new cases)
  - **Problem:** Every test of the new raw SQL — the strip CASE, the ON CONFLICT `status` CASE, the
    `status <> 'Failed'` claim guard, `DeleteTerminalExpiredAsync` — lives in the Testcontainers project and
    did not execute (Docker unavailable). The in-memory equivalents pass, but they exercise `JsonNode` and
    LINQ, not `jsonb #-`, operator precedence, or EF's parameter typing in `ExecuteSqlAsync`. The strip is a
    security control and the claim guard is a correctness control; neither has run against a server.
  - **Suggestion:** Before merge, run the suite once with Docker up:
    `dotnet test src/…/Tests/Cymulate.IntegrationServiceBus.API.UnitTests/… --filter "FullyQualifiedName~CheckpointRepositoryTests"`,
    and say in the PR body that it passed. If Docker cannot be had, at minimum confirm the strip statement
    against a scratch Postgres with a real `platform_event_json` row and paste the before/after `jsonb`.

- **I5** — `Domain/…/Models/CheckpointEntry.cs:32` and
  `UnitTests/…Infrastructure.Core.UnitTests/InMemoryCheckpointRepositoryTerminalStatusTests.cs:504`
  - **Problem:** `PayloadCredentialsJsonKey` is the one strip key not derived from anything, and its doc
    justifies that with a claim that is wrong: it says the member is "an anonymous-object member written by
    `PlatformEventFactory` … so there is nothing to take `nameof` of". `PlatformEventFactory` writes
    `IoaDeletePayload` (`Domain/Models/IoaDeletePayload.cs:20`), a typed record whose `Credentials` property
    carries `[JsonPropertyName("credentials")]` — a real, renameable declaration. The test that is supposed
    to guard the key builds its payload from `CheckpointEntry.PayloadCredentialsJsonKey` itself, so it
    asserts the const against the const and would still pass if that attribute were renamed, leaving an
    encrypted blob in a row now kept for seven days.
  - **Suggestion:** Add a serializer-contract test next to the existing one, mirroring
    `The_stripped_keys_are_the_ones_the_serializer_writes`:
    ```csharp
    string json = JsonSerializer.Serialize(new IoaDeletePayload { Credentials = "ENCRYPTED-BLOB", Rules = [] });
    Assert.True(JsonDocument.Parse(json).RootElement
        .TryGetProperty(CheckpointEntry.PayloadCredentialsJsonKey, out _));
    ```
    and correct the doc to name its two real sources — `IoaDeletePayload.Credentials`'s wire name and the
    inbound wrapped shape read at `ProcessEventCommandHandler.cs:475`.

## Nits

- **N1** — `Infrastructure/…Core/Jobs/CheckpointCleanupJob.cs:48`
  - **Problem:** The `TryParse` exists so an unparseable setting cannot throw on every fire, but
    `TimeSpan.FromDays` still throws for a parseable-but-absurd one: anything above ~10,675,199 days
    overflows, and the whole cleanup job — including the stopped sweep and the stop-request TTL — fails on
    every fire. The stated intent is not achieved for that input.
  - **Suggestion:** Clamp instead of flooring:
    `TimeSpan.FromDays(Math.Clamp(days, ConfigurationKeys.Checkpoint.MinimumFailedRetentionDays, MaximumFailedRetentionDays))`
    with `private const int MaximumFailedRetentionDays = 3650;` — and extend
    `Execute_floors_a_nonsensical_retention_setting` with `int.MaxValue.ToString()`.

- **N2** — `Infrastructure/…Core/Services/InMemoryCheckpointRepository.cs:154`
  - **Problem:** `MarkFailedAsync`'s `_store.TryUpdate(key, updated, existing)` compares by reference, but
    every other mutator in this store (`TryClaimAsync:314`, `RenewClaimAsync:353`, `ReleaseClaimAsync:331`)
    mutates the stored instance **in place**. A claim taken between the owner check and the CAS is therefore
    invisible to the CAS and gets silently overwritten — Postgres's `WHERE claimed_by_instance = …` would
    not. Harmless in a single-process host, but `The_terminal_mark_is_all_or_nothing_under_a_concurrent_write`
    reads as evidence of an atomicity this store does not have against those three methods.
  - **Suggestion:** Take the same in-place-mutation approach as its neighbours under a small `lock` on the
    entry (or a `SemaphoreSlim` keyed by the store), so the guard and the write are one critical section;
    alternatively narrow the test's docstring to say it covers concurrent `UpsertAsync` only, which is what
    it actually drives.

## Open questions

1. Is resume-after-reported-failure (I1) the intended new semantic, or an unnoticed side effect of keeping
   the row? The answer picks between the two fixes offered there.
2. What is the expected `adapter_checkpoints` row count at steady state once every failed run retains a row
   for seven days — is the seven-day default sized against observed daily failure volume, or picked as a
   round number? The table is swept by two full-scan predicates every 30 minutes.
3. Should `MarkFailedAsync` also null `scheduled_resume_at_utc`? It is unreachable today (the partial-wait
   path returns before `CompleteExecutionAsync`), so this is a question about intent rather than a defect.
