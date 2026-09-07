# Execution Notes

- 2026-09-06T06:15:12Z — Contract created. No implementation has started.
- 2026-09-06T06:24:00Z — Internal recon complete. Path: direct.
- 2026-09-06T06:32:00Z — Operator resolved the one open design question (see `decisions.md`): derive the supersession outcome from state, default to correction, gate refinement.
- 2026-09-06T06:55:00Z — Implementation complete. 153 tests pass, zero build warnings.

## What landed

**Gap A — supersession.** `Claim` gains `SupersededByClaimId`; `ResolveClaimCommand` and
`ClaimResolved` gain the replacement id, and the event also carries the derived
`SupersessionOutcome`. Superseding requires naming a replacement that exists, is not the claim
itself, and is not `Rejected`/`Superseded`. Resolving to `Validated`/`Rejected` refuses a supplied
replacement.

The outcome is derived in `CommandHandler.DeriveSupersession`, never declared: **refinement** when
the replacement is already `Validated` and no evidence on record refutes the original, **correction**
otherwise. A refinement emits `ClaimDependenciesRepointed`, and the reducer rewrites every dependent
decision and work item onto the replacement. A correction runs `AddDependencyInvalidations`
unchanged. `TaskTransitionValidator` re-derives the outcome at replay and refuses an event whose
recorded outcome does not match state, so a forged outcome cannot be committed.

**Gap B — challenge consequence.** `DisposeChallenge` with `Supported` appends a consequence chosen
by target type: `DecisionOverturned` (new event), `WorkItemBlocked` with a reason naming the
challenge, or `ClaimResolved(Rejected)` plus the existing cascade. A claim challenge requires every
piece of its evidence to refute the claim. A challenge whose consequence is already true is refused
by name rather than failing incidentally deep inside claim-resolution rules.

**Hygiene.** `AgentRunStatus.Pending` and `WorkItemStatus.Ready` removed; all ten guards simplified.
The "active or pending" wording was reworded in `CommandHandler` but initially missed in
`TaskTransitionValidator`, whose copy words the same rule differently — the regex that caught one did
not match the other. Corrected in the repair round; an earlier version of this note claimed both were
done.

## Attention items delivered

| id | artifact | outcome |
|---|---|---|
| R1 | `Core/ChallengeConsequenceTests.cs::R1_SupportingAChallengeRequiresTheConsequenceCapabilityAtCommandTime` | passes |
| R2 | `Core/EventRegistrationTests.cs::R2_EveryDerivedEventTypeIsAcceptedByTheTransitionValidator` | passes with both new event types |
| R3 | `Core/ChallengeConsequenceTests.cs::R3_ChallengeRejectionPreservesEvidenceAlreadyOnTheClaim` | passes; the reducer now unions evidence instead of replacing it |
| R4 | `Storage/NewCommandReplayTests.cs::R4_SupersessionAndChallengeConsequencesReplayThroughTheFileStore` | passes on first run — the two rule copies derive refinement identically |
| R5 | `Core/ChallengeConsequenceTests.cs::R5_SupportingAChallengeIsRefusedWhenItsConsequenceIsAlreadyTrue` | passes |

`InvalidationTests.SupersedingOpenClaimMarksInactiveDependentWorkStale` broke by design, exactly as
recon predicted, and was rewritten as
`SupersedingByAnUnvalidatedClaimIsACorrectionAndMarksDependentWorkStale`.

## Commands run

- `dotnet build AILedger.sln --no-restore` — 0 warnings, 0 errors.
- `dotnet test AILedger.sln --no-restore` — 153 passed, 0 failed. Baseline was 140.

## Backlog

1. Two of the operator's three stated gates have no kernel representation. `work complete` is
   holdable by any `ManageWork` actor with no notion of a human having confirmed the thing works,
   and nothing models the commit/push gate at all. Escalations are the only one of the three built.
2. The two rule copies still disagree on wording in several places (recon section F lists them); new
   rules here were written in both but the messages are not identical everywhere.
3. `Challenge.TargetId` is a `string`, not a typed id, and is compared case-sensitively. Gap B
   constructs typed ids from it.
4. Disposed challenges remain invisible in `task.md`, which filters to open ones, so a supported
   challenge leaves no trace in the projection beyond its consequence.
5. `constraint add --scope` and `work add --scope` still mean different things.
6. Constraint scope uniqueness is checked untrimmed at command time and trimmed at replay.

## Repairs after verifier-1

verifier-1 returned **requires-repair** with one item, plus a decision-drift record gap.

1. **`docs/architecture.md` described neither new behaviour** (violates the Whole-task criterion).
   The edit that should have added the two rule-to-mechanism rows silently no-op'd: I used
   `assert s.count(old)==1` on most replacements but not that one, and the line had been extended by
   the previous task since I read it, so the exact match found nothing. Both rows added, and the
   existing invalidation row corrected — it claimed superseding always invalidates, which is now
   false for a refinement.
2. **Decision drift written back to `decisions.md`**, not only to these notes, as the Execution Rules
   require. Decision 5 drifted for the decision branch: `DecisionInvalidated` demands a rejected
   dependency claim, so `DecisionOverturned` was introduced under the contingency already recorded.
3. **`TaskTransitionValidator.cs:524`** still said "active or pending" after the enum removal. Its
   twin in `CommandHandler` words the same rule differently, so the regex that fixed one missed the
   other. Fixed — and the false claim in these notes corrected above. Not a criterion violation, but
   leaving a written record that says a thing was done when it was not is worse than the wart.

`dotnet build` 0 warnings; `dotnet test` 153 passed, 0 failed.

## Backlog added by verifier-1

7. `TaskReducer.RepointDependencies` has no status filter, so a `Completed` work item's dependency
   list is rewritten to the replacement claim. Arguably wrong — completed work was done against the
   original claim — and it is why A7 was rejected.
8. Two tests claim broader coverage than they have: `ValidatedAndRejectedResolutionsRejectASuppliedReplacement`
   exercises only `Validated`, and `RejectedAndWithdrawnDispositionsStillEmitOnlyTheDisposalEvent`
   only `Rejected`. The verifier probed both missing cases by hand and the code is correct.
9. A forged-log gap: a supported claim or work challenge's consequence event can be stripped from a
   hand-edited log without the remaining history failing replay.

## Repairs after code-reviewer-1

Two Major findings, both repaired rather than backlogged, each with a criterion link.

1. **An evidence-free challenge could overturn a decision or block work.** The evidence bar was
   applied to one arm of three: the `claim` arm required every piece of evidence to refute the claim,
   while `decision` and `work` required none. `decisions.md` had recorded the principle — supporting a
   challenge must not be a second unevidenced route — and I implemented it only for claims, while
   `docs/operator-guide.md` in the same diff asserted it for all three. That made the docs criterion
   false. Worse, three of my own tests, including the R4 replay test, encoded the hole as expected
   behaviour by raising challenges with `[]` evidence.

   Now no challenge can be supported without at least one evidence record, mirrored in
   `ValidateDecisionOverturned`. Direction is still only checked for claims, because the model records
   `Supports`/`Refutes` against claims and has no equivalent relation for decisions or work items —
   stated in both docs rather than left implicit. Test:
   `ChallengeConsequenceTests.AnEvidenceFreeChallengeCannotBeSupportedAgainstAnyTarget`.

2. **`RepointDependencies` rewrote terminal dependents.** It had no status filter, while the
   correction path it mirrors filters to `Proposed`/`Accepted` decisions and non-`Stale` work. So a
   `Completed` work item's dependency list was overwritten, destroying the record of what finished
   work was actually built on and producing a state no valid `WorkItemAdded` could have created. The
   Both filters added. Test:
   `ClaimSupersessionTests.ARefinementLeavesTerminalDependentsPointingAtWhatTheyWereActuallyBuiltOn`.

The reviewer separately confirmed the repoint cannot resurrect causally invalidated work: the only
claim that could unblock a `Blocked` item is the one that invalidated it, and that claim is terminal,
so it can never later be refined.

`dotnet build` 0 warnings; `dotnet test` 155 passed, 0 failed.
