# Verifier 1 — failed-checkpoint retention TTL column

Diff base: `d53e347d48b71d7befa9bce6b9086b909a547093` (`state.json.baseRef`), branch
`feat/failed-checkpoint-retention-ttl`. 14 modified files + 3 new files. Everything below was read or
run by the verifier; worker claims were not taken on trust.

**Runtime coverage caveat that colours several rows:** Docker is unavailable on this machine.
`CheckpointRepositoryTests` — all 32, the 8 new ones included — fails in ~185 ms with
`DockerUnavailableException`. That is environmental, not a regression. Consequence:
**the Postgres `GetRecoverableAsync` / `DeleteExpiredAsync` / `SetRetentionAsync` predicates and the
EF migration have never executed against a database.** `CheckpointCleanupJob` has no test at all.

`CLAUDE.md` claims one pre-existing failure in `Application.UnitTests`. It did not reproduce:
93/93 green on this branch, verified by the verifier's own run.

---

## 1. Success Criteria coverage

| # | criterion | verdict | evidence |
|---|---|---|---|
| SC1 | nullable `retain_until_utc` on `adapter_checkpoints`, mapped on `CheckpointEntry` + `CheckpointDbContext`, additive EF migration | **met** | `CheckpointEntry.cs:110-115`; `CheckpointDbContext.cs:95`; `20260827124500_AddCheckpointRetainUntil.cs` = one `AddColumn<DateTime>(nullable: true, type: "timestamp with time zone")`, `Down` = one `DropColumn`, no backfill, no index. Migration never executed (Docker gate). |
| SC2 | a collector-reported failure stamps `now + retention` and does not delete | **met** | `ProcessEventCommandHandler.cs:1302-1309` (predicate + `TryRetainCheckpointAsync` + `return null` above the delete); `:1149-1157` computes `DateTime.UtcNow + FailedRetention`. Verified by run: `R2_OnlyFailureStatusesStampRetention` 7/7 rows pass, `DeleteAsync` asserted `Times.Never` on the three stamping rows. |
| SC3 | every other delete path unchanged, except unhandled-exception does not delete a retained row | **met, with one recorded deviation** | `HandleAdapterNotFoundAsync:794` still calls the unconditional `TryDeleteCheckpointAsync`; stop paths (`:324`, `:357`) still `DeleteByCorrelationIdAsync`; success delete unchanged at `:1310-1322`. Guard at `:884` → `TryDeleteCheckpointUnlessRetainedAsync:1115-1144`. Verified by run: `Handle_does_not_delete_a_checkpoint_carrying_a_hold_when_the_leg_throws` and `Handle_deletes_the_checkpoint_without_a_hold_when_no_adapter_is_found` both pass. **Deviation:** when the `GetAsync` read at `:1121` throws, the delete is now *skipped* (`:1128-1134`); today's code deleted regardless. Recorded and approved in `decisions.md`; named again in Findings F6. |
| SC4 | `GetRecoverableAsync` never returns a row with non-null `retain_until_utc`, both repos | **met (InMemory verified, Postgres unverified)** | `CheckpointRepository.cs:675`; `InMemoryCheckpointRepository.cs:281`. InMemory proven by run (`GetRecoverableAsync_never_offers_a_row_held_by_retention`, green). Postgres clause read but never executed. See F3 for a second dispatch path that is *not* filtered. |
| SC5 | `DeleteExpiredAsync` deletes retained rows past the stamp and non-retained past `TtlHours`, both repos | **met (InMemory verified, Postgres unverified)** | `CheckpointRepository.cs:446-453`; `InMemoryCheckpointRepository.cs:157-166`. Four InMemory tests green (held-before-deadline kept, held-after-deadline deleted, unheld deleted at cutoff, `ScheduledWait` still exempt). |
| SC6 | the upsert path does not clear an existing `retain_until_utc` | **met for the upsert; not met for the InMemory lock path** | Verified by grep: `retain_until_utc` occurs exactly 4 times in `CheckpointRepository.cs` — `:450`, `:451`, `:675`, `:787` — i.e. in *neither* upsert column list (INSERT `:60-66`, `DO UPDATE SET` `:81-96`) nor in `TryAcquireExecutionLockAsync` (`:600-621`). InMemory guarded at `:67-70` and `:377`; `R1_UpsertDoesNotClearRetention` passes. **But** `InMemoryCheckpointRepository.TryAcquireExecutionLockAsync:260-264` does `_store[key] = entry` with a fresh entry and drops the hold — see F1. |
| SC7 | new config key, defaults to 168 hours, wired into the cleanup job the same way `TtlHours` is | **partially met — deliberately superseded** | `ConfigurationKeys.cs:348` `Checkpoint:FailedRetentionDays`, `:362` default `7` (= 168 h). Read in the Application layer at `ProcessEventCommandHandler.cs:59-61`. The cleanup job is **not** wired to a second key — the design stamps an absolute deadline, so the job only compares. Recorded as drift in `decisions.md` before execution; correct call. Unit hazard noted in F7. |
| SC8 | tests cover: failure arm stamping not deleting; sweep skipping retained; both cleanup cutoffs; unhandled-exception guard; upsert not clearing | **met** | 8 new InMemory tests + 8 new handler tests + 8 new (Docker-gated) Postgres tests. All five named behaviours have at least one Docker-free test that the verifier ran green. |
| SC9 | solution builds; affected test projects pass; pre-existing failures distinguished | **met** | Verifier's own runs: `dotnet build …sln` → **Build succeeded, 0 Warning(s), 0 Error(s)** (NU1900 filtered). `Application.UnitTests` 93/93. `Infrastructure.Core.UnitTests` 410/410. `API.UnitTests` `StoppingIsNotEndingTests` + `CheckpointRecoveryCompatibilityTests` 25/25. `CheckpointRepositoryTests` 0/32, all `DockerUnavailableException`. The `CLAUDE.md` pre-existing `Application.UnitTests` failure did not reproduce. |
| SC10 | no file outside the repo modified | **met** | `git status` shows only the 14 tracked edits, 3 new source files, and `ai/`. No stray probe project left in the tree (the verifier's own `ToQueryString`-style probe was built in the scratchpad and is not in the repo). |

---

## 2. Assumption Disposition

| id | status | citation | actor |
|---|---|---|---|
| A1 — failure arm reached by every collector-reported failure and by no other outcome | **REJECTED (first half)** | `ProcessEventCommandHandler.cs:280-287`: `if (cancellationToken.IsCancellationRequested && !result.Success) return result;` — a collector-reported `Failure` arriving during external cancellation returns *before* `CompleteExecutionAsync` and stamps nothing. Likewise `:271` (CLAIM_LOST) and `:246-258`. The second half holds: every ISB-manufactured `FailureResult` (`:161`, `:173`, `:271`, `:374`, `:386`, `:796`, `:886`, `:928`, `:1502`, `:1580`) returns directly from `Handle` and never reaches the branch, and `R2_OnlyFailureStatusesStampRetention` (run: 7/7 green) proves no non-failure status stamps. **Must not be re-assumed:** that the retention branch is the sole terminus of every failing run. A pod-shutdown-cancelled failure leaves an unheld row on the ordinary 24 h TTL. | verifier |
| A2 — `FlushAndCleanupCheckpointAsync` can take the outcome without disturbing the refused-write early return | **VALIDATED** | `ProcessEventCommandHandler.cs:1295-1309` — the `HasRejectedCheckpointWrite` guard is still the first statement and returns before the retention branch. Run: `R3_RefusedWriteTakesPrecedenceOverRetention` green, asserting `SetRetentionAsync` `Times.Never` and `DeleteAsync` `Times.Never`. | verifier |
| A3 — nullable column needs no backfill and breaks no existing query, index or upsert | **NEVER-TESTED** | Statically supported: both raw upserts name explicit column lists (`CheckpointRepository.cs:60-66`, `:81-96`, `:600-616`), so a new column cannot perturb them; EF emits explicit projections. But the migration never ran — `MigrateAsync()` was never executed on this branch. | — |
| A4 — `GetRecoverableAsync` + `RetainUntilUtc == null` fully prevents re-dispatch, with no second path into recovery | **REJECTED** | `CheckpointRecoveryHandler.cs:85` is the only caller of `GetRecoverableAsync`, but `ICheckpointRepository.GetScheduledWaitsDueByAsync` (`:157`) is a **second dispatch path that carries no retention filter** (`CheckpointRepository.cs`, `InMemoryCheckpointRepository.cs:287-295`). A held row can reach it: hold set → external re-trigger → `PartialWaitRequired` → `TransitionToScheduledWaitAsync`, which does not clear the hold (`retain_until_utc` appears nowhere in that path). **Must not be re-assumed:** that a non-null `retain_until_utc` makes a row undispatchable. It makes it invisible to the *recovery sweep* only. | verifier |
| A5 — `DeleteExpiredAsync` carries two cutoffs without changing behaviour for null rows | **VALIDATED (InMemory)** | `InMemoryCheckpointRepository.cs:157-166`; run: `DeleteExpiredAsync_still_deletes_an_unheld_row_at_the_cutoff` and `…still_exempts_ScheduledWait…` green within 410/410. The Postgres twin (`CheckpointRepository.cs:446-453`) is textually the same expression tree but was never executed. | verifier |
| A6 — `DeleteStoppedCheckpointsAsync` keeps deleting retained rows (explicit stop outranks retention) | **VALIDATED (InMemory)** | `DeleteStoppedCheckpointsAsync` is untouched in the diff; run: `An_explicit_stop_deletes_a_retained_row_regardless_of_the_hold` green. Postgres twin exists (`CheckpointRepositoryTests`) but is Docker-gated. | verifier |
| A7 — guarding the unhandled-exception delete strands nothing, because the retention cutoff still reaps | **VALIDATED** | Run: `DeleteExpiredAsync_deletes_a_held_row_once_its_deadline_has_passed` green — a held row is reaped at its deadline. The guard's skip-on-read-failure branch (`:1128-1134`) leaves an *unheld* row, which the ordinary 24 h cutoff still reaps (`…still_deletes_an_unheld_row_at_the_cutoff`, green). | verifier |
| A8 — the upsert will not clear `retain_until_utc` on a later or duplicate write | **VALIDATED** | Postgres: grep proves `retain_until_utc` appears only at `:450`, `:451`, `:675`, `:787` — absent from both upsert column lists and from the lock upsert. InMemory: guard at `:67-70`; run: `R1_UpsertDoesNotClearRetention` green. Scope note: this is about `UpsertAsync` only — see A9 and F1 for the lock path. | verifier |
| A9 — InMemory mirrors every changed predicate, so InMemory tests reflect Postgres behaviour | **REJECTED** | The two *changed* predicates do agree semantically (`GetRecoverableAsync` `:281` vs `:675`; `DeleteExpiredAsync` `:157-166` vs `:446-453`, `nowUtc` computed in C# on one side and passed as a parameter on the other — identical meaning). But the stores **do not** agree on retention overall: `InMemoryCheckpointRepository.TryAcquireExecutionLockAsync:260-264` replaces the stored entry wholesale and drops the hold, where the Postgres `ON CONFLICT DO UPDATE` at `:614-618` names only claim columns and preserves it (F1). Second divergence: an `UpsertAsync` whose incoming entry carries a non-null `RetainUntilUtc` would *set* it in InMemory (`:67-70` only guards the null-incoming case) where Postgres can never set it at all. **Must not be re-assumed:** that a green InMemory retention test implies the Postgres behaviour. | verifier |
| A10 — the cleanup job can read a second config key through its existing job-data-map wiring | **NEVER-TESTED** | Superseded before execution: the recon-driven design stamps an absolute deadline, so no second key exists and `CheckpointCleanupJob.cs` changed only its `DefaultTtlHours` doc comment. The assumption was never put to the test. | plan owner (drift, `decisions.md`) |
| A11 — no existing test asserts that a failed run deletes its checkpoint | **VALIDATED** | Verifier read all five pre-existing `Verify(r => r.DeleteAsync(…))` sites in `ProcessEventCommandHandlerTests.cs` — `:618`, `:794`, `:1311`, `:1376`, `:1516` — every one asserts `Times.Never`. Nothing encoded delete-on-failure; nothing was rewritten. W3 §4's claim holds. | verifier |
| `lessons.md#L-16f8c597` — `UnservableTerminalAge` is a plain `CheckpointTtl * 0.75` | **REJECTED (re-confirmed)** | `CheckpointRecoveryHandler.cs:729-736` reads `max(CheckpointTtl * 0.75, UnservableEscalationAge)`. The refutation stands and the constraint it motivated held: `ConfigurationKeys.cs` adds a key and leaves `DefaultTtlHours = 24` untouched; `CheckpointCleanupJob.cs` gained no logic. | verifier |
| `lessons.md#L-6f1353b8` — ISB's flat checkpoint columns are inert bookkeeping the collector ignores | **REJECTED (re-confirmed), and neutralised here** | Collectors do read the flat columns via `CanResumeFrom`, but `MapToAdapterCheckpoint` (`ProcessEventCommandHandler.cs:1800-1824`) maps twelve fields and **does not map `RetainUntilUtc`**, so the new column is invisible to every adapter. Adding it cannot perturb collector behaviour. | verifier |
| `lessons.md#L-f80a1c15` — state lives cleanly in ISB, readability cleanly in the collector | **VALIDATED for this change** | Same citation: the retention stamp never crosses into `AdapterCheckpoint`, is never serialised into `adapter_state_json`, and appears in no wire DTO. It is purely ISB state, which is what the lesson said the boundary should look like. | verifier |

---

## 3. Attention Item Disposition

| id | final disposition | evidence |
|---|---|---|
| R1 | **handled** | Ran `dotnet test …Infrastructure.Core.UnitTests.csproj --filter "FullyQualifiedName~R1_UpsertDoesNotClearRetention"` → `Passed! Failed: 0, Passed: 1`. Independently confirmed the load-bearing omission by grep: `retain_until_utc` occurs at `CheckpointRepository.cs:450`, `:451`, `:675`, `:787` only — absent from the INSERT list `:60-66`, from `DO UPDATE SET` `:81-96`, and from `TryAcquireExecutionLockAsync` `:600-621`. Postgres parity test exists but is Docker-gated. **The check passes; the invariant is nevertheless incomplete in the InMemory store — see F1.** |
| R2 | **handled** | Ran `…Application.UnitTests --filter "…R2_OnlyFailureStatusesStampRetention"` → all 7 theory rows pass (part of `Passed: 8` with R3). Predicate at `ProcessEventCommandHandler.cs:1302-1304` names `AdapterResultStatus.Failure / TransientFailure / ValidationFailure`, never `result.Success`. Verifier ran an independent reflection probe against the pinned `Cymulate.Integration.Client` 1.2.0-preview.0 assembly: the enum has exactly 7 members, and `CancelledResult → Cancelled`, `PartialResult → PartialWaitRequired`, `SkippedResult → Skipped` — none of the three can satisfy the predicate. |
| R3 | **handled** | Ran `…Application.UnitTests --filter "…R3_RefusedWriteTakesPrecedenceOverRetention"` → pass. Read the ordering at `ProcessEventCommandHandler.cs:1295-1309`: `AwaitLastCheckpointAsync()` + `HasRejectedCheckpointWrite` → `return HandleRejectedCheckpointWriteAsync(...)` is the first statement; the retention branch is strictly below it. |
| R4 | **handled** | Read both guards at their citations. `CheckpointRecoveryHandler.cs:694-698` now reads "…deletes the row — longer still once a failed run has stamped a retention hold on it." `CheckpointCleanupJob.cs:21-26` now reads "The deletion horizon for a row carrying no retention hold — a failed run's row lives to its own stamped `retain_until_utc` instead." Both are accurate against the landed predicates. No logic changed in either file. A **third** comment in the same file went stale and was not named — F5. |
| R5 | **accepted-risk** | Confirmed the gate rather than accepting the report: `dotnet test …API.UnitTests --filter "…CheckpointRepositoryTests"` → `Failed: 32, Passed: 0, Duration: 185 ms`, `DockerUnavailableException` / `unix:///var/run/docker.sock`. `execution_notes.md` reports this honestly in both W1 and W3, and does not claim green. The residual risk is real and unmitigated: the Postgres predicates, `SetRetentionAsync`'s raw UPDATE, and the migration have no runtime evidence at all. |

---

## 4. Decision drift

| decision (`decisions.md`) | outcome |
|---|---|
| Retention expressed by one nullable column, not a status marker | **landed as decided.** No `CheckpointStatus` value added or newly written; `retain_until_utc` is the only expression of a hold. |
| Stamped as an absolute deadline at failure time, not derived from `updated_at_utc` | **landed as decided.** `ProcessEventCommandHandler.cs:1149`; `DeleteExpiredAsync` compares `RetainUntilUtc <= nowUtc` and never re-derives from `updated_at_utc`. |
| Retention window configurable, default 168 h | **changed.** Landed as `Checkpoint:FailedRetentionDays` default `7` (identical horizon, different unit). Recorded drift; see F7 for the operator hazard. |
| Unhandled-exception delete guarded in this change rather than deferred | **landed as decided**, plus an extra approved change on the same path (skip-on-read-failure) — F6. |
| Proceeding on unverified: the failure arm reaches every collector-reported failure and no other outcome | **abandoned in the first half.** The "no other outcome" half is now proven; the "every failure" half is contradicted (A1). The stated consequence — "some failures retain nothing" — is the one that materialised, on the externally-cancelled path. |
| Proceeding on unverified: no query or index assumes no further nullable timestamp | **landed, still unverified** — no runtime evidence (A3). |
| *Drift:* config key read in the Application layer; cleanup job and `DependencyInjection.cs` untouched | **landed as decided.** `git diff` shows no change to `DependencyInjection.cs`; `CheckpointCleanupJob.cs` changed by 6 lines, all comment. |
| *Drift:* `:880` stamps nothing, gains a guard only | **landed as decided.** `HandleUnhandledExceptionAsync:884` calls only `TryDeleteCheckpointUnlessRetainedAsync`; `SetRetentionAsync` is not reachable from it. |
| *Drift:* the two R4 doc comments updated in this change | **landed as decided** (see R4 row). |
| *Drift:* `DeleteExpiredAsync` ternary superseded by the indexable OR form | **landed as decided.** Both stores carry the OR form. The claim that it is indexable rests on W1's `ToQueryString()` probe, which the verifier did not reproduce; the *semantics* were checked directly and the two forms are equivalent. |
| *Drift:* two InMemory guards beyond original scope | **landed as decided** (`:67-70`, `:377`) — and incomplete, F1. |
| *Drift (W2):* skip the delete when the `GetAsync` read throws | **landed as decided** (`:1128-1134`). Behaviour differs from today on that path; F6. |

---

## 5. Findings

**F1 — R1's invariant does not hold in the InMemory store on the execution-lock path. (Not reported by any worker; the strongest finding here.)**
`InMemoryCheckpointRepository.TryAcquireExecutionLockAsync:260-264` is

```csharp
var key = BuildKey(entry.TenantId, entry.CorrelationId, entry.PlatformType, entry.Category);
entry.ClaimedByInstance = instanceId;
entry.ClaimedAtUtc = DateTime.UtcNow;
_store[key] = entry;
```

`entry` is the fresh, progress-free `CheckpointEntry` constructed at `ProcessEventCommandHandler.cs:132-139`; it never carries `RetainUntilUtc`. So the *first thing* a re-trigger does destroys the hold in the Console/dev host. Postgres preserves it, because its `ON CONFLICT DO UPDATE` at `CheckpointRepository.cs:614-618` names only `claimed_by_instance`, `claimed_at_utc`, `platform_event_json`, `updated_at_utc`. W1's execution note — "R1's invariant holds in Postgres and not in the Console/dev host [without the two added guards]" — is therefore overstated: guarding `UpsertAsync` and `TransitionFromScheduledWaitAsync` leaves the likeliest path unguarded, and no test covers it.
Mitigating: this InMemory wipe is *pre-existing and broader* — it already discards `CurrentPage`, `CursorToken` and `AdapterStateJson` on every lock acquisition, which is why the Postgres upsert's comment says "Progress fields are NOT reset on conflict". Retention is not uniquely broken; it inherits an existing store-parity defect. Severity: dev/Console host only, no production impact. But the "behaviourally identical" constraint is not satisfied, and the InMemory store cannot demonstrate resume-from-a-retained-checkpoint at all.

**F2 — Nothing ever clears `retain_until_utc`. The hold is permanent until the row is deleted.**
`retain_until_utc` is written in exactly one place (`CheckpointRepository.cs:787`) and never set back to null anywhere in either store; `TransitionFromScheduledWaitAsync` explicitly copies it forward (`InMemoryCheckpointRepository.cs:377`). Two consequences, neither covered by a test:
- A retried run that resumes from a held row and then *crashes mid-flight* leaves a row the recovery sweep will never pick up (SC4 excludes it by design), until the hold expires and `DeleteExpiredAsync` deletes it. Before this change the row would not have existed at all, so this is new territory rather than a regression — but it is the opposite of "self-healing".
- `DeleteExpiredAsync` deletes on `retain_until_utc <= now` **regardless of `updated_at_utc`**, so an actively progressing run whose row carries an elapsed hold loses its checkpoint mid-flight. Requires the retry to land near the end of the 7-day window; low probability, real.
This is consistent with the contract (which never mentions clearing) and with the user's request. Flagged as the first thing the follow-up resume/retry work will have to decide.

**F3 — `GetScheduledWaitsDueByAsync` is a second dispatch path with no retention filter.**
See A4. Benign today (dispatching a parked run is what that query is for), but it means "retained ⇒ not dispatched" is false, and a reader who takes SC4 as the whole story will be wrong.

**F4 — `SetRetentionAsync` is unguarded at the store level; R3 is defended at exactly one call site.**
Unlike `UpsertAsync`, the raw UPDATE at `CheckpointRepository.cs:781-791` has no owner predicate (`claimed_by_instance = …`) and no `NOT EXISTS (adapter_stop_requests …)` guard. The claim-lost protection is entirely the application-layer early return at `:1297-1299`. Any future second caller reintroduces exactly the failure R3 exists to prevent, and the store will not stop it. Cheap fix if wanted: add the ownership predicate to the UPDATE's `WHERE`; the method already returns `bool`.

**F5 — a third comment went stale and R4 did not name it.**
`CheckpointCleanupJob.cs:75-76`: "Same cutoff for stop-request rows — they only need to outlive any in-flight or queued run for the correlationId, **which checkpoints already TTL-bound**." Checkpoints are no longer uniformly TTL-bound. Harmless in practice (a held row that is stopped is deleted by `DeleteStoppedCheckpointsAsync` on the next 30-minute pass, long before the stop-request row ages out), but by the same standard that justified the other two edits, this one is now false.

**F6 — the skip-on-read-failure deviation is wider than SC3's exception.**
`TryDeleteCheckpointUnlessRetainedAsync:1121-1134` skips the delete when the `GetAsync` read itself throws, not only when a hold is present. SC3's exception is scoped to "a row carrying a retention stamp". The asymmetry argument in `decisions.md` is sound — falling through can destroy a hold, skipping only defers to the 24 h TTL — and it is recorded and approved, so this is not an unauthorised change. Naming it because a reader auditing SC3 against the code will find one more behavioural delta than SC3 describes.

**F7 — unit mismatch between the contract and the key.** The contract says "default 168 hours"; the key is `Checkpoint:FailedRetentionDays` with default `7`. Same horizon. The hazard is operational: an operator who half-remembers the contract and sets `Checkpoint:FailedRetentionDays=168` gets 168 **days**, and `DeleteExpiredAsync` will not reap those rows for half a year. No validation clamps the value. Consider a sanity bound or renaming to `…RetentionHours`.

**F8 — `AdapterResult.FailureResult(...)` defaults to `TransientFailure`, which the predicate matches.**
Verified by reflection probe against the pinned client assembly: `FailureResult("x","E")` yields `Status = TransientFailure`, `Success = false`. Every ISB-manufactured failure result therefore *would* stamp retention if it ever reached `FlushAndCleanupCheckpointAsync`. Today none does — all ten construction sites return directly from `Handle` (A1 row). The service-hub constraint holds **because of control flow, not because of the predicate**. One refactor that routes a manufactured failure through `CompleteExecutionAsync` would silently make ISB stamp its own verdict. Worth a line in the method or a test pinning it.

**Service-hub constraint — held.** The predicate reads `result.Status` and nothing else: no `ErrorCode` inspection, no attempt counting, no transient/non-transient judgement, no branching on `result.Success`. `ErrorCode` appears nowhere in the retention code. `TryRetainCheckpointAsync` records and logs; it does not decide.

**Claims in `execution_notes.md` checked against the diff.** All verified true except the two named above (W1's "R1's invariant … in the Console/dev host" → F1; the `ToQueryString()` plan claim, which the verifier did not reproduce and which the OR-form's semantics do not depend on). The migration-triad audit trail is **adequate, not wishful**: the verifier reproduced it mechanically — the `#pragma warning disable 612, 618 … restore` model body of `20260827124500_AddCheckpointRetainUntil.Designer.cs` is byte-identical to `CheckpointDbContextModelSnapshot.cs` (160 lines each, `diff` empty), and the delta against the previous migration's Designer body is exactly the four added lines of the `RetainUntilUtc` property block and nothing else. Combined with a clean `dotnet build`, that is as strong as static evidence gets; only `MigrateAsync()` against a live database can close it.

**W3 §5 — correct scoping, not a defect.** A run whose only failure is an unhandled exception leaves no retained row, because `HandleUnhandledExceptionAsync` returns before `CompleteExecutionAsync`. Measured against the wording: `task.md` behaviour 4 says the unhandled-exception path "must not destroy a row that carries a retention stamp" — implemented exactly. SC3 says the same. The user's request was scoped to "when a collector **reports** a failed status", and a crash is not a report. The operational counter-argument is worth the plan owner's attention — a crashed leg is precisely where a resumable checkpoint is most valuable, and it is still deleted — but that is a new requirement, not a miss against this contract. W3 was right to report rather than fix.

---

## 6. Verdict

**Pass with gaps.**

Every Success Criterion is met or deliberately superseded with the drift recorded in advance; all three
named test artifacts (R1, R2, R3) were run by the verifier and came back green; both R4 guards were read
at their citations and are accurate; the migration triad is mechanically self-consistent; the service-hub
constraint held. The build is clean and the three Docker-free suites are 93/93, 410/410 and 25/25.

Gaps, in order of what should be decided before merge:

1. **F1 — `TryAcquireExecutionLockAsync` drops the hold in the InMemory store.** R1's invariant is
   defended in two of the three places a later write touches the row. Postgres is safe; the "both
   repositories stay behaviourally identical" constraint is not satisfied. One-line fix plus one test.
2. **R5 / A3 — the entire Postgres side is runtime-unverified.** `DeleteExpiredAsync`,
   `GetRecoverableAsync`, `SetRetentionAsync`'s raw UPDATE and the migration have never executed.
   `CheckpointRepositoryTests` must be run with Docker up before this branch is trusted.
3. **F2 — nothing clears `retain_until_utc`**, so a hold outlives every subsequent resume and
   `DeleteExpiredAsync` will delete an actively progressing row once its deadline passes.
4. **A1 rejected** — an externally cancelled run reporting a failure retains nothing. Decide whether
   that is intended before the resume/retry endpoint is built on top of retention.
5. **A4 rejected, F3** — `GetScheduledWaitsDueByAsync` dispatches held rows.
6. Minor: **F4** (unguarded store-level `SetRetentionAsync`), **F5** (third stale comment),
   **F7** (days-vs-hours), **F8** (`FailureResult` defaults to a stamping status).

None of these is a reason to rewrite the design. The column, the predicates, the ordering and the
asymmetry are all correct as built.
