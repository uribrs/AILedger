# Verifier 2 — failed-checkpoint retention TTL column

Diff base `d53e347d48b71d7befa9bce6b9086b909a547093`, branch `feat/failed-checkpoint-retention-ttl`.
14 modified files + 3 new. Every claim below was read or run by this verifier; worker and cycle-1
statements were re-derived against the current tree, not carried forward.

**Runtime coverage, stated plainly.** Docker is unavailable (`docker info` fails). The whole Postgres
side is runtime-unverified: `CheckpointRepositoryTests` → **Failed: 37, Passed: 0, 248 ms**, all
`DockerUnavailableException`. That is environmental, not a regression — but it means
`SetRetentionAsync`'s raw UPDATE (**where B1 landed**), the lock upsert's `retain_until_utc = NULL`
(**where B2 landed**), the owner guard, and `MigrateAsync()` have never executed. W1's
`ToQueryString()` evidence covers only the two LINQ predicates (`GetRecoverableAsync`,
`DeleteExpiredAsync`); it says nothing about the three raw statements.

Runs performed by this verifier on the current tree:

| command | result |
|---|---|
| `dotnet build …sln --no-incremental` | **Build succeeded, 0 Error(s), 14 Warning(s)** (NU1900 filtered; warnings are the pre-existing `CS1573`/`CS9113` set) |
| `Application.UnitTests` | **93/93** |
| `Infrastructure.Core.UnitTests` | **417/417** |
| `API.UnitTests --filter StoppingIsNotEndingTests\|CheckpointRecoveryCompatibilityTests` | **25/25** |
| `API.UnitTests --filter CheckpointRepositoryTests` | **0/37, all `DockerUnavailableException`** |

`CLAUDE.md`'s "one pre-existing failure in `Application.UnitTests`" did not reproduce, same as cycle 1.

---

## 0. Did the eight changes since cycle 1 actually land?

| # | claim | landed? | evidence |
|---|---|---|---|
| B1 | claim released in the SAME UPDATE that stamps the hold, both stores | **yes** | `CheckpointRepository.cs:790-800` — one `ExecuteSqlAsync`: `SET retain_until_utc = {retainUntil}, claimed_by_instance = NULL, claimed_at_utc = NULL, updated_at_utc = {nowUtc}`. `InMemoryCheckpointRepository.cs:348-351` — four field writes before a single `return`, no second call. |
| B2 | `TryAcquireExecutionLockAsync` CLEARS; `UpsertAsync` and `TransitionFromScheduledWaitAsync` PRESERVE | **yes, both halves** | clear: `CheckpointRepository.cs:618` (`retain_until_utc = NULL` in the lock's `DO UPDATE SET`), `InMemoryCheckpointRepository.cs:265`. preserve: `retain_until_utc` occurs at exactly `:451, :618, :677, :793` in `CheckpointRepository.cs` — absent from `UpsertAsync`'s INSERT list (`:60-66`) and `DO UPDATE SET` (`:81-96`); `InMemoryCheckpointRepository.cs:69-70` and `:391`. Coherent — see Findings F1 for the two paths the rule does *not* cover. |
| B2/2 | `GetRecoverableAsync` uses `(== null \|\| <= nowUtc)`, matching `DeleteExpiredAsync` | **yes** | `CheckpointRepository.cs:677` vs `:451`; `InMemoryCheckpointRepository.cs:288` vs `:164`. They agree. **This is also the change that creates F2.** |
| F2 (cycle 1) | `DeleteExpiredAsync` = `Status != ScheduledWait && UpdatedAtUtc < cutoffUtc && (RetainUntilUtc == null \|\| <= nowUtc)` | **yes** | `CheckpointRepository.cs:449-452`, `InMemoryCheckpointRepository.cs:161-164`. Byte-for-byte the stated form, `UpdatedAtUtc` as a top-level conjunct. |
| I1 | `SetRetentionAsync` owner-guarded, `instanceId` after `category` | **yes** | `ICheckpointRepository.cs:56-64`; `AND claimed_by_instance = {instanceId}` at `CheckpointRepository.cs:800`; `string.Equals(existing.ClaimedByInstance, instanceId, StringComparison.Ordinal)` at `InMemoryCheckpointRepository.cs:344`. Call site `ProcessEventCommandHandler.cs:1159-1161` passes `InstanceId`. |
| N2/F7 | clamp low→default, high→`MaxFailedRetentionDays = 30`; handler routes through it | **yes** | `ConfigurationKeys.cs` — `MaxFailedRetentionDays = 30`, `NormalizeFailedRetentionDays` = `configuredDays <= 0 ? Default : Math.Min(configuredDays, Max)`. `ProcessEventCommandHandler.cs:59-62` is the only reader of the key. `168` now yields 30 days, not 168. |
| N1/N3 | `AsUtc` used; BOMs stripped | **yes** | `CheckpointRepository.cs:787` `DateTime? retainUntil = AsUtc(retainUntilUtc);`. First three bytes: migration `757369` (`usi`), Designer `2f2f20` (`// `), i.e. no BOM; `CheckpointDbContextModelSnapshot.cs` keeps its pre-existing `efbbbf`, matching the last EF-generated pair `20260610120000_AddAdapterStopRequests`. |
| F5 | third stale comment in `CheckpointCleanupJob.cs` corrected | **yes** | `:73-75` now reads "…and `DeleteStoppedCheckpointsAsync` above already removed a stopped run's checkpoint whatever its retention hold." Accurate: that call runs first and carries no retention predicate. |

Migration triad re-checked mechanically: the `#pragma warning disable 612, 618 … restore` model body of
`20260827124500_AddCheckpointRetainUntil.Designer.cs` is **byte-identical** to
`CheckpointDbContextModelSnapshot.cs` (160 lines each, `diff` empty); the Designer differs only in the
`[Migration(...)]` attribute, the class name and `BuildTargetModel`. `Up` is one nullable
`AddColumn<DateTime>`, `Down` one `DropColumn`, no index, no backfill.

---

## 1. Success Criteria coverage

| # | criterion | verdict | evidence |
|---|---|---|---|
| SC1 | nullable `retain_until_utc`, mapped on `CheckpointEntry` + `CheckpointDbContext`, additive EF migration | **met** | `CheckpointEntry.cs:110-115`; `CheckpointDbContext.cs:95`; `20260827124500_AddCheckpointRetainUntil.cs`. Never executed — Docker gate. |
| SC2 | a collector-reported failure stamps `now + retention` and does not delete | **met** | `ProcessEventCommandHandler.cs:1304-1309` (predicate → `TryRetainCheckpointAsync` → `return null` above the delete); `:1155` computes `DateTime.UtcNow + FailedRetention`. Ran `R2_OnlyFailureStatusesStampRetention` → **7/7 pass**; the three stamping rows also assert `DeleteAsync` `Times.Never`. |
| SC3 | every other delete path unchanged, except unhandled-exception does not delete a retained row | **met, two recorded deviations** | `HandleAdapterNotFoundAsync` still calls the unconditional `TryDeleteCheckpointAsync`; stop paths still `DeleteByCorrelationIdAsync`; success delete unchanged. Guard at `:885` → `TryDeleteCheckpointUnlessRetainedAsync:1116-1147`. Ran both handler tests (`…when_the_leg_throws`, `…when_no_adapter_is_found`) green. Deviation 1: the `GetAsync` read throwing now *skips* the delete (`:1127-1134`) — recorded in `decisions.md`. Deviation 2 (new, not recorded): `SetRetentionAsync` now also nulls the claim columns, so the failure arm writes more of the row than "stamp and leave" implies. Correct, but wider than SC2/SC3 as worded. |
| SC4 | `GetRecoverableAsync` never returns a row with non-null `retain_until_utc`, both repos | **NOT met as worded — deliberately superseded** | The predicate is now `(RetainUntilUtc == null \|\| RetainUntilUtc <= nowUtc)` (`CheckpointRepository.cs:677`, `InMemoryCheckpointRepository.cs:288`), so a row with a *non-null but expired* stamp **is** returned — pinned on purpose by `GetRecoverableAsync_offers_a_row_whose_hold_has_expired` in both stores (in-memory ran green). The intent behind SC4 (a *live* hold is never re-dispatched) is met. The superseding decision is recorded under "Reversal after blind code review", but SC4's own text was never restated, and the consequence is F2 below. |
| SC5 | `DeleteExpiredAsync` deletes retained rows past the stamp and non-retained past `TtlHours` | **met (InMemory verified, Postgres unverified)** | `CheckpointRepository.cs:449-452`; `InMemoryCheckpointRepository.cs:161-164`. Five in-memory tests green inside 417/417, including the F2-fix case (`…keeps_a_held_row_whose_deadline_passed_while_it_is_still_progressing`). |
| SC6 | the upsert path does not clear an existing `retain_until_utc` | **met** | grep above; `R1_UpsertDoesNotClearRetention` ran → **1/1 pass**. Cycle 1's F1 (the in-memory lock path dropping the hold) is resolved by making that path clear it *deliberately*. |
| SC7 | new config key, defaults to 168 hours, wired into the cleanup job the same way `TtlHours` is | **partially met — deliberately superseded** | `Checkpoint:FailedRetentionDays`, default `7` days = 168 h, read at `ProcessEventCommandHandler.cs:59-62`. The cleanup job takes no second key because the deadline is absolute. Recorded as drift before execution. The unit hazard is now bounded by `MaxFailedRetentionDays = 30`. |
| SC8 | tests cover: failure arm stamping not deleting; sweep skipping retained; both cutoffs; unhandled-exception guard; upsert not clearing | **met** | 15 in-memory + 13 Postgres + 4 handler tests. All five named behaviours have at least one Docker-free test this verifier ran green. |
| SC9 | solution builds; affected test projects pass; pre-existing failures distinguished | **met** | See the run table above. |
| SC10 | no file outside the repo modified | **met** | `git status --porcelain` shows only the 14 tracked edits, 3 new source files and `ai/`. No probe project left behind. |

---

## 2. Assumption Disposition

Rebuilt against the current code. Several dispositions differ from cycle 1 because the behaviour they
described was reversed.

| id | status | citation | actor |
|---|---|---|---|
| A1 — failure arm reached by every collector-reported failure and by no other outcome | **REJECTED (first half); second half VALIDATED** | `ProcessEventCommandHandler.cs:279` — `if (cancellationToken.IsCancellationRequested && !result.Success) return result;` returns before `CompleteExecutionAsync`, so a collector-reported failure during pod shutdown stamps nothing. Same for the CLAIM_LOST returns at `:274`, `:377`, `:389`, `:1506`. Second half proven by run: `R2_OnlyFailureStatusesStampRetention` 7/7, `Success`/`Skipped`/`Cancelled`/`PartialWaitRequired` all `Times.Never`. **Must not be re-assumed:** that the retention branch is the sole terminus of every failing run. | verifier |
| A2 — `FlushAndCleanupCheckpointAsync` takes the outcome without disturbing the refused-write early return | **VALIDATED** | `ProcessEventCommandHandler.cs:1296-1302` — the `HasRejectedCheckpointWrite` guard is still the first statement; the retention branch is at `:1304`. Ran `R3_RefusedWriteTakesPrecedenceOverRetention` → **1/1 pass**, asserting `SetRetentionAsync` and `DeleteAsync` both `Times.Never`. | verifier |
| A3 — nullable column needs no backfill and breaks no existing query, index or upsert | **NEVER-TESTED** | `MigrateAsync()` has never run on this branch (Docker gate; all 37 `CheckpointRepositoryTests` fail before touching a database). Statically supported — every raw statement names explicit columns — but note the assumption's "no upsert" clause is now *deliberately* false: `TryAcquireExecutionLockAsync`'s `DO UPDATE SET` names the column (`:618`). | — |
| A4 — `GetRecoverableAsync` + `RetainUntilUtc == null` fully prevents re-dispatch of a retained row | **REJECTED** | The predicate is no longer `== null`. `CheckpointRepository.cs:677` / `InMemoryCheckpointRepository.cs:288` read `(== null \|\| <= nowUtc)`, so a retained row **is** offered to the sweep the moment its hold expires — pinned by `GetRecoverableAsync_offers_a_row_whose_hold_has_expired`, which this verifier ran green in-memory. The second dispatch path `GetScheduledWaitsDueByAsync` still carries no retention filter (`InMemoryCheckpointRepository.cs:296-305`), though it is now hard to reach with a hold, since the stamp releases the claim and `TransitionToScheduledWaitAsync` is owner-guarded. **Must not be re-assumed:** that a stamped row is undispatchable. It is undispatchable only while the stamp is in the future. | verifier |
| A5 — `DeleteExpiredAsync` carries two cutoffs without changing behaviour for null rows | **VALIDATED (InMemory only)** | `InMemoryCheckpointRepository.cs:161-164`; run: `DeleteExpiredAsync_still_deletes_an_unheld_row_at_the_cutoff` and `…still_exempts_ScheduledWait_when_its_hold_has_elapsed` green inside 417/417. The Postgres twin (`CheckpointRepository.cs:449-452`) is the same expression tree, never executed. | verifier |
| A6 — `DeleteStoppedCheckpointsAsync` keeps deleting retained rows | **VALIDATED (InMemory only)** | `DeleteStoppedCheckpointsAsync` is untouched in the diff; run: `An_explicit_stop_deletes_a_retained_row_regardless_of_the_hold` green. Postgres twin Docker-gated. | verifier |
| A7 — guarding the unhandled-exception delete strands nothing | **VALIDATED** | Run: `DeleteExpiredAsync_deletes_a_held_row_once_its_deadline_has_passed` green. Strengthened since cycle 1: a held row is now unclaimed and invisible to the sweep, so nothing writes to it and `updated_at_utc` stays frozen at the failure time — the 24 h conjunct is satisfied long before the deadline, so the row dies at its deadline rather than drifting. The skip-on-read-failure branch leaves an *unheld* row, which the ordinary cutoff reaps. | verifier |
| A8 — the upsert will not clear `retain_until_utc` on a later or duplicate write | **VALIDATED** | grep: `retain_until_utc` at `CheckpointRepository.cs:451, 618, 677, 793` only — `:618` is the lock, not `UpsertAsync`. In-memory guard `:69-70`. Run: `R1_UpsertDoesNotClearRetention` 1/1 pass. | verifier |
| A9 — InMemory mirrors every changed predicate, so InMemory tests reflect Postgres behaviour | **REJECTED (narrower than cycle 1)** | Cycle 1's divergence is gone — the lock path now clears in both stores. One divergence remains: `InMemoryCheckpointRepository.cs:69-70` only preserves when the *incoming* entry's hold is null, so an `UpsertAsync` carrying a non-null `RetainUntilUtc` **sets or overwrites** the hold in memory where Postgres cannot touch it at all (code-reviewer I3; the unconditional-copy suggestion was not taken). Unreachable today — grep shows no production assignment to `RetainUntilUtc` on any entry handed to `UpsertAsync`. Separately, in-memory `TryClaimAsync` (`:196-213`) returns `true` unconditionally, ignoring both existence and staleness, which makes `A_stamped_row_can_be_claimed_again_immediately_by_another_instance` vacuous in that store (pre-existing, not introduced here). **Must not be re-assumed:** that a green in-memory retention test implies the Postgres behaviour. | verifier |
| A10 — the cleanup job can read a second config key through its existing job-data-map wiring | **NEVER-TESTED** | Superseded before execution; no second key exists and `CheckpointCleanupJob.cs` changed by comment only. | plan owner (drift, `decisions.md`) |
| A11 — no existing test asserts that a failed run deletes its checkpoint | **VALIDATED** | Re-read all five pre-existing `Verify(r => r.DeleteAsync(…))` sites in `ProcessEventCommandHandlerTests.cs` — `:618`, `:794`, `:1311`, `:1376`, `:1516` — every one asserts `Times.Never`. Nothing encoded delete-on-failure; nothing was rewritten. | verifier |
| `lessons.md#L-16f8c597` — `UnservableTerminalAge` is a plain `CheckpointTtl * 0.75` | **REJECTED (re-confirmed)** | `CheckpointRecoveryHandler.cs:729-737` returns `max(CheckpointTtl * 0.75, UnservableEscalationAge)`. The constraint it motivated held: `DefaultTtlHours = 24` untouched, `CheckpointCleanupJob.cs` gained no logic. | verifier |
| `lessons.md#L-6f1353b8` — ISB's flat checkpoint columns are inert bookkeeping the collector ignores | **REJECTED (re-confirmed), neutralised here** | Collectors do read the flat columns, but `MapToAdapterCheckpoint` (`ProcessEventCommandHandler.cs:1802-1826`) maps twelve fields and does **not** map `RetainUntilUtc`, so the new column never reaches an adapter. | verifier |
| `lessons.md#L-f80a1c15` — state lives cleanly in ISB, readability cleanly in the collector | **VALIDATED for this change** | Same citation: the stamp never crosses into `AdapterCheckpoint`, is never serialised into `adapter_state_json`, and appears in no wire DTO. | verifier |

---

## 3. Attention Item Disposition

| id | final disposition | evidence |
|---|---|---|
| R1 | **handled** | Judged against its invariant (*a hold must survive ordinary writes*), not the superseded "do not touch either raw upsert" wording. Ran `dotnet test …Infrastructure.Core.UnitTests.csproj --filter "FullyQualifiedName~R1_UpsertDoesNotClearRetention"` → `Passed! Failed: 0, Passed: 1`. Independently confirmed by grep that `retain_until_utc` is absent from `UpsertAsync`'s INSERT list and `DO UPDATE SET`. The invariant now holds in both stores on both ordinary-write paths (`UpsertAsync`, `TransitionFromScheduledWaitAsync`) — cycle 1's F1 hole is closed by the deliberate clear on the lock path. Postgres parity test exists, Docker-gated. |
| R2 | **handled** | Ran `…Application.UnitTests --filter "FullyQualifiedName~R2_OnlyFailureStatusesStampRetention"` → `Failed: 0, Passed: 7`. Predicate at `ProcessEventCommandHandler.cs:1304-1306` names `AdapterResultStatus.Failure / TransientFailure / ValidationFailure`; `result.Success` appears nowhere in the retention branch. |
| R3 | **handled** | Ran `…Application.UnitTests --filter "FullyQualifiedName~R3_RefusedWriteTakesPrecedenceOverRetention"` → `Failed: 0, Passed: 1`. Ordering read at `:1296-1309`. Now defended twice: the application early return *and* the store's owner guard (I1). |
| R4 | **handled** | Read all three comments at their citations. `CheckpointRecoveryHandler.cs:694-698` and `CheckpointCleanupJob.cs:21-26` are accurate against the landed predicates; `CheckpointCleanupJob.cs:73-75` — cycle 1's unnamed third — is now corrected too, and its claim ("`DeleteStoppedCheckpointsAsync` above already removed a stopped run's checkpoint whatever its retention hold") matches both the job's call order and the untouched `DeleteStoppedCheckpointsAsync`. No logic changed in either file. |
| R5 | **accepted-risk** | Confirmed the gate myself: `dotnet test …API.UnitTests --filter "FullyQualifiedName~CheckpointRepositoryTests"` → `Failed: 37, Passed: 0, Duration: 248 ms`, `DockerUnavailableException` on `unix:///var/run/docker.sock`. `execution_notes.md` reports this honestly and does not claim green. Residual risk is **larger than in cycle 1**, because B1 and B2 both landed in raw SQL that no `ToQueryString()` probe covers. Run this suite with Docker up before the branch is trusted. |

---

## 4. Decision drift

| decision (`decisions.md`) | outcome |
|---|---|
| Retention expressed by one nullable column, not a status marker | **landed as decided.** No `CheckpointStatus` value added or newly written. |
| Stamped as an absolute deadline at failure time, not derived from `updated_at_utc` | **landed as decided** (`ProcessEventCommandHandler.cs:1155`), with a caveat the note does not mention: `SetRetentionAsync` *also writes* `updated_at_utc = now`, so the stamp resets the very clock the decision was avoiding overloading. Harmless — it freezes at failure time and nothing writes afterwards — but it is a write the decision text implies does not happen. |
| Retention window configurable, default 168 h | **changed, then bounded.** Landed as `FailedRetentionDays` default `7`; F7's unit trap is now capped at 30 days rather than closed. |
| Unhandled-exception delete guarded rather than deferred | **landed as decided**, plus the approved skip-on-read-failure branch. |
| *Unverified:* the failure arm reaches every collector-reported failure and no other outcome | **abandoned in the first half** — A1. The stated consequence ("some failures retain nothing") materialised on the externally-cancelled path. |
| *Unverified:* no query or index assumes no further nullable timestamp | **landed, still unverified** — A3. |
| *Drift:* config key read in the Application layer; cleanup job and `DependencyInjection.cs` untouched | **landed as decided.** `git diff` shows no change to `DependencyInjection.cs`; `CheckpointCleanupJob.cs` changed by comment only. |
| *Drift:* `:880` stamps nothing, gains a guard only | **landed as decided.** `HandleUnhandledExceptionAsync:885` reaches only `TryDeleteCheckpointUnlessRetainedAsync`. |
| *Drift:* the R4 doc comments updated in this change | **landed as decided**, and the third one too. |
| *Drift:* `DeleteExpiredAsync` ternary → indexable OR form → F2 form | **landed as decided.** Both stores carry the final three-conjunct form; semantics checked directly. The indexability claim rests on W1's probe, which this verifier did not reproduce; the *correctness* of the form does not depend on it. |
| *Drift:* two InMemory guards beyond original scope | **landed** (`:69-70`, `:391`); the third (lock path) removed, per the reversal. |
| *Drift (W2):* skip the delete when the `GetAsync` read throws | **landed as decided** (`:1127-1134`). |
| **Reversal:** F1 repair was wrong — a takeover clears, an ordinary write preserves | **landed for the lock path only.** Clear at `CheckpointRepository.cs:618` / `InMemoryCheckpointRepository.cs:265`; preserve at `UpsertAsync` and `TransitionFromScheduledWaitAsync`. **But the rule as stated is not universal** — `TryClaimAsync` is also a takeover and preserves. See F1. |
| **B1 was a regression this change introduced; claim released in the same UPDATE** | **landed as decided**, one statement, both stores. |
| **Accepted, not fixed: I2 (a crashed leg stamps no hold), I5 (rolling-deploy window)** | **still accepted.** I5 is now *worse* than the note admits: with B1 fixed the row is unclaimed, so an old replica's sweep has nothing to wait out. The note says this; the deploy runbook does not exist yet. |

---

## 5. Findings

**F1 — the preserve-vs-clear rule holds on three of four write paths. `TryClaimAsync` is the fourth, and nobody enumerated it.**
`ICheckpointRepository.TryClaimAsync` takes a claim on an unclaimed-or-stale row (`CheckpointRepository.cs:478-516`, `InMemoryCheckpointRepository.cs:196-213`) and leaves `retain_until_utc` exactly as it found it. It is a *takeover* by the reversal's own definition, and by that rule it should clear. Its two production callers are `CheckpointRecoveryHandler.cs:295` and `:825`, both downstream of `GetRecoverableAsync`, so a *live* hold cannot reach it — only an expired one can, and both predicates already read an expired hold as "no hold". Consequence today: a row can end up **claimed-and-held** with a stale expired stamp that nothing ever clears until the row is deleted. Benign, but the invariant "a held row is never owned" is not actually enforced, and the new test `A_stamped_row_can_be_claimed_again_immediately_by_another_instance` (both stores) deliberately pins claimed-and-held *with a live seven-day hold* as acceptable — which is the opposite of what `SetRetentionAsync`'s own `<remarks>` says a hold means. `TransitionFromScheduledWaitAsync` is a fifth path that both takes a claim and preserves; it is unreachable with a hold (the stamp releases the claim, and `TransitionToScheduledWaitAsync` is owner-guarded), so it is a documentation problem rather than a live one. Either clear on all takeovers or restate the rule as "only the execution lock clears".

**F2 — the moment a hold expires, the recovery sweep re-dispatches the run. This is new behaviour, it is near-certain rather than a race, and it is not named anywhere in the artifacts.**
The B2/2 change makes `GetRecoverableAsync` offer any row whose stamp is in the past. A held row is unclaimed (B1) and its `updated_at_utc` is frozen at the failure time, so on the first sweep tick after the deadline it satisfies every clause: `Status != ScheduledWait`, `RetainUntilUtc <= nowUtc`, `ClaimedByInstance == null`. `CheckpointRecoveryJob.DefaultIntervalMinutes = 5`; `CheckpointCleanupJob.DefaultIntervalMinutes = 30`. So the sweep gets six chances before the cleanup job's first, and `TryDispatchAsync` (`CheckpointRecoveryHandler.cs:221-320`) has **no age guard** — only capacity, a `PlatformEventJson` presence check, JSON deserialisation, the in-process dedupe, and `CanReadStoredStateAsync`. If the stored state is still readable, the run is claimed and re-dispatched: a collection that failed and published its failure done a week ago runs again and publishes a second done. `EndUnservableRunAsync` does not intercept it — that path is reached only when the compatibility answer says the state cannot be read. Before this change the row did not exist, so there was nothing to pick up.
Worth noting that with B2(b) landed — the lock clears the hold — the B2(a) predicate change is no longer load-bearing: the "taken-over row stays invisible forever" scenario it was written to fix is already prevented by `TryAcquireExecutionLockAsync` nulling the column, and `DeleteExpiredAsync` reaps a held row at its deadline on its own clause. Reverting `GetRecoverableAsync` to `RetainUntilUtc == null` closes F2 without reopening B2, at the cost of one test inverting in each store. That is a judgement call for the plan owner, not a defect I can settle — but shipping F2 unnamed is not an option.
Confidence: the predicate and the two intervals are certain from source; that a re-dispatch produces a duplicate done follows from `TryDispatchAsync` having no age or terminality filter, which I read but could not execute.

**F3 — the owner guard IS reachable on the failure path; the stamp does not silently no-op.** (The highest-value check, and it passes.)
Traced end to end. The claim is taken at `ProcessEventCommandHandler.cs:145` with `InstanceId` (`:42`, `Environment.MachineName`). `UpsertAsync`'s `DO UPDATE SET` (`CheckpointRepository.cs:81-96`) does **not** overwrite `claimed_by_instance` and its guard requires the row's owner to equal the writer's, so every accepted checkpoint write leaves the claim where it was; the heartbeat renews it via `RenewClaimAsync` (`:1054`) and is cancelled only in the `finally` at `:400`, i.e. after `CompleteExecutionAsync` returns. Every path that releases the claim before completion returns first and never reaches `FlushAndCleanupCheckpointAsync`: `HandlePartialWaitAsync` (`:927`), `HandleCheckpointPersistenceFailureAsync` (`:927` region), `HandleRejectedCheckpointWriteAsync` (`:1531`), `HandBackForRecoveryAsync` (`:1578`). And a genuinely claim-lost execution is caught earlier at `:243-275`. So at `:1159` the row is still owned by `InstanceId` and the guard admits the write. Row-existence is likewise guaranteed — `TryAcquireExecutionLockAsync` INSERTs the row before the adapter runs.
The gap is in the evidence, not the code: no runnable test exercises handler-`InstanceId` against store-guard. The handler suite mocks `ICheckpointRepository` and returns `true` unconditionally, and `VerifySetRetention` matches `instanceId` with `It.IsAny<string>()`, so a handler passing the *wrong* instance id would keep every green test green. The store-level guard tests are real but Postgres-gated.

**F4 — the new tests drive production paths, with two exceptions.**
`InMemoryCheckpointRetentionTests` (15) instantiate the real `InMemoryCheckpointRepository`; `CheckpointRepositoryTests` (13 new) drive the real `CheckpointRepository` against Testcontainers. Both seed through a store write rather than through the mechanism under test (I3), except `R1_UpsertDoesNotClearRetention`, which sets `RetainUntilUtc` on the stored entry directly and says why. The exceptions: (a) the four handler tests necessarily use a Moq repository, so nothing runnable connects the handler to a real store — F3; (b) `A_stamped_row_can_be_claimed_again_immediately_by_another_instance` is vacuous in the in-memory store, whose `TryClaimAsync` returns `true` even for a key that does not exist, and it exercises `TryClaimAsync` where the actual redelivery path uses `TryAcquireExecutionLockAsync`. The Postgres twin is meaningful but unrunnable here.

**F5 — one claim in `execution_notes.md` is contradicted by the diff.**
W1 §"Files changed" still states: *"`retain_until_utc` now appears exactly once in that file, inside `SetRetentionAsync`. Verified by grep."* It appears four times — `CheckpointRepository.cs:451, 618, 677, 793` — and `:618` is the lock upsert, i.e. exactly the line the B2 section further down says was added. The file contradicts itself; the later section is the correct one. Everything else I spot-checked in the notes is true, including the migration-triad audit trail, which I reproduced (`diff` of the two model bodies is empty).

**F6 — the reversal makes retention "the last delivery's artifact", which is coherent but unstated.**
Because `TryAcquireExecutionLockAsync` clears the hold, the *next* delivery of the same correlation id destroys the artifact before anything decides whether that delivery deserves to. For a `TransientFailure` — the default status of `AdapterResult.FailureResult(...)`, redelivered by the broker within about a second — attempt N's hold is gone almost immediately; only the final attempt's survives, and if the final attempt succeeds or hits `ADAPTER_NOT_FOUND` the row is deleted outright and no artifact remains. That is a defensible semantic and probably the intended one, but the contract's "a collector-reported failure stamps and does not delete" reads as unconditional. Worth one line in the PR body.

**F7 — `SetRetentionAsync` writes `updated_at_utc`, which the decision text implicitly rules out.** Minor; see Decision drift row 2. It happens to be harmless because the row goes quiet immediately afterwards.

**Service-hub constraint — held.** The predicate at `:1304-1306` reads `result.Status` and nothing else. No `ErrorCode` inspection, no attempt counting, no transient/non-transient judgement. `TryRetainCheckpointAsync` records and logs; it does not decide.

**Contract compliance, mechanical.** No `var` in any new code (checked every added block). No `<remarks>` narration beyond the two on `ICheckpointRepository.SetRetentionAsync` and `UnservableTerminalAge`, both of which carry load-bearing rationale. Migration additive, no backfill, no index. Nothing committed, pushed, or opened as a PR. No `CheckpointStatus` value added or newly written.

---

## 6. Verdict

**Pass with gaps.**

The eight changes since cycle 1 all landed and are correct in both stores where I could read them.
B1 is genuinely fixed — one statement, both stores. B2 is genuinely fixed in both halves, and the
preserve/clear distinction is coherent on every path that can carry a live hold. The owner guard is
reachable at the moment the handler stamps, verified by tracing the claim across every path into
`CompleteExecutionAsync` — the feature does not silently no-op. Build clean; 93/93, 417/417 and 25/25
run by me; R1, R2 and R3 run individually and green.

Gaps, in the order they should be decided:

1. **F2 — a retained row is re-dispatched by the recovery sweep the first tick after its hold expires**, five-minute sweep against a thirty-minute cleanup, with no age guard on the dispatch path. New behaviour, not named in any artifact, and reachable on every retained row that is not deleted first. With the lock now clearing holds, the predicate change that causes it is no longer needed to prevent B2's stranding.
2. **R5 / A3 — the entire Postgres side is runtime-unverified, and more so than in cycle 1**, because B1 and B2 both landed in raw SQL that the `ToQueryString()` evidence does not cover. `CheckpointRepositoryTests` must run with Docker up before merge.
3. **F1 — `TryClaimAsync` is a fourth write path outside the preserve/clear rule**, and a new test pins claimed-and-held with a live hold as acceptable. Benign today; the stated invariant is not enforced.
4. **F3's evidence gap** — nothing runnable connects the handler's `InstanceId` to the store's owner guard, and the Moq matcher accepts any instance id.
5. **A1 rejected** — an externally cancelled run reporting a failure still retains nothing.
6. **A9 rejected** — in-memory `UpsertAsync` can set a hold Postgres cannot; unreachable today.
7. **F5** — one stale self-contradicting claim in `execution_notes.md`.
8. **F6 / I5** — the last-delivery-wins semantic and the rolling-deploy window both belong in the PR body.

None of these argues for redesign. The column, the stamp, the claim release, the ordering and the two
cutoffs are right as built.
