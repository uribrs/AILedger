# Review — forced resume on operator dispatch

## Scope reviewed

Working-tree state of `feat/failed-checkpoint-retention-ttl`, restricted to the six files named by the
caller (the failed-checkpoint-retention change in the same tree was read only as context, not reviewed):

- `Domain/.../Messaging/AdapterRunMessage.cs`
- `Application/Commands/ProcessEventCommand.cs`
- `Application/Messaging/IsbPlatformEventDispatcher.cs`
- `Application/Commands/ProcessEventCommandHandler.cs` — `ForceResume` threading only
- `Application.UnitTests/Commands/ForcedResumeOnOperatorDispatchTests.cs`
- `API.UnitTests/Checkpoints/SweepDispatchNeverForcesResumeTests.cs`

Read for context: `TriggerFlowMapper`, `CheckpointRecoveryHandler`, `EventsController`,
`Postgres/CheckpointRepository` (`GetAsync`, `TryAcquireExecutionLockAsync`),
`IsbPlatformEventDispatcherTests`. Docs consulted: `CLAUDE.md`, `docs/coding-standards.md`,
`docs/clean-architecture.md`.

## Containment verdict (the question that mattered most)

**Clean on every route I could follow.** The flag lives on `ProcessEventCommand` only; it is not a
member of `PlatformEvent`, and `PlatformEventJson` is written from
`JsonSerializer.Serialize(platformEvent)` at `ProcessEventCommandHandler.cs:134`, so the persisted
column structurally cannot carry it. The sweep rehydrates that column
(`CheckpointRecoveryHandler.cs:247`) and builds `new ProcessEventCommand(evt) { CapacityPreAcquired =
true }` (`:390`) — a fresh object with the flag at its `false` default. Placing the wire field at the
message **root** rather than in `payload` is what makes this hold: `AdapterRunPayload` /
`AdapterRunPayloadFlat` carry `[JsonExtensionData]`, and `TriggerFlowMapper.CopyAdditionalProperties`
copies that extension data into `PlatformEvent.Payload`, which is exactly what gets persisted. The DTO
comment saying so is accurate and load-bearing.

`grep -rn "new ProcessEventCommand"` finds 16 construction sites; only
`IsbPlatformEventDispatcher.cs:234` sets the flag. The default is inert at all the others.

Two remaining routes and their verdicts:

- **Broker retry** — `MessageOutcome.Retry` republishes the original body, so a redelivery re-reads
  `forceResume: true` and forces again. That is the same operator dispatch, and it is bounded by the
  existing `x-retry-count` budget. Correct as designed.
- **Native YAML fallback re-dispatch** — drops the flag. Contained, but silently, which is I2 below.

Concurrency is unchanged: the flag is read after `TryAcquireExecutionLockAsync` has already elected a
single owner, and it alters neither the claim, the heartbeat, nor the `ActiveExecutions` guard. Nothing
here is racy across replicas.

## Blockers

None.

## Important

- **I1** — `Hosts/.../API/Controllers/EventsController.cs:609` and `:780`
  - **Problem:** `POST /events/publish` and `/events/publish/sync` accept the *same* trigger-flow JSON
    body (`TriggerFlowMapper.IsTriggerFlowMessage` at `:80` / `:303`) and dispatch
    `new ProcessEventCommand(platformEvent)` with no `ForceResume`. `AdapterRunMessage` is now a shared
    contract carrying a field that one of its two parsers honours and the other silently discards — an
    operator who forces a run over HTTP gets a full restart and no indication the flag was ignored.
  - **Suggestions:**
    1. Preferred — move the parse into the mapper as `public static bool
       TriggerFlowMapper.ReadForceResume(string jsonMessage)`, call it from `IsbPlatformEventDispatcher`
       (replacing the private copy) and from both controller paths:
       `new ProcessEventCommand(platformEvent) { ForceResume = TriggerFlowMapper.ReadForceResume(jsonMessage) }`.
       One reader, one options instance, no layering change — `Infrastructure.Core` is already referenced
       by both call sites.
    2. If HTTP is deliberately not an operator route, reject it explicitly: in
       `ProcessTriggerFlowAsync`/`QueueTriggerFlowAsync`, `BadRequest(... "FORCE_RESUME_NOT_SUPPORTED")`
       when the body carries the flag, so it fails loudly instead of being dropped.

- **I2** — `Application/Commands/ProcessEventCommandHandler.cs:729`
  - **Problem:** `TryEnqueueNativeFallbackAsync` re-dispatches the *same logical run* (same correlation
    id) to the vendor's native collector with `new ProcessEventCommand(nativeEvent)` — the force is
    dropped. A forced run whose YAML adapter reports no definition therefore reaches the native
    collector unforced, where the stale checkpoint is declined and the run restarts from scratch: the
    exact outcome the operator forced the dispatch to avoid, with nothing in the logs tying the two.
  - **Suggestion:** thread it through. `TryEnqueueNativeFallbackAsync(platformEvent, category,
    request.ForceResume, cancellationToken)`, with the new `bool forceResume` parameter placed after
    `category` (before the `cancellationToken` tail per `docs/coding-standards.md`), and
    `new ProcessEventCommand(nativeEvent) { ForceResume = forceResume }`. Containment is unaffected —
    the fallback is an in-process re-queue of the same dispatch, not a later automatic recovery.

- **I3** — `Application/Commands/ProcessEventCommandHandler.cs:1187` and `:1197`
  - **Problem:** both early returns run `adapter.ProcessAsync` — a full restart — before the
    forced-resume log at `:1204` is reached. So the two ways a force degrades into a full recollection
    (adapter is not `IResumableAdapter`; no checkpoint row) produce a run that behaves like an ordinary
    fresh start with **zero** trace that a force was requested. `A_forced_dispatch_with_no_checkpoint_row_starts_fresh`
    pins the behaviour, which is right; it is the diagnosability that is missing.
  - **Suggestion:** log the force on both branches before returning, e.g.
    ```csharp
    if (checkpoint == null)
    {
        if (forceResume)
        {
            logger.LogWarning(
                "Forced resume requested but no checkpoint row exists for {CorrelationId}/{Platform}/{Category}; starting fresh.",
                platformEvent.CorrelationId, platformEvent.ProductType, category);
        }
        return await adapter.ProcessAsync(platformEvent, cancellationToken);
    }
    ```
    and the equivalent ("adapter is not resumable") at `:1187`. Assert it in
    `ForcedResumeOnOperatorDispatchTests` — the `RecordingLogger` is already there for it.

- **I4** — `Application.UnitTests/Messaging/IsbPlatformEventDispatcherTests.cs` (missing coverage)
  - **Problem:** both new suites drive real production paths — `SweepDispatchNeverForcesResumeTests`
    in particular runs a real forced dispatch through the real handler and the real sweep over a shared
    store, which is the right shape and does prove the round trip. But the wire→command hop is
    untested: nothing exercises `ReadForceResume` or `ProcessTriggerFlowAsync`. The entire containment
    argument rests on the flag being read from the message **root** and nowhere else, and that fact is
    currently asserted by no test. A later refactor that moves the read into `payload` breaks
    containment while every new test still passes.
  - **Suggestion:** add three cases to the existing `IsbPlatformEventDispatcherTests`, which already
    captures the dispatched `ProcessEventCommand` via the mediator callback (`:172-176`): a trigger-flow
    body with root `"forceResume": true` → `captured.ForceResume` is true; the same body without the
    field → false; the same body with `forceResume` inside `payload.action` → **false** (this is the
    one that guards the placement invariant). If I1's option 1 is taken, test
    `TriggerFlowMapper.ReadForceResume` directly instead and keep one dispatcher-level wiring test.

## Nits

- **N1** — `Application/Commands/ProcessEventCommandHandler.cs:113` and `:153`
  - **Problem:** the `ScheduledWait` short-circuit and the `EXECUTION_LOCKED` denial both discard a
    forced dispatch and log nothing about it. The `ScheduledWait` case even returns
    `AdapterResult.SuccessResult`, so the operator sees a success for a run that did not resume. Both
    are correct behaviour (the scheduler / the current owner owns the run) but a swallowed force is
    invisible when someone asks why nothing happened.
  - **Suggestion:** add `ForceResume: {ForceResume}` with `request.ForceResume` as the argument to both
    templates. Cheap, greppable, no behaviour change.

- **N2** — `Domain/.../Messaging/AdapterRunMessage.cs:47` / `IsbPlatformEventDispatcher.cs:554`
  - **Problem:** a producer that puts `forceResume` inside `payload` or `payload.action` gets it
    swallowed by `[JsonExtensionData]` and silently ignored — the flag reads as absent and the run
    restarts. The DTO comment warns readers of this repo, but nothing warns the operator.
  - **Suggestion:** in `ReadForceResume`, when the root flag is false and
    `runMessage?.Payload?.Action?.AdditionalProperties` contains a `forceResume` key, log a warning
    naming the correlation id and that the marker must sit at the message root. One `if`, no behaviour
    change.

- **N3** — PR description
  - **Problem:** `AdapterRunMessage` / `AdapterRunMessageFlat` are inbound wire contracts, and
    `CLAUDE.md` requires a DTO change to be called out explicitly rather than slipped in. The addition
    is additive and inbound-only — I confirmed neither record is ever serialized outbound, so no
    consumer's payload changes shape — but the platform side has to start emitting the field for the
    feature to do anything.
  - **Suggestion:** state in the PR body that `forceResume` is a new optional root field on both
    trigger-flow formats, absent ⇒ false, and name the producer that will emit it.

## What is correct and needs no change

- Placement of the flag at the message root, and the reason given for it — verified against
  `CopyAdditionalProperties` and the `PlatformEventJson` write.
- The default's inertness at all 15 other `new ProcessEventCommand` sites.
- Parsing robustness: `ReadForceResume`'s `catch (JsonException)` is effectively unreachable, since
  `TriggerFlowMapper.TryParseToPlatformEvent` already deserialized the same body into the same type
  with equivalent options and returned `Retry` on failure. Absent field ⇒ `false`; case-insensitive
  matching accepts `ForceResume`; a flat-format body still binds the root field because the extra
  payload members are simply unmapped. Harmless as written.
- Naming and style in the new code: `_triggerFlowJsonOptions` (private static ⇒ `_camelCase`), explicit
  types (`bool forceResume`, `AdapterRunMessage? runMessage`, `ProcessEventCommand command = new(...)`),
  braces, structured log templates with named placeholders. No violations found.
- No new project references; no layering movement.
- Interaction with the sibling retention work: `GetAsync` does not filter by status, and
  `TryAcquireExecutionLockAsync` preserves progress fields while resetting `Failed` → `Idle`, so a force
  against a retained failed row finds its checkpoint intact.

## Open questions

1. Is the operator's force delivered only as a `collectors.run` message, or can it also arrive at
   `POST /events/publish` (which accepts the identical body)? The answer decides whether I1 is a fix or
   an explicit rejection.
2. `IResumableAdapter.CanResumeFrom` ships in the `Cymulate.Integration.Client` package, so I could not
   read any real implementation — only test doubles. Does it gate on anything besides checkpoint age
   (adapter-state schema version, flow mismatch, cursor validity)? If it does, `forceResume` bypasses
   those too, and both the DTO comment and `ProcessEventCommand.ForceResume`'s doc — which say
   "staleness gate" — understate what is being skipped.
3. When a forced `ResumeAsync` throws because the state is unreadable, the run takes the generic
   `HandleUnhandledExceptionAsync` path and, if classified transient, is redelivered with the same
   forcing body — repeating the same failure until the retry budget is spent. Is that the intended
   handling, or should a forced resume that throws fall back to a fresh start on the next attempt?
