
## W1 — implementation

### Files changed

| file | change |
| --- | --- |
| `Domain/.../Interfaces/ICheckpointRepository.cs` | `MarkFailedAsync` added immediately after `DeleteAsync`; `DeleteExpiredAsync` doc corrected to say terminal-only |
| `Domain/.../Models/CheckpointEntry.cs` | `public const string CredentialsJsonKey`; added `using Cymulate.Integration.Client.Models;` |
| `Domain/.../Constants/ConfigurationKeys.cs` | `Checkpoint:FailedRetentionDays` key, `DefaultFailedRetentionDays = 7`, `MinimumFailedRetentionDays = 1`; `DefaultTtlHours` doc corrected |
| `Infrastructure.Postgres/Persistence/CheckpointRepository.cs` | `MarkFailedAsync` (one UPDATE); `GetRecoverableAsync` and `DeleteExpiredAsync` predicates |
| `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs` | `MarkFailedAsync` + `StripCredentials`; same two predicates |
| `Infrastructure.Core/Jobs/CheckpointCleanupJob.cs` | `IConfiguration` injected; `FailedRetention` property; terminal cutoff passed to `DeleteExpiredAsync`; TTL cutoff still passed to `stopRequestRepository.DeleteExpiredAsync` |
| `Application/Commands/ProcessEventCommandHandler.cs` | `FlushAndCleanupCheckpointAsync` takes `AdapterResult result`; `IsReportedFailure` + `TryMarkCheckpointFailedAsync` |

No new column, no migration. `CheckpointStatus.Failed` already existed.

### Predicates — before / after

`GetRecoverableAsync` (both stores)

- Postgres before: `c.Status != CheckpointStatus.ScheduledWait && (c.ClaimedByInstance == null || c.ClaimedAtUtc < staleBeforeUtc)`
- Postgres after: `c.Status != CheckpointStatus.ScheduledWait && c.Status != CheckpointStatus.Failed && (c.ClaimedByInstance == null || c.ClaimedAtUtc < staleBeforeUtc)`
- InMemory before: `c.Status != CheckpointStatus.ScheduledWait`
- InMemory after: `c.Status != CheckpointStatus.ScheduledWait && c.Status != CheckpointStatus.Failed`

`DeleteExpiredAsync` (both stores, signature unchanged)

- Postgres before: `c.UpdatedAtUtc < cutoffUtc && c.Status != CheckpointStatus.ScheduledWait`
- Postgres after: `c.Status == CheckpointStatus.Failed && c.UpdatedAtUtc < cutoffUtc`
- InMemory before: `kvp.Value.UpdatedAtUtc < cutoffUtc && kvp.Value.Status != CheckpointStatus.ScheduledWait`
- InMemory after: `kvp.Value.Status == CheckpointStatus.Failed && kvp.Value.UpdatedAtUtc < cutoffUtc`

`ScheduledWait` no longer needs naming in either — a `ScheduledWait` row is not `Failed`.

### Failure predicate (R2)

```csharp
private static bool IsReportedFailure(AdapterResult result)
    => result.Status is AdapterResultStatus.Failure
                     or AdapterResultStatus.TransientFailure
                     or AdapterResultStatus.ValidationFailure;
```

Enum member names confirmed by reflection against the pinned package
(`Cymulate.Integration.Client` 1.2.0-preview.0, `Directory.Packages.props:127`):
`Cymulate.Integration.Client.Enums.AdapterResultStatus` = `Success=0, PartialWaitRequired=1, Failure=2,
TransientFailure=3, ValidationFailure=4, Skipped=5, Cancelled=6`. All three names matched as written; no
`ErrorCode` is read anywhere. `Cancelled`, `Skipped`, `PartialWaitRequired` and `Success` all fall through
to the unchanged delete.

`AdapterResultStatus` needed no new `using` — the handler already uses it unqualified at `:284` and `:292`
via the project's `GlobalUsings.cs`.

### R3 — ordering

The branch sits **below** the `HasRejectedCheckpointWrite` early return in
`FlushAndCleanupCheckpointAsync`, in place of the delete. A claim-lost execution returns
`HandleRejectedCheckpointWriteAsync(...)` before reaching it and writes nothing.

### R4 — claim release

The single Postgres UPDATE sets `claimed_by_instance = NULL` and `claimed_at_utc = NULL` alongside the
status; the in-memory store nulls both fields. `TryAcquireExecutionLockAsync` therefore still admits a
redelivery.

### R5 — credentials key derivation

`CheckpointEntry.CredentialsJsonKey = nameof(PlatformEvent.Credentials)` — one public const, referenced by
both stores and available to the test project.

Evidence the derived key is the key actually on disk: `PlatformEventJson` is written by
`JsonSerializer.Serialize(platformEvent)` with **no** options (`ProcessEventCommandHandler.cs:134`,
`AdapterExecutionContext.cs:105`), so property names are written verbatim. Reflection over the pinned
package shows `PlatformEvent.Credentials` carries **no** `[JsonPropertyName]` (its only attribute is
`NullableAttribute`), and a round-trip serialize of a populated `PlatformEvent` emits
`..."Credentials":{"ClientId":"secret-id"},...`. So the key is `Credentials`, matching `nameof`.
(The lowercase `credentials` string present in the DLL belongs elsewhere — not to this property.)

- Postgres: `platform_event_json = platform_event_json - CAST({credentialsKey} AS text)`. The column is
  `jsonb` (`20250101000003_AddPlatformEventJson.cs`, `CheckpointDbContext.cs:73`). The explicit `CAST` is
  there because `jsonb - ?` is overloaded on `text` / `text[]` / `integer`; an untyped parameter risks
  "operator is not unique", which on this path would be swallowed by the caller's catch and read as
  success. A NULL column stays NULL (`NULL - 'Credentials'` is NULL).
- InMemory: `StripCredentials` parses to a `JsonObject`, `Remove(CheckpointEntry.CredentialsJsonKey)`,
  re-serializes. Null/whitespace returns unchanged; a non-object payload returns unchanged.

### Retention config

`Checkpoint:FailedRetentionDays`, default 7, floored at 1 via `MinimumFailedRetentionDays` (a 0 or negative
setting would put the cutoff at or past now and delete every terminal row on the next sweep). No upper
clamp, no helper types.

Read in `CheckpointCleanupJob` from an injected `IConfiguration`, not from a second Quartz job-data key.
The job-data route would have required editing `Infrastructure.Core/DependencyInjection.cs`, which is
outside the owned set; Quartz constructs this job through the MS DI job factory, so the extra constructor
parameter needs no wiring change. **This changes `CheckpointCleanupJob`'s constructor arity** — see the
note to W2 below.

Stop-request deletion is unchanged: `DeleteStoppedCheckpointsAsync()` still takes no cutoff and still runs
first, and `stopRequestRepository.DeleteExpiredAsync(cutoff)` still receives the **TtlHours**-derived
cutoff. Only `checkpointRepository.DeleteExpiredAsync` moved to the retention cutoff.

### Stale comments corrected

`DeleteExpiredAsync` no longer deletes non-terminal rows, which falsified three doc comments asserting the
TTL is the checkpoint deletion horizon. Two are in owned files and were rewritten:
`ConfigurationKeys.Checkpoint.DefaultTtlHours` and `CheckpointCleanupJob.DefaultTtlHours` (plus the
stop-request cutoff comment inside `Execute`). **A third is not owned and is now wrong**:
`CheckpointRecoveryHandler.CheckpointTtl` (`:701-707`) — "How long a checkpoint row lives without being
written to before `CheckpointCleanupJob` deletes it". The sweep's `UnservableTerminalAge` arithmetic is
unaffected (it deletes the row itself at `:867`), only the sentence is.

### Build

`dotnet build src/Cymulate.IntegrationServiceBus/Cymulate.IntegrationServiceBus.sln`

All four owned production projects compiled: `Domain`, `Application`, `Infrastructure.Core`,
`Infrastructure.Postgres`. 14 warnings, all `NU1900`. Two remaining `error CS` are in W2's in-flight test
files, both in `Infrastructure.Core.UnitTests`:

- `InMemoryCheckpointRepositoryTerminalStatusTests.cs(29,24)`: `CS0246` `IStopRequestRepository` — missing
  using in W2's file.
- `CheckpointCleanupJobTests.cs(28,41)`: `CS7036` — constructs `CheckpointCleanupJob` with three
  arguments. New signature is
  `CheckpointCleanupJob(ICheckpointRepository, IStopRequestRepository, IConfiguration, ILogger<CheckpointCleanupJob>)`.

Docker is **not** running on this machine (`/var/run/docker.sock` absent), so `CheckpointRepositoryTests`
and every other Testcontainers suite cannot run here. The Postgres `MarkFailedAsync` SQL — in particular
the `jsonb - text` strip and the owner guard — is **unverified at runtime**.

## W2 — tests

Written against the frozen surface in `orchestration_plan.md`, not against W1's implementation. Every
test below passed on first run against W1's code except where noted; nothing was adjusted to fit it.

### Enum members found

`AdapterResultStatus` (`Cymulate.Integration.Client` 1.2.0-preview.0):
`Success, PartialWaitRequired, Failure, TransientFailure, ValidationFailure, Skipped, Cancelled`.
Six of the seven reach `CompleteExecutionAsync`; `PartialWaitRequired` is diverted at
`ProcessEventCommandHandler.cs:284-288` and never gets there.

`CheckpointStatus` (`Domain/.../Enums/CheckpointStatus.cs`): `InFlight, Idle, ScheduledWait, Failed`.
`Failed` already existed — no enum member was added by this change.

### Task 1 — build unblocked

Forwarding `MarkFailedAsync` added to both hand-written decorators, positioned after `DeleteAsync` to
match the interface's own order:

- `RefusingStore` — `Tests/.../Checkpoints/StoppingIsNotEndingTests.cs`
- `ClaimRefusingStore` — `Tests/.../Checkpoints/CheckpointRecoveryCompatibilityTests.cs`

### Tests added, by R-id

| test | file | R-id |
|---|---|---|
| `R1_SweepNeverOffersAFailedRow` | `Tests/.../Checkpoints/FailedRunLeavesATerminalRowTests.cs` (new) | R1 + R4, end to end |
| `R1_SweepNeverOffersAFailedRow` | `UnitTests/.../Infrastructure.Core.UnitTests/InMemoryCheckpointRepositoryTerminalStatusTests.cs` (new) | R1 |
| `R1_SweepNeverOffersAFailedRow` | `Tests/.../Checkpoints/CheckpointRepositoryTests.cs` | R1 (Postgres) |
| `R2_OnlyReportedFailureMarksTerminal` | `UnitTests/.../Application.UnitTests/ProcessEventCommandHandlerTests.cs` | R2 |
| `R3_RefusedWriteTakesPrecedence` | `UnitTests/.../Application.UnitTests/ProcessEventCommandHandlerTests.cs` | R3 |
| `R4_MarkingTerminalReleasesTheClaim` | `InMemoryCheckpointRepositoryTerminalStatusTests.cs` | R4 |
| `R4_MarkingTerminalReleasesTheClaim` | `CheckpointRepositoryTests.cs` | R4 (Postgres) |
| `R5_StrippedPayloadKeepsEverythingExceptCredentials` | `InMemoryCheckpointRepositoryTerminalStatusTests.cs` | R5 |
| `R5_StrippedPayloadKeepsEverythingExceptCredentials` | `CheckpointRepositoryTests.cs` | R5 (Postgres) |
| `The_stripped_key_is_the_one_the_serializer_writes` | `InMemoryCheckpointRepositoryTerminalStatusTests.cs` | serializer-key guard |
| `MarkFailedAsync_refuses_an_instance_that_does_not_own_the_row` | both store test files | owner guard |
| `MarkFailedAsync_reports_false_when_no_row_matches` | `InMemoryCheckpointRepositoryTerminalStatusTests.cs` | return contract |
| `DeleteExpiredAsync_reaps_a_terminal_row_past_the_retention_window` | `InMemoryCheckpointRepositoryTerminalStatusTests.cs` | predicate |
| `DeleteExpiredAsync_keeps_a_terminal_row_inside_the_retention_window` | both store test files | predicate |
| `DeleteExpiredAsync_does_not_reap_a_non_terminal_row` (Theory: Idle, InFlight, ScheduledWait) | `InMemoryCheckpointRepositoryTerminalStatusTests.cs` | predicate |
| `DeleteStoppedCheckpointsAsync_still_deletes_a_terminal_row` | `CheckpointRepositoryTests.cs` | stop wins over retention |
| `Execute_reaps_terminal_checkpoints_on_the_retention_window_not_the_ttl` | `UnitTests/.../Infrastructure.Core.UnitTests/CheckpointCleanupJobTests.cs` (new) | two-cutoff seam |
| `Execute_still_deletes_stop_request_rows_on_the_ttl_not_the_retention_window` | `CheckpointCleanupJobTests.cs` | two-cutoff seam |
| `Execute_still_runs_the_stop_aware_checkpoint_sweep` | `CheckpointCleanupJobTests.cs` | job unchanged |
| `Handle_still_deletes_the_checkpoint_when_no_adapter_is_registered` | `ProcessEventCommandHandlerTests.cs` | regression |
| `Handle_still_deletes_the_checkpoint_when_the_adapter_throws` | `ProcessEventCommandHandlerTests.cs` | regression |

R2 is a `[Theory]` over the six statuses that reach the completion path, asserting mark-vs-no-mark **and**
delete-vs-no-delete per status, so success/skipped/cancelled deleting is covered by the same rows.
Each result is built through the real factory and the theory asserts the produced `Status` matches the
row — a wrong factory choice fails loudly rather than silently testing the wrong branch.
The mark is verified with `Environment.MachineName`, the handler's own instance id, never `It.IsAny<string>()`.

The serializer-key guard references `CheckpointEntry.CredentialsJsonKey` (W1's const, reachable from
Domain in all three test projects). No literal `"Credentials"` appears in any test.

### Existing tests changed

1. **`ProcessEventCommandHandlerTests` constructor** — added a default
   `.Setup(MarkFailedAsync).ReturnsAsync(true)`. A loose Moq mock answers `false` for a new `Task<bool>`
   member, which reads as "no row matched" and hides a handler that called it correctly.
2. **`CheckpointRepositoryTests.DeleteExpiredAsync_does_not_delete_ScheduledWait_regardless_of_age`** —
   **deliberate behaviour change, called out.** It used to seed an old `Idle` row as the control that
   gets deleted. Under the frozen predicate (`Status == Failed && UpdatedAtUtc < cutoffUtc`) an old
   `Idle` row is no longer deleted, so the control is now an old `Failed` row and the test additionally
   asserts the `Idle` row survives. ScheduledWait's exemption — what the test is named for — is unchanged.
3. `SeedCheckpointAsync` gained an optional `platformEventJson` parameter (trailing, per
   `docs/coding-standards.md:67-83`) so R5 can seed a payload carrying credentials.

No test anywhere asserted that a failed run deletes its checkpoint — the recon's finding held.

### Consequence worth a decision (not a test failure)

`DeleteExpiredAsync` is now terminal-rows-only, and `CheckpointCleanupJob` passes it the retention
cutoff while keeping the TTL cutoff for stop-request rows. Nothing else reaps by TTL, so **non-terminal
checkpoint rows (`Idle`, `InFlight`) are no longer deleted by any sweep** — only a stop request removes
them. That is exactly what the frozen surface specifies and what `DeleteExpiredAsync_does_not_reap_a_non_terminal_row`
now pins, but it is a real change to checkpoint-table growth and it is not stated in the contract's
success criteria.

### Results per project

| project | result |
|---|---|
| `UnitTests/...Infrastructure.Core.UnitTests` | **416 passed, 0 failed** |
| `UnitTests/...Application.UnitTests` | **92 passed, 0 failed** (the CLAUDE.md pre-existing failure did not reproduce) |
| `Tests/...API.UnitTests` — non-container tests | 629 passed |
| `Tests/...API.UnitTests` — `CheckpointRepositoryTests` (Testcontainers) | **NOT RUN** — 30 tests, all `DockerUnavailableException` (`unix:///var/run/docker.sock` unreachable). The Postgres `MarkFailedAsync`, its `jsonb` strip, its owner guard and the Postgres `DeleteExpiredAsync`/`GetRecoverableAsync` predicates are **unverified at runtime**. |
| `Tests/...API.UnitTests` — other container suites | NOT RUN — 89 further `DockerUnavailableException` (CredentialStore, Redis, QueryIntegrationRepository, ScheduledContinuation) |
| `Tests/...API.UnitTests` — `TestAdapterConnectionCommandHandlerTests` | 3 failed, **pre-existing** — reproduced identically on a clean worktree at `d53e347d` (`Moq.MockException: IAdapterRegistryManager.GetRegistration(YamlEngine, Collectors, True)` under a strict mock). Unrelated to checkpoints. |

New non-container tests that did run and pass: `FailedRunLeavesATerminalRowTests` (1),
`InMemoryCheckpointRepositoryTerminalStatusTests` (9), `CheckpointCleanupJobTests` (3),
`ProcessEventCommandHandlerTests` additions (8).

No commit, no push, no PR.

### W2 addendum — the two-cutoff seam

`CheckpointCleanupJob` computes two horizons from one run. The two tests above pin them apart with
settings that cannot be confused (`Checkpoint:TtlHours` = 24, `Checkpoint:FailedRetentionDays` = 7,
referenced through `ConfigurationKeys.Checkpoint.*`, never as literals). Each test captures the cutoff
the repository was actually handed via a Moq `Callback`, asserts its absolute age, and then asserts it
is **not** the other horizon — so a swap fails in either direction rather than only one.

The `IConfiguration` constructor parameter and the missing `using` were already fixed before this note
arrived; both were caught by the first build of these projects, and all three suites were green.

**Left untested, deliberately, and worth someone's decision:**
`ConfigurationKeys.Checkpoint.MinimumFailedRetentionDays` and the `Math.Max` floor in the job. A
configured `0` or negative retention would otherwise put the cutoff at or past now and delete every
terminal row on the next sweep. That floor is real new surface with a real footgun, but it is not in the
frozen surface W2 was given — pinning it would mean writing a test from W1's implementation, which is
the thing this split exists to prevent. Flagging it instead: if the floor is intended behaviour, it
should be added to the spec and covered.

## W1 — round 2: blockers and follow-ons

### B1 — non-terminal rows are swept again

`DeleteExpiredAsync` restored to the TTL question, `DeleteTerminalExpiredAsync` added for the retention
question. Two methods, two cutoffs, disjoint predicates.

| method | Postgres | InMemory |
| --- | --- | --- |
| `DeleteExpiredAsync(cutoffUtc)` | `UpdatedAtUtc < cutoffUtc && Status != ScheduledWait && Status != Failed` | same |
| `DeleteTerminalExpiredAsync(cutoffUtc)` | `Status == Failed && UpdatedAtUtc < cutoffUtc` | same |

`CheckpointCleanupJob` calls both: `DeleteExpiredAsync(cutoff)` on the TtlHours cutoff, then
`DeleteTerminalExpiredAsync(failedCutoff)` on the retention cutoff.

**One deliberate reading, flagged.** The instruction was to restore `DeleteExpiredAsync`'s *original*
predicate, which was `UpdatedAtUtc < cutoffUtc && Status != ScheduledWait` — it did **not** exempt `Failed`.
Taken literally, the TTL sweep (24h) would delete every terminal row before the retention sweep (7d) could
ever see one, making `DeleteTerminalExpiredAsync` unreachable and the retention window inoperative. So the
restored predicate exempts `Failed` as well, exactly as it already exempts `ScheduledWait` and for the same
reason: a status with its own lifecycle owner is not the generic sweep's business. Say so if that reading is
wrong — it is a one-line change either way.

### B2 — a redelivery no longer runs over a terminal row

Added to `TryAcquireExecutionLockAsync`'s `ON CONFLICT DO UPDATE SET`, verbatim as proposed:

```sql
status = CASE WHEN adapter_checkpoints.status = 'Failed'
              THEN 'Idle' ELSE adapter_checkpoints.status END,
```

Mirrored in the in-memory store — see the correction under follow-on 2.

### Follow-ons

1. **Done.** `TryMarkCheckpointFailedAsync` now logs a `false` return at Warning, naming tenant,
   correlation, platform, category and instance, and saying which of the two causes it is (no row, or the
   claim moved). Same level as the neighbouring delete-failure warnings. `EndUnservableRunAsync` got the
   same treatment for its own `MarkFailedAsync` call.
2. **Done, and my earlier reasoning was wrong.** `MarkFailedAsync` in the in-memory store now CASes:
   `while(true)` → `TryGetValue` → owner check → `Copy(existing)` with the mutated fields →
   `TryUpdate(key, updated, existing)`, mirroring `UpsertAsync`. Added a private `Copy` helper for the
   field list. Separately, I had judged the B2 mirror to be free because in-memory
   `TryAcquireExecutionLockAsync` replaces the row wholesale, so a `Failed` row came back `Idle` by
   construction. That is true for `Failed` and **wrong for every other status** — it also flattened
   `ScheduledWait` to `Idle`, which W2's
   `Taking_the_execution_lock_leaves_a_non_terminal_status_alone(status: ScheduledWait)` caught. The store
   now carries the stored status across and resets only `Failed`, which is what the Postgres `CASE` does.
   **Still divergent, out of scope, flagged:** the in-memory lock also discards progress, cursor,
   adapter state and `ScheduledResumeAtUtc`, all of which the Postgres `DO UPDATE` preserves. Pre-existing,
   untouched.
3. **Done.** `EndUnservableRunAsync` calls `MarkFailedAsync(..., InstanceId, ct)` instead of `DeleteAsync`.
   It already holds the claim from the `TryClaimAsync` directly above, so the owner guard is satisfied. Its
   `<summary>`, the third `<para>` of its `<remarks>`, and the terminal `LogError` ("its checkpoint
   removed" → "its checkpoint marked terminal") were corrected with it.
4. **Mostly resolved by B1 itself — report rather than edit.** With the TTL sweep restored, the three
   "TTL job will handle it" log lines (`ProcessEventCommandHandler.cs:416`, `:1134`, `:1276`) are true
   again: each describes a non-terminal row whose delete failed, and the TTL sweep does reap it. Same for
   `CheckpointRecoveryHandler.cs:694`, `:750`, `:772`, `:796`, the TTL arithmetic at `:717-725`, and the
   `updated_at_utc` index comments in `CheckpointDbContext.cs`. Editing them would be churn on correct text.
   Four were genuinely wrong and were corrected:
   - `ConfigurationKeys.Checkpoint.DefaultTtlHours` — my round-1 rewrite, wrong in the other direction once
     B1 restored the TTL sweep. Now back to the original meaning plus a pointer to the retention constant.
   - `CheckpointCleanupJob.DefaultTtlHours` — same.
   - `CheckpointRecoveryHandler.CheckpointTtl` (`:702-704`) — scoped to non-terminal rows.
   - `CheckpointStatus.Failed` (`Domain/Enums/CheckpointStatus.cs`) — "Terminal non-retryable failure" is
     now actively misleading, since B2 resets the row to `Idle` when a redelivery takes the lock. Rewritten
     to say what the state means and both ways out of it.

### Follow-on 5 — does `Headers` carry secrets? Finding: no evidence that it does.

Reported, not stripped, as instructed.

- **Only two writers, both in the API host**: `EventsController.cs:471` (add-exclusion) and `:1842`
  (`ConvertToPlatformEvent`, the general HTTP entry point for every category including collectors). Both
  are `Headers = request.Headers`, copied straight from `PublishEventRequest.Headers`
  (`Hosts/.../Models/PublishEventRequest.cs:59`), whose only documentation is "Optional HTTP headers".
- **Zero readers.** Nothing in `src/` ever reads `PlatformEvent.Headers` back — not an adapter, not a
  publisher, not the recovery path. The only `.Headers` hits elsewhere are Kafka message headers,
  `HttpTrafficLoggingHandler` (which has its own `RedactedHeaders` list), and RabbitMQ's headers exchange.
- **Nothing populates it in-repo.** No sample payload, script, or test under `src/` or `docs/` sets
  `headers`.

So the field is a free-form, caller-controlled dictionary named "HTTP headers", which is where an
`Authorization` or `X-Api-Key` would conventionally go, on a path that does reach checkpointed collector
runs — but there is no live path in this repo that puts a secret there, and no consumer that needs it.
That is the whole basis for a decision; it is yours to make. Stripping it would be one more key in the same
`-` expression and one more `Remove` in `StripCredentials`.

### Build and tests

`dotnet build src/.../Cymulate.IntegrationServiceBus.sln` → **Build succeeded**, warnings `NU1900` only.
W2's decorators already forward both `MarkFailedAsync` and `DeleteTerminalExpiredAsync`.

Non-Docker suites, run after the fixes:

- `Infrastructure.Core.UnitTests` — **428 passed, 0 failed**
- `Application.UnitTests` — **93 passed, 0 failed** (the pre-existing failure `CLAUDE.md` warns about did
  not appear)

Two of W2's tests failed on the first run and both were real defects in my code, now fixed:

- `CheckpointCleanupJobTests.Execute_floors_a_nonsensical_retention_setting("not-a-number")` —
  `configuration.GetValue<int>` **throws** on an unparseable value rather than falling back, which would
  have failed the whole cleanup job on every fire and stopped all three sweeps. Replaced with the
  `int.TryParse` fallback the repo already uses at `DependencyInjection.cs:275-280`, then the floor.
- `InMemoryCheckpointRepositoryTerminalStatusTests.Taking_the_execution_lock_leaves_a_non_terminal_status_alone(ScheduledWait)`
  — see follow-on 2.

Docker is still unavailable, so `CheckpointRepositoryTests` and the rest of the Testcontainers suites did
not run. The Postgres SQL — the `jsonb - text` strip, the owner guard, and the new `status` CASE in
`TryAcquireExecutionLockAsync` — remains **unverified at runtime**.

## W2 — round 2 (B1, B2, follow-ons)

Written to the B1/B2 spec, not to W1's code. Two tests fail against the implementation; both are
reported below and neither was adjusted.

### Two findings — spec vs implementation

**F1 — a typo'd retention setting takes the whole cleanup job down.**
`Execute_floors_a_nonsensical_retention_setting(configured: "not-a-number")` fails with
`Quartz.JobExecutionException : Failed to convert configuration value 'not-a-number' at
'Checkpoint:FailedRetentionDays' to type 'System.Int32'` out of `CheckpointCleanupJob.Execute:108`.
The `Math.Max` floor guards `0` and negative, but the `configuration.GetValue<int>` that feeds it throws
on an unparseable value, and the throw is wrapped as `JobExecutionException(refireImmediately: false)` —
so stopped-checkpoint cleanup, both TTL sweeps and stop-request cleanup all stop running on one typo.
Note the neighbouring reads are already defensive: `TtlHours` uses `MergedJobDataMap.ContainsKey` +
`GetInt`, and `DependencyInjection` parses the TTL with `int.TryParse`. The retention read is the odd
one out. `0`, `-5` and unset all pass — only the unparseable case fails.

**F2 — the in-memory store does not implement B2, and its passing B2 test passes for the wrong reason.**
`Taking_the_execution_lock_leaves_a_non_terminal_status_alone(status: ScheduledWait)` fails: expected
`ScheduledWait`, got `Idle`. `InMemoryCheckpointRepository.TryAcquireExecutionLockAsync` is unchanged
in this branch (`git diff` shows no edit) and is still the pre-existing blind overwrite
`_store[key] = entry`, so it stamps whatever status the candidate entry carries over whatever was
stored. Two consequences:
- Taking a lock over a parked row silently cancels its scheduled wait. Pre-existing, not a regression
  from B2 — but B2's own wording ("leaving other statuses alone") now names it as wrong.
- `Taking_the_execution_lock_returns_a_terminal_row_to_Idle` **passes on this store for the wrong
  reason** — the candidate entry's default status is `Idle`, so the blind overwrite lands on the right
  answer by accident. It is not evidence that B2 exists in-memory.

Postgres has B2 correctly
(`status = CASE WHEN adapter_checkpoints.status = 'Failed' THEN 'Idle' ELSE ... END`), so the two stores
have diverged — which is exactly what `InMemoryCheckpointRepositoryRefusalTests`' own header says must
not happen. The Postgres B2 tests are Docker-gated and did not run.

### Existing tests inverted — they pinned behaviour these fixes reverse

| test | file | was | now |
|---|---|---|---|
| `A_row_nobody_can_read_is_ended_with_a_failed_completion_past_the_terminal_bound` | `CheckpointRecoveryCompatibilityTests.cs` | row deleted (`BeNull`) | row survives as `Failed`, unclaimed, and `GetRecoverableAsync` no longer offers it |
| `The_terminal_bound_follows_the_configured_cleanup_TTL` | same | row deleted | row is `Failed` |
| `A_row_whose_state_cannot_be_parsed_is_also_ended_past_the_terminal_bound` | same | row deleted | row is `Failed` |
| `A_siem_rules_run_ends_without_publishing_any_terminal_event` | same | row deleted | row is `Failed` |
| `DeleteExpiredAsync_does_not_delete_ScheduledWait_regardless_of_age` | `CheckpointRepositoryTests.cs` | (round 1) old `Idle` survives, old `Failed` reaped | **reverted to the original**: old `Idle` reaped, `ScheduledWait` exempt |
| `DeleteExpiredAsync_keeps_a_terminal_row_inside_the_retention_window` | same | `DeleteExpiredAsync` reaps `Failed` on retention | **replaced** by `DeleteExpiredAsync_does_not_delete_a_terminal_row` + `DeleteTerminalExpiredAsync_deletes_only_terminal_rows_past_the_retention_window` |
| `DeleteExpiredAsync_reaps/keeps_a_terminal_row...`, `DeleteExpiredAsync_does_not_reap_a_non_terminal_row` | `InMemoryCheckpointRepositoryTerminalStatusTests.cs` | (round 1) TTL predicate = terminal rows only | **inverted** into the six-test pair of predicates below |
| `Execute_reaps_terminal_checkpoints_on_the_retention_window_not_the_ttl` | `CheckpointCleanupJobTests.cs` | `DeleteExpiredAsync` got the retention cutoff | `DeleteExpiredAsync` gets the TTL cutoff, `DeleteTerminalExpiredAsync` gets the retention cutoff |

Two strengthened rather than inverted, both in `CheckpointRecoveryCompatibilityTests.cs`:
`A_terminal_completion_that_cannot_be_published_leaves_the_row_in_place` now also asserts the row is
**not** `Failed` (the run was never reported, so it must stay recoverable), and
`Only_the_instance_that_wins_the_claim_ends_the_run` asserts this replica did not mark it.

### New coverage

**B1 — two deletes, two horizons.** `InMemoryCheckpointRepositoryTerminalStatusTests`:
`DeleteExpiredAsync_still_reaps_a_non_terminal_row_past_the_ttl` (Theory: Idle, InFlight — this is the
regression W2's round-1 finding surfaced), `DeleteExpiredAsync_does_not_reap_a_terminal_row`,
`DeleteExpiredAsync_still_exempts_ScheduledWait`,
`DeleteTerminalExpiredAsync_reaps_a_terminal_row_past_the_retention_window`,
`DeleteTerminalExpiredAsync_keeps_a_terminal_row_inside_the_retention_window`,
`DeleteTerminalExpiredAsync_does_not_reap_a_non_terminal_row` (Theory: Idle, InFlight, ScheduledWait).
Postgres parity in `CheckpointRepositoryTests`: `DeleteExpiredAsync_does_not_delete_a_terminal_row`,
`DeleteTerminalExpiredAsync_deletes_only_terminal_rows_past_the_retention_window` (four seeded rows,
one deleted).

**B2 — the lock un-terminalizes.** Both stores: `..._returns_a_terminal_row_to_Idle` and
`..._leaves_a_non_terminal_status_alone` (Theory: Idle, ScheduledWait). See **F2**.

**Follow-ons.**
- `Handle_logs_when_the_terminal_mark_finds_no_row` (`ProcessEventCommandHandlerTests`) — the handler
  emits a Warning-or-above naming the correlation id, and the adapter's own outcome still stands. The
  assertion is on level and correlation id only, never on wording; nothing else the handler logs at
  Warning on this path carries the correlation id, so it bites.
- `The_terminal_mark_is_all_or_nothing_under_a_concurrent_write` — 200 rounds of `MarkFailedAsync`
  racing a same-owner `UpsertAsync`, asserting the mark's three effects (status `Failed`, claim
  released, credentials stripped) are always all present or all absent. A read-modify-write that loses
  a race can land some of them: `Failed` but still claimed is a run nothing picks up; stripped but still
  `Idle` is a live run whose credentials were taken away underneath it. Passes.
- The retention floor — `Execute_floors_a_nonsensical_retention_setting` (Theory: "0", "-5",
  "not-a-number", unset), referencing `ConfigurationKeys.Checkpoint.MinimumFailedRetentionDays`. See **F1**.
- Both decorators gained a `DeleteTerminalExpiredAsync` forwarding member.

### Results per project

| project | result |
|---|---|
| `UnitTests/...Infrastructure.Core.UnitTests` | 426 passed, **2 failed — F1 and F2, both reported above, neither adjusted** (428 total) |
| `UnitTests/...Application.UnitTests` | **93 passed, 0 failed** |
| `Tests/...API.UnitTests` — non-container | 629 passed, 0 failed (includes all four inverted unservable tests) |
| `Tests/...API.UnitTests` — `CheckpointRepositoryTests` (Testcontainers) | **NOT RUN** — 34 tests, all `DockerUnavailableException`. The Postgres B1 predicates, B2's `CASE WHEN` un-terminalize, `MarkFailedAsync` and its jsonb strip are **unverified at runtime**. |
| `Tests/...API.UnitTests` — other container suites | NOT RUN — 89 further `DockerUnavailableException` |
| `Tests/...API.UnitTests` — `TestAdapterConnectionCommandHandlerTests` | 3 failed, **pre-existing** (reproduced on a clean worktree at `d53e347d` in round 1) |

No commit, no push, no PR.

## W1 — round 3: `Headers` stripped

**This is a defensive strip, not a remediation.** No leak was observed and none is known. The round-2
finding stands unchanged and is the reason this can be reverted safely if it ever becomes inconvenient:

- `PlatformEvent.Headers` has **two writers**, both in the API host — `EventsController.cs:471`
  (add-exclusion) and `:1842` (`ConvertToPlatformEvent`, the general HTTP entry point for every category,
  collectors included). Both copy `PublishEventRequest.Headers` verbatim; that property's entire
  documentation is "Optional HTTP headers".
- **Zero readers.** Nothing anywhere in `src/` reads `PlatformEvent.Headers` back — no adapter, no
  publisher, no recovery path.
- **No in-repo writer of a secret.** No sample payload, script, test or fixture under `src/` or `docs/`
  populates `headers` at all, let alone with a credential.

It was stripped because the field is caller-controlled and callers live outside this repo, so "nothing puts
a secret there today" is not a property this codebase can hold. Stripping cannot break a consumer because
there is no consumer. Anyone later wanting to keep headers on a terminal row should check whether a reader
has appeared since — if there still is none, the strip is still free.

### Change

Second const beside the first, derived the same way, kept distinct rather than collapsed into an array —
two names that say what each one strips read better in both stores than one list does:

```csharp
public const string CredentialsJsonKey = nameof(PlatformEvent.Credentials);
public const string HeadersJsonKey     = nameof(PlatformEvent.Headers);
```

- Postgres — one more term in the same expression:
  `platform_event_json - CAST({credentialsKey} AS text) - CAST({headersKey} AS text)`
- InMemory — one more `Remove`. `StripCredentials` renamed to `StripSensitiveFields`, since the old name
  would have been wrong the moment the second key went in.
- `MarkFailedAsync`'s interface doc now names both consts.

### Key derivation evidence (same standard as `Credentials`)

Reflection over the pinned `Cymulate.Integration.Client` 1.2.0-preview.0: **neither** `Headers` nor
`Credentials` carries a `[JsonPropertyName]`. A round-trip serialize of a `PlatformEvent` with both
populated emits `..."Headers":{"Authorization":"Bearer shhh"},"Credentials":{...}...`, so both `nameof`
values match the keys actually written to `platform_event_json`. The silent-failure risk that makes a
literal unacceptable applies identically to the second key.

### Build and tests

**Build succeeded**, `NU1900` only. `Infrastructure.Core.UnitTests` **429 passed / 0 failed** (up one from
round 2 — W2's headers coverage), `Application.UnitTests` **93 passed / 0 failed**.

Docker still unavailable, so the Postgres path — now a two-term `jsonb - text - text` strip — is still
**unverified at runtime**. `CheckpointRepositoryTests` is where that gets proven.

## W2 — round 3 (Headers stripped alongside Credentials)

### F1 and F2 from round 2 are now green

Both previously-failing tests pass unchanged — W2 did not touch either, so W1 fixed both:

- `Execute_floors_a_nonsensical_retention_setting` passes on all four rows, `"not-a-number"` included, so
  the retention read no longer throws the whole cleanup job out on an unparseable setting.
- `Taking_the_execution_lock_leaves_a_non_terminal_status_alone(ScheduledWait)` passes, so the in-memory
  store now implements B2 rather than blind-overwriting the status. Its sibling
  `..._returns_a_terminal_row_to_Idle` therefore now passes for the right reason.

### Headers

`CheckpointEntry.HeadersJsonKey = nameof(PlatformEvent.Headers)` landed while this round was being
written; the tests reference it, never a literal. Neither `"Credentials"` nor `"Headers"` appears as a
string literal in any test — both stores' payload tests iterate a shared
`StrippedKeys = [CredentialsJsonKey, HeadersJsonKey]`.

**Payload tests extended (both stores).** `R5_StrippedPayloadKeepsEverythingExceptCredentials` now:
- asserts the fixture actually carried each stripped key before the mark, so a strip cannot "pass" against
  a payload that never held the secret;
- asserts each key is gone afterwards;
- iterates every remaining property and asserts it survived byte-equal via `JsonNode.DeepEquals`. The
  Postgres fixture carries a scalar (`RetryCount`) and a nested object (`Metadata`) alongside the strings,
  so a strip that damages structure fails here. This half is the one that matters: the retry lane reads
  this payload back.

**Serializer-key guard extended.** `The_stripped_keys_are_the_ones_the_serializer_writes` is now a Theory
with one row per stripped key, asserting the emitted property name equals the const **and** carries the
value the fixture put there. An added `[JsonPropertyName]` or a naming-policy change on either property
fails loudly instead of silently leaving a secret in the row. Both rows pass, so the serializer currently
emits `Credentials` and `Headers` verbatim.

**CAS invariant widened.** `The_terminal_mark_is_all_or_nothing_under_a_concurrent_write` now requires
*both* keys stripped as part of the all-or-nothing check, so a torn mark that removes one secret and not
the other fails. 200 rounds, passing.

The in-memory fixture's `Headers` now carries `X-Api-Key = h3ader-s3cret` next to a benign `X-Trace`, so
the test would notice a strip that removes the dictionary's contents but leaves the key, or vice versa.

### Results per project

| project | result |
|---|---|
| `UnitTests/...Infrastructure.Core.UnitTests` | **429 passed, 0 failed** |
| `UnitTests/...Application.UnitTests` | **93 passed, 0 failed** |
| `Tests/...API.UnitTests` — non-container | 629 passed, 0 failed |
| `Tests/...API.UnitTests` — `CheckpointRepositoryTests` (Testcontainers) | **NOT RUN** — 34 tests, all `DockerUnavailableException`. The Postgres B1 predicates, B2's `CASE WHEN`, `MarkFailedAsync` and the jsonb strip of **both** keys are **unverified at runtime**. |
| `Tests/...API.UnitTests` — other container suites | NOT RUN — 89 further `DockerUnavailableException` |
| `Tests/...API.UnitTests` — `TestAdapterConnectionCommandHandlerTests` | 3 failed, **pre-existing** (reproduced on a clean worktree at `d53e347d` in round 1) |

No spec-vs-implementation mismatch outstanding. No commit, no push, no PR.

## W1 — round 4: claim TOCTOU, nested blob, single copy path

### B1 — `TryClaimAsync` refuses a terminal row

Confirmed the causal path before changing anything: `GetRecoverableAsync`
(`CheckpointRecoveryHandler.cs:85`) → validate/deserialize/`CanReadStoredStateAsync` → `TryClaimAsync`
(`:295`). `MarkFailedAsync` sets `claimed_by_instance = NULL`, so a row marked inside that window matched
the claim's `claimed_by_instance IS NULL` arm. Before this change set the row was **deleted** there, so the
same UPDATE matched zero rows — the regression is ours.

Postgres, one statement, guard added ahead of the claim arm:

```sql
  AND status <> 'Failed'
  AND (claimed_by_instance IS NULL OR claimed_at_utc < {staleBeforeUtc})
```

In-memory mirrored. That method previously returned `true` **unconditionally**, including for a key that
does not exist; it now returns `false` for a missing row and `false` for a `Failed` row. Both call sites
(`CheckpointRecoveryHandler.cs:295` and `:828`) select a row first and only ever want a non-terminal one,
so nothing legitimate is refused.

### I1 — the nested encrypted blob

Writer `PlatformEventFactory.cs:226`, reader `ProcessEventCommandHandler.cs:475`, both confirmed.

**Key derivation.** `Payload` is a real property → `PayloadJsonKey = nameof(PlatformEvent.Payload)`. The
inner `credentials` is a literal.

> **Superseded — the justification written here was wrong.** I claimed `credentials` had no property behind
> it, generalising from the wrong writer. It does: `AdapterRunPayloadFlat.Credentials`, declared
> `[JsonPropertyName("credentials")]`. The key is still not derivable, but for the opposite reason —
> `nameof` would yield `Credentials` and silently strip nothing. See round 5, I5, which is the accurate
> account and matches the shipped comment.

**Superseded — the SQL below is not what shipped.** This two-arm `CASE` guarded only the nested `Payload`
segment. F6, later in this same round, replaced it with a three-arm `CASE` that also guards the root. The
shipped statement is quoted under F6; this block is kept only to show what the F6 change was made against.

```sql
platform_event_json = (CASE
                          WHEN jsonb_typeof(platform_event_json -> CAST({payloadKey} AS text)) = 'object'
                          THEN platform_event_json #- ARRAY[CAST({payloadKey} AS text), CAST({payloadCredentialsKey} AS text)]
                          ELSE platform_event_json
                       END)
                      - CAST({credentialsKey} AS text)
                      - CAST({headersKey} AS text),
```

The guard is not decoration. `Payload` is a caller-supplied `JsonElement` fed from `request.Indicators` —
plural, and an array is entirely plausible — and `jsonb #- text[]` raises *"path element at position 2 is
not an integer"* when it walks into an array. Unguarded, one array payload would throw inside
`MarkFailedAsync`, be swallowed by the caller's catch, and leave the row claimed and unmarked: the same
silent-failure shape the `nameof` discipline exists to prevent. In-memory is safe by construction — it
removes only when `platformEvent[PayloadJsonKey] is JsonObject`.

Recorded for the same reason as the `Headers` strip: also **defensive**. The blob is encrypted, and the
operator confirmed the retry lane re-supplies credentials rather than reading them back, so nothing needs
it on a terminal row.

### I3 — one copy path

`TransitionFromScheduledWaitAsync`'s inlined 23-property initializer is gone; it now calls `Copy(existing)`
and sets only the five fields it changes (`Status`, `ScheduledResumeAtUtc`, `ClaimedByInstance`,
`ClaimedAtUtc`, `UpdatedAtUtc`), keeping its claim-handover comment. `Copy` is the store's single copy path
and carries a short summary saying why a missed property there is invisible. Verified mechanically:
`CheckpointEntry` has 23 `{ get; set; }` properties and `Copy` has 23 `= source.` assignments.

One incidental behaviour change worth naming: that method read `DateTime.UtcNow` twice, so `ClaimedAtUtc`
and `UpdatedAtUtc` could differ by ticks. They are now one read.

### Build and tests

**Build succeeded**, `NU1900` only. `Infrastructure.Core.UnitTests` **434 passed / 0 failed** — that was
the count at the time of this run; W2 was landing tests concurrently and the final tree is **435**. Up five
from round 3's 429, not six as first written, and I did not check which five, so "W2's coverage for all
three items" was an inference rather than something I verified. `Application.UnitTests` **93 passed /
0 failed**.

Docker remains unavailable. Everything Postgres-side added this round — the `status <> 'Failed'` claim
guard and the `jsonb_typeof` / `#-` path delete, including the array case that motivated the guard — is
**unverified at runtime** and needs `CheckpointRepositoryTests` on a machine with Docker.

## W2 — round 4 (B1 claim guard, I1 nested blob, I3 property-loss guard)

Everything passes against W1's code. No spec-vs-implementation mismatch this round.

### B1 — the claim must refuse a terminal row

**One of W2's own tests pinned the behaviour this reverses, in both stores.**
`R4_MarkingTerminalReleasesTheClaim` asserted that after the mark *another instance could take the row
with `TryClaimAsync`*. That is precisely the regression: an unclaimed terminal row is what let the sweep
re-dispatch a finished run. Inverted in both stores, and the test is now stronger rather than weaker —
it distinguishes the two callers that were being conflated:

- `TryClaimAsync` (the sweep's claim) must **refuse** the terminal row;
- `TryAcquireExecutionLockAsync` (the redelivery path) must **succeed** and flip it back to `Idle`.

The released claim was always for the second of those. R4's original point — that the redelivery is not
dead-lettered on `EXECUTION_LOCKED` — survives intact and is now demonstrated against the right method.

New, both stores:
- `A_row_marked_terminal_after_the_sweep_selected_it_cannot_be_claimed` — the interleaving itself, as one
  test: `GetRecoverableAsync` returns the row, the run is marked terminal while the sweep is still
  validating that snapshot, and the claim is then attempted **from the stale result's own fields**. It
  must lose, and the row must still be `Failed` and unclaimed afterwards. This is the shape of the actual
  regression rather than three separate properties.
- `TryClaimAsync_refuses_a_terminal_row`
- `TryClaimAsync_still_claims_an_Idle_row`
- `TryClaimAsync_reports_false_for_a_row_that_does_not_exist` (in-memory only) — it previously answered
  `true` unconditionally, so a caller branching on the claim was told it owned a row that does not exist.

### I1 — the nested credentials blob

`CheckpointEntry.PayloadJsonKey` and `CheckpointEntry.PayloadCredentialsJsonKey` landed mid-round; the
tests reference both. No literal `"Credentials"`, `"Headers"`, `"Payload"` or `"credentials"` appears in
any test.

`R5_StrippedPayloadKeepsEverythingExceptCredentials` (both stores) now asserts, in this order:
1. the fixture actually carried each secret **before** the mark — top-level `Credentials`, top-level
   `Headers`, and `Payload.credentials` — so no assertion can pass vacuously against a payload that never
   held it;
2. all three are gone afterwards;
3. **every other property survives byte-equal**, at both levels: each remaining top-level property, and
   each of `Payload`'s own siblings, compared with `JsonNode.DeepEquals`. The fixtures deliberately give
   `Payload` an array (`rules`) and scalars alongside the blob, and the Postgres fixture adds a number
   (`RetryCount`) and a nested object (`Metadata`) at the top level, so a strip that damages structure
   rather than merely missing a key fails here. This is the half that matters — the retry lane reads this
   payload back.

`The_nested_credentials_blob_sits_where_the_strip_looks_for_it` asserts the **path**, not the leaf, so a
rename of `Payload` fails loudly instead of silently leaving the blob in the row. The
`The_stripped_keys_are_the_ones_the_serializer_writes` Theory still covers the two top-level keys.
`The_terminal_mark_is_all_or_nothing_under_a_concurrent_write` now requires all three secrets gone as
part of its invariant, so a torn mark that removes some and not others fails.

### I3 — the silent-zeroing guard

`The_terminal_mark_carries_every_property_it_does_not_own` walks `CheckpointEntry` **by reflection**
rather than naming properties, so a property added to the entity is covered the day it is added — which
is the reviewer's concern, rather than today's list being wrong. It seeds every writable property with a
distinctive value (assigned by type, so a new property is populated without editing the test), marks the
row terminal, and asserts every property outside `FieldsTheMarkOwns` (`Status`, `ClaimedByInstance`,
`ClaimedAtUtc`, `UpdatedAtUtc`, `PlatformEventJson`) came through unchanged. A dropped property lands as
its type default, which cannot equal the seeded value, so the copy path cannot silently zero a column.

The test also guards itself: it counts the properties it actually compared and fails below 18, so a
future change that moved everything into the owned set could not leave the loop asserting nothing.

### Results per project

| project | result |
|---|---|
| `UnitTests/...Infrastructure.Core.UnitTests` | **435 passed, 0 failed** |
| `UnitTests/...Application.UnitTests` | **93 passed, 0 failed** |
| `Tests/...API.UnitTests` — non-container | 629 passed, 0 failed |
| `Tests/...API.UnitTests` — `CheckpointRepositoryTests` (Testcontainers) | **NOT RUN** — 37 tests, all `DockerUnavailableException`. The Postgres side of everything above — the `status <> 'Failed'` claim guard, the B1 predicates, B2's `CASE WHEN`, `MarkFailedAsync` and the jsonb strip of all three keys — is **unverified at runtime**. |
| `Tests/...API.UnitTests` — other container suites | NOT RUN — 89 further `DockerUnavailableException` |
| `Tests/...API.UnitTests` — `TestAdapterConnectionCommandHandlerTests` | 3 failed, **pre-existing** (reproduced on a clean worktree at `d53e347d` in round 1) |

No commit, no push, no PR.

### F6 — the two stores agree on a non-object payload (folded into round 4)

Read `review/verifier-2.md` F6 before acting. The finding holds and its suggested `CASE` is right, but it
is **broader than the guard I added earlier this round**: that one guarded the nested `Payload` segment
against `#-` walking into an array. The *root* `platform_event_json` was still unguarded, which is the
divergence F6 names.

Behaviour before this fix, for a root that is valid JSON but not an object:

| root shape | Postgres | InMemory |
| --- | --- | --- |
| scalar | `cannot delete from scalar` → **UPDATE aborts**, row left claimed and non-terminal | returns unchanged, mark succeeds |
| array | `- text` **silently removes matching string elements** | returns unchanged, mark succeeds |
| malformed | unreachable (column is `jsonb`) | `JsonException` escapes, swallowed by the caller → mark abandoned |

The array row is the one that decided it: that is not an error, it is a silent wrong mutation of a stored
payload, and nothing would have reported it.

Postgres now guards the root, keeping the nested guard as a second arm of the same `CASE` so there is one
expression and no duplicated root test:

```sql
platform_event_json = CASE
    WHEN jsonb_typeof(platform_event_json) <> 'object' THEN platform_event_json
    WHEN jsonb_typeof(platform_event_json -> CAST({payloadKey} AS text)) = 'object'
        THEN platform_event_json
             #- ARRAY[CAST({payloadKey} AS text), CAST({payloadCredentialsKey} AS text)]
             - CAST({credentialsKey} AS text)
             - CAST({headersKey} AS text)
    ELSE platform_event_json
         - CAST({credentialsKey} AS text)
         - CAST({headersKey} AS text)
END,
```

A NULL column takes the `ELSE` arm and stays NULL (`NULL - text` is NULL), matching the in-memory
null/whitespace early return.

The malformed-JSON half was mine to decide and I closed it the same way: `StripSensitiveFields` catches
`JsonException`, logs a Warning, and returns the payload unchanged so the mark still succeeds. Postgres
cannot reach that case at all, and throwing was strictly worse on every axis — it stripped nothing *and*
abandoned the mark, leaving the row claimed and non-terminal. The method became an instance method to get
at the logger; silence is the thing this whole change set keeps fighting.

Still divergent and still untouched, as the verifier says: the in-memory `TryAcquireExecutionLockAsync`
discards progress, cursor, adapter state and `ScheduledResumeAtUtc` where the Postgres `DO UPDATE`
preserves them. Pre-existing, out of scope, flagged in round 2.

### Correction to my own round-3 note — F5 is right, the `Headers` survey was incomplete

I recorded **two** writers of `PlatformEvent.Headers`. There are **four**. The two I missed are in the
Console host's interactive publisher, `ConsoleInteractiveService.cs:464` and `:605`, where a developer
types header values at a prompt.

This strengthens the round-3 decision rather than weakening it: a developer pasting an `Authorization`
header into the console tool is a more plausible route to a real token in `platform_event_json` than the
HTTP paths I did survey. The rest of the round-3 finding is unchanged and was independently re-confirmed
by the verifier across both `src/` and the client package: **zero readers.** The strip remains defensive.

### Build and tests

**Build succeeded**, `NU1900` only. `Infrastructure.Core.UnitTests` **435 passed / 0 failed**,
`Application.UnitTests` **93 passed / 0 failed**.

**Correction to what I first wrote here.** I attributed the +1 over the previous run to W2's F6 coverage.
That was wrong — the extra test was `TryClaimAsync_reports_false_for_a_row_that_does_not_exist`, which
belongs to B1, and at that moment F6 had **no** coverage at all: the parity was implemented and untested,
and I should have said so. It has coverage now — see round 6.

Docker still unavailable. The whole `CASE` added here is Postgres-only and therefore **unverified at
runtime** — including both cases that motivated it, the scalar abort and the array element removal.
`CheckpointRepositoryTests` is the only thing that can prove it.

### Two items I did not act on — outside what was asked, flagging rather than doing

- `decisions.md` drift (verifier §4): two of its five entries no longer describe the code — the
  "a terminal row is not written to again" rationale was reversed by B2, and the single-predicate cleanup
  design was reversed by B1. Both are stale in the direction of understating the change. Someone owns that
  file; I did not edit it.
- F4: `EscalateIfNobodyCanServe`'s log line at `CheckpointRecoveryHandler.cs:771` is mildly stale. It is
  true for the non-terminal row it describes, which is why I left it in the round-2 sweep; the verifier
  rates it differently. One line either way — say the word.

## W1 — round 5: mark-failure fallback, in-memory revive parity, corrected doc

### I3 — a missed mark falls back to the delete

The regression is real and the reasoning holds: `DeleteAsync` has **no owner guard**, so the delete this
branch replaced always removed the row. `MarkFailedAsync` is owner-guarded, so a mark that misses its guard
left a row that is non-terminal, still carrying credentials and full progress, and visible to
`GetRecoverableAsync` once the claim went stale — re-dispatching a run whose done was already published.

On `false`, both sites now delete, keeping the warning:

- `ProcessEventCommandHandler.TryMarkCheckpointFailedAsync` → `TryDeleteCheckpointAsync(...)`, the same
  swallow-and-log wrapper the branch replaced.
- `CheckpointRecoveryHandler.EndUnservableRunAsync` → `checkpointRepository.DeleteAsync(...)`, the call it
  replaced verbatim.

**I did both, where the brief described one.** Both replaced an unguarded delete in this change set and
both carry the identical exposure, so fixing only the handler would have left the same hole in the sweep.
Say so if the recovery half was meant to stay as it was.

Net effect: strictly no worse than baseline on every path. Mark succeeds → terminal row, credentials
stripped. Mark misses → row gone, exactly as before this change set. Neither path leaves a
credential-bearing row recoverable.

### I2 — the in-memory revive resumes, as production does

`TryAcquireExecutionLockAsync` replaced the row wholesale, zeroing `CurrentPage`, `CursorToken`,
`AdapterStateJson` and `ScheduledResumeAtUtc`, where the Postgres `ON CONFLICT DO UPDATE` writes five
columns and preserves the rest. So the store behind most of this suite showed **restart** semantics on
revive while production shows **resume** — the one question still open for the operator, answered backwards
by the test double. I flagged this as out of scope in rounds 2 and 4; it stopped being out of scope when
B2 made revive a behaviour the suite asserts.

The conflict path now writes exactly the five columns Postgres writes — `claimed_by_instance`,
`claimed_at_utc`, `platform_event_json`, `status` (the `Failed → Idle` flip), `updated_at_utc` — over a
`Copy(existing)`, so `created_at_utc` and every progress field survive. The insert path is unchanged. Not
mirrored, and deliberately: Postgres guards its `DO UPDATE` on unclaimed-or-stale-or-same-instance, while
this store still always succeeds, which is the pre-existing "single-process, no contention" contract stated
in its own comment.

This makes `Copy` load-bearing for a third caller, which is what round 4's I3 was for.

### I5 — the const's justification was wrong; corrected with the real reason

The reviewer is right and my comment was wrong in a way worth spelling out, because the true reason is the
stronger one.

I wrote that `credentials` has "nothing to take `nameof` of". The site I checked —
`PlatformEventFactory.BuildIoaBatchPayload:226` — really is an anonymous object, but it is the **IOA batch
mitigation** payload, and the reader at `ProcessEventCommandHandler.cs:475` is scoped to
`AdapterCategory.SiemRules`. I generalised from the wrong writer.

The blob the SIEM-rules path actually reads is the flat dispatch shape, and it **does** have a named type:
`Domain/Messaging/AdapterRunMessage.cs:156`, `AdapterRunPayloadFlat.Credentials` — declared
`[JsonPropertyName("credentials")]`. Same attribute on `AdapterRunAction.Credentials` (`:82`) and on four
other types.

So the key is not derivable, but for the **opposite** reason to the one I gave: there is a property, and
`nameof` would return `Credentials` — missing the lowercase wire key and stripping nothing while reporting
success. Deriving it here would *cause* the silent failure the derivation exists to prevent. That is the
one place in this change set where a literal is correct rather than a compromise, and the comment now says
that, names the type, and notes the reader matches the same literal.

Contrast worth keeping straight: `PlatformEvent.Credentials` and `PlatformEvent.Headers` carry **no**
`[JsonPropertyName]`, which is why `nameof` is right for those two and wrong for this one.

### Build and tests

**Build succeeded**, `NU1900` only. `Infrastructure.Core.UnitTests` **435 passed / 0 failed**,
`Application.UnitTests` **93 passed / 0 failed**. Counts are unchanged from the previous round, so W2's
coverage for these three had not landed at the time of this run — the numbers prove no regression, not that
the new behaviour is pinned.

Docker still unavailable; the Postgres suite (34 tests) still cannot run. Unchanged from every prior round
and still the largest hole in this change's evidence.

## W2 — round 5 (mark-failure fallback, revive parity)

### I2 caught the divergence on its first run, then went green

`Taking_the_execution_lock_returns_a_terminal_row_to_Idle` failed on the first run after the progress
assertions were added, and passed on the next without W2 touching it — W1 landed the fix in between. The
in-memory `TryAcquireExecutionLockAsync` now copies the stored row rather than replacing it, so
`CurrentPage`, `ProcessedItems`, `SequenceId`, `CursorToken` and `AdapterStateJson` survive the revive.

That is the point of the test rather than decoration, and worth stating plainly for the operator's open
question: **reviving a failed run resumes it, it does not restart it.** Until this round the in-memory
store answered the opposite way from Postgres, so any suite result about revive semantics was evidence
about the wrong implementation. Both stores now assert the same thing:

- in-memory `Taking_the_execution_lock_returns_a_terminal_row_to_Idle` — seeds page 17756 / 44,363,845
  items / sequence 100 / a cursor / adapter state on a `Failed` row, takes the lock with a candidate
  carrying **no progress of its own** (what a fresh execution offers), and asserts status `Idle`, the new
  owner, and every progress field intact.
- Postgres `TryAcquireExecutionLockAsync_returns_a_terminal_row_to_Idle` — same seed and same assertions,
  with `AdapterStateJson` compared through `JsonShouldEqual` because the column is jsonb.
  `SeedCheckpointAsync` gained a trailing `cursorToken` parameter for it.

The candidate row is now built by a dedicated `LockCandidate()` helper in both files rather than reusing
the seeded entry — reusing it would have made preserve and overwrite indistinguishable, since both would
have produced the same page.

### I3 — the mark's failure path deletes

`Handle_logs_when_the_terminal_mark_finds_no_row` **renamed and strengthened** to
`Handle_deletes_the_checkpoint_when_the_terminal_mark_finds_no_row` (updated, not duplicated). It now
pins all three obligations on that path:

- the adapter's own outcome still stands (`Status == Failure`) — the mark is bookkeeping, not the verdict;
- `DeleteAsync` is called once for the run's composite key, so a row carrying full credentials and
  progress is not left behind for the sweep to pick up once the claim goes stale. The unguarded delete
  this replaced always removed the row, so leaving it was a regression against baseline;
- the warning is still logged, asserted on level and correlation id only, never on wording.

Passes.

### Results per project

| project | result |
|---|---|
| `UnitTests/...Infrastructure.Core.UnitTests` | **435 passed, 0 failed** |
| `UnitTests/...Application.UnitTests` | **93 passed, 0 failed** |
| `Tests/...API.UnitTests` — non-container | 629 passed, 0 failed |
| `Tests/...API.UnitTests` — `CheckpointRepositoryTests` (Testcontainers) | **NOT RUN** — 37 test cases, all `DockerUnavailableException`. Everything Postgres-side remains **unverified at runtime**: the `status <> 'Failed'` claim guard, both delete predicates, the revive's `CASE WHEN` and its progress preservation, `MarkFailedAsync` and the jsonb strip of all three keys. |
| `Tests/...API.UnitTests` — other container suites | NOT RUN — 89 further `DockerUnavailableException` |
| `Tests/...API.UnitTests` — `TestAdapterConnectionCommandHandlerTests` | 3 failed, **pre-existing** (reproduced on a clean worktree at `d53e347d` in round 1) |

No spec-vs-implementation mismatch outstanding. No commit, no push, no PR.

## W1 — round 6: retention ceiling, and corrections to this file

### Upper clamp restored

`MaximumFailedRetentionDays = 30` added beside the floor, public so a test can assert the bound rather than
hardcode 30. `CheckpointCleanupJob.FailedRetention` moved from `Math.Max` to `Math.Clamp(days, Minimum,
Maximum)` — clamping at both ends, never throwing, matching what the low end already did.

The failure mode justifies the bound, and it is worse than "retains too long": the cutoff is
`DateTime.UtcNow - TimeSpan.FromDays(days)`, so a large enough value overflows the subtraction and throws
out of `Execute`. That is inside the job's single try, so it becomes a `JobExecutionException` and takes
**all three sweeps** down with it — the stopped-checkpoint sweep and the stop-request sweep as well as the
terminal one — on every fire, with nothing reaped and no signal beyond a job-failure log. Silent and total,
exactly as the verifier described.

Coverage: `Execute_floors_a_nonsensical_retention_setting` covers the low end. There is **no ceiling test
yet**; W2 is adding one. I am not claiming the bound is pinned.

### Corrections to this file

The notes are the durable record, so these are corrected in place rather than only appended. Three of the
four were mine to catch and I did not.

1. **Round 4, I1 — the quoted SQL was superseded within the same round.** The two-arm `CASE` shown there
   guards only the nested `Payload` segment; F6, later in that round, replaced it with the three-arm
   `CASE` that also guards the root. The block is now marked superseded and points at the F6 quote, which
   is the shipped statement. Keeping it is worth more than deleting it — it is what the F6 change was made
   against — but it was presented as current, which was wrong.
2. **Round 4, I1 — the key-derivation justification was the wrong one**, corrected in round 5 (I5) but left
   standing here, so the file contradicted itself. Now marked superseded with a pointer to the accurate
   account: there *is* a property, `AdapterRunPayloadFlat.Credentials`, and `[JsonPropertyName]` is why
   `nameof` would be wrong.
3. **Round 4 counts.** "434 … up six from round 3" — 429 → 434 is **five**, and I never checked which five,
   so "W2's coverage for all three items" was an inference stated as fact. 434 was what I measured at the
   time; W2 was landing tests concurrently and the tree is now 435. Both are now stated as such.
4. **Round 4, F6 — I claimed coverage that did not exist.** I attributed the +1 to "W2's F6 coverage". The
   extra test was `TryClaimAsync_reports_false_for_a_row_that_does_not_exist`, which is B1's. At that
   moment F6 was implemented and **untested**, and the note should have said so.

F6 does have coverage now, verified by running it rather than by reading names —
`--filter "…The_mark_leaves|…The_mark_still_ends|…TryClaimAsync|…retention"` → **25 passed / 0 failed**,
including `The_mark_leaves_a_non_object_root_alone`,
`The_mark_leaves_an_array_payload_with_all_of_its_elements`,
`The_mark_leaves_a_non_object_payload_untouched` and
`The_mark_still_ends_the_run_when_the_payload_is_not_json`. That is the **in-memory** half. The Postgres
half of F6 — the three-arm `CASE` — is still Docker-gated and unverified, as it has been every round.

### Build and tests

**Build succeeded**, `NU1900` only. `Infrastructure.Core.UnitTests` **435 passed / 0 failed**,
`Application.UnitTests` **93 passed / 0 failed**. The Postgres suite still cannot run here.

## W2 — round 6 (strip edge cases) and a correction to the record

### Correction: F6 was NOT covered before this round

The line above — "`Infrastructure.Core.UnitTests` **435 passed / 0 failed** (up one from earlier this
round — W2's F6 coverage)" — is wrong, and W2's earlier sections did not catch it. The +1 test at that
point was `TryClaimAsync_reports_false_for_a_row_that_does_not_exist`, which is B1 claim-guard coverage
and has nothing to do with the store-parity `CASE`. **No test existed for any non-object payload shape in
either store until this round.** A coverage claim nobody can resolve is worse than a stated gap, so:
before round 6, F6's `CASE` — root and nested arms both — was defended by nothing.

It is covered now, by the tests below.

### The four shapes, both stores

The guard exists to stop a weird payload becoming a corrupted one, and each shape has its own way of
going wrong. Every test asserts the mark still succeeds — a run whose payload cannot be cleaned still has
to end — and then that the payload was not damaged.

| shape | in-memory | Postgres |
| --- | --- | --- |
| array `Payload` | `The_mark_leaves_an_array_payload_with_all_of_its_elements` | same name |
| non-object `Payload` (string, number, bool, JSON null) | `The_mark_leaves_a_non_object_payload_untouched` (Theory ×4) | same name, Theory ×4 |
| non-object root (array, string, number, JSON null) | `The_mark_leaves_a_non_object_root_alone` (Theory ×4) | same name, Theory ×4 |
| NULL / malformed | `The_mark_still_ends_the_run_when_the_payload_is_not_json` (Theory ×5: null, `""`, whitespace, `{not json`, a truncated object) | `The_mark_still_ends_the_run_when_the_payload_is_null` |

The array case is the one the verifier singled out, and it is asserted the way the failure would actually
present: the payload comes back `DeepEquals` to `[1,2,3]`, so an element silently removed by a by-key
delete fails rather than passing as "still an array". The non-object cases assert the top-level strip
*still happened* alongside the payload being untouched, so a guard that bails out early — leaving the
credentials in place — fails too.

**Why Postgres has no malformed case:** `platform_event_json` is `jsonb`
(`CheckpointDbContext.cs:73-74`), so Postgres rejects malformed text on write and the shape cannot reach
the column at all. The unparseable arm therefore exists only against the in-memory store, whose column is
a plain string — which is also the only store where that arm can go wrong. Stated here rather than left
as an apparent asymmetry.

The malformed theory's final assertion is deliberately weak in one direction and strict in the other: the
column must end up **either** exactly as it was **or** null. That admits both reasonable designs while
still failing if the store writes back some *other* unparseable text, which is the actual corruption
being defended against.

### Results per project

| project | result |
|---|---|
| `UnitTests/...Infrastructure.Core.UnitTests` | **449 passed, 0 failed** (up 14 — the edge-case tests) |
| `UnitTests/...Application.UnitTests` | **93 passed, 0 failed** |
| `Tests/...API.UnitTests` — non-container | 629 passed, 0 failed |
| `Tests/...API.UnitTests` — `CheckpointRepositoryTests` (Testcontainers) | **NOT RUN** — 47 test cases, all `DockerUnavailableException`. Everything Postgres-side remains **unverified at runtime**, and that now explicitly includes the F6 `CASE` this round exists to defend: the scalar abort and the array element removal are exactly the two behaviours no run here can reach. |
| `Tests/...API.UnitTests` — other container suites | NOT RUN — 89 further `DockerUnavailableException` |
| `Tests/...API.UnitTests` — `TestAdapterConnectionCommandHandlerTests` | 3 failed, **pre-existing** (reproduced on a clean worktree at `d53e347d` in round 1) |

139 API failures = 136 container-gated + 3 pre-existing. No commit, no push, no PR.
