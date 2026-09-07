# Verifier 1 — failed-checkpoint terminal status

Diff scope: branch `feat/failed-checkpoint-retention-ttl`. **The work is uncommitted** — `HEAD` is
`d53e347d` and the change lives in the working tree (11 modified tracked files, 3 new test files).
Everything below is read from `git diff` plus the untracked files, not from a commit.

Environment as I ran it:

- `dotnet build …/Cymulate.IntegrationServiceBus.sln` → **Build succeeded, 0 errors, 14 warnings**
  (`NU1900`/`CS1573`/`CS9113`/`xUnit2013`, all pre-existing).
- `Application.UnitTests` → **92/92 passed**.
- `Infrastructure.Core.UnitTests` → **416/416 passed**.
- `API.UnitTests` → 629 passed, 122 failed = **119 `DockerUnavailableException`** (Testcontainers,
  Docker not running) + **3 pre-existing** `TestAdapterConnectionCommandHandlerTests` failures about
  YAML routing, untouched by this diff.
- `CheckpointRepositoryTests` → **30/30 `DockerUnavailableException` — NOT RUN, not failed.**
- The 83-test `Application.UnitTests` baseline was not re-run from a clean `d53e347d` worktree; it is
  consistent with the diff, which adds exactly 9 tests to that project (R2 theory × 6, R3, and the two
  `Handle_still_deletes_…` regressions). 92 − 9 = 83. The `CLAUDE.md` claim of a pre-existing failure in
  `Application.UnitTests` does not hold here — the suite is fully green.

---

## 1. Success Criteria coverage

| # | criterion | verdict | evidence |
|---|---|---|---|
| 1 | A reported failed done marks the row `Failed`, releases the claim, strips credentials, does not delete the row | **partially met** | `ProcessEventCommandHandler.cs:1251-1255`; `InMemoryCheckpointRepository.cs:153-172`. `FailedRunLeavesATerminalRowTests.R1_SweepNeverOffersAFailedRow` **passed** (real handler + real store: row survives, `Status=Failed`, claim nulled). `R2_OnlyReportedFailureMarksTerminal` **passed** (6 rows). Strip proven only in-memory (`R5_…` in `InMemoryCheckpointRepositoryTerminalStatusTests` **passed**); the Postgres `jsonb - text` strip is **NOT RUN**. |
| 2 | Every other outcome deletes exactly as before | **met** | `R2_OnlyReportedFailureMarksTerminal` asserts `DeleteAsync` `Times.Once` for `Success`/`Skipped`/`Cancelled` and `Times.Never` for the three failures — **passed**. `Handle_still_deletes_the_checkpoint_when_no_adapter_is_registered` and `…when_the_adapter_throws` **passed**. Stop-requested and `DeleteByCorrelationId` paths are untouched code (`ProcessEventCommandHandler.cs:391`, `:411`, `EventsController.cs:1055`) with no new test that ran. |
| 3 | `GetRecoverableAsync` never returns a `Failed` row, in both stores | **partially met** | In-memory: `InMemoryCheckpointRepository.cs:308-309`, two `R1_SweepNeverOffersAFailedRow` tests **passed**, one of which drives the real `CheckpointRecoveryHandler.RecoverAsync` and asserts 0 dispatched. Postgres: `CheckpointRepository.cs:707` — **NOT RUN**. |
| 4 | `DeleteExpiredAsync` deletes a `Failed` row past the window and leaves everything else alone, in both stores | **partially met** | In-memory: `InMemoryCheckpointRepository.cs:191`; `DeleteExpiredAsync_reaps_…`, `…keeps_…`, `…does_not_reap_a_non_terminal_row` (Theory × 3) all **passed**. Postgres: `CheckpointRepository.cs:485` — **NOT RUN**. |
| 5 | Stop-request row deletion unchanged | **met** | `CheckpointCleanupJob.cs:85` still passes the TTL `cutoff`. `Execute_still_deletes_stop_request_rows_on_the_ttl_not_the_retention_window` **passed** and additionally asserts the cutoff is *not* the retention horizon. `Execute_still_runs_the_stop_aware_checkpoint_sweep` **passed**. |
| 6 | Retention window configurable, sane default, clamped against a nonsensical value | **partially met** | `ConfigurationKeys.cs:348,364,371`; `CheckpointCleanupJob.cs:36-40` — `Math.Max(MinimumFailedRetentionDays, …)`, default 7, floor 1. The floor has **no test** (grep: `MinimumFailedRetentionDays` appears only in the two production files). No upper clamp; a configured `100000` is accepted. A non-numeric value throws out of `configuration.GetValue<int>` into the job's catch → `JobExecutionException`, which is loud, so acceptable. |
| 7 | Tests cover status write + two side effects, each non-failure deleting, sweep exclusion, cleanup predicate, stop still winning, stripped payload | **partially met** | All present. "Stop still winning" is `DeleteStoppedCheckpointsAsync_still_deletes_a_terminal_row` — Docker-gated, **NOT RUN**. Everything else ran and passed. |
| 8 | Builds; affected projects pass; Docker-gated reported as not-run rather than green | **met** | Counts above. `execution_notes.md` reports the Docker gate honestly and does not claim the Postgres suite green. |
| 9 | No file outside the repo modified; no new migration | **met** | `git status` shows only paths under `/Users/user/Dev/IntegrationServiceBus`. `Migrations/` is unmodified — no new file, no `Designer.cs` change, `CheckpointDbContextModelSnapshot.cs` untouched. No new `CheckpointStatus` member. |

---

## 2. Assumption Disposition

**Default is NEVER-TESTED.** A row is VALIDATED/REJECTED only where a specific hunk, a test I ran, or a
traced call path settles it.

### Prior Art

| id | status | citation | actor |
|---|---|---|---|
| `lessons.md#L-d13589c6` — retaining is storage-only, no lifecycle consequence (refuted: the delete was also releasing the claim) | VALIDATED | The lesson held and was acted on: `InMemoryCheckpointRepository.cs:166-167` and `CheckpointRepository.cs:462-463` null both claim columns in the same write. `R4_MarkingTerminalReleasesTheClaim` (in-memory) **passed** and re-claims from a second instance; `FailedRunLeavesATerminalRowTests` **passed** and asserts the same from the real handler. | verifier-1 |
| `lessons.md#L-716f3c62` — a sweep predicate and a TTL predicate over the same column should agree (refuted) | VALIDATED | The two deliberately disagree: `GetRecoverableAsync` uses `Status != Failed`, `DeleteExpiredAsync` uses `Status == Failed`, and `CheckpointCleanupJob` computes two independent cutoffs (`:53-55`). `CheckpointCleanupJobTests` pins them apart in **both** directions and **passed**. | verifier-1 |
| `lessons.md#L-44a4039e` — extending retention has no security dimension (refuted: `platform_event_json` holds plaintext credentials) | VALIDATED | `The_stripped_key_is_the_one_the_serializer_writes` **passed** — a serialized `PlatformEvent` really does emit `"Credentials":{"apiToken":"s3cret"}`. The strip itself is proven only in-memory; the Postgres half is NOT RUN. See finding F4 for a second-order consequence nobody recorded. | verifier-1 |
| `lessons.md#L-5d94327f` — the retention branch is reached by every collector-reported failure (refuted: shutdown cancellation and claim-loss return first) | VALIDATED | Still true after the change. `ProcessEventCommandHandler.cs:274-282` returns before `CompleteExecutionAsync` on external cancellation; `:266-269`, `:371-372`, `:382-386` return `CLAIM_LOST` without completing; `:1247-1249` returns the refusal above the new branch. `R3_RefusedWriteTakesPrecedence` **passed**. | verifier-1 |
| `lessons.md#L-910c8f52` — the migration and raw SQL added for retention are correct (untested: Docker unavailable) | VALIDATED | Same gate, same verdict: 30/30 `CheckpointRepositoryTests` threw `DockerUnavailableException`. The new raw `UPDATE` in `CheckpointRepository.cs:441-473` has never executed. | verifier-1 |

### Task assumptions

| id | status | citation | actor |
|---|---|---|---|
| A1 — writing `Status = Failed` reaches every collector-reported failed done and no other outcome | **REJECTED** | The second half holds (`R2_OnlyReportedFailureMarksTerminal` **passed**, 6 statuses). The first half does not: a run cancelled by pod shutdown returns at `ProcessEventCommandHandler.cs:274` and a superseded run returns `CLAIM_LOST` at `:266`/`:371`/`:382`, all before `CompleteExecutionAsync`; and `HandleAdapterNotFoundAsync` (`:790`) and `HandleUnhandledExceptionAsync` (`:880`) still **delete**. Must not be re-assumed: "every reported failure reaches the mark." | verifier-1 |
| A2 — nothing writes to a row after it is marked `Failed`, so `UpdatedAtUtc` is a stable failure timestamp | **REJECTED** | `TryAcquireExecutionLockAsync`'s `ON CONFLICT` (`CheckpointRepository.cs:646-653`) has no status guard and sets `updated_at_utc = now`, so a broker redelivery of the same event resets the retention clock on a terminal row. `UpsertAsync`'s `ON CONFLICT … WHERE` (`:97`) only excludes `ScheduledWait`, so the first checkpoint write of that redelivery flips `status` back off `Failed` entirely. Must not be re-assumed: "a terminal row is never written to again." | verifier-1 |
| A3 — `Status != Failed` on `GetRecoverableAsync` fully prevents re-dispatch; no second dispatch path | **REJECTED** | The sweep is genuinely blocked (two `R1_…` tests **passed**, one through the real `RecoverAsync`). But `ExecuteWithResumeAsync` (`ProcessEventCommandHandler.cs:1176-1186`) fetches the row by key with **no status filter**, so a redelivery resumes from the terminal row's page and state. See F5. Must not be re-assumed: "the recovery sweep is the only thing that picks a checkpoint back up." | verifier-1 |
| A4 — narrowing `DeleteExpiredAsync` to terminal rows leaks nothing | **REJECTED** | See F1. Two classes of non-terminal row are ended by neither recovery nor the unservable escalation and now live for ever. | verifier-1 |
| A5 — `Checkpoint:TtlHours` keeps deleting stop-request rows unchanged | VALIDATED | `CheckpointCleanupJob.cs:85` unchanged argument; `Execute_still_deletes_stop_request_rows_on_the_ttl_not_the_retention_window` **passed**. | verifier-1 |
| A6 — `DeleteStoppedCheckpointsAsync` still deletes a terminal row | **NEVER-TESTED** | The only test is `CheckpointRepositoryTests.DeleteStoppedCheckpointsAsync_still_deletes_a_terminal_row`, **NOT RUN** (Docker). The SQL was not modified and carries no status predicate, which is an argument, not evidence. | verifier-1 |
| A7 — releasing the claim in the same write prevents the redelivery dead-letter | VALIDATED (in-memory only) | `R4_MarkingTerminalReleasesTheClaim` **passed** and then re-claims as `isb-pod-successor`; `FailedRunLeavesATerminalRowTests` **passed** end to end. Postgres `MarkFailedAsync` NOT RUN. | verifier-1 |
| A8 — stripping `Credentials` damages nothing else, and nothing reads them back | **REJECTED** | First half VALIDATED: `R5_StrippedPayloadKeepsEverythingExceptCredentials` **passed**, comparing every non-credential property by `JsonNode.DeepEquals`. Second half is false — `CheckpointRecoveryHandler.cs:248` deserializes `PlatformEventJson` into a `PlatformEvent` and `ProcessEventCommandHandler.cs:186` hands `platformEvent.Credentials` to `adapterActivator.ActivateAsync`. Credentials **are** read back off a stored checkpoint on the recovery path. It is safe here only because `Failed` rows are excluded from the sweep. See F4. | verifier-1 |
| A9 — `CheckpointStatus.Failed` writes with no schema change; plain text, no CHECK | VALIDATED (statically) | `CheckpointDbContext.cs:87-91` — `.HasConversion<string>()`. `20260526144503_AddCheckpointStatusAndScheduledResume.cs` adds a plain `status` column and a partial index on `'ScheduledWait'`; no CHECK constraint anywhere in `Migrations/`. No write executed (Docker). | verifier-1 |
| A10 — `Handle`'s early guard behaves correctly when a redelivery meets a `Failed` row | **REJECTED** | `ProcessEventCommandHandler.cs:110-118` short-circuits on `ScheduledWait` only. A redelivery meeting a `Failed` row takes the lock (A2), resumes from it (A3), and un-terminalizes it on the first checkpoint write. That may be desirable, but no one decided it. Must not be re-assumed: "a terminal row is inert against a redelivery." | verifier-1 |
| A11 — both stores express the new predicates identically | VALIDATED for the predicates; the strip diverges | `GetRecoverableAsync` and `DeleteExpiredAsync` are textually equivalent in both stores (`InMemoryCheckpointRepository.cs:191`, `:308-309` vs `CheckpointRepository.cs:485`, `:707`). `MarkFailedAsync`'s credential strip does **not** agree on a malformed payload — see F6. | verifier-1 |

---

## 3. Attention Item Disposition

Every named test run by me, individually.

| id | final disposition | evidence |
|---|---|---|
| R1 — the sweep re-dispatches a terminal row | handled | `--filter "FullyQualifiedName~InMemoryCheckpointRepositoryTerminalStatusTests"` → **Passed, 0 failed / 14 total** (includes `R1_SweepNeverOffersAFailedRow`). `--filter "FullyQualifiedName~FailedRunLeavesATerminalRowTests"` → **Passed, 1/1**; that one drives the real `CheckpointRecoveryHandler.RecoverAsync` over the store the real handler just wrote and asserts `0` dispatched. The Postgres twin `CheckpointRepositoryTests.R1_SweepNeverOffersAFailedRow` — **NOT RUN**, `DockerUnavailableException`. |
| R2 — retention applied to the wrong outcomes | handled | `--filter "FullyQualifiedName~R2_OnlyReportedFailureMarksTerminal"` (with R3) → **Passed, 7/7**. The theory builds each result through the real factory and asserts the produced `Status`, then asserts mark-vs-delete per status with `Environment.MachineName` as the instance id, not `It.IsAny<string>()`. |
| R3 — a claim-lost execution marks another owner's row terminal | handled | Same run, **passed**. Confirmed structurally too: the new branch sits at `ProcessEventCommandHandler.cs:1251`, below the `HasRejectedCheckpointWrite` return at `:1247`. |
| R4 — redelivery dead-letters because the claim was never released | handled (in-memory); **unverified on Postgres** | `R4_MarkingTerminalReleasesTheClaim` (in-memory) **passed**; `FailedRunLeavesATerminalRowTests` **passed**. `CheckpointRepositoryTests.R4_MarkingTerminalReleasesTheClaim` — **NOT RUN**. |
| R5 — the credential strip silently no-ops | handled (in-memory); **unverified on Postgres** | `R5_StrippedPayloadKeepsEverythingExceptCredentials` (in-memory) **passed**; `The_stripped_key_is_the_one_the_serializer_writes` **passed** — this is the one that actually closes the "silent" failure mode, because it asserts the serializer emits the key `nameof` produces. No literal `"Credentials"` appears in any test (grep confirms). `CheckpointRepositoryTests.R5_…` — **NOT RUN**. |

---

## 4. Decision drift

| decision | outcome |
|---|---|
| Retention expressed by `status` + `updated_at_utc`, no new column | **landed as decided.** No migration, no snapshot change, no new enum member. |
| A terminal row is not written to again, so `updated_at_utc` is a stable failure timestamp | **abandoned in practice** — asserted, not enforced. `TryAcquireExecutionLockAsync` and `UpsertAsync` both write terminal rows (A2). Nothing in the diff guards either. |
| The recovery sweep is gated on **status**, not a timestamp | **landed as decided.** `CheckpointRepository.cs:707`, `InMemoryCheckpointRepository.cs:308-309`. |
| The cleanup job's checkpoint predicate deletes only terminal rows; non-terminal rows are left to recovery and the unservable escalation | **landed as decided, on a premise that does not hold.** The predicate is exactly as decided; the justification is wrong (F1). |
| Proceeding unverified: every reported failure reaches the branch | **landed as decided** — the known exceptions are real and remain (A1). |
| Proceeding unverified: nothing reads `Credentials` back off a stored checkpoint | **changed** — something does (A8/F4). The design is still safe, for a different reason than the one recorded. |

---

## 5. Findings

### F1 — the headline consequence: which rows now leak, and how many

`DeleteExpiredAsync` is terminal-only, and nothing else reaps by TTL. The claim in `decisions.md` that
non-terminal rows "are ended by recovery or the unservable escalation" is **not true for two classes**,
and I read the escalation to check:

`EndUnservableRunAsync` (`CheckpointRecoveryHandler.cs:811-870`) is the only remaining backstop that
deletes a non-terminal row, and it is reached from exactly two call sites — `:523` and `:592` — both
inside `CanReadStoredStateAsync`, i.e. **only when a registered, `ICheckpointStateCompatibility`-aware
collector declines the row's `AdapterStateJson`, or that state is unparseable.** Everything that returns
`DispatchOutcome.Skipped` *earlier* than that never reaches it:

1. **Unusable `PlatformEventJson`.** `TryDispatchAsync` returns `Skipped` at `:238` (empty payload —
   "pre-migration checkpoint"), `:250` (`JsonException`), and `:259` (deserialized to null). All three
   are **above** the compatibility probe, so the escalation never sees the row. Such a row is now
   immortal: re-listed by every 5-minute sweep, logged, skipped, never claimed, never deleted. Before
   this change the 24-hour TTL reaped it.
2. **Orphaned tenant partition.** `GetRecoverableAsync` is filtered by
   `CheckpointPartition.OwnedByPartitionPredicate(ExpectedTenantId)` — a dedicated pod sees only its own
   `TENANT_ID`, a shared pod sees only `NULL`/`""`/`"default"`/`"none"`. A row belonging to a tenant
   whose dedicated pod is scaled to zero or decommissioned is invisible to **every** running instance, so
   no sweep, no escalation, no delete. Before this change the cleanup job — which has no partition filter
   — removed it after 24 hours. This is the larger of the two: it is per-tenant, not per-bug.

Everything else self-heals: shutdown cancellation (`ProcessEventCommandHandler.cs:274`, and the
`TryReleaseClaimAsync` at `:332`) leaves an unclaimed row that the next sweep dispatches; the `CLAIM_LOST`
returns leave the row with the execution that now owns it; `HandleCheckpointPersistenceFailureAsync`
(`:922`) and `HandBackForRecoveryAsync` (`:1476`) both release the claim deliberately for recovery. Those
are bounded, not leaks.

`ScheduledWait` rows were already exempt from the old predicate, so nothing changed for them.

Magnitude: unbounded but slow — one row per unrecoverable correlation, plus the entire checkpoint
backlog of any retired tenant partition. It is a table-growth and PII-retention problem, not a
correctness one. It follows from the operator's own direction and W2 flagged it; I am recording that it
is *worse than the note says*, because the note attributes the safety to recovery and the escalation, and
the escalation demonstrably does not cover it.

### F2 — stop-request deletion is genuinely unchanged

Checked, not assumed. `CheckpointCleanupJob.Execute` computes `cutoff` from the job-data `TtlHours`
exactly as before (`:47-50`) and still passes it to `stopRequestRepository.DeleteExpiredAsync(cutoff, …)`
at `:85`. `DeleteStoppedCheckpointsAsync()` still takes no cutoff and still runs first (`:59`). Only
`checkpointRepository.DeleteExpiredAsync` moved to `failedCutoff`. `CheckpointCleanupJobTests` captures
both cutoffs via `Callback` and asserts each is the right horizon **and not the other** — a swap fails in
either direction. Both tests **passed**. `StopRequestRepository.cs` is not in the diff.

One thing the note does not mention and I checked: `CheckpointCleanupJob`'s constructor gained
`IConfiguration`, and it is constructed by Quartz, not by hand. `q.AddJob<CheckpointCleanupJob>(…)`
(`Infrastructure.Core/DependencyInjection.cs:290`) registers the type with the container and Quartz
3.18.1's `AddQuartz` uses the Microsoft DI job factory by default, so the new parameter resolves. No
wiring change was needed and none was made. Correct.

### F3 — the owner guard does hold at the call site

`TryMarkCheckpointFailedAsync` passes `InstanceId` (`ProcessEventCommandHandler.cs:1103`), the same value
`TryAcquireExecutionLockAsync` wrote into `claimed_by_instance` at `:141`. Nothing between the lock and
`FlushAndCleanupCheckpointAsync` releases that claim on the completion path — `TryReleaseClaimAsync` is
only called from the shutdown-cancel catch (`:332`), the persistence-failure handler (`:922`) and
`HandBackForRecoveryAsync` (`:1476`/`:1523`), none of which reach completion. `UpsertAsync`'s
`ON CONFLICT` renews `claimed_at_utc` but never clears the owner, and its ownership guard (`:104-109`)
would refuse a write from anyone else. So the guard is real, not decorative, and it does not silently
no-op on the normal path. The one case where it *does* return `false` — heartbeat-detected claim loss
that did not trip the CTS in time — leaves the row to whoever now owns it, which is the intended
behaviour.

The return value is discarded (`TryMarkCheckpointFailedAsync` awaits and ignores the `bool`). A `false`
therefore produces no log line at all. Given `MarkFailedAsync` was given a `Task<bool>` specifically so
"no row matched" is expressible, not logging it wastes the signal. Minor, but it is the difference
between "the mark silently did nothing" being diagnosable and not.

### F4 — the credential strip works, and the sweep exclusion is load-bearing for it

The `nameof` derivation is correct and, more importantly, *proven* correct rather than argued:
`CheckpointEntry.CredentialsJsonKey = nameof(PlatformEvent.Credentials)` (`CheckpointEntry.cs:16`), and
`The_stripped_key_is_the_one_the_serializer_writes` **passed**, which parses a real
`JsonSerializer.Serialize(platformEvent)` and asserts the key exists with the expected value. That closes
R5's silent-failure mode for the in-memory store. `PlatformEventJson` is written with no serializer
options (`ProcessEventCommandHandler.cs:134`), so the runtime key matches.

The consequence nobody recorded: **`CheckpointRecoveryHandler` reads credentials back off a stored
checkpoint.** It deserializes `PlatformEventJson` into a `PlatformEvent` (`:248`) and dispatches it, and
`ProcessEventCommandHandler` passes `platformEvent.Credentials` straight into
`adapterActivator.ActivateAsync` (`:186`). A stripped row dispatched through recovery would activate an
adapter with no credentials. Today that cannot happen, because `GetRecoverableAsync` excludes `Failed`.
Which means the `Status != Failed` exclusion is not only R1's fix — it is the only thing keeping the
credential strip from breaking recovery. Nothing in the code or the docs says so. If anyone ever relaxes
that predicate (say, to let a terminal row be retried by the sweep), they get a credential-less
re-dispatch, and the failure will look like a vendor auth problem.

### F5 — a redelivery after a failure now resumes from the failed run's checkpoint

Not a bug in the diff, but a behaviour change nobody wrote down. Before: a reported failure deleted the
row, so a broker redelivery of the same event started the collection fresh. Now: the row survives, the
early guard at `ProcessEventCommandHandler.cs:110-118` only short-circuits `ScheduledWait`,
`TryAcquireExecutionLockAsync` re-claims the row without checking status, and `ExecuteWithResumeAsync`
(`:1176-1186`) fetches it **by key with no status filter** and calls `resumable.ResumeAsync` if the
adapter accepts the state. So the retry silently continues the failed leg from its high-water mark. That
is plausibly what the retry lane wants, but the contract puts the retry trigger out of scope, and this is
a live behaviour change arriving without one. Downstream of it: the redelivery's `ON CONFLICT` restores a
full `platform_event_json` including credentials (`CheckpointRepository.cs:649`), the first checkpoint
write flips `status` back off `Failed`, and the retention clock resets. If that is not wanted, the fix is
one status check in the resume fetch or in the early guard — not in this diff's scope, but it should be a
ticket before this ships.

### F6 — the two stores do not agree on a malformed payload

Both agree on the predicates (A11) and on a NULL/empty payload. They diverge on a
`platform_event_json` that is valid JSON but **not an object**:

- In-memory `StripCredentials` (`InMemoryCheckpointRepository.cs:175-185`) returns the payload unchanged
  when `JsonNode.Parse` yields anything other than a `JsonObject` — the mark still succeeds.
- Postgres `jsonb - text` on a **scalar** raises `cannot delete from scalar`, aborting the whole `UPDATE`.
  The exception is swallowed by `TryMarkCheckpointFailedAsync` (`ProcessEventCommandHandler.cs:1105`),
  which logs a warning and returns — so the row is left `Idle`/`InFlight` **and still claimed**, is not
  deleted, and is not terminal. On a JSON **array**, `jsonb - text` is a different, valid overload that
  removes matching string *elements* — a silent mutation the in-memory store would never perform.

In practice the column always holds a serialized `PlatformEvent`, so this is a robustness gap rather than
a live defect. It is worth naming because "both stores stay behaviourally identical" was a stated
constraint and this is the one place it is not met.

Also unverifiable here and worth stating plainly: **the Postgres `MarkFailedAsync` has never executed.**
The `CAST({credentialsKey} AS text)` — added to disambiguate the `jsonb - text|text[]|integer` overloads
— the owner guard, the `status = 'Failed'` literal against the `HasConversion<string>()` mapping, and the
`updated_at_utc` parameter against `timestamp with time zone` are all correct by inspection
(`CheckpointDbContext.cs:74`, `:87-91` confirm `jsonb` and string status) and untested by execution.

### F7 — nothing deletes a checkpoint on a reported failure any more

Enumerated every checkpoint-delete call site in production code: `ProcessEventCommandHandler.cs:790`
(adapter-not-found), `:880` (unhandled exception), `:1259` (the non-failure branch of the flush),
`:411`/`Handle`'s stop backstop (by correlation id), `CheckpointRecoveryHandler.cs:867` (unservable
terminal age), `EventsController.cs:1055` (Stop API). None of them is on the reported-failure path. Both
`ICheckpointRepository` implementations and both hand-written test decorators (`RefusingStore`,
`ClaimRefusingStore`) were updated together; there is no third implementation.

### F8 — the tests drive production paths

I checked this specifically, because it is the failure mode the W1/W2 split exists to prevent.
`FailedRunLeavesATerminalRowTests` builds the **real** `ProcessEventCommandHandler` over the **real**
`InMemoryCheckpointRepository` and then runs the **real** `CheckpointRecoveryHandler.RecoverAsync` over
the same store — mocks stand in only for the adapter, the publishers and the capacity gate. That is the
right shape: a stand-in store would keep passing if either predicate regressed.
`InMemoryCheckpointRepositoryTerminalStatusTests` takes the claim through the real
`TryAcquireExecutionLockAsync` rather than seeding `ClaimedByInstance` by hand, so the claim the mark has
to release is a real one. `CheckpointCleanupJobTests` mocks the repositories — unavoidable, since the
subject is the job's arithmetic — but captures the actual arguments rather than `It.IsAny<>`.
`R2`/`R3` mock the repository, which is correct for a handler-branch test, and verify with the concrete
`Environment.MachineName` and concrete correlation ids.

The one soft spot: `ProcessEventCommandHandlerTests`' constructor now defaults `MarkFailedAsync` to
`true`. That is the right call (a loose mock's `false` reads as "no row matched"), and it is documented in
the test, but it means no test anywhere exercises the handler's behaviour when the mark returns `false`.
Combined with F3's discarded return value, the `false` branch is entirely unobserved.

### F9 — claims in `execution_notes.md` against the diff

Everything I could check is supported. Specifically confirmed: the before/after predicate table matches
the diff exactly in both stores; `IsReportedFailure` covers exactly the three statuses and no `ErrorCode`
is read anywhere on this path; the branch really does sit below the refusal return; both claim columns
really are nulled in the same statement; `CredentialsJsonKey` is a single public const with no literal
duplicate; the retention config is read from `IConfiguration` and needed no DI change; `Migrations/` is
untouched. The reported test counts reproduce exactly (92, 416, 629 passed / 30 Docker-gated / 3
pre-existing).

Two notes to add rather than correct:

- The note says the stale doc at `CheckpointRecoveryHandler.CheckpointTtl` is "now wrong" and was left
  because the file is unowned. Confirmed still wrong in the tree (`:702-704`: "How long a checkpoint row
  lives without being written to before `CheckpointCleanupJob` deletes it"). It is now false for every
  non-terminal row. One-line fix, should not ship as is.
- W2's "left untested, deliberately" flag on `MinimumFailedRetentionDays` is accurate — grep confirms the
  constant is referenced only from `ConfigurationKeys.cs` and `CheckpointCleanupJob.cs`, never a test.

---

## 6. Verdict

**Pass with gaps.**

The change does what the operator asked, in the shape they asked for: no column, no migration, no new
enum member, the status column and `updated_at_utc` carrying the whole feature. The two predicates are
correct and deliberately disagree, the claim release and the credential strip are in the same write, the
`nameof` key is proven against the serializer rather than argued, and the tests drive real production
paths rather than stand-ins. All five attention items are handled to the extent this machine can observe.

Gaps, in order of what I would want fixed before it ships:

1. **F1 — the non-terminal leak is real and larger than recorded.** Rows with unusable
   `PlatformEventJson` and rows in a retired tenant partition are now immortal; the unservable escalation
   does not cover either, contrary to `decisions.md`. The operator chose this direction knowingly, but
   they chose it on the stated premise that recovery ends these rows, and that premise is false.
2. **F5 — a redelivery after a failure now resumes from the failed leg** instead of starting fresh.
   Unstated, unowned, and one status check away from either behaviour.
3. **A2/A10 — a terminal row is not inert.** A redelivery re-claims it, resets its retention clock, and
   un-terminalizes it on the first checkpoint write.
4. **Postgres `MarkFailedAsync` is entirely unverified at runtime** (Docker unavailable — not-run, not
   failed). The `jsonb - text` strip and the owner guard are the two pieces I would most want a real
   database to see.
5. **F6 — the two stores diverge on a non-object payload**, against a stated constraint.
6. **F4 — the sweep exclusion is silently load-bearing for the credential strip**, and nothing says so.
7. **F3/F8 — `MarkFailedAsync`'s `false` return is discarded and untested**, so "the mark matched no row"
   is currently undiagnosable.
8. **F9 — the stale doc comment at `CheckpointRecoveryHandler.cs:702-704`** is now false and was
   knowingly left.

None of these is a reason to redo the work. 1, 2 and 8 want a decision or a line before merge; 4 wants a
run against Docker; the rest are tickets.
