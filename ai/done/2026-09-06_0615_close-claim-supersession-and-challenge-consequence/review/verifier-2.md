# Verifier 2 — close-claim-supersession-and-challenge-consequence

Round 2 of a hard cap of 2. Verified against the working tree vs
`310061fefbe858dd79ee536d583fe22541aed27c`. Work is still uncommitted; `git log` head is unchanged.

Scope of this pass: the delta since `verifier-1.md`, plus a re-confirmation that the repairs did not
disturb what round 1 already passed. I did not re-litigate round 1's passes except where a repair
touched them.

## Commands I ran myself

- `dotnet test AILedger.sln` — **155 passed, 0 failed, 0 skipped**. Round 1 was 153.
- `dotnet build AILedger.sln --no-restore` — Build succeeded, **0 Warning(s), 0 Error(s)**.
- Each of the five attention-item tests, plus the two new tests, individually — all passed.
- Eight adversarial probes written as a temporary test file, run, then deleted. Tree is clean.

## The delta

`git status` shows the same 13 modified and 2 new source files as round 1. The changes since round 1
are confined to: `docs/architecture.md`, `CommandHandler.cs`, `TaskTransitionValidator.cs`,
`TaskReducer.cs`, `ChallengeConsequenceTests.cs`, `ClaimSupersessionTests.cs`,
`NewCommandReplayTests.cs`, and the three task-state documents. Everything round 1 verified in the
other files is untouched.

---

## Repair 1 — `docs/architecture.md` (verifier-1's only required repair)

**Closed.** The rule-to-mechanism table now carries two new rows and a corrected one.

| Row | State |
|---|---|
| "Bad assumptions must affect downstream work." | Corrected. Now reads "Rejecting a claim, **or superseding it as a correction**, emits invalidation events…". The old text asserted that superseding always invalidates, which a refinement makes false. |
| "Superseding a claim is not the same act as rejecting it." | New. States the named replacement, the state-derived outcome, both branches, and that the replay validator re-derives and refuses a mismatched outcome. |
| "A challenge that is upheld must change something." | New. States the three consequences by target type, the evidence bar, the direction limitation, the already-true refusal, and the consequence-capability requirement. |

I checked every claim in the two new rows against the code rather than accepting the prose:

- "the kernel derives the consequence from state rather than from a flag set by the actor" —
  `ResolveClaimCommand` has no outcome field; `CommandHandler.DeriveSupersession` computes it.
- "the replay validator re-derives the outcome … and refuses an event whose recorded outcome does not
  match" — `TaskTransitionValidator.ValidateClaimResolved`; round 1 proved refusal in both
  directions.
- "No challenge can be supported without evidence" — true after the repair; see below.
- "a claim challenge additionally requires every piece of its evidence to refute that claim, since
  direction is only modelled for claims" — accurate, and the limitation is stated rather than hidden.
- "the disposing actor must hold the capability its consequence requires" —
  `CommandHandler.RequireConsequenceCapability`.

`docs/operator-guide.md` states the same two limitations in the same words. The two documents now
describe what the code actually does for both behaviours. The Whole-task criterion is met.

## Repair 3 — the stale `AgentRunStatus.Pending` message

**Closed in code.** `TaskTransitionValidator.cs:524` now reads "Only an active run can transition to
a terminal status."; its twin `CommandHandler.cs:568` reads "Only an active run can be completed."
`grep -rin pending src docs tests` finds no surviving reference to the removed member in any `.cs`
file. Two prose references survive in `docs/operator-guide.md:284` ("one pending/active run") and
`:327` ("no active/pending run"). Those were round 1 Backlog 2 and are still backlog — they are not
what any success criterion asks the docs to describe.

The false claim in `execution_notes.md` was corrected, and the correction is itself recorded rather
than quietly rewritten. Good.

---

## Repair 4 — the evidence bar. I tried to defeat it.

The bar now sits at the top of `CommandHandler.AddChallengeConsequence`, **before** the target-type
switch, so it is structurally impossible for one arm to miss it:

```csharp
if (challenge.EvidenceIds.Count == 0)
{
    throw new GovernanceException($"Challenge '{challenge.Id}' carries no evidence and cannot be supported.");
}
```

Eight probes, all behaved as the design claims:

| Probe | What I tried | Result |
|---|---|---|
| A | Forged `DecisionOverturned` at replay, backed by a hand-placed `Supported` challenge with `[]` evidence | Refused — `carries no evidence and cannot overturn a decision` |
| B | Control: the same forged pair with one evidence record | Accepted, decision `Invalidated` — so probe A failed on the bar, not on something incidental |
| C | Forged `ClaimResolved(Rejected, [])` at replay | Refused on the pre-existing evidence rule — this is how the claim arm's bar is mirrored |
| D | Evidence record that supports and refutes nothing, against a `decision` target | **Accepted.** The decision/work arms check existence, not direction. Documented in both docs; see below |
| E | `"  Decision "` as the target type, evidence-free | Refused — `Trim().ToLowerInvariant()` normalises before the switch, and the bar runs before it anyway |
| F | Evidence-free `claim` challenge | Refused with `no evidence`, and **not** with the direction message — code-reviewer-1 finding 6's misleading-message case is resolved for the empty-evidence path |
| G | Refinement with a `Completed` and a live dependent | `Completed` untouched at `[C1]`; live work and live decision both re-pointed to `C2` |
| H | Correction against a `Completed` dependent | `Completed` → `Stale`. See the filter-comparison finding below |

**Mirroring, arm by arm.** The lead asked specifically whether the bar is enforced in *both* rule
copies for every arm, not only the decision one that was mirrored explicitly.

| Arm | CommandHandler | TaskTransitionValidator | Mirrored? |
|---|---|---|---|
| `decision` | `EvidenceIds.Count == 0` refused | `ValidateDecisionOverturned` repeats the same check against the linked challenge | Yes — probe A |
| `claim` | same bar, plus every-piece-refutes | `ValidateClaimResolved` refuses a `Rejected` resolution with no evidence, and checks direction per piece | Yes in effect — probe C |
| `work` | same bar | **None possible.** `WorkItemBlocked` carries no `ChallengeId`, so replay cannot know the block was a challenge consequence | No |

The `work` gap is not new and is not a weakening. It is the same shape as the already-true guard and
the capability guard on that arm: the handler is strictly stricter than the validator, so every event
the handler emits still replays. Closing it needs a distinct event type carrying the `ChallengeId` —
code-reviewer-1 finding 4, deliberately deferred, and round 1 Backlog 5/9. Recorded again as backlog,
not a repair: no success criterion requires the work arm to be evidenced at all, let alone at replay.

**Where the bar can still be bought.** Probe D. For `decision` and `work`, any evidence record at all
satisfies it, including one that supports and refutes nothing. That is the honest limit of the model,
and both `docs/architecture.md` and `docs/operator-guide.md` now say so explicitly rather than
implying a direction check that does not exist. The bar is procedural, as the refinement gate is.

**Corrected tests.** `grep -rn RaiseChallengeCommand tests/` returns four call sites. Three now pass
real evidence; the fourth (`CommandAndLifecycleTests.ArchiveRejectsOpenChallenge`) raises with `[]`
but never disposes it `Supported`, so it does not encode the hole. `R4_…` now adds `E4` before
raising `CH1`. The new test `AnEvidenceFreeChallengeCannotBeSupportedAgainstAnyTarget` asserts the
refusal for all three target types **and** that neither the decision nor the work item moved. The old
hole is not asserted anywhere.

---

## Repair 5 — the repoint status filters. They do not match exactly.

The lead asked me to compare them and say how they differ if they do.

| Dependent | `TaskReducer.RepointDependencies` (refinement) | `CommandHandler.AddDependencyInvalidations` (correction) | Same? |
|---|---|---|---|
| Decisions | `Status is Proposed or Accepted` | `Status is Proposed or Accepted` | **Identical** |
| Work items | `Status is not (Stale or Completed)` | `Status is not Stale` | **Different** — the repoint additionally excludes `Completed` |

Probe H proves the difference is live: a correction against a claim that a `Completed` work item
depended on still moves that item to `Stale`, while a refinement leaves it alone.

**My judgment: the code is right and the prose around it is wrong.** Excluding `Completed` from the
repoint is exactly what the criterion and the new test are about — a finished item's dependency list
is the record of what it was actually built on. What is wrong is three statements that describe the
filters as identical when they are not:

- `TaskReducer.cs:187` comment — "The same dependents the correction path invalidates, and no others."
- `ClaimSupersessionTests.cs:93` comment — "must select the same dependents the correction path would".
- `execution_notes.md`, repair 2 — describes the correction filter as "non-`Stale` work", then says
  "Both filters added", without noting that the added work filter is deliberately narrower.

None of this violates a success criterion. The criterion says only that a refinement re-points
dependent decisions and work items; it names no filter. **Backlog**, one comment and one note.

The safety argument round 1 and code-reviewer-1 both made still holds after the change, and the
change only narrows what is touched, so it cannot have opened anything.

---

## Re-confirmation of what round 1 passed

- Gap A criteria: unchanged code apart from the repoint filters. Probe G re-confirms both branches.
- Gap B criteria: the evidence bar is additive — every previously passing refusal still refuses, and
  `R5_…` still fails on the already-true message because its challenge carries evidence.
- Hygiene: no `.cs` reference to either removed member; both messages now say "active run".
- Zero build warnings; 155/155.

## Constraints

| Constraint | Held | Note |
|---|---|---|
| Scope limited to the two gaps plus enum hygiene | yes | The delta touches no third subsystem |
| **Do not collapse the `CommandHandler` / `TaskTransitionValidator` duplication** | **yes** | The evidence bar is written out by hand twice, with a `// Mirrors the evidence bar in CommandHandler.AddChallengeConsequence.` comment rather than a shared call. No `using`, no shared static class, no helper appears on the `+` side of the diff for either file. The repairs made the duplication larger, not smaller — which is what this constraint asks for |
| Every rule in both copies, agreeing | qualified yes | Two of three arms mirrored; the `work` arm is not expressible at replay without a new event type. Handler is strictly stricter, so nothing it commits fails replay. Same pre-existing shape round 1 accepted |
| New event types registered in the validator switch | yes | `R2_…` still passes with both new types |
| No `GovernedTaskState` property reorder or rename | yes | `TaskState.cs` is not in the diff |
| No changes to `cognitive/` or `src/AILedger.Providers` | yes | Neither appears in the diff |
| No weakening of authorization or causal invalidation | yes | The delta only adds refusals and narrows what the repoint touches |
| Simplest mechanism, no hardening beyond criteria | yes | Repair 4 is one guard clause plus its mirror; repair 5 is two `Where` clauses |
| **A finding is a repair only if it violates a criterion or stop condition** | **deviated, deliberately** | Neither code-reviewer-1 Major was a criterion violation on a strict reading — the criteria require the evidence-direction rule only for `claim` targets, and name no filter for the repoint. `constraints.md` says such findings are "not fixed here". Both were fixed. `execution_notes.md` argues criterion links for each; the evidence-bar one is sound (the docs criterion was false without it), the repoint one is not (it rests on "same dependents", which the code does not do). This is the lead's call and the result is better code. Recorded so it is a decision, not an accident — it is not recorded in `decisions.md` |
| At most two verifier and two code-reviewer rounds | yes | This is verifier round 2 |
| Do not commit or push | yes | `git log` head is still `310061f` |

---

## Assumption Disposition

| id | status | name | citation | actor |
|---|---|---|---|---|
| A1 | VALIDATED | replacement-id-is-the-better-gate | Unchanged by the repairs. `Claim.SupersededByClaimId` (`GovernanceModels.cs:138`) survives replay (`NewCommandReplayTests.cs:135`) and renders in `assumptions.md`; round 1 confirmed it through a real CLI run | verifier |
| A2 | REJECTED | challenge-consequence-needs-no-new-event | `DecisionOverturned` (`Events.cs:67`) was required because `ValidateDecisionInvalidated` demands a rejected or superseded dependency claim, which a challenge against the decision itself does not supply. The stop condition was scoped to a new claim *status*, not a new event type, so execution correctly continued. The repairs deepened the dependence on the new type: `ValidateDecisionOverturned` (`TaskTransitionValidator.cs:832-837`) is the only place the evidence bar could be mirrored at replay, which `DecisionInvalidated` could never have carried | verifier |
| A3 | VALIDATED | reuse-evidence-direction-rule-for-challenges | `CommandHandler.cs:432-436` requires every cited piece to refute the claim; probe F confirms an evidence-free claim challenge now fails on the evidence bar first and never reaches the direction message, so the two refusals are distinct and each names its own reason | verifier |
| A4 | VALIDATED | enum-removal-is-safe | Enums persist as camelCase strings with `allowIntegerValues: false` (`LedgerJson.cs:18`); no `.cs` reference to either member survives; 155/155. The last stale *message* was fixed in this round (`TaskTransitionValidator.cs:524`) | verifier |
| A5 | VALIDATED | consequences-do-not-disturb-archive | The archive prerequisite tests `ChallengeStatus.Open`, and `ChallengeDisposed` is appended before any consequence (`CommandHandler.cs:387-391`). The evidence bar throws *before* any event is appended, so a refused disposal leaves the challenge `Open` and still blocking archive — asserted in `AnEvidenceFreeChallengeCannotBeSupportedAgainstAnyTarget` and in `R1_…` | verifier |
| A6 | REJECTED | new-field-is-contained-to-handler-reducer-state | Unchanged by the repairs, and the repairs made it worse rather than better. The lesson says a new field is contained to the handler, reducer and state. `SupersededByClaimId` reached eight files, and the two the lesson omits — the replay validator and the operator surface — are exactly the two that needed follow-up work in both review rounds: the validator gained ~60 lines of mirrored rules, and both doc files had to be rewritten. The lesson is a systematic under-estimate for this kernel, not a near miss | verifier |
| A7 | REJECTED | terminal-records-are-inert | The specific defect that justified round 1's rejection is **fixed** — `TaskReducer.RepointDependencies` (`TaskReducer.cs:191-204`) now filters decisions to `Proposed or Accepted` and work to `not (Stale or Completed)`, confirmed by probe G. But the assumption is still false in this kernel by a different, pre-existing path: `AddDependencyInvalidations` filters work only to `not Stale`, so a **`Completed`** work item is moved to `Stale` when a claim it depended on is later rejected or corrected. Probe H reproduces it, and it passes both rule copies. That behaviour predates this task and is out of scope, so the assumption stays rejected on evidence rather than being re-validated on a partial fix | verifier |
| A8 | VALIDATED | one-command-branching-on-a-type-column | `AddChallengeConsequence` (`CommandHandler.cs:398-475`) still serves three outcomes from one command by switching on `TargetType`, and the repair strengthened the pattern: the shared precondition was hoisted **above** the switch, so a fourth arm cannot be added without inheriting it. Its alias set matches `EnsureChallengeTargetExists`, and probe E confirms casing and whitespace normalise identically | verifier |
| A9 | VALIDATED | refinement-requires-directed-evidence | Round 1's probe 7 stands and nothing in the delta touches the gate. `Refinement` requires `replacement.Status == Validated`; `Validated` requires at least one evidence record whose `Supports` names the replacement; a claim cannot be born `Validated`; a forged outcome is refused at replay in both directions | verifier |
| A10 | VALIDATED | repoint-is-expressible-without-relaxing-currency | `ValidateClaimDependenciesRepointed` (`TaskTransitionValidator.cs:779-804`) is unchanged by the repairs and `EnsureDependenciesAreCurrent` is still unmodified — the rule is satisfied, not relaxed. `R4_…` passes on a fresh `FileGovernedTaskService` instance; probe G confirms the narrowed filters still re-point every live dependent | verifier |

## Attention Item Disposition

| id | final disposition | name | evidence |
|---|---|---|---|
| R1 | handled | challenge-capability-replay-trap | Ran `ChallengeConsequenceTests.R1_SupportingAChallengeRequiresTheConsequenceCapabilityAtCommandTime` — Passed (1/1, 21 ms). `RequireConsequenceCapability` (`CommandHandler.cs:477-484`) still demands `ResolveClaim`/`ResolveDecision`/`ManageWork` per arm, matching `RequireAuthority` in each replay validator. The evidence bar was inserted **before** the capability check, which does not weaken it: the test's actor holds `DisposeChallenge` and the challenge carries evidence, so it still reaches and fails the capability guard, naming `ResolveClaim` | verifier |
| R2 | handled | new-event-unregistered | Ran `EventRegistrationTests.R2_EveryDerivedEventTypeIsAcceptedByTheTransitionValidator` — Passed (1/1, 15 ms). It reflects over `JsonDerivedTypeAttribute`, so `ClaimDependenciesRepointed` and `DecisionOverturned` are covered automatically; both remain registered in `Events.cs:39-40`, `TaskReducer.cs:37-38` and `TaskTransitionValidator.cs:84-89`. My probes drove both types through `TaskReducer.Apply` directly, which routes through the validator switch | verifier |
| R3 | handled | claim-evidence-overwrite | Ran `ChallengeConsequenceTests.R3_ChallengeRejectionPreservesEvidenceAlreadyOnTheClaim` — Passed (1/1, 22 ms). `TaskReducer.ResolveClaim` still unions the claim's existing evidence with the event's and de-duplicates. Untouched by this round's repairs | verifier |
| R4 | handled | refinement-repoint-replay-parity | Ran `NewCommandReplayTests.R4_SupersessionAndChallengeConsequencesReplayThroughTheFileStore` — Passed (1/1, 408 ms). The test was **corrected** this round: it now adds `E4` and raises `CH1` with `[E4]` instead of `[]`, so it no longer encodes the evidence-free hole as expected behaviour. It still drives a refinement chain, a correction chain and a supported decision challenge through `FileGovernedTaskService`, reads back from a second service instance, and asserts the replacement id, both dependency lists, both work statuses, the invalidated decision and the projection text | verifier |
| R5 | handled | consequence-already-true | Ran `ChallengeConsequenceTests.R5_SupportingAChallengeIsRefusedWhenItsConsequenceIsAlreadyTrue` — Passed (1/1, 20 ms). Still refuses with `would change nothing` rather than failing incidentally. The evidence bar now runs first, so an *evidence-free* challenge against an already-terminal target reports "no evidence" instead — a message-priority choice, not a lost guard, since both are refusals. The decision and work already-true guards remain untested by name (round 1 Backlog 1); I confirmed them by reading `CommandHandler.cs:446-451` and `:460-465` | verifier |

---

## Decision Drift

`decisions.md` has eight entries. Seven landed as written. One drifted and the write-back was
completed this round. Two repairs went beyond what any decision records.

1. **Supersession names its replacement rather than requiring generic evidence.** No drift.
   `ResolveClaimCommand.SupersededByClaimId` is the only new input; `Superseded` still requires no
   evidence of its own.
2. **Supersession is two behaviours, refinement and correction.** No drift. `SupersessionOutcome`
   carries the operator's reasoning in its comment at `GovernanceModels.cs:122-129`.
3. **Derived from state, never declared by the resolving actor.** No drift. The command has no
   outcome field, and replay refuses a forged outcome in both directions.
4. **Destructive default; the cheap path carries the entry condition.** No drift.
5. **A supported challenge reuses existing causal machinery rather than introducing a parallel one.**
   Drifted for the decision branch, and the write-back is now **complete**. `decisions.md` carries a
   `**Drifted during execution.**` sub-entry naming `DecisionOverturned`, why `DecisionInvalidated`
   could not express it, and that the stop condition was scoped to a new claim *status* rather than a
   new event type. Round 1's Backlog 4 is closed for `decisions.md`. `assumptions.md` still lists A2
   as `OPEN`; that file is written back at closeout from this table, which records A2 as REJECTED.
6. **Evidence-direction rule governs the challenge path too.** No drift in what it says, but the
   implementation is now **wider** than the decision. The decision speaks only of a challenge against
   a claim. Repair 4 added an evidence-*existence* bar to all three arms. That is the decision's
   principle extended, not contradicted, and both docs state the extension and its limit — but
   `decisions.md` was not updated to record it. Documentation gap only. **Backlog.**
7. **Remove the two enum members rather than finding a use for them.** No drift. The last stale
   message was fixed this round; two prose references survive in `docs/operator-guide.md`.
   **Backlog.**
8. **Proceeding on unverified: a supported claim challenge is a normal rejection, no new claim status
   needed.** Held. No claim status was added.

**Unrecorded deviation.** `constraints.md` says a review finding that violates no criterion is
recorded as backlog and "not fixed here". Both code-reviewer-1 Majors were fixed instead. The
Execution Rules require a deviation to be recorded in `decisions.md` **and** `execution_notes.md`;
only `execution_notes.md` has it, and its criterion link for the repoint repair ("they must select
the same dependents") is not what the code does. Same class of gap as drift 5 in round 1, one level
down. **Backlog**, not a repair — the deviation produced strictly better code and violates no success
criterion or stop condition.

---

## Backlog — recorded, not fixed here

1. **The repoint and correction work-item filters are not identical**, and three places say they are:
   `TaskReducer.cs:187`, `ClaimSupersessionTests.cs:93`, and `execution_notes.md` repair 2. The code
   is right; the comments are wrong. One-line fixes.
2. **The `work` arm's evidence bar has no replay mirror**, because `WorkItemBlocked` carries no
   `ChallengeId`. Code-reviewer-1 finding 4; round 1 Backlog 5. Closing it needs a distinct event
   type. Worth recording as a decision rather than leaving it implicit.
3. **`decisions.md` does not record** the widened evidence bar (drift 6) or the deviation from the
   "not fixed here" constraint.
4. **`docs/operator-guide.md:284` and `:327`** still say "pending/active run" after
   `AgentRunStatus.Pending` was removed. Round 1 Backlog 2, code half closed this round.
5. **A `Completed` work item is still moved to `Stale`** by a later correction or rejection of a claim
   it depended on (`AddDependencyInvalidations`, work filter `not Stale`). Pre-existing, out of scope,
   and the reason A7 stays rejected. It is the mirror image of the repoint repair and deserves the
   same decision.
6. Round 1 Backlog 1, 6 and the six items already in `execution_notes.md` stand. I found nothing that
   contradicts them.

---

## Verdict

**PASS.**

No finding in this round violates a Success Criterion or a Stop Condition. Verifier-1's single
required repair is closed and its claims check out against the code. Both code-reviewer-1 Majors are
genuinely repaired: the evidence bar is hoisted above the target switch so no arm can miss it, it is
mirrored at replay everywhere the event model can carry the link, and eight adversarial probes failed
to defeat it. The repoint filters are narrower than the correction path's for work items — which is
the right behaviour and the wrong comment. The three tests that had encoded the evidence-free hole are
corrected, and no test asserts it anywhere. Every constraint held, including the two the lead named:
the `CommandHandler` / `TaskTransitionValidator` duplication is intact and hand-written twice, and
authorization was strengthened rather than weakened.

A third round is **not** required.
