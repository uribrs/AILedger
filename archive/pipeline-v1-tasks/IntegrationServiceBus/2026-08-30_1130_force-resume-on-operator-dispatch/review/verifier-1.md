# Verifier 1 — force the resume path on an operator-initiated dispatch

**Verdict: FAIL — one blocking production defect, everything else verified.**

The override mechanism itself is correct, structurally sound, and every one of its tests survives
mutation. It fails on a Success Criterion it cannot currently meet: a non-boolean `forceResume` at the
message root now **poisons the entire trigger-flow message** and puts the delivery into a Retry loop, so
a run that would have executed fine — merely unforced — never executes at all. Seven tests are red.
Section 5, F1.

## 0. Snapshot, and a caution about it

**The working tree moved twice while I was verifying it**, and was still being edited when I stopped.
Everything below is measured against this snapshot:

```
2026-08-30 12:14:27
af5df81979a9  AdapterRunMessage.cs          e72699e1694c  ProcessEventCommand.cs
6c26e58c5995  ProcessEventCommandHandler.cs 256e5e881c28  IsbPlatformEventDispatcher.cs
2b94827eac92  EventsController.cs
```

My first pass verified an earlier state (11:44) in which `ReadForceResume` was private to the
dispatcher, the HTTP endpoints ignored the marker, and `Application.UnitTests` was 98/98 green. A
round-2 pass (12:04–12:08) moved the reader onto the DTO, wired both HTTP endpoints, propagated the
flag through the native fallback, added two degradation logs, and added `ForceResumeWireContractTests`.
A round-4 file, `RetainedCheckpointIsNotConsumedTests.cs`, appeared at 12:13 with two failing arms
describing behaviour that is **not implemented** (F2).

**And a fifth round started while this report was being written.** By 12:16 the tree had moved again,
now beyond the contract's file set entirely: `ProcessEventCommandHandler.cs`, `ICheckpointRepository.cs`,
`InMemoryCheckpointRepository.cs`, `Infrastructure.Postgres/CheckpointRepository.cs`, and a new
`Domain/Models/ExecutionLockOutcome.cs` — i.e. the execution-lock/repository layer, apparently to make
F2's Arm 3 pass. That is a new persistence-layer contract on a path whose tests **cannot run without
Docker**, and it is not something this verification covers in any respect.

**Everything in this report describes the 12:14 snapshot and nothing after it. Re-verify against a
frozen tree before merging.** I stopped chasing rather than report on a moving target.

### Delta isolation

`state.json.baseRef` (`d53e347d`) is the wrong comparison. I rebuilt the pass-2 tree
(`git archive d53e347d` + `scratchpad/pass2-terminal-status-baseline.patch`, applied clean) and added
the three pass-2 files the patch could not carry because they were untracked
(`FailedRunLeavesATerminalRowTests.cs`, `CheckpointCleanupJobTests.cs`,
`InMemoryCheckpointRepositoryTerminalStatusTests.cs`). `diff -rq` over `src/` then gives this task's
delta exactly:

| file | kind |
|---|---|
| `Domain/…/Messaging/AdapterRunMessage.cs` | modified — marker on both root records + `public static ReadForceResume` |
| `Application/Commands/ProcessEventCommand.cs` | modified — `ForceResume { get; init; }` |
| `Application/Commands/ProcessEventCommandHandler.cs` | modified — gate skip, native-fallback propagation, three logs |
| `Application/Messaging/IsbPlatformEventDispatcher.cs` | modified — reads and sets the flag |
| `Hosts/…/Controllers/EventsController.cs` | modified — both HTTP trigger-flow paths (round 2) |
| `…Application.UnitTests/Commands/ForcedResumeOnOperatorDispatchTests.cs` | added |
| `…Application.UnitTests/Messaging/ForceResumeWireContractTests.cs` | added |
| `…API.UnitTests/Checkpoints/SweepDispatchNeverForcesResumeTests.cs` | added |
| `…API.UnitTests/Checkpoints/RetainedCheckpointIsNotConsumedTests.cs` | added (12:13, 2 arms failing) |

`CheckpointRecoveryHandler.cs`, `TriggerFlowMapper.cs`, both checkpoint repositories and every
pre-existing test file are byte-identical to the pass-2 baseline. `PlatformEvent` is not in this repo.

Beyond reading, I ran: the named tests; two out-of-repo harnesses (`scratchpad/fr`, `scratchpad/reg`)
replaying the real DTO sources; and **six mutations** on a pristine copy of the 12:14 tree
(`scratchpad/mutant`) to prove each test kills what it claims to. The repository itself was not
modified.

---

## 1. Success Criteria coverage

| # | criterion | verdict | evidence |
|---|---|---|---|
| 1 | Marker + checkpoint row → `ResumeAsync` without consulting `CanResumeFrom` | **MET** | `ProcessEventCommandHandler.cs:1232` — `if (forceResume \|\| resumable.CanResumeFrom(adapterCheckpoint))`. `\|\|` short-circuits. `A_forced_resume_never_consults_the_collector_gate` asserts `CanResumeFrom` `Times.Never` against a collector arranged to answer **false**. **M2** (reorder to `CanResumeFrom(…) \|\| forceResume`) fails that test and only that test. |
| 2 | Unmarked dispatch behaves exactly as before, decline-then-restart included | **MET behaviourally; see A5** | `R3_UnmarkedDispatchIsUnchanged` passes: `CanResumeFrom` Once, `ResumeAsync` Never, `ProcessAsync` Once, decline line at `:1271`. Baseline suites unchanged (§ Runs). Deviations: three log templates gained a `ForceResume` placeholder and one extra body parse per trigger-flow message. |
| 3 | Marker cannot reach an automatic sweep dispatch — **demonstrated** | **MET** | Structural: `ForceResume` lives on `ProcessEventCommand` (`:24`), not on `PlatformEvent`; `ProcessEventCommandHandler.cs:134` serializes **only** `platformEvent`; `CheckpointRecoveryHandler.cs:249/390` rehydrates only that and sets only `CapacityPreAcquired`. Behavioural: `R1_SweepDispatchNeverForcesResume` drives a real forced dispatch, orphans the leg, then runs the **real** `CheckpointRecoveryHandler.RecoverAsync` and inspects the command it dispatches. **M1** (sweep sets `ForceResume = true`) fails it. Reinforced by `I4_FlagOutsideTheRootIsIgnored` ×3, which proves the flag is not read from `payload`, `payload.action`, or `payload.metadata` — the three places that *are* persisted. |
| 4 | Marker visible in logs when it causes an override | **MET, and extended** | Three Warning lines, all naming the correlation id: the override itself (`:1226`, with `CheckpointAgeHours`), and two round-2 additions covering the cases where a force *cannot* be honoured — no resumable adapter (`:1193`) and no checkpoint row (`:1213`). **M6** (delete all three blocks) fails `R4_ForcedResumeIsLogged`, `A_force_the_adapter_cannot_honour_is_logged` and `A_force_with_nothing_to_resume_is_logged`. |
| 5 | A forced resume that fails is reported as a failure; no silent restart | **MET** | The `ProcessAsync` fall-through (`:1273`) sits after the `if` block and is unreachable when forced. `ResumeAsync`'s result is returned directly; the `finally` is telemetry that swallows its own exceptions. `R2_ForcedResumeFailureDoesNotRestart` passes; **M4** (fall through on `!Success`) fails it. Round 2 additionally closed the native-fallback leak: `:293` now passes `request.ForceResume` into `TryEnqueueNativeFallbackAsync` and `:730` sets it on the re-queued command. **M3** (drop it there) fails `The_native_fallback_re_dispatch_carries_the_force(force: True)`. |
| 6 | `AdapterRunMessage` carries the marker; `PlatformEvent` unchanged | **MET** | Root-level `[JsonPropertyName("forceResume")] public bool ForceResume` on `AdapterRunMessage:48` and `AdapterRunMessageFlat:184`. `PlatformEvent` ships in `Cymulate.Integration.Client` and is absent from the delta. |
| 7 | Tests cover: gate bypass; unmarked unchanged; sweep cannot inherit; failing forced resume does not restart | **MET** | 21 tests added across two rounds. The wire→command hop that round 1 left open is now covered by `ForceResumeWireContractTests`, which drives the real `IsbPlatformEventDispatcher.DispatchAsync` over a real `TriggerFlowMapper` and captures the real command. |
| 8 | **Solution builds; affected test projects pass**, Docker-gated reported as not-run | **NOT MET** | Build is clean (0 errors, **14 warnings** — identical to the pass-2 baseline's 14, verified by `--no-incremental` on both trees). But `Application.UnitTests` is **109 passed / 5 failed** and `API.UnitTests` carries 2 further non-container failures. See F1 and F2. |
| 9 | No migration, no new column, nothing outside this repo | **MET** | Five source files and four test files, all in this repo. No repository, entity, or SQL file changed. |

### Runs (mine, against the 12:14 snapshot)

| suite | pass-2 baseline | now | note |
|---|---|---|---|
| `Application.UnitTests` | **93 / 93 pass** | **109 pass / 5 FAIL / 114** | the 5 are `I4_NonBooleanRootValuesDoNotForce` — F1 |
| `Infrastructure.Core.UnitTests` | **449 / 449 pass** | **449 / 449 pass** | unchanged |
| `API.UnitTests` non-container checkpoint classes | **29 / 29 pass** | **30 / 30 pass** | +`R1_SweepDispatchNeverForcesResume` |
| `API.UnitTests` `Messaging` | — | **89 / 89 pass** | the mapper's own suite never exercises a bad `forceResume` value, which is why F1 survived it |
| `API.UnitTests` whole project | — | **633 pass / 141 fail / 774** | 136 `DockerUnavailableException`; 3 pre-existing `TestAdapterConnectionCommandHandlerTests`; **2 new** `RetainedCheckpointIsNotConsumedTests.Arm3_*` — F2 |

The brief's stated non-container baseline of "25 (26 after)" is low; the branch carries 29 before and
30 after. `execution_notes.md`'s 29→30 is correct.

**Pre-existing failures, confirmed by run rather than inference:** I built and ran the pass-2 baseline
tree and reproduced `TestAdapterConnectionCommandHandlerTests` 3 fail / 16 pass there. `execution_notes.md`
reached the same conclusion by argument because it could not stash; this is the harder evidence.

**Docker gate:** Docker Desktop is not running. 136 Testcontainers tests are **not run, not green**,
including all 47 Postgres `CheckpointRepositoryTests`, so the pass-2 retention SQL stays runtime-unverified.
Stated, not counted against this task, which added no SQL.

---

## 2. Assumption Disposition

| id | status | citation | actor |
|---|---|---|---|
| A1 — no collector re-checks staleness inside `ResumeAsync` or its load path | **NEVER-TESTED** | No `IResumableAdapter` implementation exists in this repo. `grep -rn "CanResumeFrom" src/` returns only `ProcessEventCommandHandler.cs:1232`, test doubles, and `ScheduledContinuationFixture.cs:377`. Falcon/CloudGuard live elsewhere. `decisions.md` records this as proceeding-on-unverified. | verifier |
| A2 — `PlatformEvent.Metadata` survives from `TriggerFlowMapper` to `ExecuteWithResumeAsync` | **NEVER-TESTED (superseded)** | The design correction removed `Metadata` from the route; `TriggerFlowMapper.cs` is byte-identical to baseline. The live equivalent — does the *root* marker survive both wire formats — is VALIDATED under A4. | verifier |
| A3 — a marker on an operator dispatch cannot reach an automatic sweep dispatch | **VALIDATED** | `ProcessEventCommandHandler.cs:134` serializes only `platformEvent`; `CheckpointRecoveryHandler.cs:249/390` rehydrates only that. `R1_SweepDispatchNeverForcesResume` passes; **M1** kills it. `I4_FlagOutsideTheRootIsIgnored` ×3 closes the second route (a marker under `payload`, which *is* persisted, is not read). | verifier |
| A4 — `AdapterRunMessage` is ISB-owned; a new optional field breaks no producer or consumer | **REJECTED** | The type is only ever deserialized in production, and an *absent* field is harmless. But a field of the wrong JSON type is now fatal where it was previously ignored: harness `scratchpad/reg` runs `"forceResume": "true" / null / 1` through the **baseline** `AdapterRunMessage` → parses fine, three times; the same bodies through the current type throw `JsonException` inside `TriggerFlowMapper` and the delivery settles Retry. Adding the field **did** break a producer contract. See F1. | verifier |
| A5 — with the marker absent, every existing path behaves byte-for-byte as today | **REJECTED as literally stated** | Three log templates gained `, ForceResume: {ForceResume}` (`IsbPlatformEventDispatcher.cs:208`, `EventsController.cs` ×2, plus the fallback line at `:740`), and one extra full-body deserialize runs per trigger-flow message (`IsbPlatformEventDispatcher.cs:200`, `EventsController.cs:601/783`). No decision path changed and all 93 pre-existing `Application.UnitTests` and 29 checkpoint tests still pass. Behaviourally inert; recorded rather than softened. | verifier |
| A6 — a forced resume that throws or fails is reported as a failure, no silent restart | **VALIDATED** | `:1232–1273` — decline branch unreachable when forced; result returned directly. `R2` passes, **M4** kills it. Round 2 also closed the native-fallback restart (`:293`/`:729`), which was a real instance of exactly this failure mode that round 1 had missed; **M3** kills its test. | verifier |
| A7 — `CanResumeFrom` has no side effect the resume path depends on | **NEVER-TESTED** | Same reason as A1 — no implementation in this repo to inspect. | verifier |
| A8 — the marker is observable in logs | **VALIDATED** | Three Warning lines at `ProcessEventCommandHandler.cs:1193/1213/1226`, plus Information on all three dispatch entry points. `R4` and both degradation tests pass; **M6** kills all three. | verifier |

### Prior Art

| id | status | citation | actor |
|---|---|---|---|
| `lessons.md#L-9db99c35` — excluding a status from a sweep's SELECT is enough to stop the dispatch (refuted: guard the claim) | **NEVER-TESTED** | Not re-encountered: this change adds no sweep predicate. R1 is enforced by type shape — the flag is not a member of the serialized type — not by any select/claim guard. | verifier |
| `lessons.md#L-bb0d01f6` — a terminal checkpoint row is inert (refuted: the lock revives it) | **REJECTED (recurred)** | The 12:13 `RetainedCheckpointIsNotConsumedTests` is this exact lesson resurfacing a third time: the execution lock revives a retained `Failed` row to `Idle`, the collector declines the stale checkpoint, and the run restarts having consumed the retained state. Its Arm 3 is red because the behaviour it asserts does not exist. See F2. | verifier |
| `lessons.md#L-a561ce63` — the Postgres raw SQL added for retention is correct (untested: Docker) | **NEVER-TESTED** | Still untested. All 47 `CheckpointRepositoryTests` fail with `DockerUnavailableException`. `CheckpointRepository.cs` is byte-identical to baseline, so this task neither improved nor endangered it. | verifier |
| `lessons.md#L-dc61d2c4` — a feature's scope can be priced by the number of predicates it changes (drifted) | **REJECTED (recurred, twice)** | `task.md` priced this at three pieces. Delivered: five production files across four rounds, a route change decided before any code, a shared DTO reader, two HTTP entry points, a native-fallback propagation, three log sites, and a still-open defect. Each round found the previous one had missed a path. This is the same drift the lesson describes. | verifier |

---

## 3. Attention Item Disposition

| id | final disposition | evidence |
|---|---|---|
| R1 — marker reaches an automatic sweep dispatch and overrides the 23h bound fleet-wide | **CLOSED** | `--filter "FullyQualifiedName~R1_SweepDispatchNeverForcesResume"` → **Passed: 1, Failed: 0**. Not a construction assertion: real handler + real `InMemoryCheckpointRepository` + real `CheckpointRecoveryHandler.RecoverAsync`, with guards against vacuity (`ResumeAsync` Times.Once, `PlatformEventJson` non-empty, `count == 1`). **M1** — `CheckpointRecoveryHandler.cs:390` set to `{ CapacityPreAcquired = true, ForceResume = true }` — fails it: *"Expected swept.ForceResume to be False … but found True"*. |
| R2 — a forced resume that fails falls through to a silent restart | **CLOSED** | `R2_ForcedResumeFailureDoesNotRestart` passes (part of **9/9** in `ForcedResumeOnOperatorDispatchTests`): collector's own `Status` and `ErrorCode` returned, `ResumeAsync` Once, `ProcessAsync` **Never**. **M4** fails it. The related native-fallback restart found in round 2 is closed and covered by a Theory over both flag states; **M3** fails its `force: True` case. |
| R3 — an unmarked dispatch changes behaviour | **CLOSED** | `R3_UnmarkedDispatchIsUnchanged` passes. 15 production `ProcessEventCommand` construction sites (recounted at the snapshot; round 2 changed sites, it did not add any); four now set the flag — `IsbPlatformEventDispatcher.cs:228`, `EventsController.cs:611/785`, `ProcessEventCommandHandler.cs:730` — and each is on the same logical operator run. The sweep's own site (`CheckpointRecoveryHandler.cs:390`) and the rest are byte-identical to baseline. **M5** — `ForceResume { get; init; } = true` — fails `R3`, `R4`, `A_non_trigger_flow_body_carrying_the_flag_does_not_force`, the five wire theories, and the pre-existing `Handle_starts_fresh_when_the_adapter_declines_the_checkpoint`. |
| R4 — the override is invisible after the fact | **CLOSED, and better than asked** | `R4_ForcedResumeIsLogged` passes. Its `NotEmpty`-on-a-differential shape is weaker than an exact match, so I checked it directly: **M6** (delete the three log blocks) fails it plus both round-2 degradation tests. The override line carries `CheckpointAgeHours`, which is what answers "why was a 2-day-old checkpoint used"; the two degradation lines answer "why did a forced run recollect anyway", which the contract did not require and which matters more given ISB reports no resumability upstream. |

All six mutations were caught. I found no vacuous assertion in any of the four new test files.

---

## 4. Decision drift

| decision (`decisions.md`) | state in code |
|---|---|
| The override is an explicit marker on the dispatch, not inferred | **HELD.** `ExecuteWithResumeAsync` reads only the boolean; nothing infers from age or origin. |
| The marker rides `PlatformEvent.Metadata` rather than a new `PlatformEvent` field | **SUPERSEDED**, correctly, by the same file's "Design correction". The reasoning cited (`ProcessEventCommandHandler.cs:134`) is accurate and I verified it. **But `constraints.md` still reads "Do not add a field to `PlatformEvent` … Use its existing free-form `Metadata` dictionary", and `state.json` step S2 still reads "TriggerFlowMapper carries it onto PlatformEvent.Metadata".** Both now instruct the one thing this change must not do. See F5. |
| The gate is bypassed wholesale rather than selectively | **HELD.** `forceResume \|\|` — no reason plumbed. |
| Proceeding on unverified: no collector re-checks staleness inside `ResumeAsync` | **STILL UNVERIFIED**, correctly — nothing in this repo can answer it. The stated fallback (fail loudly rather than restart) is what the code does. |
| Proceeding on unverified: the marker cannot reach an automatic dispatch | **NOW VERIFIED.** Upgrade this entry: A3 is validated structurally, by a mutation-killed round trip, and by three wire-placement theories. |

`execution_notes.md` checked against the diff: I found **no claim unsupported by the code**, and the
notes are candid where it counts — they report the F1 defect themselves, state the five theory cases are
left failing on purpose, and flag the HTTP hop as uncovered. Two details are now stale rather than wrong:
the round-1 W1 section still describes `ReadForceResume` as "a private static in the dispatcher" whose
"options mirror the mapper's (`PropertyNameCaseInsensitive`, camelCase)", which round 2 superseded
(it is now `AdapterRunMessage.ReadForceResume`, case-insensitive only). The round-2 section says so; a
reader of the file top-down meets the old description first. The round-1 W2 results table (98/98) is
likewise superseded by the round-2 table (109/5).

---

## 5. Findings

### F1 — BLOCKING: a non-boolean `forceResume` poisons the whole message and loops on Retry

`AdapterRunMessage.ForceResume` is a non-nullable `bool`. `AdapterRunMessage.ReadForceResume`
(`AdapterRunMessage.cs:54`) handles a bad value correctly — it catches `JsonException` and returns
`false`. But `TriggerFlowMapper.TryParseToPlatformEvent` deserializes the **same type** at
`TriggerFlowMapper.cs:143` and has no such tolerance. A root `forceResume` that is not a JSON boolean
now throws there, the mapper returns its `catch (JsonException)` at `:171`, and the dispatcher settles
the delivery as:

```
MessageOutcome { Disposition = Retry,
  Error = JSON parsing error: The JSON value could not be converted to System.Boolean.
          Path: $.forceResume | LineNumber: 4 | BytePositionInLine: 21. }
```

No command is dispatched. Retry means redelivery, which fails identically — a poison-message loop on a
collection run that would otherwise have executed fine, merely unforced. The HTTP endpoints reject the
body outright for the same reason.

**This is new, and I proved it rather than inferred it.** Harness `scratchpad/reg` runs
`"forceResume": "true"`, `null`, and `1` through the **pass-2 baseline** `AdapterRunMessage`: all three
parse cleanly (`BASELINE parse OK, flows=1` ×3), because an unrecognised root key is ignored by
`System.Text.Json`. Against the current type they throw.

Values affected: `"true"` (a string — the easy mistake for a producer templating a form value), `null`
(very plausible from a backend that always emits the field), `1`, `[true]`, `{"value":true}`. `false`
and absent are fine.

`ForceResumeWireContractTests.I4_NonBooleanRootValuesDoNotForce` catches all five and is **left failing
on purpose** — `execution_notes.md` says so explicitly and hands the fix to W1. The test is right and the
production code is wrong. Two fixes are named there; the cheaper one is `bool?` on both root records,
since `ReadForceResume` already coalesces null to false and the mapper does not read the property at all.
Note that `AdapterRunMessageFlat.ForceResume` needs the same treatment even though `ReadForceResume` never
binds it — the mapper deserializes the flat type at `:156`.

### F2 — two further red tests describe behaviour that does not exist (round 4, in flight)

`RetainedCheckpointIsNotConsumedTests` (added 12:13) has four arms; Arms 1, 2 and 4 pass, and
**`Arm3_A_declined_retained_checkpoint_fails_instead_of_restarting` and
`Arm3_The_refusal_does_not_look_like_an_ordinary_failure` fail.** They assert that an *unforced* dispatch
against a retained `Failed` row must fail rather than consume the row and restart — which the handler does
not do, and which is a behaviour change to the unmarked path that `constraints.md` forbids
("Absent marker = today's behaviour"). This is prior-art lesson `L-bb0d01f6` recurring: the execution lock
revives the terminal row, the collector declines the stale checkpoint, and the retained state is spent for
nothing.

Whatever the merits, it is **new scope arriving after the contract's Stop Conditions were met**, and it is
red. It needs either an explicit scope decision or reverting out of this task. I am flagging it, not
adjudicating it.

### F3 — round 2 fixed two real gaps I had raised, and fixed them well

Recording so the team lead can see these are closed rather than dropped:

- **HTTP entry points.** `EventsController.cs:601/611` (`/events/publish/sync`) and `:783/785`
  (`/events/publish`) now read the same root field via the same shared reader. I independently confirmed
  the coverage claim: `TriggerFlowMapper.IsTriggerFlowMessage` has exactly three real call sites
  (`IsbPlatformEventDispatcher.cs:42`, `EventsController.cs:80/303`) plus the unused `IsAdapterRunMessage`
  alias, and `KafkaConsumerService`/`DotNetEventConsumerService` contain no trigger-flow handling at all.
  No entry point silently drops the field.
- **Native fallback.** `:293`/`:730` now carry the flag onto the re-dispatched native run. My round-1
  analysis had concluded dropping it was harmless because the cloned event switches `ProductType` and so
  finds no checkpoint row — that reasoning holds for the *first* leg, but the re-dispatch is the same
  logical run and a native row can exist from an earlier leg under the same correlation id. Propagating
  it is the right call.

### F4 — `ReadForceResume`'s swallowed `JsonException` is aligned with the mapper, by construction

Worth recording because it is the one part of the parse story that is *not* broken. `ReadForceResume`
deserializes only the **nested** `AdapterRunMessage`; could a flat-format body throw there and lose a
valid root marker? No. `TriggerFlowMapper.TryParseToPlatformEvent` also deserializes the nested type
**first**, inside the same `try` (`:143`), and reaches the flat type only when that succeeded but produced
no `Payload.Action`. Any body on which the nested parse throws is rejected before a dispatch happens.
Harness `scratchpad/fr` confirms empirically: a flat body with a root marker reads `true`; a flat body
without reads `false`; unknown members are ignored, so the flat payload's `credentials`/`flows`/`filter`
pass harmlessly through the nested `AdapterRunPayload`. Dropping `PropertyNamingPolicy = CamelCase` from
the reader's options in round 2 changes nothing — every property on these records carries an explicit
`[JsonPropertyName]`.

`delivery.Body` cannot be null or blank on this path (`IsTriggerFlowMessage` at `TriggerFlowMapper.cs:50`
rejects that first), and the round-2 reader adds its own `IsNullOrWhiteSpace` guard anyway.

### F5 — `constraints.md` and `state.json` still mandate the abandoned route (documentation)

`constraints.md` line 3 still says to use `PlatformEvent.Metadata`; `state.json` step S2 still says
"TriggerFlowMapper carries it onto PlatformEvent.Metadata". Both are the design `decisions.md` reversed,
for a reason it states correctly. Left as-is, the next reader of the task folder finds two documents
telling them to do the one thing this change must not do. `state.json` also still reports every step
`pending`, `verification.status: not_started`, and `verifierRun: false`, and its `baseRef` misleads on
diff scoping — the `baseRefNote` is the only thing that saves it.

### On the tests themselves

They drive production paths, not object construction. `ForcedResumeOnOperatorDispatchTests` builds the
real handler with fourteen real constructor arguments and calls `Handle`.
`ForceResumeWireContractTests` builds the real `IsbPlatformEventDispatcher` over a real `TriggerFlowMapper`
and captures the real command off a mediator. `SweepDispatchNeverForcesResumeTests` adds the real
`InMemoryCheckpointRepository`, the real `AdapterExecutionContext` and the real `CheckpointRecoveryHandler`.
Every resume test arranges `CanResumeFrom → false`, so no pass is explained by a collector that would have
consented anyway. All six of my mutations were caught.

---

## 6. Verdict

**FAIL.** One blocking defect; the mechanism itself is sound.

Proven correct: the forced dispatch reaches `ResumeAsync` without consulting the gate; the
decline-then-restart branch is unreachable when forced; a forced resume that fails is returned as a
failure and does not restart, including across the native-collector re-dispatch; the override and both
non-honoured degradations are logged at Warning with the correlation id and the checkpoint's age; the
marker is structurally incapable of reaching an automatic sweep dispatch, demonstrated by a real round
trip through the real sweep, killed by mutation, and reinforced by three wire-placement theories; all
four dispatch entry points that accept the wire format honour the field and none silently drops it;
`PlatformEvent`, `TriggerFlowMapper`, `CheckpointRecoveryHandler` and both repositories are untouched;
build clean at the same 14 warnings as baseline.

Blocking:

1. **F1** — a non-boolean `forceResume` at the root throws inside `TriggerFlowMapper` and puts the
   delivery into a Retry loop, killing a run that would otherwise have executed unforced. New behaviour
   (proved against the baseline), reachable from ordinary producer error, and it makes A4 false. Five
   tests red. The fix is on `AdapterRunMessage`/`AdapterRunMessageFlat`, not on `ReadForceResume`.

Unresolved:

2. **F2** — `RetainedCheckpointIsNotConsumedTests` Arm 3 (2 tests) is red and asserts behaviour on the
   **unmarked** path that the contract forbids changing. Needs a scope decision or removal.
3. **F5** — `constraints.md` and `state.json` still instruct the abandoned `PlatformEvent.Metadata` route;
   `state.json` records no progress.
4. **A1 / A7 NEVER-TESTED** — no collector implementation in this repo, so "nothing re-checks staleness
   inside `ResumeAsync`" stays inference. `decisions.md` accepts this knowingly and the stated fallback
   (fail loudly rather than restart) is what the code does.
5. **A5 REJECTED as literally stated** — three changed log templates and one extra parse on the unmarked
   path. Inert; recorded rather than softened.
6. **Docker unavailable** — 136 container-gated tests did not run, including all 47 Postgres
   `CheckpointRepositoryTests`; the pass-2 retention SQL stays runtime-unverified. Not a failure of this
   task, which added no SQL.
7. **The tree was still being edited at 12:14, and moved again at ~12:16** into the execution-lock and
   repository layer (`ICheckpointRepository`, both repository implementations, a new
   `ExecutionLockOutcome`). That is outside the contract's file set, outside this verification, and on a
   path whose tests cannot run without Docker. Re-verify against a frozen tree before merging, and
   decide whether that work belongs in this task at all.
