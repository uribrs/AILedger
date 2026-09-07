# Verifier 1 — close-conceptual-gaps-against-dossier

Verdict: **PASS**. No finding violates a Success Criterion or a Stop Condition in
`prompt_contract.md`. Six items are recorded as backlog.

Evidence base: `git diff 87ca8b14652371103950095b621acd2e987635f9` (19 modified files, 6 new test
files, 903 insertions), a full build and test run executed by this verifier, the five named
attention-item tests run individually, and a live CLI smoke run of all seven new commands against a
scratch ledger.

## Independently reproduced results

- `dotnet build AILedger.sln` — Build succeeded, **0 Warning(s), 0 Error(s)** (`TreatWarningsAsErrors` is on).
- `dotnet test AILedger.sln` — **Failed: 0, Passed: 137, Skipped: 0**. Baseline was 122.
- All seven new commands drive end to end through the real CLI and exit 0: `escalation raise`,
  `escalation resolve`, `alternative record`, `constraint add`, `constraint supersede`,
  `work complete`, `work block`. Projections render `## Active Constraints`, `## Open Escalations`
  in `task.md` and `## Rejected Alternatives` in `decisions.md`.
- The tests are not stand-ins. `Support/TestTask.cs` uses the production `CommandHandler()`, whose
  parameterless constructor wires the real `TaskReducer` and `AuthorizationPolicy`
  (`CommandHandler.cs:11-14`), and `TaskReducer.Apply` calls `TaskTransitionValidator` before
  applying. `NewCommandReplayTests` and `ConstraintContextTests.R4_*` drive `FileGovernedTaskService`
  and `CliApplication` respectively. No success criterion is satisfied against a fake.

## Success Criteria

| criterion | result | evidence |
|---|---|---|
| G1 `Escalation` object with id, kind, question, status, provenance, optional work link | met | `GovernanceModels.cs` `Escalation` record — all six fields present |
| G1 exactly two kinds | met | `EscalationKind { BusinessDecision, TrueUnknown }` |
| G1 refuse `BusinessDecision` without 2 distinct options + naming recommendation | met | `CommandHandler.RaiseEscalation`; `EscalationTests.BusinessDecisionRequiresAtLeastTwoOptionsAndARecommendationNamingOne` covers 3 refusal shapes |
| G1 refuse `TrueUnknown` without an existing evidence record | met | `EnsureReferencesExist(state.Evidence, ...)`; `EscalationTests.TrueUnknownRequiresEvidenceOfTheAttemptThatFailedToAnswerIt` |
| G1 operator-only resolution, records answer and actor | met | `AuthorizationPolicy.cs:26-29`, `TaskTransitionValidator.cs:575`; `EscalationTests.OnlyAnOperatorResolvesAnEscalationAndResolutionRecordsTheAnswer` |
| G1 tests prove both refusals and both successful paths | met | two refusal tests, two successful raises (X4 BusinessDecision, X2 TrueUnknown), plus a successful resolve |
| G2 `Alternative` object with id, statement, rationale, provenance, optional decision link | met | `GovernanceModels.cs` `Alternative` record |
| G2 non-empty rejection rationale required | met | `AlternativeTests.RecordingARejectedAlternativeRequiresItsRationale` (whitespace-only rejected) |
| G2 assembled context includes alternatives | met | `ContextAssembler.BuildStateArtifacts` + `AlwaysIncludedKinds` |
| G2 alternative recorded before any decision still surfaces later | met | `AlternativeTests.AnAlternativeRejectedBeforeAnyDecisionExistsStillReachesLaterContext` |
| G3 `work complete` / `work block` end to end incl. CLI and help | met | verified by live CLI run, not only by test; help text at `CliApplication.cs:718-719` |
| G3 both require `ManageWork` | met | `AuthorizationPolicy.cs:65`, `TaskTransitionValidator.cs:648,681` |
| G3 successful run leaves work item `Paused`, not `Completed` | met | `TaskReducer.cs:146-151` ternary removed; `WorkLifecycleTests.ASuccessfulRunPausesItsWorkItemAndOnlyAnExplicitCommandCompletesIt` |
| G3 completion refused with active/pending run | met | `WorkLifecycleTests.CompletingWorkIsRefusedWhileARunIsStillActive` |
| G3 completion refused with open linked escalation | met | `WorkLifecycleTests.CompletingWorkIsRefusedWhileAnEscalationOnItIsOpen` |
| G3 blocking records a reason, may cite an escalation | met | `WorkItem.BlockReason`; `WorkLifecycleTests.BlockingRecordsAReasonAndMayCiteAnOpenEscalation` |
| G4 `Constraint` object with id, statement, source, scope, status, provenance | met | `GovernanceModels.cs` `Constraint` record |
| G4 `constraint add` / `constraint supersede` end to end | met | live CLI run, both exit 0 |
| G4 context emits active constraints; hardcoded sentence removed | met | `CognitiveArtifactLoader.cs` hunk deletes the `operator-authority` artifact; `grep -rn operator-authority src/ docs/ cognitive/` returns nothing |
| G4 superseded constraint excluded from context | met | `ConstraintContextTests.ActiveConstraintsEnterContextAndSupersededOnesDoNot` |
| Whole: write/replay round trip through `events.jsonl` | met | `NewCommandReplayTests.R1_*` replays through a *separate* service instance |
| Whole: all three appear in Markdown projections | met | asserted in `R1_*` and observed in the live smoke run |
| Whole: zero build warnings, `dotnet test` passes | met | reproduced above |
| Whole: docs describe new commands and changed run semantics | met | `docs/operator-guide.md` command table + a new section; `docs/architecture.md` four new mechanism rows and a revised run-completion paragraph |

## Constraints

| constraint | result | evidence |
|---|---|---|
| Scope is exactly the four gaps; do not open a fifth | honoured | the only behavior touched outside the four gaps is the invalidation filter, which is causally required by gap 3 introducing `work.block` |
| Personal POC; simplest mechanism | honoured on balance | see backlog 4 |
| No hardening / policy languages / abstraction layers beyond criteria | honoured | additions are guard clauses in the existing idiom; no new layer, interface, or indirection |
| Follow the existing kernel shape | honoured | every command routes `CommandHandler` → event → `TaskReducer` → `GovernedTaskState` → `CliApplication` |
| Round-trip and projections for every new object | honoured | `R1_*` asserts state and both `.md` files |
| Do not modify `cognitive/` | honoured | `cognitive/RULES.md` and `manifest.json` mtimes are 2026-09-05 22:42-22:43, before the 2026-09-06 execution window |
| Do not touch `AILedger.Providers` | honoured | absent from `git diff --stat` |
| Do not weaken authorization, causal invalidation, or reviewer isolation | honoured | all three were strengthened or left intact; `ReviewerExclusions` is byte-unchanged |
| Findings outside criteria are backlog, not repair | honoured | six items in `execution_notes.md#Backlog` |
| Review round cap | honoured | this is verifier round 1 |
| Do not commit or push | honoured | `git log` head is still `87ca8b1`; all work is uncommitted in the tree |

The constraint quoted to me as "do not refactor `TaskTransitionValidator`" is **not present** in
`constraints.md`. Reporting against the actual text instead: the validator diff removes exactly one
line (the invalidation filter) and is otherwise purely additive — 7 case arms and 7 validator
methods. No refactor occurred, so the intent behind the paraphrase holds either way.

## Assumption Disposition

| id | status | name | citation | actor |
|---|---|---|---|---|
| A1 | VALIDATED | providers-untouched | `git diff --stat 87ca8b1` lists 19 files, none under `src/AILedger.Providers` | verifier |
| A2 | VALIDATED | run-completion-blast-radius-is-tests-only | `git grep -n "WorkItemStatus.Completed" 87ca8b1 -- tests/` returns exactly one hit, `CliApplicationTests.cs:441`; in `src/` the value is now written only at `TaskReducer.cs:34` from `WorkItemCompleted`, every other occurrence is a refusal guard, so no production path consumed run-driven completion | verifier |
| A3 | VALIDATED | old-log-replays-without-schema-bump | `NewCommandReplayTests.AnEventLogWithoutTheNewEventTypesStillReplays` passes; `CurrentSchemaVersion` is `1` at both `87ca8b1:TaskState.cs:5` and `TaskState.cs:5` | verifier |
| A4 | VALIDATED | open-escalation-blocks-completion | `WorkLifecycleTests.CompletingWorkIsRefusedWhileAnEscalationOnItIsOpen` passes in the 137-test run | verifier |
| A5 | VALIDATED | constraint-kind-already-always-included | `ConstraintContextTests.ActiveConstraintsEnterContextAndSupersededOnesDoNot` passes; `ContextArtifactKind.Constraint` appears as an unchanged context line in the `ContextAssembler.cs` hunk | verifier |
| A6 | VALIDATED | pre-decision-alternative-reaches-context | `AlternativeTests.AnAlternativeRejectedBeforeAnyDecisionExistsStillReachesLaterContext` passes | verifier |
| A7 | NEVER-TESTED | fake-adapter-evidence-about-real-runs | nothing in the diff exercises a real provider. `orchestration_plan.md` nominates `NewCommandReplayTests.R1_*` as the disposing artifact, but that test issues `CompleteWorkItemCommand` directly and never a `run.complete`, so it does not touch the lifecycle semantics A7 is about | verifier |
| A8 | REJECTED | new-aggregate-is-contained-to-handler-reducer-state | contradicted in all three denied places: `TaskTransitionValidator.cs` +227 lines with 7 new case arms, `MarkdownTaskProjectionWriter.cs` +25, `ContextAssembler.cs` +47 including `AlwaysIncludedKinds` | verifier |
| A9 | REJECTED | invalidation-filter-unchanged | the filter did change: `CommandHandler.cs:797` and `TaskTransitionValidator.cs:441` moved from `not (Blocked or Stale)` to `not Stale`. Status confirmed, but the recorded *consequence* is wrong — see Decision Drift | verifier |
| A10 | REJECTED | run-status-branch-can-be-preserved | the ternary is deleted in the `TaskReducer.cs:146-151` hunk; `WorkLifecycleTests.ASuccessfulRunPausesItsWorkItemAndOnlyAnExplicitCommandCompletesIt` proves both outcomes now require separate commands | verifier |

## Attention Item Disposition

| id | final disposition | name | evidence |
|---|---|---|---|
| R1 | handled | dual-kernel-rule-drift | ran `NewCommandReplayTests.R1_EveryNewCommandCommitsAndReplaysThroughTheFileStore` — passed. It commits 13 commands through `FileGovernedTaskService` and reads state back from a fresh service instance |
| R2 | handled | unregistered-event-bricks-task | ran `EventRegistrationTests.R2_EveryDerivedEventTypeIsAcceptedByTheTransitionValidator` — passed. It reflects over `JsonDerivedTypeAttribute` and asserts none reaches the `Unsupported event data` arm |
| R3 | handled | workitem-blocked-conflation | ran `WorkLifecycleTests.R3_ManuallyBlockedItemStillReactsWhenItsClaimIsRejected` — passed. Both copies of the filter were corrected together |
| R4 | handled | constraint-artifact-id-collision | ran `ConstraintContextTests.R4_StateConstraintsReplaceTheHardcodedContextArtifact` — passed. The collision is now structurally impossible: the hardcoded artifact is deleted |
| R5 | handled | hidden-workitem-assertion | ran `CliApplicationTests.CancellationAfterSuccessfulProviderReturnStillPersistsTerminalRun` — passed. `CliApplicationTests.cs:441` now asserts `Paused` |

## Decision Drift

| decision | outcome |
|---|---|
| Escalation as its own aggregate, not a flag | landed as decided |
| Escalation payload enforced in the kernel, not the CLI | landed as decided — and mirrored in the replay validator, which is stronger than decided |
| Alternative standalone, optional decision link | landed as decided |
| Successful run → `Paused`; completion is an explicit command | landed as decided |
| `ManageWork` to complete/block; operator to resolve an escalation | landed as decided |
| Constraints on task state, replacing the hardcoded sentence | landed as decided |
| Unverified: four gaps independent behind one frozen contract change | held — no resequencing was needed |
| Unverified: changing run semantics breaks only `Completed`-asserting tests | held — exactly one baseline test asserted it |
| Execution: complete/block gated on `ManageWork` only, not the operator role | landed as decided |
| Execution: invalidation skips only `Stale`, not `Blocked` | **landed, but the recorded consequence is factually wrong** — see below |
| Execution: `WorkItem.BlockReason` added | landed as decided |
| Execution: escalation rules mirrored in the validator; collapsing the duplication deferred | landed as decided, backlog item 1 |

### The one drift finding, judged

`decisions.md` and `A9` both state that a `Completed` work item whose claim is later rejected
becoming `Stale` is "a behavior change beyond gap 3 as written". **That is not what the diff shows.**
The baseline filter at `87ca8b1:CommandHandler.cs:561` was
`.Where(item => item.Status is not (WorkItemStatus.Blocked or WorkItemStatus.Stale))`, and the
baseline guard at `87ca8b1:TaskTransitionValidator.cs` was the same pair. `Completed` was in neither
list, so at baseline a `Completed` work item already went `Stale` on a rejected claim. The R3 change
removed `Blocked` from the skip list and nothing else.

Answer to the question put to me: **acceptable scope, not drift.** The only actual behavior change is
that an operator-blocked item is no longer exempt from invalidation, which is directly caused by gap 3
introducing `work.block` and is required to keep causal invalidation intact under the constraint
"do not weaken causal invalidation". The note in `decisions.md` overstates its own blast radius; the
overstatement is a documentation error, not a code defect.

## Backlog

Not repairs. None violates a Success Criterion or Stop Condition. Ranked by how much they will cost
later.

1. **`work block` is a one-way door.** Verified live: a `Blocked` item refuses `work complete`
   ("cannot be completed before it is repaired") and refuses `run start` ("Cannot start a run for work
   item in status 'Blocked'"). No command anywhere returns a work item from `Blocked` to `Active` or
   `Paused`. The dead-end pre-dates this task for invalidation-caused blocks, but gap 3 hands the
   operator a direct command into it, and the error message promises a repair that does not exist. A
   `work unblock` command, or resolving the cited escalation clearing the block, would close it.
2. **Removing the hardcoded constraint empties default context.** The criterion demanded the removal
   and it was done correctly, but out of the box `context build` now emits zero constraints until an
   operator runs `constraint add`. The rule "Only the operator may assign roles or change governed
   resource scope" is still enforced in code and still documented, but it no longer reaches an agent's
   assembled context. Consider seeding it as a governed constraint at `task open`.
3. **The six items already in `execution_notes.md#Backlog`** stand, and item 1 there — the 642-line
   `TaskTransitionValidator` duplicating every rule, now grown by ~190 lines — is correctly identified
   as the largest defect surface in the kernel.
4. **A few guards exceed the criteria.** Operator-role gating on `constraint add`/`supersede` was not
   asked for by any criterion and is not in `decisions.md`; `CompleteWorkItem` also refuses
   `Blocked`/`Stale`, and `BlockWorkItem` refuses a `Completed` item, neither of which any criterion
   required. All follow the existing kernel idiom and are proportionate, but they are unrecorded
   scope.
5. **`HasOpenRun` tests for `AgentRunStatus.Pending`, which no path produces** (already backlog item 3
   in `execution_notes.md`). The criterion says "active or pending", so the code is right and the enum
   member is the dead thing.
6. **Contract context says branch `conceptual-gaps-v2.1`; the work is uncommitted on `main`.** The
   "do not commit" constraint was honoured, so this is only a note: 903 lines sit unstaged in the
   operator's `main` working tree.

## Action items

- Nothing to repair. The task may proceed to code review and close-out.
- Correct the `decisions.md` and `assumptions.md` A9 wording so the ledger does not record a behavior
  change that did not occur.
- Move A7 forward as OPEN into close-out rather than treating `R1_*` as its disposing artifact; the
  orchestration plan's verification obligation on that point is unmet.
- Decide whether backlog 1 (`work block` dead-end) is worth a follow-up task before the kernel is used
  for real cross-repo work.
