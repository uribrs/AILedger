# Code review — force-resume on operator dispatch

## Scope reviewed

Working tree vs `HEAD` (`d53e347d`) on `feat/failed-checkpoint-retention-ttl`, restricted to the
force-resume delta. The failed-checkpoint retention work in the same working tree
(`MarkFailedAsync`, `DeleteTerminalExpiredAsync`, `CheckpointCleanupJob`, the `GetRecoverableAsync`
and `TryClaimAsync` status guards) is treated as pre-existing baseline and is only referenced where
the new code depends on it.

Files read in full or in the relevant region:

- `Domain/.../Messaging/AdapterRunMessage.cs` (`ReadForceResume`)
- `Domain/.../Models/ExecutionLockOutcome.cs` (new)
- `Domain/.../Interfaces/ICheckpointRepository.cs` (lock signature)
- `Applications/.../Commands/ProcessEventCommand.cs`
- `Applications/.../Messaging/IsbPlatformEventDispatcher.cs`
- `Applications/.../Commands/ProcessEventCommandHandler.cs` (`Handle`, `ExecuteWithResumeAsync`,
  `FlushAndCleanupCheckpointAsync`, `TryMarkCheckpointFailedAsync`, `TryEnqueueNativeFallbackAsync`,
  `HandBackForRecoveryAsync`, `CompleteExecutionAsync`, `PublishCompletionEventAsync`,
  `MapToAdapterCheckpoint`)
- `Infrastructure.Postgres/.../CheckpointRepository.cs` (`TryAcquireExecutionLockAsync`)
- `Infrastructure.Core/.../InMemoryCheckpointRepository.cs` (`TryAcquireExecutionLockAsync`)
- `Hosts/API/Controllers/EventsController.cs:601`, `:783`
- `UnitTests/.../Commands/ForcedResumeOnOperatorDispatchTests.cs`,
  `UnitTests/.../Messaging/ForceResumeWireContractTests.cs`,
  `Tests/.../Checkpoints/RetainedCheckpointIsNotConsumedTests.cs`,
  `Tests/.../Checkpoints/SweepDispatchNeverForcesResumeTests.cs`

Docs consulted: `CLAUDE.md`, `docs/coding-standards.md`, `docs/clean-architecture.md`.

## What is correct

Stated up front so the findings below are read in proportion.

**The four-arm decision is right, including the arm that is easy to break.** In
`ProcessEventCommandHandler.cs:1234` the gate is short-circuited (`forceResume || CanResumeFrom`), so a
forced dispatch never asks. The `revivedTerminalRow` refusal at `:1277` sits *after* the resume arm, so
arm 2 — not forced, row terminal but seconds old, collector accepts — still resumes. That is the
common redelivery path and a naive `if (rowWasTerminal) fail;` would have broken it; this does not.
Arm 4 (declined, non-terminal) still restarts, unchanged.

**Containment holds.** `ForceResume` is an init-only property on the command and is not part of
`PlatformEvent`, so it cannot survive the `platform_event_json` round trip. The sweep
(`CheckpointRecoveryHandler.cs:390`) constructs its command without it and never calls
`ReadForceResume`. The flag is read at exactly three entry points — the RabbitMQ trigger-flow branch
and the two HTTP trigger-flow branches — and only from the message ROOT, which is the one place the
trigger-flow mapper does not fold into the persisted event. Reading it at the root rather than under
`payload` is the load-bearing decision and it is the one that was made.

**Parsing is tolerant in the right way.** `AdapterRunMessage.ReadForceResume` requires
`ValueKind == JsonValueKind.True`, guards null/whitespace, and swallows `JsonException`. A malformed
body, a string `"true"`, a number, an array, an object, `null` and `false` all yield false without
failing the message. `ForceResumeWireContractTests` covers each of those shapes through the real
dispatcher, and separately asserts the flag is ignored under `payload`, `payload.action` and
`payload.metadata`.

**No redelivery loop.** The refusal returns `isTransient: false`, and
`IsbPlatformEventDispatcher.ShouldRetry` → `SiemRulesPageRejection.AllowsRetry(isTransient, code)` is
`isTransient && !IsPermanent(code)`, so the delivery is ACKed without requeue. The refusal cannot
spin.

**The preserve path does preserve, on the path it was designed for.** The refusal returns a `Failure`
status, `IsReportedFailure` matches, and `FlushAndCleanupCheckpointAsync:1327` calls
`TryMarkCheckpointFailedAsync`, which re-marks the row `Failed`, releases the claim and stamps
`updated_at_utc = now` — putting the row back on the terminal retention horizon rather than the
non-terminal TTL. `RetainedCheckpointIsNotConsumedTests` arm 3 verifies the row survives with its page
and item counts, that the claim is released, and — the part most reviews would skip — that the real
recovery sweep offers it zero times afterwards.

**No new wire value escapes.** `RESUME_DECLINED_RETAINED_CHECKPOINT` is used only by `ShouldRetry` and
the logs; `PublishCompletionEventAsync:2245` maps every failure to the existing `"failed"` status
string and does not carry `ErrorCode`. The only producer-side contract change is the additive optional
root `forceResume`, which still needs an explicit call-out in the PR description.

**Tests drive production paths.** Both the `Application.UnitTests` and the `API.UnitTests` suites
construct the real `ProcessEventCommandHandler`, and the checkpoint suites run it against the real
`InMemoryCheckpointRepository` and the real `CheckpointRecoveryHandler` rather than asserting on a
freshly constructed command. `SweepDispatchNeverForcesResumeTests` in particular refuses the vacuous
version of its own assertion by first proving the forced dispatch really took the resume path.

## Blockers

None.

## Important

- **I1** — `Applications/.../Commands/ProcessEventCommandHandler.cs:1277`, with
  `Infrastructure.Postgres/.../CheckpointRepository.cs:696`
  - **Problem:** `RevivedTerminalRow` is process-local state that exists only for the lifetime of one
    `Handle` call, but the revival it reports is *durable* — the lock has already committed
    `Failed → Idle`. Every exit from `Handle` other than a reported failure leaves the row
    non-terminal: pod shutdown (`:333 TryReleaseClaimAsync`), the orphan hand-back
    (`:1597 HandBackForRecoveryAsync`), and `CLAIM_LOST` all release or abandon the claim with the row
    still `Idle`. `GetRecoverableAsync` excludes only `ScheduledWait` and `Failed`, so the row is now
    sweep-visible, and the sweep dispatches it **unforced**. On that dispatch the prior status is
    `Idle`, so `revivedTerminalRow` is false, the collector's gate declines (`MapToAdapterCheckpoint`
    hands it `CreatedAtUtc`, which the lock never refreshes, so it stays ancient forever), and the
    handler falls through to `ProcessAsync` — a full restart that silently consumes the week of
    retained progress. The protection is one-shot and is lost by any interruption between taking the
    lock and reporting an outcome, which includes the entire duration of a forced resume leg that
    makes no checkpoint write. `SweepDispatchNeverForcesResumeTests` builds exactly this sequence
    (forced dispatch → row orphaned mid-leg → row left unclaimed and recoverable → sweep re-dispatches
    unforced); it asserts the flag is not inherited, which is true, but the run it hands to the sweep
    is one that will now restart from scratch.
  - **Suggestions:**
    1. Thread the revival through the interrupt exits — preferred, because it stays inside the
       change's scope and keeps the sweep able to continue runs that *did* make progress. Capture
       `lockOutcome.RevivedTerminalRow` in a field or pass it to `TryReleaseClaimAsync` /
       `HandBackForRecoveryAsync`, and when it is set call
       `checkpointRepository.MarkFailedAsync(tenantId, correlationId, platformType, category, InstanceId, ...)`
       instead of `ReleaseClaimAsync`. `MarkFailedAsync` already releases the claim, so the
       "another instance can take it" property the release exists for is unaffected; the row simply
       goes back to the terminal state it was in before this dispatch touched it.
    2. Stop depending on the revival bit for the decision. In `ExecuteWithResumeAsync`, refuse the
       non-forced restart whenever the gate declines **and** the existing row carries real progress
       (`checkpoint.CurrentPage > 0 || checkpoint.ProcessedItems > 0`), regardless of prior status.
       This is crash-proof by construction and would let `ExecutionLockOutcome` collapse back to a
       bool — but it changes arm 4 for ordinary non-terminal rows, so it is a wider behaviour change
       and needs the product call that fix 1 does not.

- **I2** — `Applications/.../Commands/ProcessEventCommandHandler.cs:1107`
  - **Problem:** on the refusal path the handler's whole purpose is to keep the row, yet
    `TryMarkCheckpointFailedAsync` falls back to `TryDeleteCheckpointAsync` when `MarkFailedAsync`
    returns false. `MarkFailedAsync` is owner-guarded, so it returns false whenever the claim was
    stolen or staled out from under this execution — and the response to "I could not mark it" is to
    destroy the retained checkpoint outright, which is strictly worse than either alternative and
    happens without an operator asking. The fallback is defensible for the ordinary failure path it
    was written for (a row that would otherwise sit non-terminal with credentials intact); it is not
    defensible for a dispatch that just refused to run *in order to preserve that row*.
  - **Suggestion:** give `TryMarkCheckpointFailedAsync` a `bool preserveOnMarkFailure` parameter
    (placed at the end of the real parameter list, before the tail group) and pass `true` from the
    refusal path. When set, log the warning and return without deleting — the row keeps another
    instance's claim or its stale claim, and the terminal-retention sweep or the next dispatch
    resolves it. The credentials-exposure concern that motivates the delete does not apply here,
    because a retained `Failed` row has already had `credentials`/`headers` stripped by the
    `MarkFailedAsync` that created it.

## Nits

- **N1** — `Infrastructure.Postgres/.../CheckpointRepository.cs:668-707`
  - **Problem:** the comment claims the `prior` CTE can only misreport in the safe direction. The
    unsafe direction also exists: under READ COMMITTED the CTE reads the statement snapshot while
    `ON CONFLICT DO UPDATE` re-reads the latest committed row, so a peer whose `MarkFailedAsync`
    commits *during* this statement makes `prior` report the pre-failure status while the conflict
    path sees `Failed` and revives it. `revivedTerminalRow` then comes back false for a revival that
    really happened, and the caller restarts. The window is small and the checkpoint in that scenario
    is seconds old (so the gate would accept anyway), which is why this is a nit rather than a
    finding against the behaviour.
  - **Suggestion:** make the read and the update see the same row by locking it:
    `WITH prior AS (SELECT status FROM adapter_checkpoints WHERE ... FOR UPDATE)`. That serialises the
    peer's mark against this statement and lets the comment say "cannot misreport" instead of
    "misreports in the safe direction only".

- **N2** — `Infrastructure.Postgres/.../CheckpointRepository.cs:647-654`, `:709-711`, `:752-754`
  - **Problem:** the rewritten `TryAcquireExecutionLockAsync` body is new code and uses `var`
    throughout (`var sql`, `var connection`, `var openedHere`, `var acquired`, `var priorStatus`,
    `var revivedTerminalRow`), against `docs/coding-standards.md`. It is inconsistent even within the
    diff — the new `MarkFailedAsync` immediately above names every type. Some of the `var`s here are
    genuinely pre-existing lines carried through the rewrite, but the six named above are new.
  - **Suggestion:** name them — `string sql`, `DbConnection connection`, `bool openedHere`,
    `bool acquired`, `string? priorStatus`, `bool revivedTerminalRow`. Do not sweep the untouched
    `var`s elsewhere in the file.

- **N3** — `Applications/.../Commands/ProcessEventCommandHandler.cs:1291-1297`
  - **Problem:** the refusal calls `AdapterResult.FailureResult(message, code, null, false)` with
    positional arguments across a package boundary, where `:1601` and `:1604` in the same file use
    `exception: null, isTransient: false`. `isTransient` is load-bearing here — it is what stops the
    refusal from being redelivered forever — and a positional `false` is the one form that silently
    survives a parameter reorder in `Cymulate.Integration.Client`.
  - **Suggestion:** use named arguments: `exception: null, isTransient: false`.

- **N4** — `Applications/.../Commands/ProcessEventCommandHandler.cs:113-116`
  - **Problem:** a forced dispatch against a `ScheduledWait` row short-circuits at `:111` and returns
    a success result. That is the right behaviour — the scheduler owns the resume — but the log line
    does not mention that a force marker was present and ignored, so an operator who forces a resume
    and gets "success" with no collection has nothing to read.
  - **Suggestion:** add `request.ForceResume` to that template, e.g.
    `"Skipping {EventId}: session is in ScheduledWait until {ResumeAt}. ForceResume: {ForceResume}"`.

- **N5** — `Domain/.../Messaging/AdapterRunMessage.cs:47-48`
  - **Problem:** the doc comment explains *why* this is not a bound property (a typed `bool` would
    throw and fail the whole message), which is genuinely non-obvious and worth keeping, but the
    surrounding two sentences restate what the three lines of body already show.
  - **Suggestion:** keep the "deliberately not a bound property" sentence and the root-only sentence;
    drop "Only a JSON `true` is a force — every other value, and an unreadable body, is false", which
    the `ValueKind == JsonValueKind.True` check states more precisely than prose can.

## Open questions

- The refusal publishes a failed done for the run (`CompleteExecutionAsync` → `PublishCompletionEventAsync`).
  For a retained checkpoint whose original run already published a failed done a week ago, the platform
  now receives a second failed done on the same correlation id. Is a duplicate terminal event on an
  already-terminal run acceptable to the platform, or should the refusal suppress the done the way the
  `CLAIM_LOST` path does?
- Is an operator's forced dispatch expected to reuse the original `correlationId`? The checkpoint key
  is `(TenantId, CorrelationId, PlatformType, Category)`, so a forced dispatch under a new correlation
  id finds no row and degrades to a fresh collection (logged, per
  `A_force_with_nothing_to_resume_is_logged`). If the runbook does not say "same correlation id", the
  feature is reachable only by accident.
- `TryEnqueueNativeFallbackAsync` propagates the force to the native collector's own checkpoint row,
  which is a different row under a different `PlatformType`. Was that intended as "the operator's
  intent follows the logical run", or should the force apply only to the row the operator was looking
  at when they decided to resume?
- Nothing exercises the HTTP entry points at `EventsController.cs:601` and `:783` for the flag. They
  call the same static as the RabbitMQ path, so the risk is low — is that considered covered by
  `ForceResumeWireContractTests`, or is an `API.UnitTests` case wanted for the queued
  (`QueueTriggerFlowAsync`) route specifically, since that one carries the command through the
  background queue?

## Untested and materially risky

- The Postgres `TryAcquireExecutionLockAsync` CTE — the actual revival and prior-status read — is
  covered only by `CheckpointRepositoryTests`, which is Testcontainers-backed and did not execute
  here. The in-memory store's parity implementation is what every other suite exercises, and it
  cannot reproduce the snapshot race in N1 or the concurrent-claim denial at all.
- No test covers I1: a revived row whose dispatch is interrupted before it reports an outcome. The
  fixture in `SweepDispatchNeverForcesResumeTests` is one seed-status change away from being that
  test — seed `CheckpointStatus.Failed` instead of `Idle` and assert the row is `Failed` again after
  the interrupted forced leg.
- Two replicas racing the same retained row across the lock is only reasoned about, never executed.
  The loser gets `EXECUTION_LOCKED` and ACKs, which is pre-existing and fine; what is untested is
  whether the winner's `prior_status` read is correct when the loser's statement overlaps it.
