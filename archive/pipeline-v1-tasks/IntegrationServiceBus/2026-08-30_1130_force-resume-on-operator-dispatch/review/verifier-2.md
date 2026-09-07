# Verifier 2 — force the resume path on an operator-initiated dispatch (final)

**Verdict: PASS WITH GAPS.** Items 1–5 all landed and all are correct as described. Nothing is newly
broken in the sense of a defect that a test or a reading catches. What is open is a deliberate
behaviour change on the *unmarked* path that contradicts a written constraint (F1), three production
paths added in the last two rounds with no test at all (F2), and a Postgres rewrite with zero runtime
verification (F3).

## 0. Snapshot, and a caution about it — again

**The tree moved under me mid-verification, exactly as it did for cycle 1.** My first read of
`ProcessEventCommandHandler.cs` predated an edit at 12:37 that added the round-5 delete-guard. Every
measurement below was re-taken after that edit. Everything here describes this snapshot:

```
2026-08-30 12:41:40
19171c2ba7bb 12:24:52  AdapterRunMessage.cs          3c9e445ddc1d 12:14:27  ICheckpointRepository.cs
762fe9814300 12:14:11  ExecutionLockOutcome.cs       e72699e1694c 11:35:59  ProcessEventCommand.cs
c46e5eef5454 12:37:25  ProcessEventCommandHandler.cs 256e5e881c28 12:04:31  IsbPlatformEventDispatcher.cs
2b94827eac92 12:04:47  EventsController.cs           91174c5dd03d 12:14:27  InMemoryCheckpointRepository.cs
a2d838e7c8c8 12:14:59  CheckpointRepository.cs       8384ccdff66e 12:13:15  RetainedCheckpointIsNotConsumedTests.cs
d9edf6cf344c 12:19:33  ForcedResumeOnOperatorDispatchTests.cs
e188c1c481b6 12:08:11  ForceResumeWireContractTests.cs
```

Freeze the tree before merging and re-check these hashes.

### Delta isolation

I rebuilt the pass-2 baseline independently of cycle 1 — `git archive d53e347d` into a clean directory,
then `scratchpad/pass2-terminal-status-baseline.patch`, which applied clean — and diffed `src/` against
the working tree. This task's delta, with the pass-2 untracked test files
(`FailedRunLeavesATerminalRowTests.cs`, `CheckpointCleanupJobTests.cs`,
`InMemoryCheckpointRepositoryTerminalStatusTests.cs`) excluded as pass-2 additions:

| file | kind |
|---|---|
| `Domain/…/Messaging/AdapterRunMessage.cs` | modified — `public static bool ReadForceResume(string)` only; **no bound property** |
| `Domain/…/Interfaces/ICheckpointRepository.cs` | modified — lock returns `ExecutionLockOutcome` |
| `Domain/…/Models/ExecutionLockOutcome.cs` | **added** |
| `Application/Commands/ProcessEventCommand.cs` | modified — `ForceResume { get; init; }` |
| `Application/Commands/ProcessEventCommandHandler.cs` | modified — gate skip, refusal branch, native-fallback propagation, delete-guard, 4 log sites |
| `Application/Messaging/IsbPlatformEventDispatcher.cs` | modified — reads and sets the flag |
| `Hosts/…/Controllers/EventsController.cs` | modified — both HTTP trigger-flow paths |
| `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs` | modified — reports revival |
| `Infrastructure.Postgres/Persistence/CheckpointRepository.cs` | modified — lock rewritten as a CTE over a raw `DbCommand` |
| 5 existing test files | modified — mechanical adaptation to the new return type |
| 4 test files | added |

`CheckpointRecoveryHandler.cs`, `TriggerFlowMapper.cs` and `CheckpointEntry.cs` are byte-identical to
the pass-2 baseline. `PlatformEvent` is not in this repo.

**cycle 1's file set is no longer this task's file set.** Three of the nine production files above —
the repository interface, both repository implementations, and a new Domain type — arrived after
cycle 1 stopped reading. That is the scope drift the prior-art row predicted, and it is what puts F3
on the list.

Beyond reading, I ran every suite named below and **eight mutations** on a full copy of this snapshot
(`scratchpad/v2mut`). The repository itself was not modified except for this file.

---

## 1. Success Criteria coverage

| # | criterion | verdict | evidence |
|---|---|---|---|
| 1 | Marker + checkpoint row → `ResumeAsync` without consulting `CanResumeFrom` | **MET** | `ProcessEventCommandHandler.cs:1257` — `if (forceResume \|\| resumable.CanResumeFrom(adapterCheckpoint))`; `\|\|` short-circuits. `A_forced_resume_never_consults_the_collector_gate` verifies `CanResumeFrom` `Times.Never` against a collector arranged to answer **false**. `RetainedCheckpointIsNotConsumedTests.Arm1` repeats it against the real in-memory store. |
| 2 | Unmarked dispatch behaves exactly as before, decline-then-restart included | **NOT MET as written; MET for a non-terminal row** | `Arm4_A_declined_non_terminal_checkpoint_still_restarts` passes and `R3_UnmarkedDispatchIsUnchanged` passes. But an unmarked dispatch against a **revived terminal** row now fails instead of restarting (`:1301`). Deliberate — item 4 of the brief — and contradicts `constraints.md`. See F1. |
| 3 | Marker cannot reach an automatic sweep dispatch — **demonstrated** | **MET** | Structural: `ForceResume` is on `ProcessEventCommand:24`, not on `PlatformEvent`; `ProcessEventCommandHandler.cs:134` serializes only `platformEvent`; the sweep builds `new ProcessEventCommand(evt) { CapacityPreAcquired = true }` (`CheckpointRecoveryHandler.cs:390`) — I re-grepped all 15 production construction sites, four set the flag (`IsbPlatformEventDispatcher.cs:228`, `EventsController.cs:611/785`, `ProcessEventCommandHandler.cs:736`), none is the sweep. Behavioural: `R1_SweepDispatchNeverForcesResume` **Passed: 1**. Reinforced by `I4_FlagOutsideTheRootIsIgnored` ×3. |
| 4 | Marker visible in logs when it causes an override | **MET, and extended** | Four sites, all naming the correlation id: the override at `:1250` (Warning, with `CheckpointAgeHours`), the two degradations at `:1214` (adapter not resumable) and `:1236` (no checkpoint row), and the refusal at `:1279` (Error, with page and age). Plus `ForceResume:` on the three dispatch-entry Information lines. |
| 5 | A forced resume that fails is reported as a failure; no silent restart | **MET** | The `ProcessAsync` fall-through (`:1327`) sits after the `if` block and is unreachable when forced; `ResumeAsync`'s result is returned directly and the `finally` swallows its own telemetry exceptions. `R2_ForcedResumeFailureDoesNotRestart` passes; mutation **M-E** (drop the flag in the native fallback) fails `The_native_fallback_re_dispatch_carries_the_force(force: True)`. |
| 6 | `AdapterRunMessage` carries the marker; `PlatformEvent` unchanged | **MET, by a different mechanism than cycle 1 saw** | The bound `[JsonPropertyName("forceResume")] bool` is **gone from both root records**; the marker is read by `AdapterRunMessage.ReadForceResume` (`:50`), a `JsonDocument` probe. `PlatformEvent` ships in `Cymulate.Integration.Client` and is absent from the delta. |
| 7 | Tests cover the four named behaviours | **MET for the four; NOT MET for three paths added later** | 26 tests added (9 + 12 + 1 + 5, minus the 1 counted twice). Every one drives a real handler / real dispatcher. But the two HTTP endpoints and the round-5 delete-guard have no test — mutations **M-F** and **M-G** both survive every suite. See F2. |
| 8 | Solution builds; affected test projects pass; Docker-gated reported as not-run | **MET** | `dotnet build --no-incremental -m:1`: **Build succeeded, 0 errors, 13 code warnings** (22×CS1573 + 4×CS9113 occurrences across 13 distinct lines, all pre-existing SiemRules/primary-constructor warnings; none in a file this task touched) plus 2 `MSB3026` file-lock retries which are environment noise. Suites below. |
| 9 | No migration, no new column, nothing outside this repo | **MET** | No entity, no `DbContext`, no `Migrations/` file changed. `CheckpointEntry.cs` is byte-identical to pass-2. |

### Runs (mine, against the 12:41 snapshot, after a clean rebuild)

| suite | result |
|---|---|
| `Application.UnitTests` | **114 / 114 pass** |
| `Infrastructure.Core.UnitTests` | **449 / 449 pass** |
| `API.UnitTests` — the five non-container checkpoint classes | **32 / 32 pass** |
| `API.UnitTests` whole project | **635 pass / 139 fail / 774** |

The 139: **136 Docker-gated** (`QueryIntegrationRepositoryTests` 59, `CheckpointRepositoryTests` 47,
`CredentialStoreTests` 9, `RedisCredentialChangeBusTests` 9, `RedisDistributedLockFactoryTests` 7,
`ScheduledContinuationIntegrationTests` 5 — all `DockerUnavailableException`) and **3 pre-existing**
`TestAdapterConnectionCommandHandlerTests`, which cycle 1 reproduced on the pass-2 baseline tree.
Cycle 1's two red `Arm3_*` tests are now green. **No new failure.**

**Docker gate:** Docker Desktop is down and there is no local Postgres (`psql` absent, nothing on
5432). 136 Testcontainers tests did not run, **not** "passed". That includes all 47 Postgres
`CheckpointRepositoryTests` — so the pass-2 retention SQL remains runtime-unverified, and so does
**this task's rewrite of `TryAcquireExecutionLockAsync`**, which is new and is worse than the
pass-2 situation. See F3.

---

## 2. Assumption Disposition

Rebuilt from scratch against the current code. Cycle 1's dispositions are superseded where they differ.

| id | status | citation | actor |
|---|---|---|---|
| A1 — no collector re-checks staleness inside `ResumeAsync` or its load path | **NEVER-TESTED** | `grep -rn "CanResumeFrom" src/` returns one production line (`ProcessEventCommandHandler.cs:1257`) and test doubles only; `grep -rln "IResumableAdapter" src/` finds no implementation outside tests. Falcon/CloudGuard ship in `Cymulate.Integration.Client`. `decisions.md` records this as proceeding-on-unverified and the stated fallback (fail loudly, not restart) is what the code does. | verifier |
| A2 — `PlatformEvent.Metadata` survives `TriggerFlowMapper` → `ExecuteWithResumeAsync` | **NEVER-TESTED (superseded)** | The `Metadata` route was abandoned before any code was written; `TriggerFlowMapper.cs` is byte-identical to pass-2. The live equivalent is A4. | verifier |
| A3 — a marker on an operator dispatch cannot reach an automatic sweep dispatch | **VALIDATED** | Structural (the flag is not a member of the serialized type; the sweep's own construction site sets only `CapacityPreAcquired`) **and** behavioural (`R1_SweepDispatchNeverForcesResume` drives a real forced dispatch, orphans the leg, runs the real `CheckpointRecoveryHandler.RecoverAsync`, inspects the dispatched command — **Passed: 1**). `I4_FlagOutsideTheRootIsIgnored` ×3 closes the second route: the flag is not read from `payload`, `payload.action` or `payload.metadata`, the three places that *are* persisted. | verifier |
| A4 — `AdapterRunMessage` is ISB-owned; a new optional field breaks no producer or consumer | **VALIDATED** (cycle 1: REJECTED — **fixed**) | There is no longer a field. Neither `AdapterRunMessage` nor `AdapterRunMessageFlat` binds `forceResume`, and neither root record carries `[JsonExtensionData]`, so `System.Text.Json` ignores any shape at the root. `ReadForceResume` requires `ValueKind == JsonValueKind.True` and catches `JsonException`. `I4_NonBooleanRootValuesDoNotForce` drives `"true"`, `null`, `1`, `[true]`, `{"value":true}`, `false` through the **real** dispatcher and asserts a command was still dispatched — 6/6 pass. Mutation **M-D** (`!= JsonValueKind.False`) fails five of the six. | verifier |
| A5 — with the marker absent, every existing path behaves byte-for-byte as today | **REJECTED** | Two grounds, the second material. (a) Cosmetic: three log templates gained `ForceResume: {ForceResume}` and one extra full-body `JsonDocument.Parse` runs per trigger-flow message at each of three entry points. (b) **Behavioural:** an unmarked dispatch against a revived terminal row now returns `RESUME_DECLINED_RETAINED_CHECKPOINT` instead of restarting (`:1301`). That is a decision-path change on the unmarked path, and it is what `constraints.md` line "Absent marker = today's behaviour, byte for byte" forbids. It is item 4 of the brief, so it is intended — but the constraint was not amended. See F1. | verifier |
| A6 — a forced resume that throws or fails is reported as a failure, no silent restart | **VALIDATED** | `:1257–1327`: the decline branch is unreachable when forced and the result is returned directly. `R2` passes; **M-E** kills the native-fallback theory; **M-A** (disable the refusal branch) fails both `Arm3_*` tests. The native re-dispatch carries the flag (`:736`), so a forced run whose YAML adapter has no definition stays forced on the native leg. | verifier |
| A7 — `CanResumeFrom` has no side effect the resume path depends on | **NEVER-TESTED** | Same reason as A1 — no implementation in this repo to inspect. | verifier |
| A8 — the marker is observable in logs | **VALIDATED** | Four structured sites (`:1214`, `:1236`, `:1250`, `:1279`) plus the three entry-point Information lines. `R4_ForcedResumeIsLogged`, `A_force_the_adapter_cannot_honour_is_logged`, `A_force_with_nothing_to_resume_is_logged` all pass, and `Arm3_The_refusal_does_not_look_like_an_ordinary_failure` asserts the refusal emits a line an ordinary failure does not. | verifier |

### Prior Art

| id | status | citation | actor |
|---|---|---|---|
| `lessons.md#L-9db99c35` — excluding a status from a sweep's SELECT is enough to stop that sweep dispatching such a row | **REJECTED (re-confirmed)** | The naive claim is false again here. `Arm3` asserts `RecoverAsync` returns **0** after the refusal, and that only holds because `TryMarkCheckpointFailedAsync` genuinely puts the row **back** to `Failed` with the claim released — the SELECT's exclusion alone would not have covered the window in which the lock had already flipped the row to `Idle`. The lesson was applied; the claim it refutes stays refuted. | verifier |
| `lessons.md#L-bb0d01f6` — a terminal checkpoint row is inert | **REJECTED (re-confirmed; this cycle is the fix)** | The entire `ExecutionLockOutcome.RevivedTerminalRow` mechanism exists because taking the lock revives a `Failed` row to `Idle` by design (`CheckpointRepository.cs:694`, `InMemoryCheckpointRepository.cs:385`). Cycle 1 flagged this as the lesson recurring a third time; this cycle turned it into code. | verifier |
| `lessons.md#L-a561ce63` — the Postgres raw SQL added for retention is correct (untested: Docker) | **NEVER-TESTED, and the exposure grew** | Still untested — all 47 `CheckpointRepositoryTests` fail with `DockerUnavailableException`, and there is no local Postgres to fall back on. Cycle 1 could say "`CheckpointRepository.cs` is byte-identical to baseline, so this task neither improved nor endangered it." **That is no longer true**: this task rewrote `TryAcquireExecutionLockAsync` in that file, from one EF `ExecuteSqlAsync` into a hand-built CTE over a raw `DbCommand` with 16 typed parameters. See F3. | verifier |
| `lessons.md#L-dc61d2c4` — a feature's scope can be priced by the number of predicates it changes | **REJECTED (re-confirmed, third time)** | `task.md` priced this at three pieces. Delivered: nine production files across five rounds, including a `Domain` interface signature change, a new `Domain` type, both repository implementations, two HTTP entry points, a native-fallback propagation, a refusal branch on the *unmarked* path, and a delete-guard added in round 5 to stop the refusal destroying the row it exists to preserve. Each round found the previous one had missed a path — the drift the lesson describes, unchanged. | verifier |

---

## 3. Attention Item Disposition

I ran each named test myself against the 12:41 snapshot after a clean rebuild.

| id | final disposition | evidence |
|---|---|---|
| R1 — marker reaches an automatic sweep dispatch and overrides the 23h bound fleet-wide | **CLOSED** | `--filter "FullyQualifiedName~R1_SweepDispatchNeverForcesResume"` → **Passed: 1, Failed: 0**. Not a construction assertion: real handler + real `InMemoryCheckpointRepository` + real `CheckpointRecoveryHandler.RecoverAsync`. Reinforced structurally — I re-enumerated all 15 production `new ProcessEventCommand` sites and confirmed the sweep's is not among the four that set the flag. |
| R2 — a forced resume that fails falls through to a silent restart | **CLOSED** | `ForcedResumeOnOperatorDispatchTests` → **Passed: 9, Failed: 0**, including `R2_ForcedResumeFailureDoesNotRestart` (`ResumeAsync` Once, `ProcessAsync` **Never**, the collector's own `Status`/`ErrorCode` returned) and `The_native_fallback_re_dispatch_carries_the_force` over both flag states. Mutation **M-E** (`new(nativeEvent)` without the flag) fails the `force: True` case and nothing else. |
| R3 — an unmarked dispatch changes behaviour | **CLOSED for the mechanism, OPEN as a product question** | `R3_UnmarkedDispatchIsUnchanged` passes (`CanResumeFrom` Once, `ResumeAsync` Never, `ProcessAsync` Once), and `Arm4` proves decline-then-restart is untouched for a non-terminal row. The flag's default is inert at all 11 non-setting sites. **But** the unmarked path did change for a revived-terminal row, by design. The attention item as written is closed; the constraint it protected is not. F1. |
| R4 — the override is invisible after the fact | **CLOSED** | `R4_ForcedResumeIsLogged` passes, as do both degradation tests and `Arm3_The_refusal_does_not_look_like_an_ordinary_failure`. The override line carries `CheckpointAgeHours`, which is what answers "why was a 2-day-old checkpoint used"; the refusal line carries page and age, which answers "why did nothing run". |

### Mutations (eight, on a full copy of the snapshot)

| id | mutation | result |
|---|---|---|
| M-A | disable the refusal branch (`if (false && revivedTerminalRow)`) | **killed** — both `Arm3_*` fail |
| M-B | the naive fix: `forceResume \|\| (CanResumeFrom(…) && !revivedTerminalRow)` | **killed** — `Arm2_An_immediate_redelivery_still_resumes_a_terminal_row` fails, and only it |
| M-C | in-memory store never reports `GrantedAfterReviving` | **killed** — both `Arm3_*` fail |
| M-D | `ReadForceResume` accepts any non-`false` value | **killed** — 5 of 6 `I4_NonBooleanRootValuesDoNotForce` cases fail |
| M-E | native fallback drops the force | **killed** — `The_native_fallback_re_dispatch_carries_the_force(force: True)` fails |
| M-F | **both HTTP endpoints hard-code `forceResume = false`** | **SURVIVES** — every suite green. F2. |
| M-G | **round-5 delete-guard removed (`deleteWhenMarkMisses: true`)** | **SURVIVES** — every suite green. F2. |
| M-H | delete-guard inverted (`deleteWhenMarkMisses: false` always) | **killed** — 1 `Application.UnitTests` failure |

---

## 4. Decision drift

| decision (`decisions.md`) | state in code |
|---|---|
| The override is an explicit marker on the dispatch, not inferred from age, origin, or anything else | **HELD** for the *force*. **Partially reversed for the refusal**: `revivedTerminalRow` is an inferred signal — the handler now takes a consequential decision (fail rather than restart) from a fact about the row's prior status, not from anything the operator sent. The reason the decision gives for preferring an explicit marker — "inference is what makes an override unexplainable later" — applies to the refusal too, and is mitigated only by the Error log. |
| The marker rides `PlatformEvent.Metadata` rather than a new `PlatformEvent` field | **SUPERSEDED**, correctly, by the same file's "Design correction", which is accurate and which I verified against `ProcessEventCommandHandler.cs:134`. `constraints.md` and `state.json` step S2 have both been corrected since cycle 1 — cycle 1's F5 is **closed**. The stale bullet survives in `decisions.md` immediately above its own reversal, which is fine for a decision log. |
| The gate is bypassed wholesale rather than selectively; `CanResumeFrom` returns a bare bool for both "stale" and "damaged" | **HELD, and it is now load-bearing in a second place.** `forceResume \|\|` plumbs no reason — correct for the force. But the refusal branch inherits the same blindness: it cannot tell a decline-for-staleness (which the refusal is meant to catch) from a decline-for-damage (which it should not). F1. |
| Proceeding on unverified: no collector re-checks staleness inside `ResumeAsync` | **STILL UNVERIFIED**, correctly — nothing in this repo can answer it. |
| Proceeding on unverified: the marker cannot reach an automatic dispatch | **NOW VERIFIED.** Upgrade this entry: A3 is validated structurally, by a real round trip through the real sweep, and by three wire-placement theories. |

`execution_notes.md` checked against the diff: **no claim unsupported by the code**, and the round-5
section is candid about the limitation it did not fix. Two details are stale rather than wrong — the
round-1 W1 section still describes `ReadForceResume` as a private static in the dispatcher and as
binding a property, which rounds 2 and 4 superseded; a reader going top-down meets the old description
first. The round-5 results table reports "14 warnings"; my clean serial rebuild counts 13 distinct
code-warning lines plus 2 environment `MSB3026` retries. Immaterial either way — none is in a file
this task touched.

---

## 5. Findings

Weighted, as asked, toward what is still wrong and what is **newly** wrong.

### F1 — IMPORTANT, and new this cycle: the refusal cannot distinguish "stale" from "damaged", so it can permanently stall a run that previously recovered

The four-arm logic itself is right, and I proved the case a naive fix would break:

- forced + gate accepts → resume, gate never called ✓
- forced + gate declines → resume, gate never called ✓
- **unforced + gate accepts → resume** ✓ — the immediate-redelivery path. `Arm2` seeds a `Failed` row
  four seconds old with `canResume: true` and asserts `ResumeAsync` Once / `ProcessAsync` Never. The
  refusal sits **after** the resume `if` (`:1301` vs `:1257`), so it is structurally unreachable
  whenever the gate accepts. Mutation **M-B** — the plausible wrong fix, refusing on "terminal" alone
  — kills `Arm2` and nothing else. The common path is safe.
- unforced + gate declines + revived terminal → **fail** `RESUME_DECLINED_RETAINED_CHECKPOINT`
- unforced + gate declines + not revived → restart, unchanged ✓ (`Arm4`)

The problem is inside the fourth arm. `decisions.md` records that `CanResumeFrom` returns a bare bool
for **both** "the checkpoint is stale" and "the checkpoint is damaged", and that plumbing the reason
out was judged not worth it. That judgement was made for the *force* path, where a damaged blob
failing fast is an acceptable outcome for a deliberately-pressed button. The refusal branch inherits
it in a context where it is not acceptable:

- decline **for staleness** on a retained row → refusing is right; the state is intact and a later
  forced dispatch will use it.
- decline **for damage / schema mismatch / cursor invalidity** on a retained row → refusing is wrong.
  The state is unusable. Under the baseline the run restarted and could complete. It now fails, and a
  forced re-dispatch will hit the same damaged state and fail too. The run is stuck until someone
  deletes the row by hand or the retention sweep does it seven days later.

The platform cannot see the difference either: `PublishCompletionEventAsync` puts
`status = "failed"` on the `AdapterDoneMessage` (`:2270`) and `AdapterDoneMessage` carries no error
code, so the distinguishing information is log-only.

This also makes A5 false and contradicts `constraints.md` ("Absent marker = today's behaviour, byte
for byte") and the contract's own Success Criterion 2. It is item 4 of the brief, so it is a decision
someone made deliberately — but no document was amended to record that the constraint was relaxed,
and the failure mode above was not, as far as I can see from `execution_notes.md`, weighed.

**Not a blocker to my mind, but it is an operator's call, not a verifier's.** The cheap mitigation if
the risk is unwanted: gate the refusal on checkpoint age as well as prior status — refuse only when
the row is older than the collector's own window, which is exactly the population the retention change
exists to protect, and let a fresh damaged row restart as it always did.

### F2 — IMPORTANT: three production paths added in the last two rounds have no test at all

Both survive mutation, which is the definition of untested.

**M-F — the two HTTP trigger-flow endpoints.** Replacing
`bool forceResume = AdapterRunMessage.ReadForceResume(jsonMessage);` with `= false;` at
`EventsController.cs:601` and `:783` leaves `Application.UnitTests` 114/114, the checkpoint classes
32/32, and every other suite green. There is no test anywhere that exercises `EventsController` —
`grep -rl "EventsController" src/` returns exactly one file, the controller itself. Item 2 of the
brief landed and is correct by reading (both sites use the same shared `AdapterRunMessage.ReadForceResume`,
so the two parsers cannot diverge — that part of the fix is real), but nothing stops a later change
from silently dropping it again.

**M-G — the round-5 delete-guard.** `FlushAndCleanupCheckpointAsync` passes
`deleteWhenMarkMisses: result.ErrorCode != ResumeDeclinedRetainedCheckpointCode` (`:1352`), and
`TryMarkCheckpointFailedAsync` returns early on a missed mark when that is false (`:1119`). This is
the fix that stops the refusal path from *deleting* the very row it exists to preserve, when
`MarkFailedAsync`'s owner guard misses. Hard-coding `true` — i.e. reintroducing the bug — passes every
suite. The inverse (**M-H**, always `false`) is killed by one `Application.UnitTests` test, so the
ordinary delete-fallback is covered and only the new guard is not.

A minor consequence of the same design worth one line: the guard keys on an error-code **string**
compared against `result.ErrorCode`, which travels on a public result type an out-of-repo adapter
populates. An adapter that returned `"RESUME_DECLINED_RETAINED_CHECKPOINT"` for its own reasons would
change ISB's cleanup behaviour. Contrived, but it is a string comparison across a package boundary.

### F3 — IMPORTANT: the Postgres execution lock was rewritten and has zero runtime verification

`TryAcquireExecutionLockAsync` went from a single EF `ExecuteSqlAsync` to a hand-built statement over
a raw `DbCommand`:

```sql
WITH prior AS (SELECT status FROM adapter_checkpoints WHERE …),
     attempted AS (INSERT … ON CONFLICT … DO UPDATE SET … WHERE … RETURNING 1)
SELECT (SELECT count(*) FROM attempted) AS acquired,
       (SELECT status FROM prior)       AS prior_status
```

with 16 explicitly-typed parameters and manual connection open/close. Its two dedicated assertions
(`CheckpointRepositoryTests.cs:399` and `:505`, both tightened this cycle to
`.Should().Be(ExecutionLockOutcome.GrantedAfterReviving)`) are Testcontainers-gated and **did not
run**. Docker is down; there is no local Postgres and no `psql`. The only store exercised in every
green run above is `InMemoryCheckpointRepository`, which by construction always grants and which is
not the production store.

I checked the semantics by reading and found nothing wrong: a data-modifying CTE is executed exactly
once regardless of whether the primary query reads its output, so `attempted` always runs; all CTEs
see the same statement snapshot, so `prior` genuinely reports the pre-INSERT status (and the comment
in the file correctly identifies the peer-commit race and correctly argues it errs safe);
`count(*)` is `bigint` so `GetInt64(0)` is right; `nameof(CheckpointStatus.Failed)` matches the
`'Failed'` literal in the `ON CONFLICT` guard; the unique key makes the scalar subquery single-row.
**That is a reading, not evidence.** This statement is the sole production source of
`RevivedTerminalRow`, and the entire refusal arm hangs off it.

Secondary, low: the raw-ADO path bypasses `EnableRetryOnFailure(5)` and `CommandTimeout(30)`
configured at `Infrastructure.Postgres/DependencyInjection.cs:36-37`, since those apply to
EF-executed commands and no `CommandTimeout` is set on the hand-made command. `UpsertAsync` at
`CheckpointRepository.cs:129` already uses this exact pattern in the pass-2 baseline, so this is
consistent with the file rather than novel — worth knowing, not worth changing here.

### F4 — the refusal does re-mark the row terminal; it *resets* the retention clock rather than restoring it

Answering the brief's question directly, and it is good news with a caveat.

It genuinely re-marks the row terminal. Trace: the failure returns to `Handle`, is not
`PartialWaitRequired`/`Skipped`, reaches `CompleteExecutionAsync` → `FlushAndCleanupCheckpointAsync`
→ `IsReportedFailure` (true: `AdapterResultStatus.Failure`) → `TryMarkCheckpointFailedAsync` →
`MarkFailedAsync`, whose owner guard this execution satisfies because it holds the claim it just
took. `Arm3` proves the outcome rather than the path: the row survives with `CurrentPage` and
`ProcessedItems` intact, `ClaimedByInstance` is null, and the real sweep's `RecoverAsync` returns 0 —
which it only can if the row is `Failed` again. **It does not strand a non-terminal row.**

The caveat: `MarkFailedAsync` sets `updated_at_utc = now` (`CheckpointRepository.cs:479`), and
`DeleteTerminalExpiredAsync` filters on `Status == Failed && UpdatedAtUtc < cutoff` (`:508`). So the
row gets a **fresh** seven days, not the remainder of its original window. Over-retention, not
under — harmless for correctness, but "restoring the retention clock" is not quite what happens and
a row that keeps receiving unmarked dispatches never ages out.

### F5 — it cannot loop

Confirmed by reading both ends. `AdapterResult.FailureResult(msg, code, null, false)` with
`isTransient: false` produces `Status = AdapterResultStatus.Failure, IsTransient = false`
(`Cymulate.Integration.Client/Models/AdapterResult.cs:117`). `IsbPlatformEventDispatcher` returns
`MessageOutcome.Retry` only on `result.IsTransient`, and otherwise
`MessageOutcome.Ack` — "Non-transient trigger flow failure, acknowledging without retry". No
redelivery, no poison loop. The `MarkFailedAsync` that follows releases the claim, so a *new*
dispatch on the same correlation id can still take the lock; it would simply refuse again, which is
the intended steady state.

### F6 — the process-local revival flag, disclosed and unfixed

`execution_notes.md` round 5 states this itself and I confirmed the shape: the `Failed → Idle` flip
happens inside the lock statement and is committed before the handler sees the answer, while
`RevivedTerminalRow` lives only on this process's stack. Any exit from `Handle` other than a reported
failure — crash, eviction, cancellation, claim loss detected after the lock — leaves the row `Idle`,
silently downgraded from the 7-day terminal clock to the ordinary TTL, with the retained state
unconsumed and no record. The notes lay out both candidate fixes and why neither belongs in this
task. I agree with leaving it; it is on the open list because it is a real hole in the retention
guarantee this whole branch exists to provide.

### On the tests themselves

They drive production paths, not object construction. `ForcedResumeOnOperatorDispatchTests` builds
the real handler and calls `Handle`. `ForceResumeWireContractTests` builds the real
`IsbPlatformEventDispatcher` over a real `TriggerFlowMapper` and captures the real command off a
mediator — and its `I4_NonBooleanRootValuesDoNotForce` asserts a command was dispatched *at all*,
which is the assertion that would have caught cycle 1's F1. `SweepDispatchNeverForcesResumeTests` and
`RetainedCheckpointIsNotConsumedTests` add the real `InMemoryCheckpointRepository` and, for R1, the
real `CheckpointRecoveryHandler`. Every resume test arranges `CanResumeFrom → false`, so no pass is
explained by a collector that would have consented anyway. `Arm3_The_refusal_does_not_look_like_an_ordinary_failure`
builds a second fixture and differences the two log streams rather than pattern-matching a string,
which is the right shape. I found no vacuous assertion. Six of my eight mutations were caught; the
two that were not are F2.

---

## 6. Verdict

**PASS WITH GAPS.**

Proven correct this cycle: the forced dispatch reaches `ResumeAsync` without consulting the gate; the
force survives the native-collector re-dispatch; both HTTP entry points read the same shared reader,
so the two parsers cannot diverge; degraded forces are logged at both early returns; a malformed
`forceResume` of any shape is now inert rather than fatal, because the bound property is gone
entirely; the four-arm decline logic is right in all four combinations and the immediate-redelivery
path is explicitly protected by a test that the plausible wrong fix kills; the refusal genuinely
re-marks the row terminal, releases the claim, and is invisible to the sweep afterwards; it settles
`Ack` and cannot loop; the lock's return-type change has exactly one production caller; the marker is
structurally incapable of reaching an automatic sweep dispatch; build clean; 114/114, 449/449, 32/32,
and no new failure in the full `API.UnitTests` run.

Nothing is newly broken in the sense of a defect I can demonstrate. The pattern the brief asked me to
look for — each pass introducing a defect while fixing one — did **not** repeat as a bug this time. It
repeated as **scope**: three production files that were not in the contract's file set, and two
untested branches.

Open for the operator:

1. **F1 — the refusal cannot tell "stale" from "damaged".** A revived terminal row whose collector
   declines for a non-staleness reason now fails permanently instead of restarting, and a force will
   not rescue it. Contradicts `constraints.md` and Success Criterion 2, which were not amended.
   Decide whether to accept it, or gate the refusal on checkpoint age as well as prior status.
2. **F2 — two untested production paths**, both proven untested by surviving mutation: the HTTP
   endpoints (M-F) and the round-5 delete-guard (M-G).
3. **F3 — the Postgres `TryAcquireExecutionLockAsync` rewrite is runtime-unverified.** Its two
   dedicated assertions are Docker-gated and did not run. This is the sole production source of the
   signal the whole refusal arm depends on. It needs `CheckpointRepositoryTests` green before
   shipping.
4. **F6 — `RevivedTerminalRow` is process-local while the revival is durable.** Disclosed in
   `execution_notes.md`, unfixed by design, leaves a silent retention downgrade on any non-failure
   exit.
5. **A1 / A7 NEVER-TESTED** — no `IResumableAdapter` implementation exists in this repo, so "nothing
   re-checks staleness inside `ResumeAsync`" remains inference. `decisions.md` accepts this knowingly.
6. **Docker unavailable** — 136 container-gated tests did not run, including all 47 Postgres
   `CheckpointRepositoryTests`. The pass-2 retention SQL stays runtime-unverified too. Not this
   task's doing, but F3 means this task now depends on it.
7. **The tree moved during this verification**, at 12:37. Freeze it and re-check the hashes in §0
   before merging.
