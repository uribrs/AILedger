# Orchestration Plan

## Problem Classification

- Contract sufficiency: sufficient
- Classes (maximum 3):
  - `ailedger-kernel` — minted; the target subsystem is this repo's own governance kernel.
  - `persisted-state` — existing ledger tag; three new aggregates must serialize, replay, and project.
  - `lifecycle-semantics` — minted; gap 3 changes what a work item's status means after a run.
- Additional classified prior art: A10 (`lessons.md#L-5838490b`). A7-A9 were seeded by the designer.
- Recon correction: classes confirmed. Recon refined the shape decisively — the kernel is a closed
  type hierarchy with exhaustive switches, and `TaskTransitionValidator.cs` (642 lines, added during
  the previous task's review rounds) re-validates every event at replay time, so every rule in this
  codebase is written twice in two idioms.
- New or changed artifacts:
  - 7 new `LedgerCommand` records → `CommandHandler.HandleExisting` switch (`CommandHandler.cs:66-81`) → dispatches to a per-command handler; unlisted commands hit the `default:` arm and throw.
  - 7 new `LedgerEventData` records → `TaskTransitionValidator.Validate` switch (`TaskTransitionValidator.cs:13-62`) → **design-invalidating**: its `default:` arm (`:60-61`) throws `Unsupported event data`, and `TaskReducer.Apply:10` calls it *before* applying. An unregistered event fails at write time with a misleading message, and a persisted one makes the whole task permanently unreadable.
  - 3 new `GovernedTaskState` dictionaries → `FileGovernedTaskService.MaterializedStateIsCurrentAsync` (`:278-300`) → byte-compares `state.json` against a fresh replay; appending is safe, reordering invalidates every existing file.
  - New `Capability` members → `RoleDefaults.For` (`RoleDefaults.cs:7-25`) → operator is widened automatically by `Enum.GetValues<Capability>()`; every non-operator role must be edited by hand or silently lacks the capability.
  - New state-derived `Constraint` artifact → `ContextAssembler` dedupe `GroupBy((Kind,Id)).Select(First)` (`:68-69`) → collides with the hardcoded `operator-authority` artifact (`CognitiveArtifactLoader.cs:52-56`); the lexicographically smaller content silently wins.

### Attention Items

| id | name | failure mode | causal path and impact | planned handling | source |
|---|---|---|---|---|---|
| R1 | dual-kernel-rule-drift | A rule written in `CommandHandler` disagrees with its copy in `TaskTransitionValidator` | Command validates and emits, then `TaskReducer.Apply` calls the validator, which rejects it. Command fails at write time with a replay-side message; rules silently diverge as either side is edited. | `test:tests/AILedger.Tests/Storage/NewCommandReplayTests.cs::R1_EveryNewCommandCommitsAndReplaysThroughTheFileStore` | `research/internal-recon.md` Landmine 1; `TaskTransitionValidator.cs:13-62`, `TaskReducer.cs:10` |
| R2 | unregistered-event-bricks-task | A new event type is not added to the validator switch | `default:` at `TaskTransitionValidator.cs:60-61` throws. If such an event ever reaches disk, `FileGovernedTaskService` replays the whole log and every later read fails permanently, with no repair short of editing `events.jsonl`. | `test:tests/AILedger.Tests/Core/EventRegistrationTests.cs::R2_EveryDerivedEventTypeIsAcceptedByTheTransitionValidator` | `research/internal-recon.md` Landmine 1; `RecoveryTests.cs:63-77` |
| R3 | workitem-blocked-conflation | Operator-set `Blocked` is indistinguishable from claim-invalidation `Blocked` | `CommandHandler.cs:561` skips already-`Blocked` items during invalidation, so a manually blocked item never becomes `Stale` when its claim is later rejected — causal invalidation silently stops covering it. | `test:tests/AILedger.Tests/Core/WorkLifecycleTests.cs::R3_ManuallyBlockedItemStillReactsWhenItsClaimIsRejected` | `research/internal-recon.md` Landmine 6; `CommandHandler.cs:559-568` |
| R4 | constraint-artifact-id-collision | A state Constraint and the hardcoded artifact share id `operator-authority` | Dedupe at `ContextAssembler.cs:68-69` keeps whichever has the lexicographically smaller content, so a governed constraint can be silently replaced by the hardcoded sentence. | `test:tests/AILedger.Tests/Core/ConstraintContextTests.cs::R4_StateConstraintsReplaceTheHardcodedContextArtifact` | `research/internal-recon.md` Landmine 3; `CognitiveArtifactLoader.cs:52-56` |
| R5 | hidden-workitem-assertion | The one test asserting `Completed` after a provider run is named after cancellation | `CliApplicationTests.cs:441` sits inside `CancellationAfterSuccessfulProviderReturnStillPersistsTerminalRun`, so a scan for affected tests misses it and gap 3 looks complete while a stale assertion still passes or fails confusingly. | `test:tests/AILedger.Tests/Cli/CliApplicationTests.cs::CancellationAfterSuccessfulProviderReturnStillPersistsTerminalRun` updated to assert `Paused` | `research/internal-recon.md` Landmine 5 |

### Research Questions

No research needed — all four gaps are internal state-model and command-surface changes in a single
repository. No external system behavior is in play, so no external answer could change the solution.

## Complexity Decision
- Path: direct
- Axis scores: Complexity medium | Separability low | Coupling high | Dependency order low | Execution risk medium | Worker clarity low
- Rationale: Recon found no *file-level* disjoint sets. Its phase-1 split is method-level ownership
  inside shared files, which subagents cannot hold concurrently — two workers writing
  `CommandHandler.cs` clobber each other regardless of which methods they were assigned. Coupling is
  read off recon, not estimated: thirteen constructs are contended by all four changes.

## Research Decisions
- External research: none needed. See Research Questions above.
- Internal recon: complete → research/internal-recon.md

## File Ownership
- Disjoint sets found: 0 at file level (1 near-disjoint: docs + `CognitiveArtifactLoader.cs`)
- No disjoint sets. Overlapping paths, each contended by three or four of the four changes:
  `src/AILedger.Core/Contracts/Commands.cs`, `Events.cs`, `GovernanceModels.cs`, `TaskState.cs`,
  `ContextContracts.cs`, `src/AILedger.Core/Application/CommandHandler.cs`, `ContextAssembler.cs`,
  `src/AILedger.Core/Domain/AuthorizationPolicy.cs`, `TaskReducer.cs`, `TaskTransitionValidator.cs`,
  `src/AILedger.Cli/CliApplication.cs`, `src/AILedger.Storage/MarkdownTaskProjectionWriter.cs`.
  These are closed type hierarchies with exhaustive switches; there is no per-aggregate file boundary
  to split on. The single near-disjoint set (docs) must be written after implementation anyway,
  because it describes what actually landed.
- Shared surface frozen in phase 0: not applicable — direct path.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Verification Obligations
- Cross-check against every Success Criterion in `prompt_contract.md`.
- A7 requires evidence about lifecycle semantics that does not come from a fake provider adapter.
  R1's test drives `FileGovernedTaskService` and a real replay, so it is the artifact that disposes A7.
- A3 requires proof that a pre-existing `events.jsonl` written before this change still replays.
- Confirm the two review caps held: at most two verifier and two code-reviewer rounds.
