# Orchestration Plan

## Problem Classification

- Contract sufficiency: sufficient. The one open design question — what supersession means for
  dependents — was escalated to the operator as a business decision and resolved: derive the outcome
  from state, default to destructive, gate the cheap path.
- Classes (maximum 3):
  - `ailedger-kernel` — the target subsystem.
  - `lifecycle-semantics` — both gaps change what a status transition causes.
  - `persisted-state` — new fields and events must replay.
- Additional classified prior art: A6-A8, seeded by the designer from this repo's preceding task.
- Recon correction: classes confirmed. Recon settled the question that decided whether Gap B is
  buildable at all — a later event in a batch is validated against the state produced by earlier
  events in that same batch (`CommandHandler.cs:731-746`, `TaskReducer.cs:10`), with
  `ClaimResolved → DecisionInvalidated` as existing precedent.
- New or changed artifacts:
  - `ResolveClaimCommand` gains a replacement id → `CommandHandler.ResolveClaim` and its validator
    counterpart → both must agree on the refinement/correction derivation or replay rejects a
    committed command.
  - `ClaimResolved` gains the replacement id and the derived outcome → `TaskReducer.ResolveClaim`
    (`:86-90`) → **design-invalidating**: that applier replaces the claim's whole evidence list, so
    Gap B rejecting a claim with the challenge's evidence overwrites evidence the claim already had.
  - A re-point event for refinement → `TaskTransitionValidator` → its existing dependency rule
    requires a claim to be current, so re-pointing must satisfy it against the *replacement*.
  - A challenge-caused decision event → `TaskTransitionValidator.Validate` switch → **design-invalidating**:
    `DecisionInvalidated` requires a rejected dependency claim (`:316-321`), so Gap B's decision
    branch cannot reuse it and needs a new type registered in `Events.cs` and both switches.
  - Removal of `AgentRunStatus.Pending` → ten guard sites → one message at `CommandHandler.cs:418`
    names "pending" and must be reworded; `docs/architecture.md:42` states the invariant in prose.

### Attention Items

| id | name | failure mode | causal path and impact | planned handling | source |
|---|---|---|---|---|---|
| R1 | challenge-capability-replay-trap | A supported challenge emits a consequence event whose replay-side capability differs from the one checked at command time | `DisposeChallenge` requires only `DisposeChallenge` (`AuthorizationPolicy.cs:56`), but the emitted `ClaimResolved` is validated at replay against `ResolveClaim` (`TaskTransitionValidator.cs:183`) using the same actor. An actor holding one and not the other passes the command and then makes the task permanently unreplayable. | `test:tests/AILedger.Tests/Core/ChallengeConsequenceTests.cs::R1_SupportingAChallengeRequiresTheConsequenceCapabilityAtCommandTime` | `research/internal-recon.md` Landmine 1 |
| R2 | new-event-unregistered | The challenge-caused decision event is added to `Events.cs` but missed in a validator switch | `TaskTransitionValidator.Validate` default arm throws and `TaskReducer.Apply` calls it before applying, so the command fails at write time and a persisted instance would brick the log | `test:tests/AILedger.Tests/Core/EventRegistrationTests.cs::R2_EveryDerivedEventTypeIsAcceptedByTheTransitionValidator` (existing; must still pass with the new type) | `research/internal-recon.md` Landmine 5 |
| R3 | claim-evidence-overwrite | Rejecting a claim as a challenge consequence replaces its whole evidence list | `TaskReducer.cs:86-90` sets `EvidenceIds = resolved.EvidenceIds`, so a claim that already carried supporting evidence loses it when a challenge rejects it with only refuting evidence | `test:tests/AILedger.Tests/Core/ChallengeConsequenceTests.cs::R3_ChallengeRejectionPreservesEvidenceAlreadyOnTheClaim` | `research/internal-recon.md` Landmine 6 |
| R4 | refinement-repoint-replay-parity | Re-pointing a dependent at the replacement is accepted at command time but rejected at replay | The validator requires a dependency claim to be current (`EnsureDependenciesAreCurrent`); a re-point must satisfy that against the replacement, and the two copies must derive refinement identically or a committed command fails to replay | `test:tests/AILedger.Tests/Storage/NewCommandReplayTests.cs::R4_SupersessionAndChallengeConsequencesReplayThroughTheFileStore` | `research/internal-recon.md` Landmine 13; section D |
| R5 | consequence-already-true | A challenge is supported against a target whose consequence has already happened | A claim rejected independently before the challenge is disposed makes `EnsureClaimResolution` throw from inside `DisposeChallenge`, failing for a reason the operator did not ask about; same for an already-`Invalidated` decision or `Stale` work item | `test:tests/AILedger.Tests/Core/ChallengeConsequenceTests.cs::R5_SupportingAChallengeIsRefusedWhenItsConsequenceIsAlreadyTrue` | `research/internal-recon.md` Landmine 3 |

### Research Questions

No research needed — both gaps are internal semantics in one repository, and the only genuinely open
question was an operator business decision, already resolved.

## Complexity Decision
- Path: direct
- Axis scores: Complexity medium | Separability low | Coupling high | Dependency order medium | Execution risk medium | Worker clarity low
- Rationale: recon found no file-level disjoint sets. Gap A and Gap B are explicitly non-separable —
  Gap B's claim branch emits `ClaimResolved` and therefore consumes whatever contract Gap A leaves on
  that event and on `EnsureClaimResolution`. Only the enum hygiene is region-disjoint, and it is too
  small to be worth a worker.

## Research Decisions
- External research: none needed. See Research Questions.
- Internal recon: complete → research/internal-recon.md

## File Ownership
- Disjoint sets found: 0 at file level
- No disjoint sets. Overlapping paths: `src/AILedger.Core/Contracts/GovernanceModels.cs` (all three
  changes land in it), `Commands.cs`, `Events.cs`,
  `src/AILedger.Core/Application/CommandHandler.cs`, `src/AILedger.Core/Domain/TaskTransitionValidator.cs`,
  `src/AILedger.Core/Domain/TaskReducer.cs`, `src/AILedger.Cli/CliApplication.cs`,
  `src/AILedger.Storage/MarkdownTaskProjectionWriter.cs`.
- Shared surface frozen in phase 0: not applicable — direct path.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Verification Obligations
- Cross-check every Success Criterion in `prompt_contract.md`, including the amended Gap A block.
- A9 needs evidence that an agent cannot reach the refinement path without producing directed
  evidence for the replacement — R4's replay test is the artifact.
- `InvalidationTests.cs:52` supersedes a claim with no replacement and empty evidence. It breaks by
  design and must be updated, not worked around.
- Confirm the review caps held: at most two verifier and two code-reviewer rounds.
