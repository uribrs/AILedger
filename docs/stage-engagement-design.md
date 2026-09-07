# Closing the gap: stages as the trigger, roles as the staffing

Design for the next session. The authoritative record is the ledger — decisions LD11 to LD16 and
constraint K19 in task `ledger-learning`. Only K19 exists; an earlier draft of this line cited K19
to K24. This document is the prose that will not fit in a
record until artifacts land, and it is committed rather than kept beside the ledger.

## The gap, stated precisely

The kernel already contains the pipeline's whole sequence and never walks it.

- The eleven stages are the pipeline's phases: Discovery, Research, Design, Scope, Ready, Execution,
  Verification, Repair, Review, Learn, Archive.
- Roles already filter skills correctly. A planning lead receives `workflow-coordinator`,
  `prompt-contract-designer`, `task-orchestrator` and `technical-researcher`; a code reviewer
  receives only `code-reviewer`.
- Every read of `state.Stage` in the codebase is the transition validating itself or printing it.
  Not one rule decides anything from it. The manifest publishes the stage as a `TaskGoal` artifact,
  which is to say as trivia.
- `EnsureStagePrerequisites` is the hook where sequencing belongs. It has two arms out of eleven:
  Execution requires a work item, Archive requires no active runs, no open challenges and at least
  one lesson.

So `task-orchestrator` reaches every implementation lead — 39,822 characters of it — and is never
engaged, because nothing invokes it and nothing it would produce has anywhere to go. Recon, problem
classification, the six axis scores and the orchestration plan are all sections of files whose
destination does not exist in a governed run.

## Why it happened

`GovernedExecutionBriefing` hands a launched agent six executable CLI commands and tells it findings
are not results until they are in the ledger. The skills tell it to write `orchestration_plan.md` to
a `taskPath`, and the manifest has no `taskPath`. Two protocols reached the agent; one of them ran in
the environment the agent was actually in. Every agent picked that one, correctly.

The substrate's protocol displaced the methodology's. That is the accident to undo.

## A. Stage prerequisites become the trigger

Give `EnsureStagePrerequisites` an arm per stage. Each arm requires state that only exists if the
stage was engaged, so the transition itself is the confirmation and it is refused without evidence.

| Entering      | Requires                                                                 |
|---------------|--------------------------------------------------------------------------|
| Research      | a recorded research topic, and an engaged Researcher once it is left     |
| Design        | at least one recorded alternative or accepted decision                    |
| Scope         | a current PromptContract artifact                                         |
| Ready         | a current OrchestrationPlan artifact, and every required role assigned    |
| Execution     | at least one work item — exists today                                     |
| Verification  | at least one completed run under a working role                           |
| Repair        | at least one open challenge or a verifier finding                         |
| Review        | at least one completed Verifier run                                       |
| Learn         | at least one completed CodeReviewer run for code-bearing work             |
| Archive       | no active run, no open challenge — exists today                          |

**Replay safety.** Superseded by LD16: the arms are command-time only, in `CommandHandler`, and
`TaskTransitionValidator` carries a comment saying why they are deliberately absent.

This paragraph originally argued for both copies, on the grounds that no live task has ever
transitioned stage — all three sit in Discovery with zero `stage.transitioned` events between them,
so replay cannot reject a transition no history contains. That is true of the histories on disk and
stops being true the moment a task transitions. Once a task reaches Ready under an arm as first
written, editing that arm rejects a transition that was legal when written, and these arms encode a
methodology the operator is still changing under K1, so they are the rules most likely to be tuned.
The three sequencing rules this repository already imposes — the working-run requirement, the
verifier requirement and the cross-provider rule — all live in `CommandHandler` only for the same
reason.

## B. Roles become staffing that can be confirmed

Two distinct properties, both readable from state:

- **Assigned** — the role exists in `state.Roles` against some actor.
- **Engaged** — some completed run carries that role in `AgentRun.SubjectRole`.

Engagement is what the operator asked to confirm, and it is already recordable because every run
carries its subject's role. Add to `who` a role coverage section naming, for each role, the actor
holding it and whether it has completed a run. Then the stage arms above can require engagement
rather than mere assignment: Review requires an engaged Verifier, Learn requires an engaged
CodeReviewer.

Entering Ready requires that every role the task needs is assigned to a distinct actor. Combined
with the existing cross-provider verification rule, that guarantees at least two providers appear in
any task that reaches Ready, without the kernel having to name which model does what.

## C. Artifacts become the destination

The arms for Scope and Ready require artifacts the kernel cannot hold. This is the same work as
C30 and it is already designed: task `ledger-artifacts` carries a `GovernedArtifact` aggregate with
its alternatives already discarded and its replay hazard already identified. Implement that design.

Kinds needed by the arms above: `UserRequest`, `PromptContract`, `OrchestrationPlan`,
`VerifierOutput`, and a recon kind for `research/internal-recon.md`. Those four already exist as
`ContextArtifactKind` members that nothing produces, and they are already excluded from a code
reviewer's manifest — an exclusion that is vacuous until something creates them.

Once an artifact record exists, a skill has both a trigger and a destination: the stage says which
skill applies, the artifact record says where its output lands, and the stage gate refuses the
transition until it is there.

## D. What this does not solve

Nothing here makes a session open a task. The kernel becomes organic once inside a task and stays
voluntary at the door. `CLAUDE.md` points a session at `context build` and no mechanism enforces
reading it.

Deliberately not proposed: a task-size classifier. The ceremony should scale with the stages the work
needs rather than with a judgement the kernel cannot make. Trivial work goes Discovery to Execution
and never touches Research or Design. Non-trivial work cannot reach Ready without a contract, a plan
and staffed roles, because the gate refuses.

## Sequencing

Artifacts first, because two stage arms depend on them. Then the stage arms. Then role coverage,
which is additive and can land at any point.

1. `GovernedArtifact` — the aggregate, its command, its event, replay validation. `src/AILedger.Core`
2. The artifact store and its projection. `src/AILedger.Storage`
3. `artifact record` and `artifact show`. `src/AILedger.Cli`
4. Stage arms in both copies of `EnsureStagePrerequisites`. `src/AILedger.Core`
5. Role coverage in `who`. `src/AILedger.Cli`
6. Tests for each. `tests`

Those are five disjoint directory scopes, so items 1, 2, 3 and 6 can run concurrently against a
frozen contract, as this task has done throughout. Item 4 waits on item 1, and shares
`src/AILedger.Core` with it, so the two are sequential items rather than concurrent ones. Items 3
and 5 share `src/AILedger.Cli` on the same terms.

What a narrow scope costs is recorded as LC24: the launch guard requires an agent's working
directory and every additional directory to sit inside its item's scope, so a dispatched agent
cannot build the solution or run the suite. The lead builds and tests; a verifier reads its own
subtree. LA5 records the rejected alternative of scoping every item to the repository root.

## Verification each piece needs

Every arm needs a test that has been seen to fail with the arm removed, then restored. Every arm also
needs a positive test that replay accepts a task that never transitioned, since that is the shape of
every history now on disk. The cross-provider rule applies: whoever authors a piece, the other model
verifies it.
