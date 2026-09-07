# Verifier 2 — failed-checkpoint terminal status

Diff scope: branch `feat/failed-checkpoint-retention-ttl`. **The work is still uncommitted** — `HEAD` is
`d53e347d` (`git merge-base` confirms it is the branch base) and the change lives in the working tree:
13 modified tracked files, 3 new test files. Everything below is read from `git diff d53e347d -- src/`
plus the untracked files and the current source, not from a commit.

Environment as I ran it, all four commands mine:

- `dotnet build …/Cymulate.IntegrationServiceBus.sln` → **Build succeeded, 0 errors, 0 warnings.**
- `Application.UnitTests` → **93/93 passed.** (Baseline at `d53e347d` is 83; the diff adds exactly 10
  tests to that project — R2 theory ×6, R3, `Handle_logs_when_the_terminal_mark_finds_no_row`, and the
  two `Handle_still_deletes_…` regressions. 93 − 10 = 83, consistent.)
- `Infrastructure.Core.UnitTests` → **429/429 passed.**
- `API.UnitTests --filter FailedRunLeavesATerminalRowTests|CheckpointRecoveryCompatibilityTests|StoppingIsNotEndingTests`
  → **26/26 passed.**
- `docker info` → **DOCKER DOWN.** `API.UnitTests --filter CheckpointRepositoryTests` →
  **34 tests, 34 `DockerUnavailableException`. NOT RUN, not failed.**

**The Postgres raw SQL has never executed.** `MarkFailedAsync` (the two-term `jsonb - text - text`
strip, the owner guard, the `status = 'Failed'` literal), `DeleteExpiredAsync`,
`DeleteTerminalExpiredAsync`, and the `CASE WHEN … 'Failed' THEN 'Idle'` in
`TryAcquireExecutionLockAsync` are correct by inspection only. Everything I mark VALIDATED on the
Postgres side is validated against a diff hunk, never against a run.

`CLAUDE.md`'s claim of a pre-existing `Application.UnitTests` failure does not reproduce — the suite is
fully green, as it was in cycle 1.

---

## 1. Success Criteria coverage

| # | criterion | verdict | evidence |
|---|---|---|---|
| 1 | A reported failed done marks the row `Failed`, releases the claim, strips credentials, does not delete the row | **partially met** | `ProcessEventCommandHandler.cs:1259-1263` (branch), `:1090-1093` (`IsReportedFailure` = `Failure`/`TransientFailure`/`ValidationFailure`), `InMemoryCheckpointRepository.cs:153-179`, `CheckpointRepository.cs:441-476`. `FailedRunLeavesATerminalRowTests.R1_SweepNeverOffersAFailedRow` **passed** — real handler over real in-memory store: row survives, `Status=Failed`, both claim columns null. `R2_OnlyReportedFailureMarksTerminal` **passed** (6 rows). Strip proven in-memory only (`R5_…` **passed**); the Postgres `jsonb - text - text` strip is **NOT RUN**. Partial for that reason alone. |
| 2 | Every other outcome deletes exactly as before | **met** | `R2_OnlyReportedFailureMarksTerminal` asserts `DeleteAsync` `Times.Once` for `Success`/`Skipped`/`Cancelled` and `Times.Never` for the three failures, and the mirror for `MarkFailedAsync` — **passed**. `Handle_still_deletes_the_checkpoint_when_no_adapter_is_registered` and `…when_the_adapter_throws` **passed**. Stop-requested is untouched code (`ProcessEventCommandHandler.cs:411-416`, `EventsController.cs:1055`), no new test. `PartialWaitRequired` and native-fallback `Skipped` return before `CompleteExecutionAsync` (`:284-296`) — unchanged, and the R2 fixture asserts that by refusing to build a `PartialWaitRequired` result. |
| 3 | `GetRecoverableAsync` never returns a `Failed` row, in both stores | **partially met** | In-memory `InMemoryCheckpointRepository.cs:368-369`; two `R1_SweepNeverOffersAFailedRow` **passed**, one driving the real `CheckpointRecoveryHandler.RecoverAsync` and asserting 0 dispatched. Postgres `CheckpointRepository.cs:727` — **NOT RUN**. |
| 4 | `DeleteExpiredAsync` deletes a `Failed` row older than the retention window and leaves everything else alone, in both stores | **met, by a different shape than the criterion words** | The criterion's literal shape was **abandoned deliberately** after cycle-1 blocker B1: `DeleteExpiredAsync` was restored to the *non-terminal* TTL question and a new `DeleteTerminalExpiredAsync` owns the retention question. The capability the criterion asks for exists and is exercised — in-memory `DeleteTerminalExpiredAsync_reaps/keeps_…` and `…_does_not_reap_a_non_terminal_row` (Theory ×3) all **passed**; Postgres twins **NOT RUN**. Flagged rather than failed: the split is strictly better and `execution_notes.md:263-271` records the deviation and invites correction. |
| 5 | Stop-request row deletion unchanged | **met** | `CheckpointCleanupJob.cs:106` still passes the TTL `cutoff`, not `failedCutoff`. `Execute_still_deletes_stop_request_rows_on_the_ttl_not_the_retention_window` **passed** and asserts the cutoff is *not* the retention horizon. `Execute_still_runs_the_stop_aware_checkpoint_sweep` **passed**. `StopRequestRepository.cs` is not in the diff. |
| 6 | Retention window configurable, sane default, clamped against a nonsensical value | **met** | `ConfigurationKeys.cs:348` (`Checkpoint:FailedRetentionDays`), `:365` (default 7), `:371` (floor 1); `CheckpointCleanupJob.cs:37-49` — `int.TryParse` fallback then `Math.Max(Minimum, days)`. `Execute_floors_a_nonsensical_retention_setting` (Theory: `"0"`, `"-5"`, `"not-a-number"`, unset) **passed** — this is the cycle-1 N2 gap, now closed, and the `"not-a-number"` row is the one that caught a real defect (`GetValue<int>` threw the whole job out). No **upper** clamp — see F10. |
| 7 | Tests cover status write + two side effects, each non-failure deleting, sweep exclusion, cleanup predicate, stop still winning, stripped payload | **partially met** | All present. "Stop still winning" is `DeleteStoppedCheckpointsAsync_still_deletes_a_terminal_row` — Docker-gated, **NOT RUN**; no in-memory twin exists, so that criterion has no executed evidence at all. Everything else ran and passed. |
| 8 | Builds; affected projects pass; Docker-gated reported as not-run rather than green | **met** | Counts above, all mine. `execution_notes.md:551-560` reports the gate honestly and never claims the Postgres suite green. |
| 9 | No file outside the repo modified; no new migration | **met** | `git status` shows only paths under `/Users/user/Dev/IntegrationServiceBus`. `git status Migrations/` is **empty** — no new migration, no `Designer.cs` or snapshot change. No new `CheckpointStatus` member (`Failed` was already declared; only its doc comment changed). |

---

## 2. Assumption Disposition

**Default is NEVER-TESTED.** Rebuilt against current code; several rows differ from cycle 1 because the
code moved under them. VALIDATED/REJECTED cites a hunk I read, a test I ran, or a call path I traced.

### Prior Art

| id | status | citation | actor |
|---|---|---|---|
| `lessons.md#L-d13589c6` — retaining a failed run's checkpoint is storage-only with no lifecycle consequence (refuted: the delete was also releasing the claim) | VALIDATED | The lesson held and was acted on. `InMemoryCheckpointRepository.cs:170-171` and `CheckpointRepository.cs:461-463` null both claim columns inside the same write as the status change. `R4_MarkingTerminalReleasesTheClaim` **passed** and then re-claims as `isb-pod-successor`; `FailedRunLeavesATerminalRowTests` **passed** and asserts the same through the real handler. Postgres twin NOT RUN. | verifier-2 |
| `lessons.md#L-716f3c62` — a sweep predicate and a TTL predicate over the same column should agree (refuted: they answer different questions) | VALIDATED, more strongly than in cycle 1 | There are now **three** disjoint predicates on **three** horizons: `GetRecoverableAsync` `Status != Failed` (`:727`/`:368-369`), `DeleteExpiredAsync` `Status != ScheduledWait && Status != Failed` on the TTL (`:490-493`/`:203-207`), `DeleteTerminalExpiredAsync` `Status == Failed` on retention (`:498-505`/`:237-249`). `CheckpointCleanupJobTests` pins the two cutoffs apart in both directions — **passed**. | verifier-2 |
| `lessons.md#L-44a4039e` — extending retention has no security dimension (refuted: `platform_event_json` holds plaintext credentials) | VALIDATED | `The_stripped_keys_are_the_ones_the_serializer_writes` (Theory ×2, `Credentials` and `Headers`) **passed** against the pinned `Cymulate.Integration.Client` assembly, proving the `nameof` keys are the keys the serializer actually emits. I confirmed independently that neither property carries `[JsonPropertyName]` (`IntegrationInfra/src/Cymulate.Integration.Client/Models/PlatformEvent.cs:90,95`). **But the strip is not durable — see F5.** | verifier-2 |
| `lessons.md#L-5d94327f` — the retention branch is reached by every collector-reported failure (refuted: shutdown cancellation and claim-loss return first) | VALIDATED | Still true after the change. `ProcessEventCommandHandler.cs:302-310` returns on external cancellation before `CompleteExecutionAsync`; the `CLAIM_LOST` returns are above it; the refused-write return at `:1256-1257` is above the new branch. `R3_RefusedWriteTakesPrecedence` **passed**. | verifier-2 |
| `lessons.md#L-910c8f52` — the migration and raw SQL added for retention are correct (untested: Docker unavailable) | VALIDATED | Identical gate, identical verdict, one round later and with *more* raw SQL in play. `docker info` → down; 34/34 `CheckpointRepositoryTests` threw `DockerUnavailableException` on my run. `MarkFailedAsync`, both delete predicates and the new `CASE WHEN` have never executed. | verifier-2 |

### Task assumptions

| id | status | citation | actor |
|---|---|---|---|
| A1 — writing `Status = Failed` in place of the delete reaches every collector-reported failed done and no other outcome | **REJECTED** | Second half holds: `R2_OnlyReportedFailureMarksTerminal` **passed** across 6 statuses, with `Environment.MachineName` and concrete correlation ids rather than `It.IsAny<>`. First half does not: shutdown cancellation returns at `ProcessEventCommandHandler.cs:302`, `CLAIM_LOST` returns above `CompleteExecutionAsync`, `HandleAdapterNotFoundAsync` and `HandleUnhandledExceptionAsync` still **delete** (both **passed** as tests), and `PartialWaitRequired`/native-fallback `Skipped` return at `:284-296`. Must not be re-assumed: "every reported failure reaches the mark." | verifier-2 |
| A2 — nothing writes to a row after it is marked `Failed`, so `UpdatedAtUtc` is a stable failure timestamp | **REJECTED** | Was accidentally false in cycle 1; is now **deliberately** false. `TryAcquireExecutionLockAsync`'s `ON CONFLICT DO UPDATE` (`CheckpointRepository.cs:664-670`) flips `Failed → Idle`, rewrites `platform_event_json` from `EXCLUDED`, and sets `updated_at_utc = EXCLUDED.updated_at_utc`; the in-memory store mirrors it (`:339-350`). `Taking_the_execution_lock_returns_a_terminal_row_to_Idle` and `…_leaves_a_non_terminal_status_alone` (Theory: Idle, ScheduledWait) both **passed** in-memory; Postgres twins NOT RUN. A redelivery therefore resets the retention clock. `decisions.md` still asserts the opposite — see §4. | verifier-2 |
| A3 — `Status != Failed` on `GetRecoverableAsync` fully prevents re-dispatch; no second dispatch path | **REJECTED** | The sweep is genuinely blocked (two `R1_…` **passed**, one through the real `RecoverAsync`). But `ExecuteWithResumeAsync` (`ProcessEventCommandHandler.cs:1184-1195`) still fetches the row **by key with no status filter**, and by then `TryAcquireExecutionLockAsync` has already flipped it to `Idle`. A broker redelivery resumes from the failed leg's page and adapter state. Must not be re-assumed: "the recovery sweep is the only thing that picks a checkpoint back up." | verifier-2 |
| A4 — narrowing `DeleteExpiredAsync` to terminal rows leaks nothing, because non-terminal rows are ended by recovery or the unservable escalation | **VALIDATED** (was REJECTED in cycle 1) | The narrowing was **reverted**. `DeleteExpiredAsync` is back on the TTL question with `Status != ScheduledWait && Status != Failed` (`CheckpointRepository.cs:490-493`, `InMemoryCheckpointRepository.cs:203-207`), so the assumption no longer has to carry any weight: non-terminal rows are reaped by TTL regardless of whether recovery reaches them. `DeleteExpiredAsync_still_reaps_a_non_terminal_row_past_the_ttl` (Theory: Idle, InFlight) and `…_still_exempts_ScheduledWait` **passed**. Postgres twin NOT RUN. See F1. | verifier-2 |
| A5 — `Checkpoint:TtlHours` keeps deleting stop-request rows unchanged | **VALIDATED** | `CheckpointCleanupJob.cs:106` passes `cutoff` (TTL), never `failedCutoff`. `Execute_still_deletes_stop_request_rows_on_the_ttl_not_the_retention_window` **passed** and asserts the cutoff is not the retention horizon in both directions. | verifier-2 |
| A6 — `DeleteStoppedCheckpointsAsync` still deletes a terminal row | **NEVER-TESTED** | The only test is `CheckpointRepositoryTests.DeleteStoppedCheckpointsAsync_still_deletes_a_terminal_row`, **NOT RUN** (Docker), and no in-memory twin exists. Statically it holds — neither store's stopped sweep has a status predicate (`CheckpointRepository.cs:830-836` deletes by `correlation_id IN (SELECT … FROM adapter_stop_requests)`; `InMemoryCheckpointRepository.cs:460-473` filters on correlation id only) and the job runs it **first** (`CheckpointCleanupJob.cs:70`). That is an argument, not evidence. | verifier-2 |
| A7 — releasing the claim in the same write prevents the redelivery dead-letter | **VALIDATED (in-memory only)** | `R4_MarkingTerminalReleasesTheClaim` **passed** and then takes the claim from a second instance without waiting out the stale threshold; `FailedRunLeavesATerminalRowTests` **passed** end to end from the real handler. The Postgres single-statement `UPDATE … claimed_by_instance = NULL, claimed_at_utc = NULL` (`:461-463`) is **NOT RUN**. | verifier-2 |
| A8 — stripping `Credentials` damages nothing else in the payload, and nothing reads them back | **REJECTED** | First half VALIDATED, and now stronger: `R5_StrippedPayloadKeepsEverythingExceptCredentials` in **both** suites asserts the fixture actually carried each stripped key beforehand, then compares every surviving property by `JsonNode.DeepEquals` — in-memory **passed**, Postgres NOT RUN. Second half is false: `CheckpointRecoveryHandler.cs:248` deserializes `PlatformEventJson` into a `PlatformEvent` and `ProcessEventCommandHandler.cs:186` hands `platformEvent.Credentials` to `adapterActivator.ActivateAsync`. Safe only because `Failed` rows are excluded from the sweep — the exclusion is load-bearing for the strip, not just for R1, and nothing says so. | verifier-2 |
| A9 — `CheckpointStatus.Failed` can be written with no schema change; the column is plain text with no CHECK | **VALIDATED (statically)** | `CheckpointDbContext.cs:87-91` — `.HasConversion<string>()`, `.HasDefaultValue(Idle)`. `20260526144503_AddCheckpointStatusAndScheduledResume.cs:20-25` adds `status text NOT NULL DEFAULT 'Idle'`; no CHECK constraint anywhere in `Migrations/`. The only index touching status is the partial `WHERE status = 'ScheduledWait'`. No write executed. | verifier-2 |
| A10 — `Handle`'s early guard, which today only considers `ScheduledWait`, behaves correctly when a redelivery meets a `Failed` row | **REJECTED** | The guard is unchanged and still `ScheduledWait`-only (`ProcessEventCommandHandler.cs:111`). A redelivery meeting a `Failed` row now falls through and **deliberately revives it** (A2). Whether that is "correct" is precisely the operator question the contract left open — so the assumption cannot be marked validated by anyone but the operator. Must not be re-assumed: "a terminal row is inert against a redelivery." It is the opposite of inert by design. | verifier-2 |
| A11 — both stores can express the new predicates identically | **VALIDATED for the predicates and the status flip; the strip still diverges** | The three predicates are textually equivalent (`CheckpointRepository.cs:490-493`, `:498-505`, `:727` vs `InMemoryCheckpointRepository.cs:203-207`, `:237-249`, `:368-369`), and the `Failed → Idle` flip is now semantically equivalent (`:664-670` vs `:339-350`) where in cycle 1 the in-memory store did not implement it at all. `MarkFailedAsync`'s sensitive-field strip does **not** agree on a `platform_event_json` that is valid JSON but not an object — see F6. | verifier-2 |

---

## 3. Attention Item Disposition

Every named test run by me, individually, on the current tree.

| id | final disposition | evidence |
|---|---|---|
| R1 — the sweep re-dispatches a terminal row | **handled** | `--filter "FullyQualifiedName~InMemoryCheckpointRepositoryTerminalStatusTests"` is inside the **429/429** `Infrastructure.Core.UnitTests` run — includes `R1_SweepNeverOffersAFailedRow`. `--filter "FullyQualifiedName~FailedRunLeavesATerminalRowTests|…"` → **26/26 passed**; that one drives the real `CheckpointRecoveryHandler.RecoverAsync` over the store the real handler just wrote and asserts `0` dispatched, having first confirmed the row is unclaimed — which is exactly what would otherwise make it look recoverable. Postgres twin `CheckpointRepositoryTests.R1_SweepNeverOffersAFailedRow` — **NOT RUN**, `DockerUnavailableException`. |
| R2 — retention applied to the wrong outcomes | **handled** | Inside the **93/93** `Application.UnitTests` run. The theory builds each result through the real factory and asserts the produced `Status` before acting, which matters more than it looks: `AdapterResult.FailureResult(msg, code)` *derives* transience from the error code (`AdapterResult.cs:117-130`), so a fixture using a code outside `NonTransientErrorCodes` would silently produce `TransientFailure` and test the wrong row. The assertion catches that. Mark-vs-delete is asserted in both directions per status. |
| R3 — a claim-lost execution marks another owner's row terminal | **handled** | Same run, **passed**. Confirmed structurally too: the new branch sits at `ProcessEventCommandHandler.cs:1259`, below the `HasRejectedCheckpointWrite` return at `:1256`. |
| R4 — redelivery dead-letters because the claim was never released | **handled (in-memory); unverified on Postgres** | `R4_MarkingTerminalReleasesTheClaim` **passed** (429 run); `FailedRunLeavesATerminalRowTests` **passed** (26 run) and asserts both claim columns null from the real handler. `CheckpointRepositoryTests.R4_…` — **NOT RUN**. |
| R5 — the credential strip silently no-ops | **handled (in-memory); unverified on Postgres** | `R5_StrippedPayloadKeepsEverythingExceptCredentials` **passed**; `The_stripped_keys_are_the_ones_the_serializer_writes` (Theory ×2) **passed** — this is the one that closes the *silent* failure mode, because it asserts the serializer emits the exact key `nameof` produces, for both keys. `The_terminal_mark_is_all_or_nothing_under_a_concurrent_write` (200 rounds) **passed**, and now requires both keys stripped as part of the all-or-nothing invariant. No literal `"Credentials"` or `"Headers"` appears in any test — both suites share a `StrippedKeys` array built from the consts. `CheckpointRepositoryTests.R5_…` — **NOT RUN**. |

No attention item is `unresolved` or `accepted-risk`. The only thing standing between R1/R4/R5 and full
closure is the Docker gate, which is an environment fact, not a disposition.

---

## 4. Decision drift

`decisions.md` was written before the cycle-1 blockers and **two of its five entries no longer describe
the code.** Both are stale in the direction of understating what the change now does.

| decision | outcome |
|---|---|
| Retention expressed by the existing `status` column plus `updated_at_utc`, not a new column | **landed as decided.** No migration, no snapshot change, no new enum member — `git status Migrations/` is empty. |
| "A terminal row is not written to again, so `updated_at_utc` is a stable failure timestamp" | **reversed, deliberately, and the note was not updated.** B2 makes `TryAcquireExecutionLockAsync` write to terminal rows on purpose: it flips `Failed → Idle`, rewrites `platform_event_json`, and stamps `updated_at_utc`. `updated_at_utc` is now the timestamp of the **most recent** event on the row, and the retention clock restarts on every redelivery. This is a defensible design, but the recorded rationale for choosing the status column over a new one is now false as written. **`decisions.md` should be corrected before this ships.** |
| The recovery sweep is gated on **status**, not on a timestamp | **landed as decided.** `CheckpointRepository.cs:727`, `InMemoryCheckpointRepository.cs:368-369`. |
| "The cleanup job's checkpoint predicate deletes only terminal rows. Non-terminal rows are left to recovery and to the unservable escalation, which end them loudly" | **the decision itself was reversed, which makes the false premise moot.** Cycle 1 established that recovery and the escalation do **not** cover every non-terminal row. Rather than fix the premise, B1 removed the dependency on it: the cleanup job now runs both deletes, and non-terminal rows are reaped by the TTL exactly as before this change. So the answer to the brief's question is: **the premise is still false, and it no longer matters** — nothing now relies on it. `decisions.md` should be rewritten to describe the two-horizon design, not the single-predicate one it currently describes. |
| Proceeding unverified: every collector-reported `failed` done reaches the branch that replaces the delete | **landed as decided** — the documented exceptions are real and remain (A1). |
| Proceeding unverified: nothing reads `Credentials` back off a stored checkpoint | **changed — something does.** `CheckpointRecoveryHandler.cs:248` → `ProcessEventCommandHandler.cs:186`. The design is still safe, for a reason nobody recorded: the `Status != Failed` exclusion. |

---

## 5. Findings

### F1 — the non-terminal leak is closed, for both classes cycle 1 named

Checked directly rather than inferred from the fix's intent.

`DeleteExpiredAsync` is back on the TTL cutoff with the predicate
`UpdatedAtUtc < cutoffUtc && Status != ScheduledWait && Status != Failed`
(`CheckpointRepository.cs:490-493`, `InMemoryCheckpointRepository.cs:203-207`), and
`CheckpointCleanupJob.Execute` calls it with `cutoff = UtcNow.AddHours(-ttlHours)` (`:81`). Neither
delete has a partition filter — that is what makes them reach rows no sweep can see. So:

1. **Rows with unusable `PlatformEventJson`** — the three `Skipped` returns above the compatibility
   probe. Their status stays `Idle`/`InFlight`, so `DeleteExpiredAsync` reaps them at 24h, exactly as
   it did at baseline. **Closed.**
2. **Rows in a retired tenant partition** — invisible to every replica's `GetRecoverableAsync`, but the
   cleanup job never asks about partitions. Reaped at 24h. **Closed.** And a *terminal* row in the same
   position is reaped by `DeleteTerminalExpiredAsync` at 7d, which has no partition filter either.

The one class of row I looked for that could still be immortal: a row whose `MarkFailedAsync` **threw**
(F6's Postgres scalar case) stays `Idle` *and still claimed* — but `DeleteExpiredAsync` has no claim
predicate, so it too is reaped at 24h. There is no immortal row left that I can construct.

The deviation W1 flagged is real and I endorse it: the instruction said restore the *original* predicate,
which did not exempt `Failed`. Taken literally the 24h TTL would delete every terminal row before the 7d
retention sweep could see one, making `DeleteTerminalExpiredAsync` dead code. Exempting `Failed` is the
only reading under which the feature exists. `execution_notes.md:263-271` says so and invites correction.

### F2 — the two cutoffs stay separate, and stop-request deletion is genuinely untouched

Read at the call site, not assumed. `CheckpointCleanupJob.Execute` computes two independent values —
`cutoff = UtcNow.AddHours(-ttlHours)` (`:62`) from the Quartz job-data map, and
`failedCutoff = UtcNow - FailedRetention` (`:64`) from `IConfiguration` — and hands each to exactly one
delete: `DeleteExpiredAsync(cutoff)` (`:81`), `DeleteTerminalExpiredAsync(failedCutoff)` (`:95`),
`stopRequestRepository.DeleteExpiredAsync(cutoff)` (`:106`). Stop-request rows are on the TTL cutoff,
character for character as at baseline; only the surrounding comment changed.

`DeleteStoppedCheckpointsAsync()` still takes no cutoff and still runs **first** (`:70`), so an explicit
stop outranks the retention hold. Neither store's implementation has a status predicate, so it deletes
terminal rows too — verified by reading both (`CheckpointRepository.cs:830-836`,
`InMemoryCheckpointRepository.cs:460-473`), not by the Docker-gated test.

`CheckpointCleanupJobTests` captures the actual `DateTime` each delete receives via `Callback` and
asserts each is the right horizon **and explicitly not the other**, in both directions. A swap fails.
Both tests **passed**.

One wiring fact worth restating because the constructor changed: `CheckpointCleanupJob` gained an
`IConfiguration` parameter and is constructed by Quartz, not by hand. `q.AddJob<CheckpointCleanupJob>`
registers the type with the container and Quartz's Microsoft-DI job factory resolves it. No wiring change
was needed and none was made.

### F3 — the `Failed → Idle` flip: what it fixes, and the one thing it changes that nobody named

The flip is correct and I could not construct a row that is revived when it should have stayed terminal
*within the sweep's reach*:

- Only `TryAcquireExecutionLockAsync` performs the flip, and it only fires when the row is unclaimed,
  staleged, or already this instance's (`CheckpointRepository.cs:672-674`). A terminal row is always
  unclaimed, so the flip is available to any redelivery — which is the point.
- **No race with the sweep.** The sweep never offers a `Failed` row, so it cannot be the thing that takes
  the lock. After the flip the row is `Idle` *and freshly claimed*, so it is not stale either.
- **No race with the retention delete.** Both are single statements; either order is safe. A redelivery
  that loses to the delete simply INSERTs a fresh row (the `status` column defaults to `'Idle'`, and the
  INSERT column list deliberately omits `status`, so the default applies — I checked the migration).
- `ScheduledWait` survives the flip in both stores (`CASE` in Postgres, explicit carry-across in-memory).
  The in-memory half of that is new this cycle and was a real defect in cycle 1: the store blind-wrote
  `_store[key] = entry`, which flattened `ScheduledWait` to `Idle` *and* made
  `…_returns_a_terminal_row_to_Idle` pass for the wrong reason. Both are now right, and
  `…_leaves_a_non_terminal_status_alone(ScheduledWait)` is the test that proves it.

**The thing nobody named:** `EndUnservableRunAsync`'s `<remarks>` now claims "the checkpoint moves
forward only, and a run that has been reported terminal does not restart"
(`CheckpointRecoveryHandler.cs:807-810`). After B2 that sentence is only true of the **sweep**. A broker
redelivery of the same event takes the lock, flips the row to `Idle`, and resumes the run from page 34 —
publishing a second done for a run the platform was already told failed. At baseline the row was deleted,
so a redelivery started fresh and published its own done; the difference is *resume-from-high-water-mark*
versus *start over*, not one done versus two. Low likelihood (the unservable case exists precisely because
the triggering message was acknowledged long ago) but the prose overstates the guarantee. One sentence.

### F4 — `EndUnservableRunAsync` marking instead of deleting: no run left without a done

The ordering is preserved and is the part that matters: `TryClaimAsync` (`:822`) → `PublishUnservableDoneAsync`
(`:855`) → `MarkFailedAsync` (`:872`). The publish still gates the mark, and a publish failure still
returns before it (`:866`), leaving the row exactly as it was for the next sweep. **No run can reach a
terminal row without a done having been published first.**

The owner guard is satisfied by construction — `TryClaimAsync` immediately above writes `InstanceId` into
`claimed_by_instance`, and `MarkFailedAsync` passes the same `InstanceId`. This is strictly better than
the delete it replaces: at baseline a *failed* delete left the row claimed, stale and re-declined on every
subsequent sweep; now a failed mark leaves it visible to the next sweep, which retries, and a *succeeded*
mark makes the row invisible so the escalation cannot fire twice.

What the escalation now *means* changes only in that the evidence survives: the row's page and item
counts stay readable for the retention window instead of vanishing with the delete. The four
`CheckpointRecoveryCompatibilityTests` that pinned `BeNull` were inverted to assert `Status == Failed` +
unclaimed + `GetRecoverableAsync` empty, and two were *strengthened* rather than inverted — the
publish-failed case now asserts the row is **not** `Failed` (the run was never reported, so it must stay
recoverable) and the lost-claim case asserts this replica did not mark it. All **passed** in my 26/26 run.

One stale sentence left behind: `EscalateIfNobodyCanServe`'s operator-facing log (`:771`) still says the
checkpoint "will be deleted by the cleanup job". In the ordinary sequence the row is marked terminal at
`UnservableTerminalAge` (18h) and then deleted at retention (7d), so the statement is eventually true but
names the wrong horizon. Cosmetic; the brief listed it among the corrected lines and it was not corrected.

### F5 — the `Headers` strip is safe, and the payload survives — but the strip is not durable

**Safe.** I re-verified the zero-readers claim independently rather than trusting the note: across all of
`src/`, `PlatformEvent.Headers` is only ever **written** — nothing reads it back, in any adapter,
publisher, or recovery path. I also checked the out-of-repo `Cymulate.Integration.Client` source, where
`Headers` is declared (`Models/PlatformEvent.cs:90`) and never consumed. Stripping it cannot break a
consumer because there is no consumer.

**The payload survives intact.** `R5_StrippedPayloadKeepsEverythingExceptCredentials` in both suites now
asserts the fixture *carried* each stripped key beforehand (so a strip cannot "pass" against a payload
that never held the secret), asserts each is gone, then walks every remaining property and requires
`JsonNode.DeepEquals`. The Postgres fixture deliberately carries a scalar (`RetryCount`) and a nested
object (`Metadata`) so a strip that damages structure fails — though that half is NOT RUN.

**One correction to `execution_notes.md`:** it says `Headers` has "only two writers, both in the API
host". There are **four** — `EventsController.cs:471`, `:1842`, and `ConsoleInteractiveService.cs:464`,
`:605`. Doesn't change the conclusion (still zero readers), but the survey was incomplete.

**Not durable, and this is the real caveat.** `TryAcquireExecutionLockAsync` writes
`platform_event_json = EXCLUDED.platform_event_json` on conflict (`CheckpointRepository.cs:667`), so the
next redelivery puts the full payload — credentials and headers — straight back. Since `TransientFailure`
is both a mark-terminal status *and* the one that triggers redelivery (see F9), mark-then-restore is the
**normal** transient sequence, not an edge case. The honest guarantee is therefore *"a `Failed` row
carries no secrets **for as long as it stays `Failed`**"*, not "a `Failed` row carries no secrets". This
is cycle-1 I3's first half and it is still open. It should be stated that way in the PR body; anyone
reading the retained row as unconditionally secret-free would be wrong.

### F6 — the two stores still disagree on a non-object payload

Unaddressed from cycle 1 (N4/F6), and worth restating because "both implementations stay behaviourally
identical" is a stated constraint:

- In-memory `StripSensitiveFields` (`:216-227`) returns the payload unchanged when `JsonNode.Parse`
  yields anything that is not a `JsonObject` — the mark still succeeds. A **malformed** payload throws
  `JsonException` out of the method, which `TryMarkCheckpointFailedAsync` swallows.
- Postgres `jsonb - text` on a **scalar** raises `cannot delete from scalar`, aborting the `UPDATE`; on
  an **array** it silently removes matching string *elements*, a mutation the in-memory store would never
  perform.

In practice the column always holds a serialized `PlatformEvent`, so this is a robustness gap, not a live
defect — and with the TTL restored, a row left non-terminal by an aborted mark is reaped at 24h rather
than living forever, which is what made it worth flagging in cycle 1. The one-line fix is still available:
`CASE WHEN jsonb_typeof(platform_event_json) = 'object' THEN … ELSE platform_event_json END`.

Also still divergent and still out of scope: the in-memory `TryAcquireExecutionLockAsync` discards
progress, cursor, adapter state and `ScheduledResumeAtUtc` where the Postgres `DO UPDATE` preserves them
(the SQL's own comment says "Progress fields are NOT reset on conflict"). Pre-existing, correctly flagged
in `execution_notes.md`, untouched.

### F7 — the tests drive production paths

Checked specifically, because it is the failure the W1/W2 split exists to prevent.

`FailedRunLeavesATerminalRowTests` builds the **real** `ProcessEventCommandHandler` over the **real**
`InMemoryCheckpointRepository` and then runs the **real** `CheckpointRecoveryHandler.RecoverAsync` over
the same store; mocks stand in only for the adapter, publishers and capacity gate. A stand-in store would
keep passing if either predicate regressed — this one would not.
`InMemoryCheckpointRepositoryTerminalStatusTests` takes the claim through the real
`TryAcquireExecutionLockAsync` rather than seeding `ClaimedByInstance` by hand, so the claim the mark
must release is a real one, and `The_terminal_mark_is_all_or_nothing_under_a_concurrent_write` races the
mark against a real `UpsertAsync` 200 times — the test that justifies the CAS rewrite.
`CheckpointCleanupJobTests` mocks the repositories (unavoidable; the subject is the job's arithmetic) but
captures the actual arguments rather than asserting `It.IsAny<>`.
`Handle_logs_when_the_terminal_mark_finds_no_row` closes cycle-1 I1's untested branch and asserts on
level + correlation id, never on wording.

Two soft spots, both minor:

- `FailedRunLeavesATerminalRowTests` puts `Credentials` on its event but never asserts the stored payload
  lost them, so the end-to-end strip is covered only at store level.
- The `MarkFailedAsync` mock still defaults to `true` in `ProcessEventCommandHandlerTests`' constructor.
  That remains the right call (a loose mock's `false` reads as "no row matched" and would hide a correct
  handler), and the `false` branch now *is* driven by its own test, so the cycle-1 gap is closed.

The Postgres suite is 34 well-shaped tests against real SQL that **cannot run here**. That is the single
largest hole in this change's evidence and no amount of in-memory coverage substitutes for it.

### F8 — `execution_notes.md` against the diff

Everything material is supported. Confirmed line by line: the before/after predicate table matches both
stores exactly; `IsReportedFailure` covers exactly three statuses and reads no `ErrorCode`; the branch
sits below the refusal return; both claim columns are nulled in the same statement; `CredentialsJsonKey`
and `HeadersJsonKey` are single public consts with no literal duplicate anywhere in `src/`; the retention
config is read from `IConfiguration` and needed no DI change; `Migrations/` is untouched. The reported
test counts reproduce exactly (93 / 429 / 34-not-run).

Three notes rather than corrections:

- **The "eight comments and two log lines" claim is substantially supported.** I count nine doc/comment
  edits (`CheckpointRecoveryHandler.CheckpointTtl` at `:702-704` — the one the brief singles out — plus
  `EndUnservableRunAsync`'s `<summary>` and `<remarks>`, `ConfigurationKeys.DefaultTtlHours`,
  `CheckpointStatus.Failed`, `CheckpointCleanupJob`'s class doc and `DefaultTtlHours`,
  `ICheckpointRepository.DeleteExpiredAsync`, and two inline comments in the job) and two log-line edits,
  both in `CheckpointRecoveryHandler`. The three "TTL job will handle it" lines in
  `ProcessEventCommandHandler` (`:416`, `:1134`, `:1276`) were correctly left alone — B1 made them true
  again. `EscalateIfNobodyCanServe`'s `:771` was not corrected and is mildly stale (F4).
- The `Headers` writer survey is incomplete — four writers, not two (F5).
- `MinimumFailedRetentionDays` is no longer untested; `Execute_floors_a_nonsensical_retention_setting`
  covers `0`, `-5`, unparseable and unset, and **passed** on all four.

### F9 — the still-open operator question: precise de-facto behaviour

**Not fixed, not to be treated as fixed.** Reporting exactly what the code does today so the decision can
be made on facts.

**Which failures trigger a redelivery at all.** `IsbPlatformEventDispatcher.ShouldRetry` delegates to
`SiemRulesPageRejection.AllowsRetry(result.IsTransient, result.ErrorCode)` =
`isTransient && !IsPermanent(errorCode)`. `IsTransient` is true only for `TransientFailure`. So of the
three statuses that now mark a row terminal:

- **`TransientFailure` with a non-permanent error code → the broker redelivers.** This is the common case.
- **`Failure` and `ValidationFailure` → Acked without retry.** No redelivery. The row sits `Failed` until
  the retention sweep, unless some *other* trigger re-sends the same correlation id.

**What a redelivery does to the terminal row, step by step:**

1. `Handle`'s early guard (`ProcessEventCommandHandler.cs:111`) checks `ScheduledWait` only — a `Failed`
   row falls through.
2. `TryAcquireExecutionLockAsync` succeeds (the row is unclaimed), and in the same statement flips
   `Failed → Idle`, rewrites `platform_event_json` from the incoming event — **restoring credentials and
   headers** — and resets `updated_at_utc`, restarting the retention clock.
3. `ExecuteWithResumeAsync` (`:1184-1195`) fetches the row **by key with no status filter** and, if the
   adapter's `CanResumeFrom` accepts, calls `ResumeAsync` — **the retry continues the failed leg from its
   high-water mark** (page, cursor, adapter state), it does not start over.
4. If the retry fails again, the row is marked `Failed` again with a fresh timestamp.

**So the de-facto answer is: a broker redelivery resumes a failed run from its checkpoint, automatically,
with no explicit trigger.** At baseline the row was deleted on failure, so a redelivery started the
collection fresh — this is a live behaviour change. Note that the alternative is not free: B2 exists
because a row left `Failed` during a retry is invisible to the recovery sweep, so a pod loss inside that
window would strand the run with no done at all. If the operator wants explicit-trigger-only, the change
is a status check in the early guard or the resume fetch — one line — but it must be paired with a way for
the sweep to see the row, or B2's failure mode comes back.

### F10 — residual smaller items, none blocking

- **No upper clamp on the retention setting.** `Math.Max(Minimum, days)` floors it; nothing ceilings it.
  `TimeSpan.FromDays(int.MaxValue)` overflows (`TimeSpan.MaxValue.TotalDays` ≈ 1.07e7), throwing inside
  `Execute`'s `try` → `JobExecutionException` → the whole cleanup job stops on every fire. That is the
  same failure class the `int.TryParse` fix just closed, reached by a different bad value. Requires an
  absurd setting, so low severity, but the fix is `Math.Clamp`.
- **Cycle-1 N1 and N3 unaddressed.** The four new single-line `if`s in `InMemoryCheckpointRepository`
  (`:163`, `:166`, `:217`, `:220`) are unbraced against `docs/coding-standards.md`; `MarkFailedAsync`'s
  `WHERE` still has no `status <> 'ScheduledWait'` guard in either store. The latter is unreachable today
  (the early guard and `HandlePartialWaitAsync` both return before completion) and both stores are
  consistently missing it, so it is a nit, not a divergence.
- **`InMemoryCheckpointRepository.Copy` is a hand-maintained field list.** I checked it against
  `CheckpointEntry` — all 23 properties are copied, none dropped. But a property added later will be
  silently lost by the terminal mark with no compiler help.
- **No metric or count for `Failed` rows.** The retention window is the only thing bounding them, and
  nothing tells an operator the table is growing. Cycle-1 open question, still open.

---

## 6. Verdict

**Pass with gaps.**

Both cycle-1 blockers landed and are correct. B1 is closed the right way — two methods, two horizons,
disjoint predicates — and I could not construct an immortal row: both classes cycle 1 named (unusable
`PlatformEventJson`, retired tenant partition) are reaped by the restored TTL, and even a row whose mark
threw is reaped at 24h. B2 is now implemented in **both** stores, where in cycle 1 the in-memory half was
absent and its test passed by accident. The two cleanup cutoffs are pinned apart in both directions by a
test that captures the actual arguments, stop-request deletion is byte-identical to baseline, and
`DeleteStoppedCheckpointsAsync` still runs first with no status predicate so an explicit stop outranks
the retention hold. `EndUnservableRunAsync` preserves publish-before-mark, so no run reaches a terminal
row without a done. The `Headers` strip is genuinely free — I re-verified zero readers across `src/` and
the client package myself — and the payload survives byte-equal. The three cycle-1 items with test gaps
(I1's discarded return, N2's untested floor, I2's non-atomic in-memory mark) are all closed with tests
that drive production paths.

Gaps, in the order I would want them addressed:

1. **The Postgres SQL has still never executed.** Four raw statements now — `MarkFailedAsync`'s two-term
   `jsonb - text - text` strip and owner guard, both delete predicates, and the new `CASE WHEN` in
   `TryAcquireExecutionLockAsync`. 34 well-shaped tests exist and cannot run. Start Docker once, run
   `--filter "FullyQualifiedName~CheckpointRepositoryTests"`, paste the result into the PR. This is the
   single largest hole and it is one command wide.
2. **`decisions.md` is stale in two places** and understates what the change now does: the "a terminal row
   is not written to again" premise was deliberately reversed by B2, and the "cleanup deletes only
   terminal rows" decision was reversed by B1. The false premise cycle 1 found no longer matters — but the
   file still asserts both. Correct it before this ships.
3. **F9 — the redelivery-resume behaviour is unowned.** A failed run now resumes from its checkpoint
   automatically on any broker redelivery, and `TransientFailure` both marks terminal *and* triggers
   redelivery, so this is the common path, not an edge case. Deliberately left to the operator; it needs a
   decision, not a fix.
4. **F5 — the secret-free guarantee is conditional.** A redelivery restores the full payload via
   `platform_event_json = EXCLUDED.platform_event_json`. State the weaker guarantee in the PR body.
5. **F6 — the two stores still diverge on a non-object payload**, against a stated constraint. One `CASE
   WHEN jsonb_typeof(...)` closes it.
6. **A6 has no executed evidence** in either store — the "stop still wins over a terminal row" criterion
   rests entirely on a Docker-gated test and a static reading.
