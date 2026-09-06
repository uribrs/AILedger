# Verifier 2 — close-conceptual-gaps-against-dossier

Verdict: **PASS**. The amendment landed complete, both repairs hold, and nothing verifier-1 passed
was disturbed. One constraint deviation is recorded below as a note, not a repair.

Evidence base: `git diff 87ca8b14652371103950095b621acd2e987635f9` (20 modified files, 6 new test
files, 1013 insertions), a full build and test run executed by this verifier, nine named tests run
individually, and two live CLI runs against scratch ledgers — one driving the whole `work block` →
`work unblock` → `run` → `work complete` path and the causal-invalidation refusal, one driving the
escalation option rules.

## Independently reproduced results

- `dotnet build AILedger.sln --no-restore` — Build succeeded, **0 Warning(s), 0 Error(s)**.
- `dotnet test AILedger.sln` — **Failed: 0, Passed: 140, Skipped: 0**. Verifier-1 saw 137.
- Live CLI, real file store: `work block --id W1 --reason waiting` → `work unblock --id W1` →
  `run start` → `run complete` → `work complete` all exit 0, ending `W1` at `completed`.
- Live CLI, causal case: claim `C1` rejected while `W2` was running → `W2` becomes `blocked` →
  `work unblock --id W2` exits 1 with
  `Work item depends on 'rejected' claim 'C1'. Add a replacement work item on a current claim instead of unblocking this one.`
- Live CLI, repair path is genuinely open: `claim add C2` then
  `work add --id W3 --depends-on C2` both exit 0 after the invalidation.

## The delta, checked

### 1. `work unblock` end to end

Present at every layer the amendment names: `UnblockWorkItemCommand`
(`src/AILedger.Core/Contracts/Commands.cs:197`, registered `work.unblock` at `Commands.cs:26`),
`WorkItemUnblocked` (`Events.cs:63`, registered `work.unblocked` at `Events.cs:38`), reducer arm
(`TaskReducer.cs:36`), replay validator arm (`TaskTransitionValidator.cs:81`, method at `:705`),
`ManageWork` gate (`AuthorizationPolicy.cs:64`), CLI case (`CliApplication.cs:234`), option table
(`CliApplication.cs:638`), help text (`CliApplication.cs:725`).

`ManageWork` is the same gate as complete and block, and at the same strength. All three validator
sites call `RequireAuthority(..., Capability.ManageWork)` without `operatorRequired`
(`TaskTransitionValidator.cs:655, 687, 710`), while `work add` keeps `operatorRequired: true` at
`:360`. That matches the amendment and the execution decision already in `decisions.md`.

### 2. The two rule copies agree

Compared `CommandHandler.UnblockWorkItem` (`CommandHandler.cs:688-710`) against
`TaskTransitionValidator.ValidateWorkItemUnblocked` (`TaskTransitionValidator.cs:705-725`) predicate
by predicate:

| predicate | command copy | replay copy | agree |
|---|---|---|---|
| work item exists | `Get(state.WorkItems, …)` | `Get(state.WorkItems, …)` | yes |
| status must be `Blocked` | `!= Blocked` throws | `!= Blocked` throws | yes, message identical |
| no dependency claim `Rejected` or `Superseded` | `FirstOrDefault(… is Rejected or Superseded)` | `Any(… is Rejected or Superseded)` | yes |
| authority | `AuthorizationPolicy.cs:64` → `ManageWork` | `RequireAuthority(… ManageWork)` | yes |

Two cosmetic differences, neither a divergence: the command copy reads `state.Claims[claimId]` where
the replay copy uses `Get(…)`, and the two refusal messages differ in wording. The raw indexer is safe
because `AddWorkItem` enforces `EnsureReferencesExist(state.Claims, …)` at `CommandHandler.cs:341` and
no path deletes a claim. Recorded as backlog.

### 3. `work unblock` cannot undo causal invalidation

Three independent reasons, all checked:

- The claim guard refuses while any dependency claim is `Rejected` or `Superseded`, in both copies.
  Proved by `WorkLifecycleTests.UnblockingCannotUndoCausalInvalidation` and reproduced live.
- `Rejected` and `Superseded` are terminal claim statuses, so the guard never lifts.
- `Stale` is unreachable by unblock at all — the status guard requires `Blocked`. `Stale` is written
  in exactly one place, `CommandHandler.cs:830` inside `AddDependencyInvalidations`, so the
  amendment's premise that `Stale` always implies a rejected dependency is confirmed by grep, not
  assumed.

The repair path the refusal message promises is real: verified live that a new claim plus a
replacement work item are both accepted after the invalidation.

### 4. Reviewer isolation repair holds and did not over-correct

`ContextArtifactKind.Escalation` is in `ReviewerExclusions` (`ContextAssembler.cs:32`); `Alternative`
is not. `IsAllowedForRole` is applied to `stateArtifacts.Concat(availableArtifacts)`
(`ContextAssembler.cs:70-72`), so the exclusion covers escalations built from task state, not only
supplied artifacts — this matters, because escalations are state artifacts and a filter applied only
to `availableArtifacts` would have looked correct and done nothing.

`ContextIsolationTests.CodeReviewerNeverSeesAnOpenEscalationButStillSeesRejectedAlternatives` asserts
both halves: no `Escalation` artifact and no content containing `Recommended`, but `ALT1` present.
Passed. `R4_CodeReviewerManifestExcludesForbiddenArtifacts` still passes, so the four original
exclusions are intact. Gap 2's criterion "assembled context includes recorded alternatives" therefore
survives even for the most restricted role.

Residual, backlog: `relevantIds` is computed from `stateArtifacts` at `ContextAssembler.cs:65-67`
before the role filter runs, so an excluded escalation's `RelatedIds` still widen a reviewer's
relevance set. No forbidden *kind* leaks — those are filtered by kind regardless — so isolation is
not weakened, but an escalation on work item B can still admit a B-related external artifact into a
reviewer's manifest. Code-reviewer-1 flagged the same mechanism as inert; it stays inert.

### 5. Escalation option rules, both kinds, both copies

The empty and duplicate checks are hoisted above the `switch` in both files
(`CommandHandler.cs:489-494`, `TaskTransitionValidator.cs:529-534`). Reproduced live through the CLI:

| input | result |
|---|---|
| `true-unknown` + `--option "" --option ""` | refused, `An escalation cannot carry an empty option.` |
| `true-unknown` + `--option a --option a` | refused, `Options cannot contain duplicates.` |
| `true-unknown` + `--option a --option b` | accepted |
| `business-decision` + `--option a --option ""` | refused, empty option |
| `business-decision` + `--option a --option a --recommend a` | refused, duplicates |

### 6. Documentation corrections

`docs/architecture.md` no longer claims both new kinds are unconditionally eligible — the "Code review
must remain independent" row now names open escalations, and the "Context behavior" paragraph states
the one-role exception and why. The "Finished work is asserted, not inferred" row describes
`work unblock` and its invalidation limit. `docs/operator-guide.md:122` lists `work unblock` in the
command surface and `:143-151` documents it with the refusal rule under the heading
"Blocking is reversible, invalidation is not".

## Success Criteria

Gaps 1, 2 and 4 were confirmed met by verifier-1 and are unchanged in substance; the only edit
touching them is the option hoist (tightening, checked above) and the `Escalation` exclusion (checked
above, no criterion depends on escalations reaching a reviewer). Re-confirmed by the 140-test green
run. Re-stated below only where the delta touches them.

| criterion | result | evidence |
|---|---|---|
| G1 all six criteria | met | unchanged from verifier-1; `EscalationTests` all pass in the 140-test run; refusals now also cover `TrueUnknown` |
| G2 all four criteria | met | `AlternativeTests` pass; alternatives still reach context, including a `CodeReviewer` |
| G3 all seven criteria | met | unchanged; `TaskReducer.CompleteRun` (`:135-153`) has no branch on run status |
| G4 all five criteria | met | unchanged; `ConstraintContextTests` pass |
| G3a `work unblock` end to end: command, event, reducer, replay validator, CLI, help | met | all seven sites listed in section 1; live CLI run exits 0 |
| G3a requires `ManageWork`, same gate as complete and block | met | `AuthorizationPolicy.cs:64`; `TaskTransitionValidator.cs:655, 687, 710` all identical |
| G3a returns `Blocked` to `Paused` and clears block reason | met | `TaskReducer.cs:36`; `AManuallyBlockedItemUnblocksAndCanThenRunAndComplete` asserts `Paused` and `Null(BlockReason)`; `NewCommandReplayTests` asserts both after replay |
| G3a `Stale` is not unblockable | met | status guard requires `Blocked` in both copies; `Stale` written only at `CommandHandler.cs:830` |
| G3a refused while a dependency claim is `Rejected` or `Superseded`, message names the repair path | met | both copies; live refusal message quoted above |
| G3a test: manually blocked item unblocks, then runs and completes | met | `WorkLifecycleTests.AManuallyBlockedItemUnblocksAndCanThenRunAndComplete` — passed individually |
| G3a test: item blocked by claim invalidation is refused | met | `WorkLifecycleTests.UnblockingCannotUndoCausalInvalidation` — passed individually |
| Whole: round trip through `events.jsonl` | met | `NewCommandReplayTests.R1_*` now commits 15 commands including block/unblock and asserts `W3` replays `Paused` with null reason |
| Whole: projections carry escalations, alternatives, constraints | met | asserted in `R1_*`; unchanged by the delta |
| Whole: zero build warnings, tests pass | met | reproduced above |
| Whole: docs describe the new commands and changed run semantics | met | section 6 |

## Stop Conditions

None tripped. A2 and A3 remain validated, so neither the run-auto-completion nor the schema-bump stop
condition fired. No criterion required a constraint violation to meet. This is verifier round 2, so
the round cap is respected — returning PASS means no third round is needed.

## Assumption Disposition

| id | status | name | citation | actor |
|---|---|---|---|---|
| A1 | VALIDATED | providers-untouched | `git diff --stat 87ca8b1` lists 20 modified files, none under `src/AILedger.Providers`; the two repairs added no provider file | verifier |
| A2 | VALIDATED | run-completion-blast-radius-is-tests-only | unchanged by the delta; `TaskReducer.cs:147-152` still writes only `Paused`, and `WorkItemStatus.Completed` is written in `src/` only from `WorkItemCompleted` at `TaskReducer.cs:33` | verifier |
| A3 | VALIDATED | old-log-replays-without-schema-bump | `NewCommandReplayTests.AnEventLogWithoutTheNewEventTypesStillReplays` passes in the 140-test run; `CurrentSchemaVersion` still `1`; the new `work.unblocked` type is appended, not inserted | verifier |
| A4 | VALIDATED | open-escalation-blocks-completion | `WorkLifecycleTests.CompletingWorkIsRefusedWhileAnEscalationOnItIsOpen` passes in the 140-test run | verifier |
| A5 | VALIDATED | constraint-kind-already-always-included | `ConstraintContextTests.ActiveConstraintsEnterContextAndSupersededOnesDoNot` passes; `ContextArtifactKind.Constraint` unchanged in `AlwaysIncludedKinds` at `ContextAssembler.cs:12` | verifier |
| A6 | VALIDATED | pre-decision-alternative-reaches-context | `AlternativeTests.AnAlternativeRejectedBeforeAnyDecisionExistsStillReachesLaterContext` passes; strengthened by `ContextIsolationTests.CodeReviewerNeverSeesAnOpenEscalationButStillSeesRejectedAlternatives`, which shows the alternative survives even the strictest role filter | verifier |
| A7 | VALIDATED | fake-adapter-evidence-about-real-runs | overturned from verifier-1's NEVER-TESTED. `NewCommandReplayTests.cs:40-46` now issues `run.start` then `run.complete` through `FileGovernedTaskService` with no adapter at all and asserts `Paused`, then `work.complete`. The structural reason it generalises: `TaskReducer.CompleteRun` (`TaskReducer.cs:135-153`) no longer branches on `AgentRunStatus`, so the work-item transition is a total function of the event and cannot vary with adapter identity. `CliApplicationTests.cs:443` additionally drives `provider launch` end to end and asserts `Paused`. Scope stated honestly: no test invokes a vendor CLI binary, and none in this suite can | verifier |
| A8 | REJECTED | new-aggregate-is-contained-to-handler-reducer-state | unchanged, and the delta reinforces it: `work unblock` alone required arms in `Commands`, `Events`, `TaskReducer`, `TaskTransitionValidator`, `AuthorizationPolicy` and `CliApplication` | verifier |
| A9 | REJECTED | invalidation-filter-unchanged | status unchanged. The wording correction is verified accurate: `git show 87ca8b1:…/CommandHandler.cs:561` reads `not (Blocked or Stale)`, so `Completed` was never in the baseline skip list. `decisions.md` and `assumptions.md` now say so | verifier |
| A10 | REJECTED | run-status-branch-can-be-preserved | unchanged; the ternary is gone at `TaskReducer.cs:147-152` | verifier |
| A11 | VALIDATED | unblock-returns-to-paused-without-restoring-prior-status | `TaskReducer.cs:36` sets `Paused` and clears `BlockReason`; `AManuallyBlockedItemUnblocksAndCanThenRunAndComplete` proves `Paused` is workable by running and completing from it, and the live CLI run reproduced it on an item whose pre-block status was `proposed`. The clause "and a repaired one" is vacuous rather than false — an invalidation-caused block can never be unblocked, per A12 | verifier |
| A12 | VALIDATED | refusal-leaves-a-usable-repair-path | refusal proved by `UnblockingCannotUndoCausalInvalidation` and reproduced live. The repair path was exercised live and is open: after `C1` was rejected, `claim add C2` and `work add --id W3 --depends-on C2` both exit 0. `CommandHandler.cs:341-342` shows why the old item cannot be salvaged — `EnsureDependenciesAreCurrent` refuses a new work item on a rejected claim, so a new claim is required, which is exactly the recorded intent | verifier |

## Attention Item Disposition

| id | final disposition | name | evidence |
|---|---|---|---|
| R1 | handled | dual-kernel-rule-drift | ran `NewCommandReplayTests.R1_EveryNewCommandCommitsAndReplaysThroughTheFileStore` — passed. It now commits 15 commands including `work.block`, `work.unblock`, `run.start` and `run.complete` through `FileGovernedTaskService` and reads state back from a fresh service instance. I also compared the unblock rule copies by hand — see delta section 2 |
| R2 | handled | unregistered-event-bricks-task | ran `EventRegistrationTests.R2_EveryDerivedEventTypeIsAcceptedByTheTransitionValidator` — passed. This is the test that catches a missing `WorkItemUnblocked` arm, and it is the reason the amendment could not silently brick a log |
| R3 | handled | workitem-blocked-conflation | ran `WorkLifecycleTests.R3_ManuallyBlockedItemStillReactsWhenItsClaimIsRejected` — passed. Still correct after the amendment: a manually blocked item whose claim is rejected becomes `Stale`, which unblock then refuses |
| R4 | handled | constraint-artifact-id-collision | ran `ConstraintContextTests.R4_StateConstraintsReplaceTheHardcodedContextArtifact` — passed. Also ran `ContextIsolationTests.R4_CodeReviewerManifestExcludesForbiddenArtifacts` — passed, confirming the exclusion-list edit did not disturb the original four |
| R5 | handled | hidden-workitem-assertion | ran `CliApplicationTests.CancellationAfterSuccessfulProviderReturnStillPersistsTerminalRun` — passed. `CliApplicationTests.cs:443` asserts `Paused` with a comment naming R5 |

## Decision Drift

| decision | outcome |
|---|---|
| Escalation as its own aggregate, not a flag | landed as decided |
| Escalation payload enforced in the kernel, not the CLI | landed as decided, mirrored in the replay validator |
| Alternative standalone, optional decision link | landed as decided |
| Successful run → `Paused`; completion is an explicit command | landed as decided |
| `ManageWork` to complete/block; operator to resolve an escalation | landed as decided; `work unblock` joined the `ManageWork` group consistently |
| Constraints on task state, replacing the hardcoded sentence | landed as decided |
| Unverified: four gaps independent behind one frozen contract change | held; the amendment reused the same frozen contract shape with no rework |
| Unverified: changing run semantics breaks only `Completed`-asserting tests | held |
| Execution: complete/block gated on `ManageWork` only | landed as decided |
| Execution: invalidation skips only `Stale`, not `Blocked` | landed as decided, and the overstated consequence verifier-1 found is now corrected in both `decisions.md` and `A9`. I re-checked the baseline at `87ca8b1:CommandHandler.cs:561` — the correction is accurate |
| Execution: `WorkItem.BlockReason` added | landed as decided; `work unblock` clears it, closing code-reviewer-1's cosmetic note for the unblock path |
| Execution: escalation rules mirrored in the validator; collapsing the duplication deferred | landed as decided, backlog item 1 |

### Two decisions made during the repair round are missing from `decisions.md`

Both are recorded in `execution_notes.md` under "Repairs after review round 1", so nothing is hidden,
but the Execution Rules ask for `decisions.md` too and neither reached it:

1. **Excluding `Escalation` from reviewer context while deliberately keeping `Alternative`.** This is
   a real judgment call with a stated rationale — an escalation carries a recommendation, an
   alternative is design history like a `Decision`. It belongs in the decision ledger, not only in the
   execution log. The code comment at `ContextAssembler.cs:28-31` carries the reasoning, which
   mitigates it.
2. **Fixing code-reviewer-1's Minor 4 instead of backlogging it.** See the constraint note below.

## Constraint check on the repairs

Eleven constraints in `constraints.md`, checked against the delta only. Ten honoured:

- Scope stayed inside the amendment; no sixth gap opened. The `run start` guard on `Completed` that
  looked like new scope is baseline (`87ca8b1:CommandHandler.cs:376`).
- Simplest mechanism: unblock is one arm per switch site, no new abstraction.
- Kernel shape followed at every one of the seven sites.
- Round trip and projection coverage extended to the new event.
- `cognitive/` untouched; `AILedger.Providers` untouched.
- Authorization not weakened — a new command under an existing capability. Causal invalidation not
  weakened — strengthened, since unblock explicitly refuses to undo it. Reviewer isolation restored
  to at least its baseline strength.
- Review round cap respected.
- Not committed: `git log` head is still `87ca8b1`, everything is in the working tree.

**One deviation, recorded as a note rather than a repair.** The constraint reads: "A code-review or
verifier finding is a repair only if it violates a success criterion or stop condition in this
contract. Everything else is recorded as a backlog item, not fixed in this task." Code-reviewer-1's
Minor 4 (empty and duplicate options accepted for `TrueUnknown`) violates no success criterion — G1's
"at least two distinct options" applies only to `BusinessDecision`, and duplicates were already
refused there. It was fixed anyway. The fix is four lines hoisted above a switch in two files, it
tightens rather than loosens, both copies moved together, and `execution_notes.md` states plainly
that it was done. Proportionate call for a personal POC: flag it so the operator sees the scope
discipline slipped, do not require a revert.

By contrast, the reviewer-isolation repair was correctly treated as mandatory. It fixes a violation of
"do not weaken code-reviewer context isolation", and leaving it would have meant shipping a delivered
constraint breach. Fixing it rather than stopping was the right call, because the fix satisfies both
that constraint and Gap 2's context criterion at once.

## Backlog

Not repairs. Verifier-1's six items stand except item 1, which the amendment closed. New or revised:

1. **Both unblock rule copies use a different claim lookup.** `CommandHandler.cs:701` indexes
   `state.Claims[claimId]`; `TaskTransitionValidator.cs:720` uses `Get(…)`. Equivalent today because
   `AddWorkItem` enforces claim existence and nothing deletes a claim. It is the same shape as the
   untrimmed-scope divergence code-reviewer-1 found — worth normalising if either site is touched.
2. **Escalation `RelatedIds` still widen a code reviewer's relevance set** even though the escalation
   itself is excluded, because `relevantIds` is built before the role filter
   (`ContextAssembler.cs:65-72`). Inert today; becomes real when `PromptContract` or `VerifierOutput`
   artifacts are actually supplied.
3. **An item unblocked from `Blocked` lands on `Paused` even if it was never started.** Verified live:
   a `proposed` item that is blocked and unblocked ends `paused`. Functionally correct — it runs and
   completes — but the status name implies a history the item does not have.
4. **`decisions.md` is missing the two repair-round decisions** listed under Decision Drift.
5. **`README.md` says 137 automated tests; the suite is now 140.** The count was updated once during
   execution and not again after the repairs. No criterion covers it.
6. **Removing the hardcoded constraint still empties default context** (verifier-1 item 2,
   code-reviewer-1 Minor 3). Criterion-mandated removal, correctly left unfixed. Seeding
   `operator-authority` as a governed constraint at `task open` remains the natural follow-up.
7. **Verifier-1 items 3, 5 and 6 stand unchanged**: the 642-line `TaskTransitionValidator` duplication
   (now larger again), the unproduced `AgentRunStatus.Pending`, and the work sitting uncommitted on
   `main` rather than on `conceptual-gaps-v2.1`.

## Action items

- Nothing to repair. The task may proceed to code-reviewer round 2 and close-out.
- Add the two repair-round decisions to `decisions.md` before archiving, so the ledger records the
  reviewer-isolation judgment where decisions are read.
- Update the `README.md` test count to 140 at close-out.
- Note in the lessons ledger that A7's disposal came from deleting the branch the belief depended on,
  not from testing a real provider. That is the reusable form of the lesson.

**PASS.**
