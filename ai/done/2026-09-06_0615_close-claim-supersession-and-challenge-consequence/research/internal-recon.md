# Internal Recon

Branch `conceptual-gaps-v2.1`, HEAD `310061f`. Read-only pass. Every rule cited in
`CommandHandler.cs` (command time) is paired with its `TaskTransitionValidator.cs` counterpart
(replay time, called from `TaskReducer.Apply` before applying).

## Durable sources read

- `src/AILedger.Core/Application/CommandHandler.cs` (968 lines) — command-time rule copy.
- `src/AILedger.Core/Domain/TaskTransitionValidator.cs` (892 lines) — replay-time rule copy.
- `src/AILedger.Core/Domain/TaskReducer.cs` (218) — state application; calls the validator at `:10`.
- `src/AILedger.Core/Contracts/Commands.cs`, `Events.cs`, `GovernanceModels.cs`, `TaskState.cs`.
- `src/AILedger.Core/Domain/AuthorizationPolicy.cs` — command-time capability gate.
- `src/AILedger.Cli/CliApplication.cs` (745), `CommandLine.cs`, `RoleDefaults.cs`.
- `src/AILedger.Storage/MarkdownTaskProjectionWriter.cs`, `LedgerJson.cs`,
  `FileGovernedTaskService.cs` (envelope/sequence validation).
- All 21 non-obj test files under `tests/AILedger.Tests/`.
- `docs/architecture.md`, `docs/operator-guide.md`.

---

## A. `ResolveClaimCommand` / `ClaimResolved` / `Claim` end to end

| Layer | Location |
| --- | --- |
| Command record | `src/AILedger.Core/Contracts/Commands.cs:53-59` — `ResolveClaimCommand(ActorId, EventId?, string CorrelationId, ClaimId, ClaimStatus Status, IReadOnlyList<EvidenceId> EvidenceIds)` |
| Command JSON discriminator | `Commands.cs:9` — `"claim.resolve"` |
| Command-time enum check | `CommandHandler.cs:775-777` (`ValidateEnums` → `RequireDefined(resolve.Status)`) |
| Command-time capability | `AuthorizationPolicy.cs:51` — `ResolveClaimCommand => [Capability.ResolveClaim]` |
| Handler dispatch | `CommandHandler.cs:70` — `ResolveClaimCommand resolve => ResolveClaim(state, resolve)` |
| Handler method | `CommandHandler.cs:137-179` |
| Status-transition guard (cmd) | `CommandHandler.cs:849-865` `EnsureClaimResolution` — rejects `Open` target, rejects re-resolving a `Rejected`/`Superseded` claim, rejects no-op `claim.Status == target` |
| Evidence requirement (cmd) | `CommandHandler.cs:146-149` — `Validated` or `Rejected` needs ≥1 evidence. `Superseded` needs none. |
| Evidence direction (cmd) | `CommandHandler.cs:151-166` + `EvidenceDirection` at `:181-186` — `Validated` needs `evidence.Supports.Contains(claimId)`, `Rejected` needs `Refutes`, everything else (`Superseded`) is `_ => true` |
| Event emission | `CommandHandler.cs:168-178` — always `ClaimResolved`, then `AddDependencyInvalidations` when status is `Rejected` **or** `Superseded` (`:173-176`) |
| Event record | `src/AILedger.Core/Contracts/Events.cs:44` — `ClaimResolved(ClaimId, ClaimStatus, IReadOnlyList<EvidenceId>)`; discriminator `"claim.resolved"` at `Events.cs:19` |
| Validator dispatch | `TaskTransitionValidator.cs:24-26` |
| Validator method | `TaskTransitionValidator.cs:178-211` |
| Replay capability | `TaskTransitionValidator.cs:183` — `RequireAuthority(state, @event.ActorId, Capability.ResolveClaim)` |
| Status-transition guard (replay) | `TaskTransitionValidator.cs:767-778` `EnsureClaimResolution` — same rule, **different message** (one collapsed message instead of three) |
| Evidence requirement (replay) | `TaskTransitionValidator.cs:190-193` |
| Evidence direction (replay) | `TaskTransitionValidator.cs:195-210` — **different message wording** from the command copy |
| Reducer applier | `TaskReducer.cs:17` → `ResolveClaim` at `TaskReducer.cs:84-93`; sets `Status` and **replaces** `EvidenceIds` wholesale with the event's list |
| Domain record | `GovernanceModels.cs:124-130` — `Claim(ClaimId Id, string Statement, ClaimStatus Status, IReadOnlyList<EvidenceId> EvidenceIds, string? ConsequenceIfWrong, Provenance Provenance)` |
| `ClaimStatus` enum | `GovernanceModels.cs:71-77` — `Open, Validated, Rejected, Superseded` |
| CLI dispatch | `CliApplication.cs:149-154` — `case "claim resolve"` |
| CLI AllowedOptions | `CliApplication.cs:606-607` — `Options("root","task","actor","id","status","evidence","cause","correlation")` |
| CLI help line | `CliApplication.cs:712` — `claim resolve  --task ID --actor ID --id ID --status STATUS [--evidence ID]` |
| Operator-guide duplicate of that help line | `docs/operator-guide.md:109` (and a worked example at `:74`) |
| Markdown projection | `MarkdownTaskProjectionWriter.cs:112-125` `RenderAssumptions` → `assumptions.md`; line format at `:121`: ``- `{id}` — **{Status}** — {Statement} Evidence: {list}.{consequence}`` |
| Context artifact | `ContextAssembler.cs:252-257` — `$"{claim.Status}: {claim.Statement}"`, references = evidence ids |

**Claim-construction sites** (a new required `Claim` member touches all of them):
`CommandHandler.cs:127-133` (`AddClaim`), `TaskReducer.cs:86-90` (`ResolveClaim` `with`-expression),
plus test constructions at `tests/AILedger.Tests/Core/ReducerTests.cs:24-26`.
Validator `ValidateClaimAdded` at `TaskTransitionValidator.cs:163-176` asserts a new claim is
`Open` with zero evidence (`:170-173`) — a new nullable field will need an equivalent "must be
null on add" assertion in both copies.

**Serialization**: `LedgerJson.CreateOptions` (`src/AILedger.Storage/LedgerJson.cs:8-21`) —
camelCase properties, `JsonStringEnumConverter(camelCase, allowIntegerValues: false)`,
`DefaultIgnoreCondition = WhenWritingNull`. A new nullable `Claim` member is therefore omitted from
JSON when null, so old `events.jsonl` / `state.json` deserialize without migration.

---

## B. `AddDependencyInvalidations` and the invalidation events

**Exact current filters** — `CommandHandler.cs:807-833`:

```
foreach (var decision in state.Decisions.Values
             .Where(item => item.DependsOnClaims.Contains(claimId))
             .Where(item => item.Status is DecisionStatus.Proposed or DecisionStatus.Accepted)
             .OrderBy(item => item.Id.Value, StringComparer.Ordinal))
{
    events.Add(new DecisionInvalidated(decision.Id, claimId));
}

foreach (var workItem in state.WorkItems.Values
             .Where(item => item.DependsOnClaims.Contains(claimId))
             .Where(item => item.Status is not WorkItemStatus.Stale)
             .OrderBy(item => item.Id.Value, StringComparer.Ordinal))
{
    var status = workItem.Status == WorkItemStatus.Active
        ? WorkItemStatus.Blocked
        : WorkItemStatus.Stale;
    events.Add(new WorkItemInvalidated(workItem.Id, claimId, status));
}
```

Ordering is deterministic: decisions first, then work items, each ordinal by id.
Only caller today: `CommandHandler.cs:175` inside `ResolveClaim`.

**`DecisionInvalidated` replay preconditions** — `TaskTransitionValidator.cs:308-322`. Verbatim:

```
RequireAuthority(state, @event.ActorId, Capability.ResolveClaim);
var decision = Get(state.Decisions, invalidated.DecisionId, "decision");
EnsureDecisionIsCurrent(decision);
var claim = Get(state.Claims, invalidated.RejectedClaimId, "claim");
if (claim.Status is not (ClaimStatus.Rejected or ClaimStatus.Superseded) ||
    !decision.DependsOnClaims.Contains(claim.Id))
{
    throw new GovernanceException("A decision can only be invalidated by one of its rejected or superseded claims.");
}
```

`EnsureDecisionIsCurrent` (`:759-765`) throws when status is `Superseded` or `Invalidated`.
So a caller must satisfy, **at the moment this event is applied**: actor holds `ResolveClaim`;
decision exists and is `Proposed` or `Accepted`; the named claim exists, is already
`Rejected`/`Superseded`, and is in `decision.DependsOnClaims`.
**Consequence:** the `ClaimResolved` event must be sequenced *before* the invalidations, which the
current batch order does.

**`WorkItemInvalidated` replay preconditions** — `TaskTransitionValidator.cs:387-417`. Verbatim:

```
RequireAuthority(state, @event.ActorId, Capability.ResolveClaim);
RequireDefined(invalidated.Status, nameof(invalidated.Status));
var workItem = Get(state.WorkItems, invalidated.WorkItemId, "work item");
if (workItem.Status is WorkItemStatus.Stale)
{
    throw new GovernanceException("An already invalidated work item cannot be invalidated again.");
}

var expectedStatus = workItem.Status == WorkItemStatus.Active
    ? WorkItemStatus.Blocked
    : WorkItemStatus.Stale;
if (invalidated.Status != expectedStatus)
{
    throw new GovernanceException($"Work item invalidation must transition to '{expectedStatus}'.");
}

var claim = Get(state.Claims, invalidated.RejectedClaimId, "claim");
if (claim.Status is not (ClaimStatus.Rejected or ClaimStatus.Superseded) ||
    !workItem.DependsOnClaims.Contains(claim.Id))
{
    throw new GovernanceException("A work item can only be invalidated by one of its rejected or superseded claims.");
}
```

The `Status` carried on the event is not free: the validator recomputes it from the work item's
current status and refuses a mismatch. Both copies carry the R3 comment
(`CommandHandler.cs:822-824`, `TaskTransitionValidator.cs:395-397`) explaining why `Blocked` is
*not* skipped: an operator `work block` writes the same member, so skipping it would leave a
manually blocked item permanently un-staled.

Reducers: `TaskReducer.cs:101-105` (`InvalidateDecision`, forces `DecisionStatus.Invalidated`),
`TaskReducer.cs:113-117` (`InvalidateWorkItem`, writes the event's `Status` verbatim).
Event records: `Events.cs:48` and `Events.cs:52`. No CLI command emits either directly.

---

## C. `DisposeChallengeCommand` / `ChallengeDisposed` end to end

| Layer | Location |
| --- | --- |
| Command record | `Commands.cs:99-104` — `DisposeChallengeCommand(ActorId, EventId?, string, ChallengeId, ChallengeStatus Status)`; discriminator `"challenge.dispose"` at `Commands.cs:14` |
| Enum check | `CommandHandler.cs:781-783` |
| Capability (cmd) | `AuthorizationPolicy.cs:56` — `[Capability.DisposeChallenge]` |
| Handler dispatch | `CommandHandler.cs:75` |
| Handler method | `CommandHandler.cs:315-331` — only two rules: challenge must be `Open` (`:320-323`), disposition must not be `Open` (`:325-328`). Emits exactly `[new ChallengeDisposed(id, status)]`. **No evidence check, no target-type branch, no consequence event.** |
| Event record | `Events.cs:50` — `ChallengeDisposed(ChallengeId, ChallengeStatus)`; discriminator `"challenge.disposed"` at `Events.cs:25` |
| Validator dispatch | `TaskTransitionValidator.cs:42-44` |
| Validator method | `TaskTransitionValidator.cs:344-356` — `RequireAuthority(..., Capability.DisposeChallenge)` then one combined guard: `challenge.Status != Open \|\| disposed.Status == Open` → `"Only an open challenge can transition to a terminal disposition."` |
| Reducer | `TaskReducer.cs:23` → `DisposeChallenge` at `TaskReducer.cs:107-111` — sets `Status` only |
| `ChallengeStatus` enum | `GovernanceModels.cs:87-93` — `Open, Supported, Rejected, Withdrawn` |
| `Challenge` record | `GovernanceModels.cs:150-157` — `TargetType` and `TargetId` are **plain `string`**, not typed ids |
| CLI dispatch | `CliApplication.cs:180-184` |
| CLI AllowedOptions | `CliApplication.cs:619-620` — `Options("root","task","actor","id","status","cause","correlation")` |
| CLI help line | `CliApplication.cs:720` — `challenge dispose  --task ID --actor ID --id ID --status supported\|rejected\|withdrawn`; duplicated at `docs/operator-guide.md:117` |
| Markdown projection | `MarkdownTaskProjectionWriter.cs:103-107` — **Open Challenges only**; disposed challenges vanish from `task.md` |
| Archive prerequisite | `CommandHandler.cs:881-884` and `TaskTransitionValidator.cs:815-820` — open challenges block `TaskStage.Archive` |

**Raise side** (needed for Gap B evidence): `RaiseChallengeCommand` at `Commands.cs:89-97`;
handler `CommandHandler.cs:290-313`; validator `TaskTransitionValidator.cs:324-342`; CLI dispatch
`CliApplication.cs:174-179`, options `:616-618`, help `:718-719`. Challenge evidence is validated
only for *existence* (`CommandHandler.cs:300-301` / `TaskTransitionValidator.cs:338-339`) — never
for direction against the target.

**`EnsureChallengeTargetExists` — the target-type mapping.** Two byte-identical copies:
`CommandHandler.cs:888-902` and `TaskTransitionValidator.cs:792-806`:

```
var exists = targetType.Trim().ToLowerInvariant() switch
{
    "claim"    => state.Claims.Keys.Any(id => id.Value == targetId.Trim()),
    "decision" => state.Decisions.Keys.Any(id => id.Value == targetId.Trim()),
    "work" or "workitem" or "work-item" => state.WorkItems.Keys.Any(id => id.Value == targetId.Trim()),
    _ => throw new GovernanceException($"Unsupported challenge target type '{targetType}'.")
};

if (!exists)
{
    throw new GovernanceException($"Challenge target '{targetType}:{targetId}' does not exist.");
}
```

Accepted strings, after `Trim().ToLowerInvariant()`: **`claim`, `decision`, `work`, `workitem`,
`work-item`** — five spellings, three kinds. Anything else throws. The stored `Challenge.TargetType`
is `command.TargetType.Trim()` (`CommandHandler.cs:306`) — trimmed but **case preserved**, so a
disposition consequence must re-lower it, and the same normalisation must exist in both copies.
There is no shared helper to reuse; both copies must be edited.

---

## D. Emitting multiple events from one command

`CommandHandler.ApplyEvents` — `CommandHandler.cs:720-749`. The loop verbatim (`:731-746`):

```
for (var index = 0; index < eventData.Count; index++)
{
    var @event = new LedgerEvent(
        GovernedTaskState.CurrentSchemaVersion,
        CreateEventId(taskId, state?.Version ?? 0),
        taskId,
        command.ActorId,
        now,
        previous?.EventId ?? command.CausationId,
        command.CorrelationId.Trim(),
        eventData[index]);

    state = _reducer.Apply(state, @event);
    events.Add(@event);
    previous = @event;
}
```

**Event id assignment** — `CreateEventId` at `CommandHandler.cs:751-752`:
`$"{taskId.Value}:{currentVersion + 1:D10}"`. `state` is reassigned on line `:743` each iteration,
and `TaskReducer.Apply` bumps `Version` by exactly 1 (`TaskReducer.cs:40`), so ids are strictly
sequential across the batch.

**Causation chaining** — `:739`: the first event carries `command.CausationId`; every later event
carries the *previous event's* `EventId`. A batch is one causal chain, not a fan-out.
Asserted at `tests/AILedger.Tests/Core/CommandAndLifecycleTests.cs:25`.

**Correlation** — `:740`: every event in the batch shares the command's correlation id
(the CLI computes one per invocation, `CommandLine.cs:14-19`).

**Order and state visibility — confirmed.** `state = _reducer.Apply(state, @event)` runs *inside*
the loop, and `TaskReducer.Apply` calls `TaskTransitionValidator.Validate(state, @event)` at
`TaskReducer.cs:10` **before** applying. Therefore:

> A later event in the same batch is validated against the state produced by the earlier events of
> that same batch, **not** against the state at command start.

This is already load-bearing: `AddDependencyInvalidations` emits `DecisionInvalidated` after
`ClaimResolved`, and `ValidateDecisionInvalidated` (`TaskTransitionValidator.cs:317`) requires the
claim to *already* be `Rejected`/`Superseded`. The batch only validates because the earlier event
was applied first. The same mechanism will carry
`ChallengeDisposed → ClaimResolved → DecisionInvalidated/WorkItemInvalidated`.

**Downstream envelope check** — `FileGovernedTaskService.cs:340-353` re-walks the batch and
`ValidateEventEnvelope` (`:364-389`) requires each id to be exactly
`{taskId}:{index+1:D10}` and each `CausationId` to name a *prior* event
(`IsPriorEventId`, `:391-402`, requires `sequence <= priorEventCount`). Chaining to the immediately
preceding event satisfies this (`sequence == priorEventCount`). It also asserts
`outcome.State.Version == startVersion + Events.Count` (`:347-352`) — so a batch must produce
exactly one version bump per event.

---

## E. Everything that breaks if `AgentRunStatus.Pending` and `WorkItemStatus.Ready` are deleted

### `WorkItemStatus.Ready`

Declaration: `src/AILedger.Core/Contracts/GovernanceModels.cs:98`.
**Reads in `src/`: zero. Reads in `tests/`: zero.** `grep -rn "WorkItemStatus\.Ready" src tests`
returns nothing. Deletion is a one-line change with no guard to simplify.

> **Do not confuse it with `TaskStage.Ready`** (`GovernanceModels.cs:9`), which is live:
> `StageTransitionPolicy.cs:13-14`, `tests/.../CommandAndLifecycleTests.cs:43,57,74`.
> The two share a name and nothing else.

### `AgentRunStatus.Pending`

Declaration: `GovernanceModels.cs:108`. **Never written** — `StartRun` constructs
`AgentRunStatus.Active` directly (`CommandHandler.cs:403`) and the validator requires exactly that
(`TaskTransitionValidator.cs:426`). Every occurrence below is a read in a guard; all ten simplify
by dropping the `Pending` disjunct.

| # | File:line | Guard | Effect of deletion |
| --- | --- | --- | --- |
| 1 | `CommandHandler.cs:390` | `run.Status is AgentRunStatus.Pending or AgentRunStatus.Active` — one-active-run check in `StartRun` | → `run.Status is AgentRunStatus.Active` |
| 2 | `CommandHandler.cs:416` | `if (run.Status is not (AgentRunStatus.Active or AgentRunStatus.Pending))` in `CompleteRun` | → `is not AgentRunStatus.Active`; message at `:418` `"Only an active or pending run can be completed."` needs rewording |
| 3 | `CommandHandler.cs:421` | `if (command.Status is AgentRunStatus.Active or AgentRunStatus.Pending)` — terminal-status check | → `== AgentRunStatus.Active` |
| 4 | `CommandHandler.cs:714` | `HasOpenRun` | → `Active` only |
| 5 | `CommandHandler.cs:876` | archive prerequisite | → `Active` only |
| 6 | `TaskTransitionValidator.cs:450` | replay copy of #1 | → `Active` only |
| 7 | `TaskTransitionValidator.cs:469-470` | replay copy of #2 and #3 combined | message at `:472` `"Only an active or pending run can transition to a terminal status."` needs rewording |
| 8 | `TaskTransitionValidator.cs:670` | replay copy of #4 (inlined in `ValidateWorkItemCompleted`) | → `Active` only |
| 9 | `TaskTransitionValidator.cs:816` | replay copy of #5 | → `Active` only |

Pairs: 1↔6, 2/3↔7, 4↔8, 5↔9. Every command-time guard has its replay twin; none is orphaned.

**Not to be touched** (name collision on the substring `Pending`, unrelated concept):
`GovernedTaskState.PendingOpeningActor` at `TaskState.cs:24`, written at `TaskReducer.cs:73`,
cleared at `TaskReducer.cs:81`, read at `TaskTransitionValidator.cs:144`.

### Serialization, fixtures and JSON string literals

- Enum values are serialised by name in camelCase with `allowIntegerValues: false`
  (`LedgerJson.cs:18`). A persisted `"pending"` or `"ready"` would therefore fail to deserialize
  after removal.
- **No `.ailedger`, `events.jsonl`, or kernel-fixture file exists anywhere in the repo.**
  `find . -name "events.jsonl"` returns nothing; every test builds its log at runtime under
  `TemporaryDirectory` (`tests/AILedger.Tests/Support/TemporaryDirectory.cs`).
- **No test contains `"pending"` or `"ready"` as a JSON/status string literal.** The only
  case-insensitive `ready` hits in tests are the English word "already"
  (`AlternativeTests.cs:31,33`, `WorkLifecycleTests.cs:73`) and `TaskStage.Ready`.
- The two files that do contain `"status": "pending"` —
  `ai/active/2026-09-06_0615_.../state.json:17-20` and
  `ai/active/2026-09-05_1851_.../state.json:34,44` — are **workflow task-state files for this
  orchestration pipeline**, not ledger state. Unrelated schema; leave them alone.
- `docs/architecture.md:42` prose: *"At most one `Pending` or `Active` run may exist…"* — a doc line
  that becomes wrong on deletion.

---

## F. Existing tests, helpers, and message-substring assertions

### Which file owns which area

| Area | File | Notes |
| --- | --- | --- |
| Claim resolution rules | `tests/AILedger.Tests/Core/CommandValidationTests.cs` | `:9-19` validated-claim-needs-evidence; `:34-54` directional-evidence theory over `Validated`/`Rejected`. **No `Superseded` case anywhere.** |
| Claim-driven invalidation | `tests/AILedger.Tests/Core/InvalidationTests.cs` (74 lines) | `:10-41` reject → `DecisionInvalidated` + `WorkItemInvalidated`, asserts exact 3-event order via `Assert.Collection`; `:44-55` supersede an open claim → dependent work `Stale`, `ResolveClaimCommand(..., Superseded, [])` with **empty evidence**; `:58-73` rejected claim cannot gain new dependents |
| Work lifecycle + block/unblock + R3 | `tests/AILedger.Tests/Core/WorkLifecycleTests.cs` (151) | `:75-94` R3 manual block still goes `Stale`; `:121-141` unblock cannot undo causal invalidation |
| Challenges | **No dedicated file.** `CommandAndLifecycleTests.cs:64-84` (open challenge blocks archive) and `EndToEnd/TwoLeadGovernanceTests.cs:32,40` (raise a `"decision"` challenge, assert it stays `Open`). **No test disposes a challenge at all.** |
| Decision supersession (the pattern Gap A mirrors) | `tests/AILedger.Tests/Core/DecisionReplacementTests.cs` (64) | closest existing model for "replacement must exist and be current" |
| Replay-side rule parity | `tests/AILedger.Tests/Storage/NewCommandReplayTests.cs` (108) | R1: drives real commands through `FileGovernedTaskService` then replays through a **fresh** service. Any new command belongs here. Also asserts projection contents at `:66-71`. |
| Event-type routing | `tests/AILedger.Tests/Core/EventRegistrationTests.cs` (60) | R2: reflects over `[JsonDerivedType]` on `LedgerEventData` and asserts every one is routed by the validator switch. **A new event type not added to `Events.cs` attributes *and* both switches fails here.** |
| Direct reducer/replay forgery | `tests/AILedger.Tests/Core/ReducerTests.cs` (177) | builds `LedgerEvent`s by hand via the `Event(...)` helper at `:164-176` |
| Authorization | `tests/AILedger.Tests/Core/AuthorizationTests.cs` (197) | per-capability refusals |
| CLI surface | `tests/AILedger.Tests/Cli/CliApplicationTests.cs` (763) | **no `claim resolve` or `challenge dispose` invocation exists**; unknown-option tests at `:285,301` |

### `tests/AILedger.Tests/Support/` — what it provides

- **`TestTask.cs`** (40 lines) — the workhorse. Constructor opens a task as `operator`; `Apply` runs
  a command through a real `CommandHandler` at `Epoch.AddMinutes(_commandNumber)` (Epoch =
  `2026-09-05T12:00:00Z`, `:8`) and keeps `State`; `NextCorrelation()` yields `correlation-N`;
  `Assign(actorId, role, params Capability[])`. In-memory only — no file store, no replay.
- **`TemporaryDirectory.cs`** (20) — `IDisposable` temp dir, used by every storage/e2e test.
- **`ScriptedProcessRunner.cs`** (59) — `IProcessRunner` fake for provider tests. Not relevant here.
- Cross-file helper worth knowing: `EscalationTests.Prepare(out ActorId lead)` and
  `EscalationTests.Raise(...)` are `internal static` (`tests/.../Core/EscalationTests.cs:64,74`) and
  are called from `WorkLifecycleTests.cs:41,59,100`. Test classes here do reach across files.

### Tests that assert on exception message substrings (break if wording changes)

Kernel-relevant, i.e. in `Core/` and `Storage/`:

| File:line | Substring | Source of the message |
| --- | --- | --- |
| `Core/ReducerTests.cs:60` | `"opening role"` | `TaskTransitionValidator.cs:150` |
| `Core/ReducerTests.cs:80` | `"provenance"` | `TaskTransitionValidator.cs:755` |
| `Core/ReducerTests.cs:106` | `"operator"` | `TaskTransitionValidator.cs:736` |
| `Core/ReducerTests.cs:121` | `"Unknown decision"` | `TaskTransitionValidator.cs:834` (`Get`) |
| `Core/ReducerTests.cs:147` | `"cannot transition"` | `TaskTransitionValidator.cs:305` |
| `Core/CommandAndLifecycleTests.cs:59` | `"not legal"` | `StageTransitionPolicy.cs` |
| `Core/CommandAndLifecycleTests.cs:83` | `"open challenges"` | `CommandHandler.cs:883` |
| `Core/CommandValidationTests.cs:31` | `"both support and refute"` | `CommandHandler.cs:205` |
| **`Core/CommandValidationTests.cs:52`** | **`"does not support"` / `"does not refute"`** | **`CommandHandler.cs:163-164` via `EvidenceDirection` (`:181-186`)** |
| `Core/DecisionReplacementTests.cs:33,59` | `"predecessor is current"` | `CommandHandler.cs:281` |
| `Core/AuthorizationTests.cs:27,41,62,74,92,131,170` | `"Only an operator"`, `"Only an operator role"`, `"absolute paths"`, `"own authority"`, `"Only work owner"`, `"Only run actor"` | `AuthorizationPolicy.cs` / `CommandHandler.cs:107,352,100,445,458` |
| `Core/AuthorizationTests.cs:112` | `nameof(Capability.AddClaim)` | `AuthorizationPolicy.cs:40` |
| **`Core/WorkLifecycleTests.cs:18`** | **`"run is still active"`** | **`CommandHandler.cs:654` — reworded by the `Pending` removal? No; that string is unaffected, but the guard behind it is #4** |
| `Core/WorkLifecycleTests.cs:46` | `"escalation on it is open"` | `CommandHandler.cs:659` |
| `Core/WorkLifecycleTests.cs:139` | `"replacement work item"` | `CommandHandler.cs:706` |
| `Storage/RecoveryTests.cs:176,197,243,260,283,323,341,356` | `"Event sequence is invalid"`, `"unknown or non-prior cause"`, `"invalid provenance"`, `"Invalid event JSON"`, `"opening role"`, `"Only an operator"`, `"limit of N events"`, `"limit of N bytes"` | `FileGovernedTaskService.cs:374,387,381,…` and the validator |
| `Storage/PathAndProjectionTests.cs:47-48` | ``"`C1` — **Open** — Claim continued"``, ``"`D1` — **Proposed** — Choose files"`` | **`MarkdownTaskProjectionWriter.cs:121,133` — any change to the assumptions/decisions line format breaks these two** |
| `Storage/NewCommandReplayTests.cs:67-71` | `"Stay local"`, `"Ship now or harden first?"`, `"Use a database"` + `DoesNotContain("Old rule")` | projection contents |

The messages the two rule copies **already disagree on** — so a test written against one copy will
not hold against the other:

- `EnsureClaimResolution`: `CommandHandler.cs:853/858/863` (three distinct messages) vs
  `TaskTransitionValidator.cs:771/776` (two).
- Evidence direction: `CommandHandler.cs:163-164` (`"…does not support claim 'C1'."`) vs
  `TaskTransitionValidator.cs:207-208` (`"…does not support the requested 'Rejected' claim transition."`).
- `EnsureDependenciesAreCurrent`: `CommandHandler.cs:845` says
  `"…cannot support new or actionable work."`; `TaskTransitionValidator.cs:788` says
  `"…cannot support actionable work."`
- Archive: `CommandHandler.cs:878/883` (two separate messages) vs
  `TaskTransitionValidator.cs:819` (one combined). `CommandAndLifecycleTests.cs:83` only passes
  because it goes through the command path.
- Unblock: `CommandHandler.cs:704-706` vs `TaskTransitionValidator.cs:722-723`.

---

## Patterns to mirror

1. **Replacement-must-be-current, for Gap A** — `ProposeDecision`/`ResolveDecision` already encode
   exactly the shape Gap A needs. Self-reference refusal: `CommandHandler.cs:234-237` ↔
   `TaskTransitionValidator.cs:251-254` (`"A decision cannot supersede itself."`). Target-must-be-current:
   `CommandHandler.cs:239-243` ↔ `EnsureDecisionIsCurrent` at `TaskTransitionValidator.cs:759-765`.
   One-replacement-only: `TaskTransitionValidator.cs:283-289`.
   Optional-id-on-a-record: `Decision.Supersedes` (`GovernanceModels.cs:147`), carried on the
   command at `Commands.cs:80` and the CLI at `CliApplication.cs:167`
   (`OptionalId(input.Optional("supersedes"), value => new DecisionId(value))`).
2. **Rejecting a field that must be absent** — `ValidateEscalationRaised` at
   `TaskTransitionValidator.cs:517-520` (`"A newly raised escalation cannot carry a resolution."`)
   is the model for "resolving to `Validated`/`Rejected` must reject a supplied replacement id".
3. **Deriving one event's consequence from another in the same batch** —
   `ResolveDecision` at `CommandHandler.cs:275-287` builds a `List<LedgerEventData>` and appends a
   second `DecisionResolved` for the predecessor. `ResolveClaim` at `:168-178` does the same with
   `AddDependencyInvalidations`. Gap B follows this shape.
4. **Cross-copy comment discipline** — the R1/R3 comments
   (`TaskTransitionValidator.cs:536-537`, `:395-397`, `:717`; `CommandHandler.cs:822-824`) mark
   paired rules explicitly. New paired rules should carry the same marker.
5. **Optional typed id in a CLI option** — `OptionalId` at `CliApplication.cs:687-688`, used at
   `:167`, `:200`, `:213`, `:232`, `:329`, `:680`.

## Shared surface to freeze

Any change to these is felt by every worker; they need one owner:

- `src/AILedger.Core/Contracts/GovernanceModels.cs` — `Claim` record (`:124-130`), `ClaimStatus`
  (`:71-77`), `ChallengeStatus` (`:87-93`), `WorkItemStatus` (`:95-104`), `AgentRunStatus` (`:106-114`).
  Gap A, Gap B and the hygiene change all land in this one file.
- `src/AILedger.Core/Contracts/Commands.cs` — `ResolveClaimCommand` (`:53-59`).
- `src/AILedger.Core/Contracts/Events.cs` — `ClaimResolved` (`:44`) and the `[JsonDerivedType]`
  attribute block (`:16-38`), which `EventRegistrationTests` reflects over.
- `src/AILedger.Core/Application/CommandHandler.cs` — `ResolveClaim` (`:137-179`),
  `DisposeChallenge` (`:315-331`), `AddDependencyInvalidations` (`:807-833`),
  `EnsureClaimResolution` (`:849-865`), `EnsureChallengeTargetExists` (`:888-902`), plus the five
  `AgentRunStatus.Pending` guards.
- `src/AILedger.Core/Domain/TaskTransitionValidator.cs` — the counterpart of each of the above.
- `src/AILedger.Cli/CliApplication.cs` — `DispatchAsync` switch (`:123-258`), `AllowedOptions`
  (`:593-647`), `HelpText` (`:700-741`). Three separate places per command; all three sit in one file.
- `docs/operator-guide.md:105-136` duplicates the help block verbatim; `docs/architecture.md:38-52`
  states the invariants in prose.

## Disjoint sets available

The work does **not** decompose cleanly by file — `GovernanceModels.cs`, `CommandHandler.cs`,
`TaskTransitionValidator.cs` and `CliApplication.cs` are each touched by two or three of the three
changes. What *is* disjoint:

- **Hygiene (`Pending`/`Ready` removal)** touches only the ten guard sites listed in section E plus
  two enum lines and `docs/architecture.md:42`. It overlaps Gap A/Gap B in *file* but not in
  *region*: none of those ten lines is inside `ResolveClaim`, `DisposeChallenge`,
  `AddDependencyInvalidations`, `EnsureClaimResolution`, or `EnsureChallengeTargetExists`.
  It is the only change that can safely run in parallel with the others, and it is small enough that
  serialising it costs nothing.
- **Tests** are genuinely disjoint by file: Gap A wants a claim-supersession file (or additions to
  `CommandValidationTests.cs` + `InvalidationTests.cs`); Gap B wants a new challenge-consequence
  file, since no challenge test file exists today. `NewCommandReplayTests.cs` and
  `EventRegistrationTests.cs` are shared and should be edited last by one owner.
- Gap A and Gap B **must not** be split across workers: Gap B's claim branch emits `ClaimResolved`
  with `Rejected`, so it consumes whatever contract Gap A leaves on that event and on
  `EnsureClaimResolution`.

## Landmines

1. **Capability mismatch across the two copies for Gap B.** Command time asks only for
   `DisposeChallenge` (`AuthorizationPolicy.cs:56`). Replay time asks per *event*: the emitted
   `ClaimResolved` hits `RequireAuthority(..., Capability.ResolveClaim)`
   (`TaskTransitionValidator.cs:183`) and each cascade event hits the same capability
   (`:313`, `:392`) — all against `@event.ActorId`, which `ApplyEvents` sets to `command.ActorId`
   (`CommandHandler.cs:737`). **An actor holding `DisposeChallenge` but not `ResolveClaim` will
   pass the command and then fail at replay — the task becomes unreplayable.** Check
   `RoleDefaults.cs:7-27`: today only `RoleKind.Operator` gets `DisposeChallenge`, and only
   `Operator` and `PlanningLead` get `ResolveClaim`, so the default roles happen not to expose it —
   but `actor attach --capability` can construct the broken combination, and
   `AuthorizationTests.cs` demonstrates that state can be built with arbitrary capability sets.
   Either require both capabilities at command time or relax the replay check; decide once and
   write it in both copies.
2. **`Challenge.TargetId` is a `string`, not a typed id** (`GovernanceModels.cs:152`). Gap B must
   construct `new ClaimId(challenge.TargetId)`, and the stored value is `Trim()`-ed but
   **case-preserved** (`CommandHandler.cs:307`), whereas `EnsureChallengeTargetExists` compares
   `id.Value == targetId.Trim()` — case-sensitively. `TargetType` is lower-cased only inside the
   switch, never on storage. Both copies need identical normalisation or they will disagree.
3. **A supported claim challenge can target an already-terminal claim.** Nothing stops
   `challenge raise --target-type claim` against a claim, and then the claim being rejected
   independently before the challenge is disposed. `EnsureClaimResolution` will then throw from
   inside `DisposeChallenge` — a `challenge dispose` that fails for a reason the operator did not
   ask about. Same for a challenge on a decision already `Invalidated`
   (`EnsureDecisionIsCurrent`, `TaskTransitionValidator.cs:759-765`) or a work item already `Stale`
   (`TaskTransitionValidator.cs:398-401`). Gap B needs an explicit answer per target kind for
   "the consequence is already true".
4. **`WorkItemInvalidated` for Gap B's work branch has no claim to name.** Its event record is
   `WorkItemInvalidated(WorkItemId, ClaimId RejectedClaimId, WorkItemStatus)` (`Events.cs:52`) and
   the validator requires that claim to be a rejected/superseded *dependency* of the item
   (`TaskTransitionValidator.cs:411-416`). A challenge-driven "work → blocked" therefore **cannot**
   reuse `WorkItemInvalidated`; the natural fit is `WorkItemBlocked(WorkItemId, Reason,
   EscalationId?)` (`Events.cs:62`, validator `:682-703`) — which requires a non-empty `Reason` and
   permits a null escalation id, and whose reducer clears/sets `BlockReason`
   (`TaskReducer.cs:35`, `:181-189`). Note its guard refuses only `Completed`, so it is legal on an
   already-`Blocked` item and would silently overwrite the existing block reason.
5. **`DecisionInvalidated` for Gap B's decision branch has the same problem** — it also requires a
   rejected/superseded dependency claim (`TaskTransitionValidator.cs:316-321`). There is no
   "invalidated by challenge" path today. Either the event record grows an alternative cause or a
   new event type is needed — and a new event type must be registered in `Events.cs` *and* both
   switches, or `EventRegistrationTests.cs:15` fails.
6. **`ClaimResolved` replaces the claim's whole evidence list** (`TaskReducer.cs:86-90`). Gap B
   rejecting a claim "using the challenge's own evidence" will therefore *overwrite* any evidence
   the claim already carried. And the direction check (`CommandHandler.cs:151-166` /
   `TaskTransitionValidator.cs:195-210`) demands every one of those evidence ids `Refutes` the
   claim — which is exactly the Gap B precondition, but it means the refutation test lives in two
   places and must not drift.
7. **`Superseded` currently requires no evidence and accepts any evidence.** `CommandHandler.cs:146`
   and `TaskTransitionValidator.cs:190` gate only `Validated`/`Rejected`; the direction switch falls
   through to `_ => true` (`CommandHandler.cs:158`, `TaskTransitionValidator.cs:202`). Gap A adds a
   *different* required argument for the same status. `InvalidationTests.cs:52` calls
   `ResolveClaimCommand(..., Superseded, [])` with no replacement — **that test breaks by design**
   and must be updated, not worked around.
8. **`AddDependencyInvalidations` fires for `Superseded` too** (`CommandHandler.cs:173`). Gap A adds
   a replacement claim, but the current cascade still blocks/stales every dependent — it does not
   re-point dependents at the replacement. If the intent is that superseding is gentler than
   rejecting, that is a behaviour change to state explicitly; if not, the replacement id is
   provenance only. The contract does not say. **Ask before implementing.**
9. **Three CLI edit sites per command, in one file.** `DispatchAsync` (`:149-154`, `:180-184`),
   `AllowedOptions` (`:606-607`, `:619-620`), `HelpText` (`:712`, `:720`). Missing the
   `AllowedOptions` entry produces "Unknown option(s)" at runtime with no compile error
   (`CommandLine.cs:62-74`). `docs/operator-guide.md:105-136` is a fourth, hand-maintained copy of
   the help text.
10. **Disposed challenges are invisible in `task.md`** — `MarkdownTaskProjectionWriter.cs:103-107`
    filters to `ChallengeStatus.Open`. A supported challenge and its consequence leave no trace in
    the projection; only `assumptions.md` (claim status) and `task.md`'s work-item line will move.
11. **Projection line formats are asserted verbatim** at `PathAndProjectionTests.cs:47-48`. Adding a
    replacement-claim suffix to the `assumptions.md` line at
    `MarkdownTaskProjectionWriter.cs:121` will break `:47` unless the suffix is appended after the
    matched prefix.
12. **`RunCompleted`'s reducer silently protects `Blocked`/`Stale`** (`TaskReducer.cs:148-153`) — a
    consequence-blocked item will not be flipped back to `Paused` by a later run completion.
    Asserted at `InvalidationTests.cs:36-40`. Good news for Gap B; do not remove it.
13. **The two copies already disagree on wording** (listed at the end of section F). Any new rule
    written once and copied by hand inherits that risk; `NewCommandReplayTests.cs` is the only test
    that exercises both copies against the same real event, so every new rule needs a case there.
14. **`docs/architecture.md:42`** asserts the `Pending`-or-`Active` invariant in prose and becomes
    factually wrong the moment the enum member is deleted.
