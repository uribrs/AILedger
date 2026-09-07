## W1 — implementation

### Files changed

| file | change |
|---|---|
| `src/Cymulate.IntegrationServiceBus/Domain/Cymulate.IntegrationServiceBus.Domain/Messaging/AdapterRunMessage.cs` | `ForceResume` marker added to both root records |
| `src/Cymulate.IntegrationServiceBus/Applications/Cymulate.IntegrationServiceBus.Application/Commands/ProcessEventCommand.cs` | `public bool ForceResume { get; init; }`, directly beside `CapacityPreAcquired` |
| `src/Cymulate.IntegrationServiceBus/Applications/Cymulate.IntegrationServiceBus.Application/Messaging/IsbPlatformEventDispatcher.cs` | reads the marker off the trigger-flow body, sets the command flag on the trigger-flow site only |
| `src/Cymulate.IntegrationServiceBus/Applications/Cymulate.IntegrationServiceBus.Application/Commands/ProcessEventCommandHandler.cs` | flag threaded to `ExecuteWithResumeAsync`, which skips the gate |

No test file, `PlatformEvent`, `TriggerFlowMapper`, `CheckpointRecoveryHandler` or repository was touched.

### Which `AdapterRunMessage` variant carries the marker

**Both — at the message root, not in the payload.** `TriggerFlowMapper.TryParseToPlatformEvent`
deserializes `AdapterRunMessage` (nested) first and falls back to `AdapterRunMessageFlat` (flat)
depending on what the producer sent, so `collectors.run` genuinely uses both. The marker is declared
identically on `AdapterRunMessage` and `AdapterRunMessageFlat` as
`[JsonPropertyName("forceResume")] public bool ForceResume { get; init; }`. Absent = `false`.

Root placement is load-bearing, and is a second instance of the R1 leak the design correction already
caught once:

- `AdapterRunAction` and `AdapterRunPayloadFlat` both carry `[JsonExtensionData]`, and
  `TriggerFlowMapper.CopyAdditionalProperties` (`TriggerFlowMapper.cs:476`) copies every extension
  property into `payloadObject`, which becomes `PlatformEvent.Payload`.
- `ProcessEventCommandHandler.cs:134` serializes the whole `PlatformEvent` into `platform_event_json`,
  and `CheckpointRecoveryHandler.cs:390` rehydrates it on every later automatic sweep.
- So a marker placed inside `payload` / `payload.action` would have been persisted and replayed by the
  sweep — exactly the fleet-wide 23h override this change must not produce. Root-level message fields
  are consumed by the mapper into `Topic` / `CorrelationId` / `ClientId` / `Timestamp` and are never
  copied into the payload or the metadata, so a root marker structurally cannot reach
  `platform_event_json`. The XML doc on the property says so, in one line, to stop the next reader
  moving it.

### Which dispatcher site sets it

`IsbPlatformEventDispatcher.ProcessTriggerFlowAsync` — the `collectors.run` / trigger-flow path,
reached from `DispatchAsync` branch 1 via `TriggerFlowMapper.IsTriggerFlowMessage`. That is the site
formerly at `:225`, now `:234`:

```csharp
ProcessEventCommand command = new(platformEvent) { ForceResume = forceResume };
```

The other two sites (`:91` standard platform event, `:161` wrapped adapter message) are untouched and
construct the command exactly as before.

The message was **not** in scope at that point — `TriggerFlowMapper` returns only a `PlatformEvent` —
so rather than route the marker through `PlatformEvent` (the forbidden path), the dispatcher reads it
off the raw body it already holds, in a private static `ReadForceResume(string body)`. One
`JsonSerializer.Deserialize<AdapterRunMessage>` covers both wire formats because the marker is at the
root; the options mirror the mapper's (`PropertyNameCaseInsensitive`, camelCase), a `JsonException` is
swallowed to `false`, and the body has already been proved parseable by `IsTriggerFlowMessage`.

The existing trigger-flow `LogInformation` gained a `ForceResume` placeholder so a marked dispatch is
visible even when it turns out there is no checkpoint row to resume from.

### Exactly where the gate is skipped

`ProcessEventCommandHandler.ExecuteWithResumeAsync`. Signature gained `bool forceResume` after
`executionContext` and before `cancellationToken` (coding-standards tail-group rule). The single call
site (`:231`) passes `request.ForceResume`.

Both early returns are unchanged and still run first, so nothing about the no-adapter and no-checkpoint
cases moves:

```csharp
if (resumable == null)  return await adapter.ProcessAsync(...);
...
if (checkpoint == null) return await adapter.ProcessAsync(...);   // forced + no row == today
```

Then, after `MapToAdapterCheckpoint`:

```csharp
if (forceResume) { logger.LogWarning("Operator-forced resume: bypassing the adapter's resume gate. ..."); }

if (forceResume || resumable.CanResumeFrom(adapterCheckpoint))
```

`||` short-circuits, so when `forceResume` is true `CanResumeFrom` is **not called at all** — that is
observable on a mock, not merely asserted.

**R4:** the override log is `LogWarning` (not Information — an Information line in a collector run's
log volume is not "a level someone will actually see"), and carries `CorrelationId`, `Page` and
`CheckpointAgeHours` (`Math.Round((UtcNow - checkpoint.UpdatedAtUtc).TotalHours, 1)`), so the answer to
"why was a 2-day-old checkpoint used" is one query away. It fires only when a checkpoint row exists,
i.e. only when the flag actually changes the decision path.

### How a forced-resume failure is prevented from restarting (R2)

Structural, not defensive. The fall-through to `ProcessAsync` lives **after** the `if` block and is
reached only when the condition is false:

```csharp
if (forceResume || resumable.CanResumeFrom(adapterCheckpoint))
{
    ...
    try { return await resumable.ResumeAsync(platformEvent, adapterCheckpoint, cancellationToken); }
    finally { /* LogResumeWaste telemetry only */ }
}

logger.LogInformation("Adapter declined resume from checkpoint. ... Starting fresh.");
return await adapter.ProcessAsync(platformEvent, cancellationToken);
```

When `forceResume` is true the condition is true unconditionally, so the decline branch is
unreachable on that path. `ResumeAsync`'s `AdapterResult` — success or failure — is `return`ed
directly, and a thrown exception propagates. Neither can reach `ProcessAsync`. The `finally` only
records resume-waste telemetry and already swallows its own exceptions.

### R3 — unmarked dispatches

`ForceResume` defaults to `false` and is set at exactly one construction site. Grep over `src/`
(excluding `obj/` and test projects) finds **15 production `ProcessEventCommand` construction sites**:
`ProcessEventCommandHandler.cs:729`, `CheckpointRecoveryHandler.cs:390`,
`IsbPlatformEventDispatcher.cs:91/161/234`, `EventsController.cs:401/521/609/699/780/842/892/939`,
`KafkaConsumerService.cs:416`, `DotNetEventConsumerService.cs:47`. Fourteen of them are byte-for-byte
unchanged; only `:234` sets the flag. (The plan says 16; the count here is 15 — the difference is a
counting question, not a behavioural one, and every site other than `:234` is untouched.) The sweep's
own command at `CheckpointRecoveryHandler.cs:390` is among the unchanged fourteen, which together with
the marker never reaching `platform_event_json` is the R1 guarantee.

### Build result

`dotnet build src/Cymulate.IntegrationServiceBus/Cymulate.IntegrationServiceBus.sln`
→ **Build succeeded, 0 errors, 14 warnings**, all pre-existing (`NU1900`, `CS1573` on
`ISiemRulesResultPublisher`/`SiemRules*`, `CS9113` `siemRulesStorageLocator` unread ×2, one
`xUnit2013`). This matches the 14-warning baseline verifier-1 recorded for the pass-2 tree, i.e. this
delta added none.

Test suites were **not** run: W2 owns the test projects and was editing them concurrently, so any
result would have been read against a moving tree. The solution — including every test project —
compiles.

## W2 — tests

### Files added

- `src/Cymulate.IntegrationServiceBus/UnitTests/Cymulate.IntegrationServiceBus.Application.UnitTests/Commands/ForcedResumeOnOperatorDispatchTests.cs`
- `src/Cymulate.IntegrationServiceBus/Tests/Cymulate.IntegrationServiceBus.API.UnitTests/Checkpoints/SweepDispatchNeverForcesResumeTests.cs`

No existing test file was modified.

### Tests by name

| test | R-id | what it drives |
|---|---|---|
| `SweepDispatchNeverForcesResumeTests.R1_SweepDispatchNeverForcesResume` | R1 | Real handler, forced dispatch, real `InMemoryCheckpointRepository`; the leg is orphaned mid-resume so `HandBackForRecoveryAsync` leaves a recoverable row carrying the forced dispatch's `PlatformEventJson`. The real `CheckpointRecoveryHandler.RecoverAsync` then rehydrates that row and its dispatched `ProcessEventCommand` is captured off the mediator: `ForceResume` false, `CapacityPreAcquired` true. Also asserts the stored JSON carries no force marker. |
| `ForcedResumeOnOperatorDispatchTests.R2_ForcedResumeFailureDoesNotRestart` | R2 | Forced resume returns a failure → that failure is returned (same `Status` and `ErrorCode` as the collector's own result), `ProcessAsync` never called. |
| `ForcedResumeOnOperatorDispatchTests.R3_UnmarkedDispatchIsUnchanged` | R3 | No flag, collector declines → `CanResumeFrom` consulted once, `ResumeAsync` never, `ProcessAsync` once, decline logged. |
| `ForcedResumeOnOperatorDispatchTests.R4_ForcedResumeIsLogged` | R4 | Differential: the forced run and an otherwise identical consenting run (`CanResumeFrom` true, no flag) are compared on the log lines naming the correlation id; the forced run must emit at least one the consenting run does not. Wording is not pinned. |
| `ForcedResumeOnOperatorDispatchTests.A_forced_resume_never_consults_the_collector_gate` | — | Absence assertion: `CanResumeFrom` `Times.Never` with the collector arranged to answer **false**, so the pass cannot be explained by a collector that would have consented. |
| `ForcedResumeOnOperatorDispatchTests.A_forced_dispatch_with_no_checkpoint_row_starts_fresh` | — | Flag set, no row → `ProcessAsync` once, `ResumeAsync` never, no crash. |

Every test arranges `CanResumeFrom` → **false**, so a forced resume happening at all is the override doing the work.

### Non-vacuity checks performed

- R1: verified the forced dispatch really took the resume path (`ResumeAsync` `Times.Once`) and that the
  row's `PlatformEventJson` is non-empty — the sweep skips rows without it, which would have made the
  test pass for the wrong reason.
- R4: the differential was inverted once to inspect the difference. It resolves to exactly one line,
  `"Operator-forced resume: bypassing the adapter's re…"`. A first draft asserted a word list
  (`forc`/`override`/`bypass`); that draft matched an unrelated pre-existing line
  (`"Collector run stays on the native collector: useYaml…"`) and was replaced by the differential.

### Spec gap (reported, not worked around)

The frozen surface says *"`AdapterRunMessage` carries an optional bool marker"* but does not name the
property or fix which record it sits on (`AdapterRunMessage` / `AdapterRunPayload` / `AdapterRunAction`,
and the `*Flat` variants). Under the isolation rule I could not read W1's implementation to obtain it, so
the `AdapterRunMessage` → `IsbPlatformEventDispatcher` → `ProcessEventCommand.ForceResume` mapping —
the operator-dispatch end of the wire — is **not covered**. Everything from `ProcessEventCommand` inward
is. A follow-up test in
`UnitTests/…Application.UnitTests/Messaging/IsbPlatformEventDispatcherTests.cs` would close it once the
marker's name is settled.

### Results

| project | result | note |
|---|---|---|
| `Application.UnitTests` | **98/98 passed** | 93 pre-existing + 5 added |
| `Infrastructure.Core.UnitTests` | **449/449 passed** | unchanged |
| `API.UnitTests` — non-container checkpoint suites | **30/30 passed** | 29 pre-existing + 1 added. `CheckpointRecoveryCompatibilityTests` 23, `SiemRulesRecoveryTenantScopeTests` 3, `StoppingIsNotEndingTests` 2, `FailedRunLeavesATerminalRowTests` 1, `SweepDispatchNeverForcesResumeTests` 1. The brief's stated baseline of 25 is 4 low against what is on the branch. |
| `API.UnitTests` — whole project | 630 passed, 139 failed | **136 are `DockerUnavailableException` — not run, not green.** See below for the other 3. |

**Not attributable to this change:** 3 failures in
`API.UnitTests.Commands.TestAdapterConnectionCommandHandlerTests` fail with
`Moq.MockException: IAdapterRegistryManager.GetRegistration(PlatformType.YamlEngine, AdapterCategory.Collectors, True) invocation failed with mock behavior Strict`.
Not stashed to confirm — W1 is editing the same working tree concurrently and a stash would have
destroyed their in-flight work. Instead: that test touches none of
`ProcessEventCommand`/`ForceResume`/`ResumeAsync`, and every file on its path
(`TestAdapterConnectionCommandHandler.cs`, `AdapterRegistry.cs`, `IAdapterRegistryManager.cs`,
and the test file itself) is unmodified in the working tree. Pre-existing on this branch.

### Existing construction sites

`grep -rn "new ProcessEventCommand"` — all pre-existing sites compile and behave unchanged. The three in
`ScheduledContinuation/ScheduledContinuationIntegrationTests.cs` are inside the Docker-gated suite and
are **not run**. The resume-behaviour assertions in
`Application.UnitTests/ProcessEventCommandHandlerTests.cs`
(`Handle_resumes_from_checkpoint_when_the_broker_requeued_the_message`,
`Handle_still_resumes_when_the_application_retried_the_message`,
`Handle_starts_fresh_when_no_checkpoint_exists`,
`Handle_starts_fresh_when_the_adapter_declines_the_checkpoint`) all still pass.

## W1 — review round 2 (I1, I2, I3)

### Scope expansion, stated explicitly

I1 and I2 cannot be fixed inside the four files W1 was given. Two files outside the original
ownership set were edited on the team lead's instruction:

- `src/Cymulate.IntegrationServiceBus/Hosts/Cymulate.IntegrationServiceBus.API/Controllers/EventsController.cs` (I1)
- (I2 was inside `ProcessEventCommandHandler.cs`, already owned)

Neither collides with W2, who owns test projects only. Nothing else was touched.

### I2 — the native-fallback re-dispatch keeps the force

Confirmed real. `Handle` → `:293` → `TryEnqueueNativeFallbackAsync` re-queued the same logical run
(same correlation id, same checkpoint row) as a bare `new ProcessEventCommand(nativeEvent)`, so a
forced run whose YAML adapter reported no definition arrived at the native collector unforced, hit
`CanResumeFrom`, was declined, and restarted.

- `TryEnqueueNativeFallbackAsync` gained `bool forceResume` (after `category`, before the token).
- Call site `:293` passes `request.ForceResume`.
- The queued command is now `ProcessEventCommand nativeCommand = new(nativeEvent) { ForceResume = forceResume };`
- The existing re-dispatch `LogInformation` gained a `ForceResume` placeholder, so the two dispatches
  are tied together by correlation id **and** carry the same force state in the log.

The XML summary gained the clause "and the operator's force-resume flag" beside the existing "keeps
the same correlation id (one logical run)" — the two travel for the same reason. No `<remarks>`
(I drafted one, then removed it; the brief forbids them and the repo rule is explicit).

### I1 — the two HTTP endpoints honour the marker

Both controller paths take the same trigger-flow body as `collectors.run` and now read the same field:

| entry | dispatcher | now |
|---|---|---|
| `EventsController.cs:80` → `ProcessTriggerFlowAsync` (`:573`) | `mediator.Send` at `:609` | sets `ForceResume` |
| `EventsController.cs:303` → `QueueTriggerFlowAsync` (`:756`) | `eventCommandQueue.QueueAsync` at `:780` | sets `ForceResume` |

Honoured rather than rejected: one wire field with one meaning on every path that accepts the body is
the option that leaves no trap. Both endpoints' existing log lines gained a `ForceResume` placeholder.

**Coverage is now complete, checked rather than assumed.** `TriggerFlowMapper.IsTriggerFlowMessage`
has exactly four call sites: `IsbPlatformEventDispatcher.cs:42` and `EventsController.cs:80/:303`
(plus `IsAdapterRunMessage`, an unused alias). `KafkaConsumerService` and `DotNetEventConsumerService`
contain no trigger-flow handling at all (`grep TriggerFlow` → 0 in both), so no consumer silently
drops the field.

### Shared reader moved onto the DTO

Three call sites needed the same root-field read, so the reader moved out of the dispatcher and onto
the type that declares the field:

```csharp
public static bool AdapterRunMessage.ReadForceResume(string json)
```

with a private static `ForceResumeReadOptions` (`PropertyNameCaseInsensitive`; no naming policy needed
— the property carries an explicit `[JsonPropertyName]`). Null/whitespace and `JsonException` both
return `false`. The dispatcher's private copy and its options field were deleted. One parse rule, next
to the property it parses, reachable from Domain by both the Application dispatcher and the API host.

`AdapterRunMessageFlat.ForceResume` is still declared and is not read by `ReadForceResume` (which
binds the nested type; the field is at the root of both, so one deserialize serves both). It is kept
deliberately: it is the declared contract for the flat format, and its doc comment is what stops the
next reader "fixing" the asymmetry by moving the field into the payload — which is the R1 leak.

### I3 — a degraded force now says so

Both early returns in `ExecuteWithResumeAsync` precede the override log, so the two ways a force
silently became a full recollection were indistinguishable from an ordinary fresh run. Each now logs
at **Warning**, guarded by `if (forceResume)` so no unforced run gains a line:

- `resumable == null` → *"Force-resume requested but not honoured: adapter {Platform} does not support
  resume. Running a full collection."*
- `checkpoint == null` → *"Force-resume requested but not honoured: no checkpoint row for this run.
  Running a full collection."* (with Platform and Category)

With ISB not reporting resumability to the backend, these two lines plus the `:1204` override warning
are the only place the resume/restart distinction exists. All three are Warning, and all three name the
correlation id.

### Files changed this round

| file | finding |
|---|---|
| `Domain/.../Messaging/AdapterRunMessage.cs` | shared `ReadForceResume` |
| `Applications/.../Messaging/IsbPlatformEventDispatcher.cs` | uses the shared reader; local copy deleted |
| `Applications/.../Commands/ProcessEventCommandHandler.cs` | I2 + I3 |
| `Hosts/.../Controllers/EventsController.cs` | I1 (outside the original ownership set) |

### Build

`dotnet build src/Cymulate.IntegrationServiceBus/Cymulate.IntegrationServiceBus.sln`
→ **Build succeeded, 0 errors, 14 warnings** — the same 14 pre-existing warnings as the previous
round. No new warning, and no test project broken.

Tests still not run here: W2 owns them. I4 (the wire→command hop) is W2's to cover, and the hop it
needs to test now has three producers — the RabbitMQ dispatcher and the two HTTP endpoints.

## W2 — tests, round 2 (I4 + the two new behaviours)

### Files

- **added** `src/Cymulate.IntegrationServiceBus/UnitTests/Cymulate.IntegrationServiceBus.Application.UnitTests/Messaging/ForceResumeWireContractTests.cs`
- **extended** `src/Cymulate.IntegrationServiceBus/UnitTests/Cymulate.IntegrationServiceBus.Application.UnitTests/Commands/ForcedResumeOnOperatorDispatchTests.cs`

### Tests by name

| test | id | what it drives |
|---|---|---|
| `ForceResumeWireContractTests.I4_RootFlagSetsTheCommand` | I4 | Real `IsbPlatformEventDispatcher.DispatchAsync` over a real `TriggerFlowMapper`; `"forceResume": true` at the root → captured `ProcessEventCommand.ForceResume` true. |
| `ForceResumeWireContractTests.I4_AbsentFlagLeavesTheDispatchUnforced` | I4 | Key absent → false. |
| `ForceResumeWireContractTests.I4_FlagOutsideTheRootIsIgnored` | I4 | Theory ×3 — flag at `payload`, `payload.action`, `payload.metadata`. Each of those IS mapped into the `PlatformEvent` and persisted, so reading any of them would collapse containment. All must yield false. **Passes.** |
| `ForceResumeWireContractTests.I4_NonBooleanRootValuesDoNotForce` | I4 | Theory ×6 — `"true"`, `null`, `1`, `[true]`, `{"value":true}`, `false`. **5 of 6 FAIL — see the mismatch below.** |
| `ForceResumeWireContractTests.A_non_trigger_flow_body_carrying_the_flag_does_not_force` | I4 | Only the trigger-flow branch reads the flag; a plain platform-event body carrying it produces an unforced command. |
| `ForcedResumeOnOperatorDispatchTests.The_native_fallback_re_dispatch_carries_the_force` | new-1 | Theory ×2 (forced / unforced). YAML adapter returns `Skipped` + `fallbackToNative`; the queued native command must carry the same `ForceResume` as the original. |
| `ForcedResumeOnOperatorDispatchTests.A_force_the_adapter_cannot_honour_is_logged` | new-2 | Adapter does not implement `IResumableAdapter`; run starts fresh AND the forced run logs something the identical unforced run does not. |
| `ForcedResumeOnOperatorDispatchTests.A_force_with_nothing_to_resume_is_logged` | new-2 | No checkpoint row; same shape. |

Both log tests use the differential established in R4 — an otherwise identical unforced run on a fresh
fixture — so the degradation is pinned as reported without pinning wording.

### Spec-vs-implementation mismatch — reported, not adjusted

**A non-boolean `forceResume` at the root poisons the whole trigger-flow message.**

`AdapterRunMessage.ForceResume` is a non-nullable `bool`. `AdapterRunMessage.ReadForceResume` handles a
bad value correctly — it catches `JsonException` and returns false. But `TriggerFlowMapper.TryParseToPlatformEvent`
deserializes the *same* `AdapterRunMessage` and has no such tolerance, so the added property makes a bad
value fatal to parsing the entire message:

```
MessageOutcome { Disposition = Retry,
  Error = JSON parsing error: The JSON value could not be converted to System.Boolean.
          Path: $.forceResume | LineNumber: 4 | BytePositionInLine: 21. }
```

No command is dispatched. The delivery is settled **Retry**, so the message is redelivered and fails the
same way — a poison-message loop on a run that would otherwise have executed fine, merely unforced. A
producer sending `"forceResume": "true"` (a string — the easy mistake) gets a collection that never runs.
`"forceResume": false` is unaffected; only non-boolean values.

Before the change an unrecognised root key was ignored, so this is new. Two candidate fixes, both W1's
call: make the property `bool?` on `AdapterRunMessage` and `AdapterRunMessageFlat`, or give
`TriggerFlowMapper` the same tolerance `ReadForceResume` already has. The five failing theory cases are
left failing on purpose.

### Coverage still open

`EventsController.cs:601` and `:783` call `AdapterRunMessage.ReadForceResume` on the HTTP trigger-flow
paths — the same wire hop over a different transport. Not covered here: `EventsController` lives in
`Hosts/` and its suite is the Docker-gated `API.UnitTests`. The root-vs-payload argument is proven at the
`AdapterRunMessage` level, which both transports share, so the risk is thin, but the HTTP hop itself is
unasserted.

### Results

| project | result | note |
|---|---|---|
| `Application.UnitTests` | **109 passed, 5 failed / 114** | The 5 are `I4_NonBooleanRootValuesDoNotForce` above. 93 pre-existing + 21 added across both rounds. |
| `Infrastructure.Core.UnitTests` | **449/449 passed** | unchanged |
| `API.UnitTests` — non-container checkpoints | **30/30 passed** | unchanged |
| `API.UnitTests` — `Messaging` (incl. `TriggerFlowMapperTests`) | **89/89 passed** | the mapper's own suite does not exercise a bad `forceResume` value |
| `API.UnitTests` — whole project | not re-run in full | Docker still unavailable; 136 Testcontainers tests remain **not run**, plus the 3 pre-existing `TestAdapterConnectionCommandHandlerTests` strict-mock failures documented in round 1. |

## W2 — tests, round 3 (retained checkpoint must not be consumed)

### File

- **added** `src/Cymulate.IntegrationServiceBus/Tests/Cymulate.IntegrationServiceBus.API.UnitTests/Checkpoints/RetainedCheckpointIsNotConsumedTests.cs`

Driven against the **real** `InMemoryCheckpointRepository`, not a mocked `ICheckpointRepository`. Two
reasons: the `Failed → Idle` revival inside `TryAcquireExecutionLockAsync` really happens, and the row is
inspectable afterwards — the point of this change is what is LEFT behind, which no return value shows.
It also means these tests do not depend on how W1 chooses to have the lock report the revival.

### Tests by name

| test | arm | status |
|---|---|---|
| `Arm1_A_forced_dispatch_resumes_the_retained_checkpoint` | forced → resume regardless of gate | **passes** |
| `Arm2_An_immediate_redelivery_still_resumes_a_terminal_row` | not forced, gate accepts → resume | **passes** |
| `Arm3_A_declined_retained_checkpoint_fails_instead_of_restarting` | not forced, gate declines, row terminal → fail | **FAILS — not yet implemented** |
| `Arm4_A_declined_non_terminal_checkpoint_still_restarts` | not forced, gate declines, row not terminal → restart | **passes** |
| `Arm3_The_refusal_does_not_look_like_an_ordinary_failure` | distinguishability | **FAILS — not yet implemented** |

Arm 2 seeds the row `Failed` but only 4 seconds old with the collector accepting, so it pins the common
path: a rule that refused on "terminal" alone rather than "terminal AND declined" breaks the ordinary
transient-failure redelivery, and this test is what catches that.

### What arm 3 asserts beyond the return value

- `ProcessAsync` never called, `ResumeAsync` never called.
- The row survives with `CurrentPage` 61 and `ProcessedItems` 610 intact, and unclaimed, so a later
  forced dispatch can still use it.
- **The real recovery sweep offers it zero times.** This one is worth keeping: the execution lock has
  already revived the row out of its terminal status by the time the refusal fires. A refusal that leaves
  the row revived is undone on the next sweep tick — `GetRecoverableAsync` excludes only `ScheduledWait`
  and `Failed`, so an `Idle` unclaimed row is re-dispatched a minute later and the restart happens anyway,
  just from somewhere else. Refusing and re-terminalising are one change, not two, and only running the
  real sweep over the same store can see that.

Distinguishability is asserted two ways, neither pinning wording: the refusal's `ErrorCode` must differ
from that of an ordinary adapter failure, and the refusal run must log something about the correlation id
that the ordinary-failure run does not.

### Current behaviour, for W1

Confirmed on the branch as it stands: the seeded `Failed` row is revived by the lock, the collector
declines, the handler falls through to `ProcessAsync`, and the run reports **success**. The retained
checkpoint has been consumed and the run restarted. That is what the two failing tests describe.

### Results

| project | result |
|---|---|
| `API.UnitTests` — non-container checkpoints | **33 passed, 2 failed / 35** — the 2 are arm 3, above |
| `Application.UnitTests` | **109 passed, 5 failed / 114** — the 5 are `I4_NonBooleanRootValuesDoNotForce`, round 2 |
| `Infrastructure.Core.UnitTests` | **449/449 passed** |
| `API.UnitTests` — Testcontainers suites | **not run** — Docker unavailable |

No regression in arms 1, 2 or 4, so the existing decline-then-restart path is untouched so far.

## W1 — round 3: retained checkpoint consumed by a plain dispatch

### Scope expansion, stated explicitly

This fix cannot be made inside the four original files. On the team lead's instruction, three more
production files were edited, two of which the original brief named as off-limits:

- `Domain/.../Models/ExecutionLockOutcome.cs` (**new file**)
- `Domain/.../Interfaces/ICheckpointRepository.cs` (a port, previously off-limits)
- `Infrastructure.Postgres/Persistence/CheckpointRepository.cs` (a repository, previously off-limits)
- `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs` (a repository, previously off-limits)

### What was verified before changing anything

Two facts the fix depends on, both read rather than assumed:

1. **A failure result preserves the row.** `FlushAndCleanupCheckpointAsync` (`:1281`) branches on
   `IsReportedFailure` (`:1091`, true for `Failure`/`TransientFailure`/`ValidationFailure`) and calls
   `TryMarkCheckpointFailedAsync` — it deletes the row only on every *other* outcome. So returning a
   failure re-marks the row terminal and puts it back on the 7-day retention clock. Had this path
   deleted instead, the fix would have destroyed exactly what it protects.
2. **The result reaches that path.** `ExecuteWithResumeAsync`'s return at `:232` flows through the
   claim-loss checks and the native-fallback branch into `CompleteExecutionAsync` (`:298`) →
   `FlushAndCleanupCheckpointAsync` (`:815`).

### The lock now reports what it did

`Task<bool>` → `Task<ExecutionLockOutcome>`, a `readonly record struct (bool Acquired, bool RevivedTerminalRow)`
with three named constants (`Denied`, `Granted`, `GrantedAfterReviving`) so no call site constructs a
bare pair of bools.

**In-memory** is trivial: it already tested `existing.Status == CheckpointStatus.Failed` to do the
flip; that same bool is now returned.

**Postgres** could not report it from `rowsAffected`, and the prior status had to come from the same
statement — a separate read before the lock is a race, as instructed. The rewrite follows the pattern
this very file already established for the upsert (`:52-243`): a data-modifying CTE plus raw ADO with
every parameter explicitly typed.

```sql
WITH prior AS (SELECT status FROM adapter_checkpoints WHERE <key>),
     attempted AS (INSERT ... ON CONFLICT ... DO UPDATE SET ... WHERE ... RETURNING 1)
SELECT (SELECT count(*) FROM attempted) AS acquired,
       (SELECT status FROM prior)       AS prior_status
```

The upsert body, its `DO UPDATE SET` list, its `WHERE` guards and the `Failed → Idle` CASE are
**unchanged** — B2's revival stays exactly as it was. What is added is the `prior` CTE and the outer
SELECT. One statement, one round trip, one snapshot.

**The one caveat, documented in four lines at the statement** (the file's existing `<remarks>` at
`:245` establishes why this matters): `prior` reads the statement snapshot while the `ON CONFLICT`
guards read the latest committed row, so a peer committing in that window can make this report a
revival that was really the peer's. That direction is the safe one — the caller then preserves the
checkpoint instead of restarting. The opposite error requires a peer committing `Failed` inside the
window, and yields today's behaviour.

### The new branch

`ExecuteWithResumeAsync` gained `bool revivedTerminalRow` (after `forceResume`, before the token).
The four cases now read exactly as specified:

| forced | gate | row was terminal | outcome |
|---|---|---|---|
| yes | not consulted | — | resume |
| no | accepts | — | resume |
| no | declines | **yes** | **fail, do not restart** |
| no | declines | no | restart (unchanged) |

The third case returns
`AdapterResult.FailureResult(..., "RESUME_DECLINED_RETAINED_CHECKPOINT", null, false)`. The explicit
`isTransient: false` is deliberate: the Client's `FailureResult` "automatically determines if the error
is transient based on error code", and a transient classification here would have the dispatcher retry
the delivery straight back into the same branch — a redelivery loop. Non-transient means the dispatcher
Acks without retry (`IsbPlatformEventDispatcher.ShouldRetry`).

It logs at **Error**, naming correlation id, platform, category, page, checkpoint age, and what to do
about it ("dispatch with the force-resume marker"). A run that refuses to start needs to explain
itself or it reads as an unexplained failure.

The ordinary immediate-redelivery case is untouched, as the lead noted: the checkpoint is seconds old,
the gate accepts, and the second row of the table applies.

### Build

- **All four production hosts build: 0 errors.** API, API.Query, Console, API.SiemRules.
- The **solution** build fails with **34 errors, every one in a test project** — the mechanical
  consequence of the return-type change, and W2's to absorb. Nothing in `src/` production code errors.

Test files W2 needs to update (error counts from the solution build):

| file | shape of change |
|---|---|
| `Application.UnitTests/ProcessEventCommandHandlerTests.cs` (52) | `ReturnsAsync(true)` → `ReturnsAsync(ExecutionLockOutcome.Granted)` |
| `Infrastructure.Core.UnitTests/InMemoryCheckpointRepositoryTerminalStatusTests.cs` (8) | `Assert.True(await ...)` → assert `.Acquired` |
| `API.UnitTests/Checkpoints/StoppingIsNotEndingTests.cs` (2) | fake store's return type |
| `API.UnitTests/Checkpoints/CheckpointRecoveryCompatibilityTests.cs` (2) | fake store's return type |
| `Application.UnitTests/Commands/SiemRulesFallbackDoneTests.cs` (2) | `ReturnsAsync` |
| `Application.UnitTests/Commands/ForcedResumeOnOperatorDispatchTests.cs` (2) | `ReturnsAsync` |

A missed Moq setup fails loudly rather than silently: `default(ExecutionLockOutcome)` is
`Acquired = false`, which makes the handler return `EXECUTION_LOCKED` instead of quietly proceeding.

### Untested, and worth stating plainly

The Postgres statement is the one piece I cannot exercise: `CheckpointRepositoryTests` is
Testcontainers-backed and Docker is not running here (30/30 `DockerUnavailableException` in
verifier-1's run, unchanged). The rewrite is type-checked and follows an in-file precedent, but its SQL
has not executed against a real server in this pass. It should not ship without that suite green.

## W2 — round 4: adapting to `Task<ExecutionLockOutcome>`, and the four arms now green

### Build fix

`ICheckpointRepository.TryAcquireExecutionLockAsync` now returns `Task<ExecutionLockOutcome>`
(`readonly record struct (bool Acquired, bool RevivedTerminalRow)`, with `Denied` / `Granted` /
`GrantedAfterReviving`). 34 errors across the test projects, in three shapes:

1. **Two decorators** — `StoppingIsNotEndingTests.RefusingStore` and
   `CheckpointRecoveryCompatibilityTests.ClaimRefusingStore`. Both are pure pass-throughs on this member
   and stay pass-throughs; only the return type changed, so the revival flows through untouched, which is
   right for decorators that refuse writes and claims rather than locks.
2. **28 Moq setups** across `ProcessEventCommandHandlerTests`, `SiemRulesFallbackDoneTests` and
   `ForcedResumeOnOperatorDispatchTests` — `.ReturnsAsync(true)` → `ExecutionLockOutcome.Granted`,
   `.ReturnsAsync(false)` → `ExecutionLockOutcome.Denied`. `Granted` rather than `GrantedAfterReviving`
   is the meaning-preserving choice: none of those tests seeds a terminal row, so none of them was ever
   describing a revival, and `GrantedAfterReviving` would have silently pushed them all down the new
   branch.
3. **Six assertions that had to choose.** These are where a mechanical `.Acquired` unwrap would have
   compiled and dropped the distinction:

   | file | site | now asserts | why |
   |---|---|---|---|
   | `InMemoryCheckpointRepositoryTerminalStatusTests` | `StoreWithClaimedRowAsync` | `Granted` | fresh row, nothing revived |
   | `InMemoryCheckpointRepositoryTerminalStatusTests` | redelivery after `MarkFailedAsync` | **`GrantedAfterReviving`** | the row it takes was retained terminal |
   | `InMemoryCheckpointRepositoryTerminalStatusTests` | `Taking_the_execution_lock_returns_a_terminal_row_to_Idle` | **`GrantedAfterReviving`** | the revival is the test's whole subject |
   | `InMemoryCheckpointRepositoryTerminalStatusTests` | `The_terminal_mark_is_all_or_nothing…` CAS loop | `Granted` | ordinary Idle row |
   | `CheckpointRepositoryTests` | `TryAcquireExecutionLockAsync_returns_a_terminal_row_to_Idle` | **`GrantedAfterReviving`** | seeded `Failed` |
   | `CheckpointRepositoryTests` | `R4_MarkingTerminalReleasesTheClaim` redelivery | **`GrantedAfterReviving`** | acquires the row it just marked `Failed` |

   The two `CheckpointRepositoryTests` sites are Postgres/Testcontainers and are **not run** here, so
   those two assertions are compiled but unverified.

### The four arms are now green

`RetainedCheckpointIsNotConsumedTests` — **5/5 passed**, where round 3 had arms 1, 2 and 4 passing and
both arm-3 tests failing. So on the branch as it now stands:

- a plain dispatch meeting a declined retained checkpoint **fails instead of restarting**;
- the row survives with `CurrentPage` 61 and `ProcessedItems` 610, unclaimed;
- **the real recovery sweep offers it zero times** — the refusal is not undone a tick later, which was the
  assertion most likely to catch a half-fix;
- the refusal's `ErrorCode` differs from an ordinary adapter failure's, and it logs a line about the
  correlation id that an ordinary failure does not.

Per the brief the assertions are behavioural; `RESUME_DECLINED_RETAINED_CHECKPOINT` is not named anywhere
in the tests, so renaming it would not break them.

Arms 1, 2 and 4 still pass unchanged, so the forced path, the immediate-redelivery resume and ordinary
decline-then-restart are all untouched by the fix.

### Still failing — round 2's mismatch, unchanged

`I4_NonBooleanRootValuesDoNotForce` ×5, same cause, verified again after the rebuild:

```
MessageOutcome { Disposition = Retry,
  Error = JSON parsing error: The JSON value could not be converted to System.Boolean. Path: $.forceResume }
```

A non-boolean `forceResume` at the root still fails `TriggerFlowMapper.TryParseToPlatformEvent` and
poisons the whole message. Unfixed.

### Results

| project | result |
|---|---|
| `Application.UnitTests` | **109 passed, 5 failed / 114** — the 5 above |
| `Infrastructure.Core.UnitTests` | **449/449 passed** — including the two now asserting `GrantedAfterReviving`, so the in-memory store really does report the revival |
| `API.UnitTests` — non-container checkpoints | **35/35 passed** |
| `API.UnitTests` — Testcontainers suites | **not run**, Docker unavailable |

Solution build is clean: 0 errors.

## W1 — round 4: a malformed marker must not kill the message

### Cause

The strictness was mine, and it came from having the field in two places. `TriggerFlowMapper`
deserializes `AdapterRunMessage` / `AdapterRunMessageFlat` to build the `PlatformEvent`; once the
marker was a typed `bool` property on those records, `System.Text.Json` threw on any non-boolean
value for `$.forceResume`. That throw is caught by `TriggerFlowMapper.TryParseToPlatformEvent` and
returned as `error = "JSON parsing error: ..."`, which the dispatcher settles as
`MessageOutcome.Retry`. So the delivery never produced a command at all — a retry loop, caused by a
field that is supposed to be optional and additive. Before this change the same key was an unknown
property and was ignored.

### Fix — one place decides what the flag means

The typed property is **gone from both records**. `AdapterRunMessage.ReadForceResume` is now the only
thing that knows the field exists, and it reads the raw body defensively:

```csharp
using JsonDocument document = JsonDocument.Parse(json);

return document.RootElement.ValueKind == JsonValueKind.Object
       && document.RootElement.TryGetProperty("forceResume", out JsonElement marker)
       && marker.ValueKind == JsonValueKind.True;
```

Only a JSON `true` is a force. `null`, `1`, `"true"`, `[true]`, `{"value":true}`, `false`, a missing
key, a non-object root, and an unparseable body all yield false, and none of them throw. The mapper is
back to ignoring an unknown key, so the whole message parses as it did before this task started.

The method's doc says in one line why it is deliberately not a bound property — that is the comment
that stops the next reader "tidying up" by adding the property back and reintroducing the incident.
No `<remarks>` (I drafted one, removed it; the brief forbids them).

Deliberate narrowing worth recording: the old reader used `PropertyNameCaseInsensitive`, so
`ForceResume` would also have matched. `TryGetProperty` is case-sensitive, so the accepted spelling is
now exactly `forceResume` — which is the contract, and matches every other root field (`topic`,
`vendor`, `correlationId`, `payload`). No test asserts any other casing.

### The HTTP endpoints, confirmed rather than assumed

Both controller paths were affected by the same defect for the same reason and are fixed by the same
change — they call `AdapterRunMessage.ReadForceResume`, and they call
`triggerFlowMapper.TryParseToPlatformEvent`, which was the thing throwing. With the property gone,
neither can fail a message over this field. Covered structurally: there is one reader and one parse
path, and no typed binding of the marker anywhere.

### Results

- `dotnet build` on the solution: **Build succeeded, 0 errors, 14 warnings** (the same pre-existing set).
- `ForceResumeWireContractTests`: **12/12 passed**, including all six `I4_NonBooleanRootValuesDoNotForce`
  cases (the five malformed values plus `false`).
- `Application.UnitTests`: **114/114 passed**.
- `Infrastructure.Core.UnitTests`: **449/449 passed**.
- `API.UnitTests`: 635 passed, 139 failed — **136 Docker-gated** (`DockerUnavailableException`,
  Testcontainers; Docker is not running) and **3 pre-existing**
  `TestAdapterConnectionCommandHandlerTests` YAML-routing failures, exactly the three verifier-1
  recorded for the pass-2 tree. No regression.

The Postgres `TryAcquireExecutionLockAsync` rewrite from round 3 is still unexercised for the same
Docker reason, and still should not ship without `CheckpointRepositoryTests` green.

## W1 — round 5: the refusal path could delete the row it preserves

### I2 — fixed

`TryMarkCheckpointFailedAsync` fell back to `TryDeleteCheckpointAsync` whenever `MarkFailedAsync`
returned false. That mark is owner-guarded, so it returns false exactly when the claim was stolen or
staled out — and on the `RESUME_DECLINED_RETAINED_CHECKPOINT` path, whose entire purpose is to keep a
week of retained progress, the response to "I could not mark it" was to delete it.

Narrowed rather than removed, because the fallback was itself a prior-pass fix whose reasoning still
holds on the ordinary failure path: a mark that misses its guard would otherwise leave a non-terminal
row carrying credentials and progress, visible to the sweep once the claim staled.

The refusal is now identified by its error code, hoisted to a single private const beside the existing
`YamlFallbackMarker` / `FallbackToNativeDataKey` so the producer and the consumer cannot drift — a
typo in a duplicated literal would have silently re-enabled the delete:

```csharp
private const string ResumeDeclinedRetainedCheckpointCode = "RESUME_DECLINED_RETAINED_CHECKPOINT";
```

`FlushAndCleanupCheckpointAsync` passes the decision down:

```csharp
await TryMarkCheckpointFailedAsync(
    tenantId, platformEvent, category,
    deleteWhenMarkMisses: result.ErrorCode != ResumeDeclinedRetainedCheckpointCode);
```

and the helper returns early on a missed mark when the flag is false, logging at Warning that the row
is being left alone. The row was already terminal before this execution's lock revived it, and a
missed mark means another execution now owns it — deleting is wrong under both readings. Every other
failure keeps the delete fallback byte for byte.

### I1 — known limitation, not fixed

**`RevivedTerminalRow` is process-local; the revival it describes is durable and already committed.**

`TryAcquireExecutionLockAsync` flips `Failed → Idle` in the same statement that takes the claim, and
that write is committed before the handler does anything with the answer. The flag that remembers it
happened lives only in this process's stack. So any exit from `Handle` other than a reported failure —
a crash, a pod eviction, a cancellation, a claim loss detected after the lock — leaves the row `Idle`:
silently downgraded from the 7-day terminal retention clock to the ordinary 24-hour TTL, with nothing
having consumed the retained state and no record that the downgrade happened. The next dispatch sees
an ordinary idle row.

Not fixed here. The fix is a restructure — do not revive until the resume decision has been made — and
that re-opens the prior pass's blocker B2: without the flip at lock time, a redelivery runs against a
row the recovery sweep cannot see, and losing the pod in that window strands the run with no done
message and no recovery, until the retention horizon deletes it. Trading a silent retention downgrade
for a silently stranded run is not obviously a win, and the change belongs to whoever revisits the
lock ordering with time to re-derive B2's constraints.

Whoever picks this up: the two candidate shapes are (a) revive only after the resume decision, which
needs B2's stranding window closed some other way, and (b) make the revival itself recoverable —
record on the row that it was revived and by whom, so a later sweep can put it back. (b) needs a
column, which this task was constrained against.

### Results

- `dotnet build --no-incremental` on the solution: **Build succeeded, 0 errors, 14 warnings** — the
  same pre-existing set (NU1900, CS1573, CS9113 ×2, xUnit2013).
- `Application.UnitTests`: **114/114 passed** (includes `ForceResumeWireContractTests` 12/12).
- `Infrastructure.Core.UnitTests`: **449/449 passed**.
- `API.UnitTests`: 635 passed, 139 failed — **136 Docker-gated**, **3 pre-existing**
  `TestAdapterConnectionCommandHandlerTests` YAML-routing failures. Byte-identical to the round-4 run:
  no regression.

`CheckpointRepositoryTests` (30 tests, including `TryAcquireExecutionLockAsync_returns_a_terminal_row_to_Idle`
and `TryAcquireExecutionLockAsync_leaves_a_non_terminal_status_alone`) is among the Docker-gated set and
has **not run** in any round of this task. The round-3 Postgres rewrite still needs it green before shipping.

## W1 — round 6: first live Postgres run

### The operator-precedence fix

`MarkFailedAsync`'s payload-strip expression parsed as
`platform_event_json #- (ARRAY[...] - key - key)` because Postgres binds `#-` looser than `-`, giving
`text[] - text`, which has no operator — `42883`. Parenthesised so each strip applies to the result of
the previous one; the `ELSE` branch was already correct (all `-`, left-associative).

**Attribution, because it changes what the round-3 rewrite's status is:** this statement is not from my
work. `MarkFailedAsync` is part of the uncommitted pass-2 terminal-status change that was already in
the working tree at the start of this task — `git diff` shows the whole method as added, and my only
edit to this file was the wholesale replacement of `TryAcquireExecutionLockAsync`. The fix is the same
either way; the point is that the round-3 lock rewrite was not the thing that was broken.

### CheckpointRepositoryTests: 46 / 47

The round-3 Postgres rewrite is now **validated against a live server for the first time**, along with
every other raw statement in the file. Explicitly green:

- `TryAcquireExecutionLockAsync_returns_a_terminal_row_to_Idle` — the `prior` CTE reports the revival
- `TryAcquireExecutionLockAsync_leaves_a_non_terminal_status_alone` (Idle, ScheduledWait)
- `TryClaimAsync_still_claims_an_Idle_row`, `TryClaimAsync_refuses_a_terminal_row` — the claim guard
- `DeleteExpiredAsync_*`, `DeleteTerminalExpiredAsync_*`, `DeleteStoppedCheckpointsAsync_*` — both delete predicates
- the whole `MarkFailedAsync` payload-strip family, including `R5_StrippedPayloadKeepsEverythingExceptCredentials`

### The one remaining failure — reported, not fixed

`A_row_marked_terminal_after_the_sweep_selected_it_cannot_be_claimed`
(`CheckpointRepositoryTests.cs:527`) — `InvalidOperationException: Sequence contains no matching element`.

**This is a defect in the test's setup, not in production code, and the file's own convention proves
it.** The test seeds

```csharp
await SeedCheckpointAsync(correlationId, CheckpointStatus.Idle,
    claimedAtUtc: DateTime.UtcNow, claimedByInstance: "pod-a", tenantId: "");
```

then calls `GetRecoverableAsync(TimeSpan.FromMinutes(5))` and does `.Single(...)` on the result.
`GetRecoverableAsync` filters on `ClaimedByInstance == null || ClaimedAtUtc < now - staleThreshold`;
a claim stamped `DateTime.UtcNow` is neither, so the row is correctly excluded and `Single` throws.
The partition is not a factor — `tenantId: ""` is inside the general partition that a null tenant
selects (`CheckpointPartition.OwnedByPartitionPredicate`).

The passing sibling that wants the same "recoverable" state seeds it the other way:
`GetRecoverableAsync_excludes_ScheduledWait_rows` (`:835`) uses `claimedAtUtc: staleAt`. So the fix is
in the seed — a stale `claimedAtUtc`, or `claimedByInstance: null` — not in the repository. The test's
three real assertions (mark succeeds for the owner, the sweep's later claim is refused, the row ends
`Failed`) are untouched by this and should pass once the row is actually selected.

Not fixed here: it is W2's file, and the instruction was to report rather than fix silently.

### Full API.UnitTests with Docker up: 765 / 774

Nine failures, none of them from this task's changes:

| # | failure | classification |
|---|---|---|
| 3 | `TestAdapterConnectionCommandHandlerTests` YAML-routing | pre-existing; verifier-1 recorded these three in the pass-2 tree |
| 4 | `KeyVault` `RedisCredentialChangeBusTests` / `RedisDistributedLockFactoryTests` | **flaky**, `RedisConnectionException`. A different set of four fails on each run (full run vs isolated run overlap on only two names), same count each time — Redis container connection contention. Nothing in this task touches Redis or KeyVault |
| 1 | `ScheduledContinuationIntegrationTests.PartialResult_then_resume_completes_full_session` | pre-existing, and **committed** — see below |
| 1 | `A_row_marked_terminal_after_the_sweep_selected_it_cannot_be_claimed` | test-setup defect, above |

### The ScheduledContinuation failure is pre-existing and committed

`Expected _fx.ProcessAsyncCallCount to be 1 ... but found 0`. The fixture's fake adapter has
`CanResumeFrom(checkpoint) => true` (`ScheduledContinuationFixture.cs:377`). On a first dispatch
`TryAcquireExecutionLockAsync` INSERTs the row, `ExecuteWithResumeAsync` then reads that same
just-created row back, `CanResumeFrom` says yes, and the run goes to `ResumeAsync` — `ProcessAsync` is
never called. The test encodes the older semantics in which `RetryCount` gated resume.

Not caused by this task: with `forceResume` and `revivedTerminalRow` both false,
`if (forceResume || resumable.CanResumeFrom(...))` is literally equivalent to the previous
`if (resumable.CanResumeFrom(...))`, and the refusal branch after it is unreachable. The
presence-based decision is in **committed HEAD** (`ProcessEventCommandHandler.cs:1160` in
`git show HEAD`, with the `<remarks>` at `:1117` stating "the resume decision is made on CHECKPOINT
PRESENCE, never on RetryCount"). It predates both this task and the uncommitted pass-2 change. It has
simply never run, because the suite is Docker-gated.

Worth a ticket in its own right: the test and the shipped behaviour disagree about what a first
dispatch does, and one of them is wrong.

## Postgres verification — the gap that was open all session

Docker was reported unavailable for the entire duration of this change set. That was wrong: the
tooling was installed and a `colima` VM (`default`, aarch64, 4 CPU, 10GiB) was merely **stopped**.
`colima start` produced a working Docker endpoint (server 29.5.2) and the container-backed suites ran
for the first time.

### What the first real database run found

`CheckpointRepositoryTests`: **32 of 47 passing, 15 failures, one root cause.**

```
Npgsql.PostgresException : 42883: operator does not exist: text[] - text
```

`CheckpointRepository.MarkFailedAsync`'s credential/header strip relied on `#-` and `-` binding
left-to-right. Postgres binds `#-` looser, so

```sql
platform_event_json #- ARRAY[...] - credentialsKey - headersKey
```

parsed as `platform_event_json #- (ARRAY[...] - credentialsKey - headersKey)`, whose right side is
`text[] - text`. The statement threw, nothing was ever marked terminal, and every test depending on
the mark failed behind it. **The whole retention feature was inoperative against real Postgres.**

Fixed by parenthesising each strip so it applies to the result of the previous one.

### Why six review passes did not catch it

Three verifier passes and three blind code reviews read that expression. One reviewer raised operator
precedence explicitly and it was assessed as correct by inspection. It reads correctly. Only a real
database disproves it.

### Final state

- `CheckpointRepositoryTests`: **47/47** against real Postgres.
- `Application.UnitTests` 114/114; `Infrastructure.Core.UnitTests` 449/449.
- Full `API.UnitTests` 766/774. The 8 failures are **pre-existing**: identical failing test names with
  the change stashed, in `TestAdapterConnectionCommandHandlerTests`,
  `RedisDistributedLockFactoryTests` and `ScheduledContinuationIntegrationTests`. They were invisible
  earlier only because the container runtime was stopped.
