# Internal Recon

Target: AILedger 2.0 kernel at `/Users/user/Dev/Uri/localprojects/AILedger`.
Planned change: three new state aggregates (`Escalation`, `Alternative`, `Constraint`), two new
work-item lifecycle commands (`work complete`, `work block`), and removal of the automatic
`WorkItem → Completed` transition on successful run completion (must become `Paused`).

Read-only pass. Reports what exists; proposes no design.

---

## Durable sources read

- `cognitive/RULES.md` — the operator rules snapshot. Settles: pipeline entry
  (`workflow-coordinator` → `prompt-contract-designer` → `task-orchestrator`), the Assumption
  Evidence Rule (moving an assumption out of OPEN requires an actor and a citation), the Stop
  Conditions, and the rule that the verifier and isolated code-reviewer passes are mandatory and
  may not be merged or shortcut. It is **also a runtime artifact**: it is hashed in
  `cognitive/manifest.json:8-10` and loaded as `ContextArtifactKind.Rules`. Editing it breaks
  `CognitiveArtifactLoader` hash verification.
- No `CLAUDE.md` or `AGENTS.md` exists at the repo root or under `src/`.
- `docs/architecture.md:32-48` is the closest thing to a design contract: a rule-to-mechanism
  table that must remain true after the change. Row `:39` describes the claim-invalidation
  semantics; row `:48` the lifecycle policy. `docs/architecture.md:50` states the cognitive text
  is a deliberate byte-for-byte snapshot.

---

## Files in scope

Legend for "touched by": **1** Escalation, **2** Alternative, **3** Constraint,
**4** work complete/block + run-completion status change.

| Path | Role | Touched by |
|---|---|---|
| `src/AILedger.Core/Contracts/Identifiers.cs` | id record structs | 1, 2, 3 |
| `src/AILedger.Core/Contracts/GovernanceModels.cs` | enums (`Capability`, per-aggregate status) + state records | 1, 2, 3, 4 |
| `src/AILedger.Core/Contracts/Commands.cs` | command records + JSON discriminators | 1, 2, 3, 4 |
| `src/AILedger.Core/Contracts/Events.cs` | event records + JSON discriminators | 1, 2, 3, 4 |
| `src/AILedger.Core/Contracts/TaskState.cs` | `GovernedTaskState` dictionaries | 1, 2, 3 |
| `src/AILedger.Core/Application/CommandHandler.cs` | command→event dispatch, validation, `ValidateEnums` | 1, 2, 3, 4 |
| `src/AILedger.Core/Domain/AuthorizationPolicy.cs` | command→required-capability map | 1, 2, 3, 4 |
| `src/AILedger.Core/Domain/TaskReducer.cs` | event→state | 1, 2, 3, 4 |
| `src/AILedger.Core/Domain/TaskTransitionValidator.cs` | replay-time re-validation of every event | 1, 2, 3, 4 |
| `src/AILedger.Cli/CliApplication.cs` | CLI dispatch, `AllowedOptions`, `HelpText` | 1, 2, 3, 4 |
| `src/AILedger.Cli/RoleDefaults.cs` | default capability sets per role | 1, 2, 3, 4 (only if new capabilities added) |
| `src/AILedger.Storage/MarkdownTaskProjectionWriter.cs` | task.md / assumptions.md / decisions.md projections | 1, 2, 3, 4 |
| `src/AILedger.Core/Application/ContextAssembler.cs` | manifest artifact selection | 1, 2, 3 |
| `src/AILedger.Core/Contracts/ContextContracts.cs` | `ContextArtifactKind` enum | 1, 2, 3 |
| `src/AILedger.Cli/CognitiveArtifactLoader.cs` | hardcoded constraint / stop-condition artifacts | 3 |
| `tests/AILedger.Tests/Core/*.cs` | unit tests | all |
| `tests/AILedger.Tests/Cli/CliApplicationTests.cs` | CLI integration | 4 (breaks today) |
| `docs/operator-guide.md`, `docs/architecture.md`, `README.md` | command reference + rule table | all |

Confirmed **not** in scope: `src/AILedger.Storage/LedgerJson.cs`,
`src/AILedger.Storage/StringIdentifierConverterFactory.cs` (reflection-driven — any new
`readonly record struct XId(string Value)` is handled automatically), and
`src/AILedger.Storage/FileGovernedTaskService.cs` (no per-aggregate switch).

---

## Patterns to mirror

### A. One aggregate end to end — the `Challenge` template

| Concern | Exemplar | Convention it encodes |
|---|---|---|
| Identifier | `src/AILedger.Core/Contracts/Identifiers.cs:28-31` | `public readonly record struct ChallengeId(string Value)` with `ToString() => Value`. Nothing else needed for JSON. |
| Status enum | `src/AILedger.Core/Contracts/GovernanceModels.cs:62-68` | `ChallengeStatus { Open, Supported, Rejected, Withdrawn }` — first member is the creation state. |
| Capabilities | `src/AILedger.Core/Contracts/GovernanceModels.cs:29-44` | One capability per verb: `RaiseChallenge`, `DisposeChallenge`. |
| State record | `src/AILedger.Core/Contracts/GovernanceModels.cs:125-132` | `Challenge(Id, TargetType, TargetId, Reason, Status, EvidenceIds, Provenance)` — carries `Provenance`. `WorkItem` (`:134-140`) is the one outlier with no provenance. |
| Command discriminators | `src/AILedger.Core/Contracts/Commands.cs:13-14` | `[JsonDerivedType(typeof(RaiseChallengeCommand), "challenge.raise")]` on `LedgerCommand`. |
| Command records | `src/AILedger.Core/Contracts/Commands.cs:81-96` | `sealed record`; first three params are always `(ActorId, EventId? CausationId, string CorrelationId)` forwarded to the base. |
| Event discriminators | `src/AILedger.Core/Contracts/Events.cs:24-25` | `"challenge.raised"` / `"challenge.disposed"` — past tense of the command verb. |
| Event records | `src/AILedger.Core/Contracts/Events.cs:41-42` | Creation event carries the whole entity (`ChallengeRaised(Challenge)`); mutation event carries id + delta only (`ChallengeDisposed(ChallengeId, ChallengeStatus)`). |
| State dictionary | `src/AILedger.Core/Contracts/TaskState.cs:17` | `IReadOnlyDictionary<ChallengeId, Challenge> Challenges { get; init; } = new Dictionary<…>();` |
| Command dispatch | `src/AILedger.Core/Application/CommandHandler.cs:74-75` | Arm in the `HandleExisting` switch expression. |
| Command handlers | `src/AILedger.Core/Application/CommandHandler.cs:282-305` (raise), `:307-323` (dispose) | `private static IReadOnlyList<LedgerEventData>`; order is `RequireId` → `EnsureNew` → `RequireText` → `EnsureUnique` → `EnsureReferencesExist` → domain rule → build record with `new Provenance(command.ActorId, now, "<command discriminator>")` → return a collection expression. All string fields `.Trim()`ed. |
| Enum validation | `src/AILedger.Core/Application/CommandHandler.cs:526-528` | `case DisposeChallengeCommand dispose: RequireDefined(dispose.Status, …)` inside `ValidateEnums`. |
| Authorization | `src/AILedger.Core/Domain/AuthorizationPolicy.cs:44-45` | Arm in `RequiredCapabilities`. Its `_ =>` at `:50` throws, so an unlisted command fails closed. |
| Reducer | `src/AILedger.Core/Domain/TaskReducer.cs:22-23` | Creation is an inline `Require(state) with { X = Set(…) }`; mutation delegates to a named private static (`DisposeChallenge`, `:99-103`). `Set` (`:171-182`) copies the dictionary. Version bump is central at `:32`. |
| Replay validation | `src/AILedger.Core/Domain/TaskTransitionValidator.cs:39-44` (switch), `:300-318` (raise), `:320-332` (dispose) | Duplicates the command-handler rules, plus `RequireAuthority` and `ValidateProvenance`. Its `default:` at `:60-61` throws. |
| Markdown projection | `src/AILedger.Storage/MarkdownTaskProjectionWriter.cs:88-92` | A `## Open Challenges` section inside `RenderTask`; `AppendItems` (`:125-138`) writes `_None._` when empty; every user string passes through `Clean` (`:146-150`); ids ordered `StringComparer.Ordinal`. |
| CLI dispatch | `src/AILedger.Cli/CliApplication.cs:174-184` | `case "challenge raise":` → `await ExecuteAsync(service, input, new RaiseChallengeCommand(Actor(input), Cause(input), Correlation(input), …), cancellationToken)`. |
| CLI option allow-list | `src/AILedger.Cli/CliApplication.cs:574-578` | `["challenge raise"] = Options("root", "task", "actor", "id", …, "cause", "correlation")`. |
| CLI help text | `src/AILedger.Cli/CliApplication.cs:660-662` | Raw string literal, verb column-aligned at 19 characters. |
| Docs | `docs/operator-guide.md:115-117`, `docs/architecture.md:32-48` | The operator guide duplicates the help text verbatim. |
| Test | `tests/AILedger.Tests/Core/CommandAndLifecycleTests.cs:63-84` | Behaviour-named `[Fact]`, `TestTask` fixture, `Assert.Contains(<substring>, exception.Message, StringComparison.Ordinal)`. |

**Total: 12 source files + 2 doc files for one aggregate.**

### B. The four mechanisms

- **Command → event dispatch.** A single C# switch *expression* on the command type in
  `CommandHandler.HandleExisting`, `src/AILedger.Core/Application/CommandHandler.cs:66-81`. Every
  arm returns `IReadOnlyList<LedgerEventData>` — never a `LedgerEvent`. The envelope is built
  centrally in `ApplyEvents` (`:465-494`), which assigns `EventId = "{taskId}:{version+1:D10}"`
  (`:496-497`) and chains `CausationId` to the previous event in the batch (`:484`).
  Authorization runs once at `:64`, before the switch. One command may emit several events —
  see `AddDependencyInvalidations` (`:546-569`).
- **Event → state.** A switch expression on `@event.Data` in `TaskReducer.Apply`,
  `src/AILedger.Core/Domain/TaskReducer.cs:12-30`, preceded by `ValidateEnvelope` (`:35-56`) and
  `TaskTransitionValidator.Validate` (`:10`). Version increments once, centrally, at `:32`.
- **JSON polymorphic discriminators.**
  `[JsonPolymorphic(TypeDiscriminatorPropertyName = "commandType")]` on `LedgerCommand` at
  `src/AILedger.Core/Contracts/Commands.cs:5`; `"eventType"` on `LedgerEventData` at
  `src/AILedger.Core/Contracts/Events.cs:15`. Names are dotted lowercase and match the CLI verb.
  Serializer config is `src/AILedger.Storage/LedgerJson.cs:8-21` — camelCase,
  `JsonStringEnumConverter(camelCase, allowIntegerValues: false)`, plus the reflection-based
  `StringIdentifierConverterFactory`.
- **`ValidateEnums`.** `src/AILedger.Core/Application/CommandHandler.cs:509-536`, called from
  `Handle` at `:25` immediately after `ValidateEnvelope`. A switch *statement* with one `case`
  per command that carries an enum, calling `RequireDefined` (`:538-544`, `Enum.IsDefined`).
  It is **opt-in per command**: a new command with an enum field not added here silently accepts
  out-of-range casts. Pinned by
  `tests/AILedger.Tests/Core/CommandValidationTests.cs:73-84`.

### C. Every `WorkItemStatus` read and write

Enum declaration: `src/AILedger.Core/Contracts/GovernanceModels.cs:70-79` —
`{ Proposed, Ready, Active, Paused, Blocked, Stale, Completed }`.
`Ready` is currently **never written by any code path**. Field on the entity:
`GovernanceModels.cs:138`. Carried on an event: `Events.cs:44`
(`WorkItemInvalidated(WorkItemId, ClaimId, WorkItemStatus)`).

**Writes (only four sites set a work-item status):**

| Site | Transition |
|---|---|
| `src/AILedger.Core/Application/CommandHandler.cs:356` | creation → `Proposed` |
| `src/AILedger.Core/Application/CommandHandler.cs:564-566` | on claim rejection/supersession: `Active → Blocked`, everything else → `Stale` (chooses the status carried on the event) |
| `src/AILedger.Core/Domain/TaskReducer.cs:116` | `RunStarted` → `Active` |
| `src/AILedger.Core/Domain/TaskReducer.cs:127-154` | `RunCompleted` → `Completed` or `Paused` |

**The exact location change 4 targets:** `src/AILedger.Core/Domain/TaskReducer.cs:142-145`

```csharp
var status = completed.Status == AgentRunStatus.Completed
    ? WorkItemStatus.Completed
    : WorkItemStatus.Paused;
workItems = Set(workItems, workItemId, currentWorkItem with { Status = status });
```

guarded by `:140` — `if (currentWorkItem.Status is not (WorkItemStatus.Blocked or WorkItemStatus.Stale))`.
Nothing in `TaskTransitionValidator.ValidateRunCompleted` (`:429-457`) asserts the resulting
work-item status, so the reducer is the sole authority for this transition.

**Reads / guards:**

| Site | Guard |
|---|---|
| `src/AILedger.Core/Application/CommandHandler.cs:376` | cannot start a run for `Blocked`/`Stale`/`Completed` |
| `src/AILedger.Core/Application/CommandHandler.cs:561` | invalidation skips already-`Blocked`/`Stale` items |
| `src/AILedger.Core/Application/CommandHandler.cs:564` | invalidation target status |
| `src/AILedger.Core/Domain/TaskReducer.cs:140` | completion does not overwrite `Blocked`/`Stale` |
| `src/AILedger.Core/Domain/TaskTransitionValidator.cs:342` | a newly added work item must be `Proposed` |
| `src/AILedger.Core/Domain/TaskTransitionValidator.cs:371` | cannot re-invalidate a `Blocked`/`Stale` item |
| `src/AILedger.Core/Domain/TaskTransitionValidator.cs:376-378` | expected invalidation status (mirror of `CommandHandler.cs:564-566`) |
| `src/AILedger.Core/Domain/TaskTransitionValidator.cs:417` | replay mirror of `CommandHandler.cs:376` |

Status is also rendered, not branched on, at
`src/AILedger.Storage/MarkdownTaskProjectionWriter.cs:81` (`task.md`, `## Work Items`) and
`src/AILedger.Core/Application/ContextAssembler.cs:232` (work-item context artifact content).

**Tests asserting a work-item status:**

| Test | Assertion |
|---|---|
| `tests/AILedger.Tests/Cli/CliApplicationTests.cs:441` | **`WorkItemStatus.Completed` after a successful provider run — this is the one test change 4 breaks.** Inside `CancellationAfterSuccessfulProviderReturnStillPersistsTerminalRun` (`:412-442`). |
| `tests/AILedger.Tests/Core/InvalidationTests.cs:34` | `Blocked` after claim rejection |
| `tests/AILedger.Tests/Core/InvalidationTests.cs:40` | still `Blocked` after `run.complete` — the "completion cannot erase Blocked" rule |
| `tests/AILedger.Tests/Core/InvalidationTests.cs:54` | `Stale` for inactive dependent work |
| `tests/AILedger.Tests/Core/AuthorizationTests.cs:133` | `Proposed` after a rejected run start |
| `tests/AILedger.Tests/Core/AuthorizationTests.cs:152` | `Active` after run start on unowned work |
| `tests/AILedger.Tests/Core/AuthorizationTests.cs:195` | `Active` after an operator starts owned work |

`CliApplicationTests.cs:441` is the **only** test that asserts a work-item status after a
provider run. `AuthorizationTests.cs:155-177` completes a run but asserts only the run status,
because that run has no work item.

### D. `ContextAssembler` artifact selection

- **Always-included kinds** — `src/AILedger.Core/Application/ContextAssembler.cs:8-16`:
  `Rules`, `Skill`, `TaskGoal`, `Constraint`, `StopCondition`. Note `Constraint` is **already**
  in this set, so a Constraint aggregate reaches every manifest unnarrowed unless deliberately
  changed.
- **Reviewer exclusions** — `:18-25`: `UserRequest`, `PromptContract`, `OrchestrationPlan`,
  `VerifierOutput`, applied at `:127-129`.
- **Work-item narrowing** — `IsRelevant`, `:146-157`. An artifact survives only if its kind is
  in `AlwaysIncludedKinds` **or** `workItem is null` **or** its `Id`/`RelatedIds` intersect the
  relevant-id set. That set is built at `:57-59` from the state artifacts produced by
  `BuildStateArtifacts` (`:159-195`), which narrows claims to `workItem.DependsOnClaims`
  (`SelectClaims`, `:197-201`), decisions to those depending on those claims (`:171-174`), and
  evidence to those supporting/refuting them (`:175-178`).
- **Where a new artifact kind must be registered** — three places:
  1. `src/AILedger.Core/Contracts/ContextContracts.cs:3-18` — add the enum member. Ordering
     matters: the manifest sorts by `artifact.Kind` at `ContextAssembler.cs:65`, and
     `ContextIsolationTests.cs:57` asserts that order.
  2. `ContextAssembler.cs:8-16` — add to `AlwaysIncludedKinds` if it must survive work-item
     narrowing; otherwise it is dropped unless step 3 links it into the dependency graph.
  3. `ContextAssembler.BuildStateArtifacts` (`:159-195`) plus a `ToArtifact` overload
     (exemplars at `:203-233`) — this is what emits the artifact and populates `RelatedIds`,
     which is what makes narrowing find it.
  Optionally `ReviewerExclusions` (`:18-25`) if a code reviewer must not see it.
  Deduplication is `GroupBy((Kind, Id)).Select(First)` at `:68-69` — colliding ids silently
  collapse.
- **Hardcoded constraint sentence** — `src/AILedger.Cli/CognitiveArtifactLoader.cs:52-56`:
  `ContextArtifactKind.Constraint`, id `"operator-authority"`, content
  `"Only the operator may assign roles or change governed resource scope."`. Beside it at
  `:57-61` is the stop condition, id `"governance-stop"`, content
  `"Stop when proceeding would exceed assigned capabilities, alter scope, or bypass required independent verification."`
  Both are appended unconditionally after the manifest-driven Rules/Skill artifacts (`:29-50`).

### E. CLI option table and help text

- **Table**: `src/AILedger.Cli/CliApplication.cs:551-589` —
  `IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedOptions`, keyed
  `StringComparer.OrdinalIgnoreCase` by the space-joined lowercased verb.
  Entry shape (`:577-578`):
  ```csharp
  ["challenge dispose"] = Options(
      "root", "task", "actor", "id", "status", "cause", "correlation"),
  ```
  `Options` is `:595-596`. Provider commands share `ProviderOptions()` (`:591-593`).
  Enforcement is `ValidateOptions` (`:222-230`) → `CommandLine.EnsureOnlyAllowedOptions`
  (`src/AILedger.Cli/CommandLine.cs:62-74`). **A missing table entry throws
  `Unknown command` before dispatch** (`:224-227`), so registering a `case` in `DispatchAsync`
  without a table entry makes the command unreachable. `root`, `task`, `actor`, `cause`,
  `correlation` appear in every mutating entry.
- **Help text**: `src/AILedger.Cli/CliApplication.cs:642-673`, a `private const string` raw
  string literal, printed at `:80` and `:87`. Verb column padded to 19 characters; required
  options bare, optional in `[…]`. Duplicated verbatim in `docs/operator-guide.md:102-125`.
- No test enumerates the table or asserts the help text. The only CLI-usage test is
  `tests/AILedger.Tests/Cli/CliApplicationTests.cs:235-245`
  (`UsageFailureReturnsTwoAndWritesConciseError`, unknown verb → exit 2) and
  `:247-283` (a misspelled option is rejected before the run starts).

### F. Test layout and conventions

- Layout: `tests/AILedger.Tests/{Core,Cli,Storage,Providers,EndToEnd,Support}/`. Single test
  project, xUnit, implicit usings (`AILedger.Tests.GlobalUsings.g.cs`). Files are auto-
  discovered — no registration.
- One `public sealed class <Area>Tests` per file, namespace mirrors the directory
  (`namespace AILedger.Tests.Core;`).
- Method names are behaviour sentences, not `Method_Condition_Result`. Examples:
  `RejectingClaimInvalidatesDecisionAndBlocksActiveDependentWork`
  (`tests/AILedger.Tests/Core/InvalidationTests.cs:10`),
  `CompletionMustBeTerminalAndPreserveSessionIdentity`
  (`tests/AILedger.Tests/Core/CommandValidationTests.cs:57`). A few carry a requirement prefix:
  `R4_CodeReviewerManifestExcludesForbiddenArtifacts`
  (`tests/AILedger.Tests/Core/ContextIsolationTests.cs:11`),
  `R1_ReopenReplaysEventsWhenMaterializedStateIsStale`
  (`tests/AILedger.Tests/Storage/RecoveryTests.cs:13`).
- Negative assertions use
  `Assert.Contains(<substring>, exception.Message, StringComparison.Ordinal)` — exception
  message text is therefore load-bearing.
- Support types, all `internal sealed`:
  - `tests/AILedger.Tests/Support/TestTask.cs:6-40` — in-memory `CommandHandler` fixture. Opens
    a task in the constructor (`:16`, default `"task-1"` / `"operator"`), fixed clock
    `Epoch = 2026-09-05T12:00:00Z` (`:8`) advanced one minute per command (`:25`).
    API: `Apply(LedgerCommand) → CommandOutcome`, `NextCorrelation()`,
    `Assign(ActorId, RoleKind, params Capability[])`, properties `TaskId`, `OperatorId`, `State`.
    Used by every `Core/` test.
  - `tests/AILedger.Tests/Support/TemporaryDirectory.cs:3-20` — `IDisposable` temp dir under
    `Path.GetTempPath()`, recursive delete on dispose. Used by every `Storage/`, `Cli/`, and
    `EndToEnd/` test.
  - `tests/AILedger.Tests/Support/ScriptedProcessRunner.cs:5-54` — queue-backed `IProcessRunner`
    with a fluent `Enqueue(exitCode, stdout, stderr)` and a delegate overload; records
    `Invocations`. Plus `ScriptedProcessResult` (`:56-59`).
- CLI tests build their own fakes locally rather than in `Support/`:
  `FixedResultAdapter`, `SuccessfulCancellingAdapter`, `CompletionFailureService` and the
  `Service` / `Create` / `FindCognitiveRoot` helpers all live at the bottom of
  `tests/AILedger.Tests/Cli/CliApplicationTests.cs` (`:700-761`).

---

## Shared surface to freeze

Everything here must be agreed and written before any parallel work starts.

- `LedgerCommand` base signature — `(ActorId ActorId, EventId? CausationId, string CorrelationId)`,
  `Commands.cs:19`. Produced by the CLI helpers `Actor`/`Cause`/`Correlation`
  (`CliApplication.cs:625-628`); consumed by `ValidateEnvelope` (`CommandHandler.cs:499-507`)
  and `ApplyEvents`.
- The exact **names of the five new commands and their discriminators**
  (`escalation.raise`, `escalation.resolve`, `alternative.record`, `constraint.add`,
  `constraint.supersede`, `work.complete`, `work.block`) and their **event** counterparts.
  Three files must gain matching entries in the same commit: `Commands.cs`, `Events.cs`, and
  the `TaskTransitionValidator` switch.
- `abstract record LedgerEventData` plus both `[JsonPolymorphic]` attribute lists. Producer:
  `CommandHandler`. Consumers: `TaskReducer`, `TaskTransitionValidator`, and the on-disk
  `events.jsonl`.
- `GovernedTaskState` shape, `TaskState.cs:3-21` — the new dictionary property names and their
  position in the record. It is serialized verbatim to `state.json` and byte-compared against a
  fresh replay in `FileGovernedTaskService.MaterializedStateIsCurrentAsync`
  (`src/AILedger.Storage/FileGovernedTaskService.cs:278-300`). Appending a dictionary is safe
  (empty dictionaries serialize identically both ways); reordering or renaming invalidates every
  existing `state.json`.
- New `Capability` members, `GovernanceModels.cs:29-44`. Producers: `RoleDefaults.For`
  (`RoleDefaults.cs:7-25`) and the operator grant at `CommandHandler.cs:48`
  (`Enum.GetValues<Capability>()`). Consumers: `AuthorizationPolicy.RequiredCapabilities`
  (`:36-51`) and `TaskTransitionValidator.RequireAuthority` (`:477+`). Adding a member
  automatically widens the operator role; every non-operator role must be edited by hand at
  `RoleDefaults.cs:10-23`.
- New `ContextArtifactKind` members and their **position** in the enum,
  `ContextContracts.cs:3-18` — the enum's declaration order is the manifest's sort order
  (`ContextAssembler.cs:65`), asserted by `ContextIsolationTests.cs:57`.
- The new per-aggregate status enums and entity record shapes in `GovernanceModels.cs`.
- The decision on `WorkItemStatus`: whether `work block` sets the same `Blocked` member that
  claim invalidation sets, and whether `Ready` (today unreachable) gets used.
- `IContextAssembler.Build` signature, `ContextContracts.cs:38-46` — unchanged, but the new
  `ToArtifact` overloads are additions to the same class.

---

## Disjoint sets available

**Plain answer: not as four independent workers, and not without a freeze pass first.**

All four changes edit the same eleven files, and in most of them the same single construct:

| Shared construct | File:line | Contended by |
|---|---|---|
| `LedgerCommand` `[JsonDerivedType]` list | `Commands.cs:5-18` | 1, 2, 3, 4 |
| `LedgerEventData` `[JsonDerivedType]` list | `Events.cs:15-30` | 1, 2, 3, 4 |
| `HandleExisting` switch expression | `CommandHandler.cs:66-81` | 1, 2, 3, 4 |
| `ValidateEnums` switch statement | `CommandHandler.cs:509-536` | 1, 3, 4 |
| `RequiredCapabilities` switch expression | `AuthorizationPolicy.cs:36-51` | 1, 2, 3, 4 |
| `TaskReducer.Apply` switch expression | `TaskReducer.cs:12-30` | 1, 2, 3, 4 |
| `TaskTransitionValidator.Validate` switch | `TaskTransitionValidator.cs:13-62` | 1, 2, 3, 4 |
| `DispatchAsync` switch statement | `CliApplication.cs:123-216` | 1, 2, 3, 4 |
| `AllowedOptions` dictionary literal | `CliApplication.cs:551-589` | 1, 2, 3, 4 |
| `HelpText` const | `CliApplication.cs:642-673` | 1, 2, 3, 4 |
| `Capability` enum | `GovernanceModels.cs:29-44` | 1, 2, 3, 4 |
| `GovernedTaskState` properties | `TaskState.cs:8-20` | 1, 2, 3 |
| `RenderTask` sections | `MarkdownTaskProjectionWriter.cs:60-95` | 1, 2, 3, 4 |

This is a closed type hierarchy with exhaustive switches over it. It has no per-aggregate file
boundary. Four workers editing `CommandHandler.cs` would produce four conflicting versions of the
same switch expression.

**It can, however, be frozen and then split**, in two phases:

*Phase 0 — one worker, serial, no parallelism.* Write every declaration and every switch arm as
a skeleton: identifiers, status enums, entity records, capability members, command records,
event records, both discriminator lists, state dictionaries, every switch arm delegating to a
named `private static` stub, the `AuthorizationPolicy` arms, the `ValidateEnums` cases, the CLI
`case` labels, the `AllowedOptions` entries, the `HelpText` lines, and the `ContextArtifactKind`
members. Stubs throw `NotImplementedException`. This touches all eleven shared files once and
must compile. It is roughly a mechanical hour and is not worth splitting.

*Phase 1 — four workers over genuinely disjoint bodies.* After phase 0, each worker owns only
the bodies of its own named methods, which are contiguous non-overlapping regions:

- `escalation-rules` — `CommandHandler.RaiseEscalation`/`ResolveEscalation`,
  `TaskReducer.ResolveEscalation`, `TaskTransitionValidator.ValidateEscalation*`,
  `tests/AILedger.Tests/Core/EscalationTests.cs` (new file).
  Independent of: alternative-rules, constraint-rules, workitem-lifecycle.
- `alternative-rules` — same shape, plus `tests/…/Core/AlternativeTests.cs` (new file).
- `constraint-rules` — same shape, plus `ContextAssembler` `ToArtifact(Constraint)` and its
  `BuildStateArtifacts` line, plus `tests/…/Core/ConstraintTests.cs` (new file).
  Overlaps `context-and-docs` in `ContextAssembler.cs` — assign both to one worker or split at
  the method level.
- `workitem-lifecycle` — `TaskReducer.CompleteRun` (`:127-154`),
  `CommandHandler.CompleteWorkItem`/`BlockWorkItem`,
  `TaskTransitionValidator.ValidateWorkItem*`, and the **existing** test edits
  (`CliApplicationTests.cs:441`, `InvalidationTests.cs`, `AuthorizationTests.cs`).
  Independent of all three aggregate workers.
- `context-and-docs` — `CognitiveArtifactLoader.cs`, `docs/operator-guide.md`,
  `docs/architecture.md`, `README.md`. Independent of every kernel body; blocked only on the
  frozen verb names from phase 0.

Even in phase 1, `MarkdownTaskProjectionWriter.RenderTask` (`:60-95`) is a single method that
three workers would each want a section in. Either write all new sections in phase 0 or give
that file to one owner.

**Recommendation:** the phase-0 freeze is unavoidable, so the honest cost comparison is
"serial skeleton + 4 parallel bodies" versus "one worker, whole change". Given eleven shared
files and roughly 8.5k lines of source total, the coordination overhead of decomposition is
likely to exceed its benefit. Decomposition earns its keep here only if the domain rules for the
three new aggregates turn out to be substantial; if they are as thin as `Challenge`
(~20 lines of handler each), the direct path is cheaper.

---

## Landmines

### 1. Every rule is written twice — `TaskTransitionValidator` is a full second kernel

`src/AILedger.Core/Domain/TaskTransitionValidator.cs` (642 lines) re-validates **every** event at
replay time, called from `TaskReducer.Apply:10` before the state transition. It is not a
lightweight envelope check. Per event type it performs, in order:

1. `ValidateEventMetadata` (`:65-75`) — ids, correlation, non-default `RecordedAt`. Runs for
   every event.
2. A `switch` on `@event.Data` (`:13-62`) routing to a per-event validator.
3. Inside each validator: `RequireAuthority(state, @event.ActorId, <Capability>)`
   (`:477+`, with an `operatorRequired` overload) — an authority check the `CommandHandler` does
   through `AuthorizationPolicy` instead, so **the same rule is expressed against two different
   APIs**.
4. `RequireDefined` on every enum on the payload — a second, independent copy of `ValidateEnums`.
5. Existence, uniqueness and reference checks against state, using its **own private copies** of
   `Get`, `EnsureNew`, `EnsureUnique`, `EnsureReferencesExist`, `RequireText`, `RequireId`
   (`:576+`).
6. The domain rule itself, restated. Compare `CommandHandler.cs:624-638` with
   `TaskTransitionValidator.cs:542-556` — byte-identical `EnsureChallengeTargetExists`. Compare
   `CommandHandler.cs:603-622` with `TaskTransitionValidator.cs:558-571` — the same archive
   prerequisite with different exception text.
7. `ValidateProvenance(@event, entity.Provenance, "<command discriminator>")` — checks that the
   entity's recorded provenance matches the event actor and timestamp. The `CommandHandler` has
   no equivalent because it *constructs* the provenance. This is a forgery check that only
   exists on the replay side.

Additionally it enforces invariants the command handler cannot: that a started run's
`StartedAt` equals the event `RecordedAt` and `EndedAt` is null (`:399-403`), that a completed
run's `EndedAt` equals the event timestamp and follows the start (`:448-451`), and the
opening-role forgery checks (`:96-120`, pinned by `ReducerTests.cs:44-81`).

**What breaks if a new event is not registered there:** the `default:` arm at `:60-61` throws
`GovernanceException($"Unsupported event data '{...}'.")`. Since `TaskReducer.Apply` calls the
validator **before** its own switch, the failure happens on the very first write — the command
succeeds through `CommandHandler`, then `ApplyEvents` calls the reducer, which calls the
validator, which throws. So the command fails at write time with a confusing "unsupported event
data" message rather than a validation message. If it were somehow persisted, every subsequent
`GetStateAsync` would fail permanently: `FileGovernedTaskService` replays the whole log, so one
unregistered event type makes the entire task unreadable, with no repair path short of editing
`events.jsonl`. `RecoveryTests.cs:63-77` and `:163-178` pin the fail-closed replay behaviour.

**Consequence for this change:** each of the five new commands needs its rules written in two
files, in two different idioms, kept in agreement. This is the single largest source of
avoidable defects in the work.

### 2. `ContextAssembler` narrowing silently drops unregistered kinds

`ContextAssembler.cs:146-157`. A new aggregate that is not emitted by `BuildStateArtifacts`
(`:159-195`) and whose kind is not in `AlwaysIncludedKinds` (`:8-16`) disappears from every
work-item-scoped manifest with no error — the manifest is simply smaller. There is no test that
would catch it. `ContextArtifactKind.Constraint` is already in `AlwaysIncludedKinds` (`:14`),
which cuts the other way: a Constraint aggregate is unnarrowed by default and will appear in
every manifest regardless of relevance.

### 3. The hardcoded constraint artifact can collide with a Constraint aggregate

`src/AILedger.Cli/CognitiveArtifactLoader.cs:52-56` appends
`(Constraint, "operator-authority", "Only the operator may assign roles or change governed resource scope.")`
unconditionally. Deduplication at `ContextAssembler.cs:68-69` is
`GroupBy((Kind, Id)).Select(group => group.First())`, and ordering before it (`:64-67`) is by
kind, then id, then **content** ordinal. So a state-derived Constraint sharing the id
`"operator-authority"` would be silently replaced by whichever has the lexicographically smaller
content. Different ids coexist safely.

### 4. `ValidateEnums` is opt-in and duplicated

`CommandHandler.cs:509-536`. A new command carrying, say, an escalation severity enum that is not
given a `case` there will accept `(Severity)999` at the core boundary. The replay-side copy
(`RequireDefined` calls scattered through `TaskTransitionValidator`) is separate and equally
opt-in. `CommandValidationTests.cs:73-84` tests only the three enums that exist today.

### 5. Change 4 breaks a currently passing test whose name hides it

`tests/AILedger.Tests/Cli/CliApplicationTests.cs:441` asserts
`WorkItemStatus.Completed` after a successful provider run. It must become `Paused`. The test is
named `CancellationAfterSuccessfulProviderReturnStillPersistsTerminalRun` (`:412-442`) — the name
is about cancellation and terminal run persistence, so the work-item assertion is easy to miss
when scanning for affected tests.

### 6. `WorkItemStatus.Completed` stays reachable by guard even after the reducer stops writing it

Removing `TaskReducer.cs:142-143` leaves two guards that still test for `Completed`:
`CommandHandler.cs:376` and `TaskTransitionValidator.cs:417` (both refuse to start a run on a
`Completed` item). They become dead until `work complete` exists, then live again — correct, but
only if the new command actually writes that member.

Separately, `Blocked` is currently **only** written by claim invalidation, and both
`CommandHandler.cs:559-568` and `TaskTransitionValidator.cs:371-382` encode a two-outcome map
(`Active → Blocked`, else `Stale`) that the validator asserts exactly
(`"Work item invalidation must transition to '{expectedStatus}'."`, `:379-382`). An operator-set
`Blocked` from `work block` will interact with:
- `CommandHandler.cs:561` — invalidation *skips* items already `Blocked`, so a manually blocked
  item never becomes `Stale` when its claim is later rejected;
- `TaskTransitionValidator.cs:371-374` — refuses to invalidate an already-`Blocked` item, which
  is consistent, but means the two kinds of blocking are indistinguishable in state.

Decide whether operator-`Blocked` and claim-caused-`Blocked` are the same status **before**
writing either command; they are currently the same enum member with different provenance and
no way to tell them apart.

`WorkItemStatus.Ready` (`GovernanceModels.cs:73`) is declared but never written anywhere — worth
knowing before adding more members.

### 7. `state.json` is byte-compared against a fresh replay

`FileGovernedTaskService.MaterializedStateIsCurrentAsync`, `:278-300`, compares the stored file
to `JsonSerializer.Serialize(replayedState, _stateJson) + "\n"` with `StringComparison.Ordinal`.
Any change to `GovernedTaskState` property order or naming makes every existing `state.json`
stale. It degrades gracefully (falls back to full replay, `RecoveryTests.cs:12-41`) rather than
failing, but it is a silent performance and determinism change.

### 8. `cognitive/manifest.json` pins SHA-256 hashes

`CognitiveArtifactLoader.VerifyHashAsync` (`:111-123`) throws `InvalidDataException` on mismatch,
for `RULES.md` and all six `SKILL.md` files plus two reference files. `docs/architecture.md:50`
states the snapshot is deliberately byte-for-byte. Editing any cognitive file without
regenerating the manifest breaks `context build` and every `provider launch`. Several CLI tests
call `FindCognitiveRoot()` and therefore depend on this holding.

### 9. `MarkdownTaskProjectionWriter` writes exactly three files

`WriteAsync` (`:24-36`) fans out to the three names in `TaskWorkspaceLayout`
(`src/AILedger.Core/Contracts/PersistenceContracts.cs:3-9`). New aggregates go either into a new
`##` section of `RenderTask` (`:60-95`) or need a new layout field plus a fourth
`WriteAtomicAsync`. `TaskWorkspaceLayout` is a public record with all-defaulted parameters, so
appending a field is source-compatible. Note `RenderTask` only lists **open** challenges
(`:90`) — a precedent for filtering a projection by status.

### 10. `work add` requires the operator *role*, not merely the capability

`AuthorizationPolicy.cs:20-23` hard-codes `assignment.Role != RoleKind.Operator` for
`AddWorkItemCommand`, on top of the `ManageWork` + `ManageScope` capability pair at `:46`.
Mirrored at `TaskTransitionValidator.cs:336-337` with `operatorRequired: true`. Decide
explicitly whether `work complete` and `work block` inherit that operator-only gate or run on
`ManageWork` alone — and make both files agree, or replay will reject what the command accepted.

### 11. Exception message text is part of the test contract

Tests assert on message substrings (`Assert.Contains(…, StringComparison.Ordinal)`) — for example
`"Only work owner"` (`AuthorizationTests.cs:131`), `"not legal"`
(`CommandAndLifecycleTests.cs:59`), `"open challenges"` (`:83`), `"cannot transition"`
(`ReducerTests.cs:147`). Rewording an existing message while adding a new one nearby will break
tests that look unrelated to the change.
