# Execution Notes

- 2026-09-06T05:11:12Z — Contract created. No implementation has started.
- 2026-09-06T05:22:00Z — Internal recon complete. Path chosen: direct (no file-level disjoint sets).
- 2026-09-06T05:50:00Z — Implementation complete. 137 tests pass, zero build warnings.

## What landed

**Gap 1 — escalation.** `Escalation` aggregate (`EscalationId`, `EscalationKind`, `EscalationStatus`),
commands `escalation.raise` / `escalation.resolve`, events `EscalationRaised` / `EscalationResolved`,
capabilities `RaiseEscalation` / `ResolveEscalation`. `BusinessDecision` requires >= 2 distinct
non-empty options plus a recommendation that names one of them; `TrueUnknown` requires >= 1 existing
evidence reference. Resolution is operator-role gated in both `AuthorizationPolicy` and
`TaskTransitionValidator`, and `--status resolved` requires an answer.

**Gap 2 — alternatives.** `Alternative` aggregate, command `alternative.record`, event
`AlternativeRecorded`, capability `RecordAlternative`. Rejection rationale is required. Alternatives
are in `AlwaysIncludedKinds`, so work-item narrowing cannot hide them.

**Gap 3 — work lifecycle.** Commands `work.complete` / `work.block`, events `WorkItemCompleted` /
`WorkItemBlocked`, both gated on `ManageWork` (deliberately *not* operator-role gated, unlike
`work add`). `TaskReducer.CompleteRun` now moves a work item to `Paused` on any terminal run status;
`Completed` is only ever written by `work.complete`. Completion is refused while a run is active or an
escalation on the item is open. `WorkItem` gained `BlockReason`.

**Gap 4 — constraints.** `Constraint` aggregate, commands `constraint.add` / `constraint.supersede`,
events `ConstraintAdded` / `ConstraintSuperseded`, capability `ManageConstraints`, operator-role gated.
`ContextAssembler` emits active constraints from state; the hardcoded `operator-authority` artifact in
`CognitiveArtifactLoader` is removed.

Projections: `task.md` gained `## Active Constraints` and `## Open Escalations`; `decisions.md` gained
`## Rejected Alternatives`.

## Attention items delivered

| id | artifact | outcome |
|---|---|---|
| R1 | `tests/AILedger.Tests/Storage/NewCommandReplayTests.cs::R1_EveryNewCommandCommitsAndReplaysThroughTheFileStore` | passes |
| R2 | `tests/AILedger.Tests/Core/EventRegistrationTests.cs::R2_EveryDerivedEventTypeIsAcceptedByTheTransitionValidator` | passes |
| R3 | `tests/AILedger.Tests/Core/WorkLifecycleTests.cs::R3_ManuallyBlockedItemStillReactsWhenItsClaimIsRejected` | passes |
| R4 | `tests/AILedger.Tests/Core/ConstraintContextTests.cs::R4_StateConstraintsReplaceTheHardcodedContextArtifact` | passes |
| R5 | `tests/AILedger.Tests/Cli/CliApplicationTests.cs::CancellationAfterSuccessfulProviderReturnStillPersistsTerminalRun` | updated to assert `Paused` |

R1 was not theoretical. Changing the invalidation filter in `CommandHandler` for R3 left the mirror in
`TaskTransitionValidator.ValidateWorkItemInvalidated` disagreeing, and the R3 test failed with
`An already invalidated work item cannot be invalidated again` thrown from the replay validator. Both
copies now carry the same rule and an R3 comment.

## Commands run

- `dotnet build AILedger.sln --no-restore` — 0 warnings, 0 errors (`TreatWarningsAsErrors` is on).
- `dotnet test AILedger.sln --no-restore` — 137 passed, 0 failed. Baseline was 122.

## Backlog

Noticed during execution, outside this contract's success criteria, deliberately not fixed:

1. `TaskTransitionValidator` (642 lines) duplicates every rule in `CommandHandler` against a second
   API, including byte-identical helpers. Every command in this task had to be written twice. It is
   the largest avoidable defect surface in the kernel and it grew by ~190 lines here.
2. `WorkItemStatus.Ready` is declared and never written by any path.
3. `AgentRunStatus.Pending` is declared and never produced; `StartRun` writes `Active` directly.
4. A `Superseded` claim resolution still requires no evidence yet still triggers dependent
   invalidation.
5. A `Supported` challenge disposition still has no causal consequence.
6. `constraint.add --scope` accepts arbitrary strings, unlike `work add --scope`, which requires
   existing absolute paths. Deliberate for now — a constraint's scope may be conceptual — but the two
   `--scope` options now mean different things.

## Repairs after review round 1

Verifier-1 returned PASS with no repairs. Code-reviewer-1 returned two material findings; both were
repaired because both violate a stated constraint or the operator's amendment.

1. **`work block` was a one-way door** (code-reviewer-1 MAJOR 1, and the operator amended the contract
   to require the fix). Added `work.unblock` end to end: `UnblockWorkItemCommand`, `WorkItemUnblocked`,
   reducer arm, replay validator arm, `ManageWork` gate, CLI case, options, help text. It returns a
   `Blocked` item to `Paused` and clears `BlockReason`. It is refused when any dependency claim is
   `Rejected` or `Superseded`, so unblocking cannot undo causal invalidation — the repair path for
   invalidated work stays a replacement work item on a current claim.
   Tests: `WorkLifecycleTests.AManuallyBlockedItemUnblocksAndCanThenRunAndComplete` proves the exit is
   real by running and completing afterwards; `WorkLifecycleTests.UnblockingCannotUndoCausalInvalidation`
   proves the refusal; `NewCommandReplayTests` now blocks and unblocks W3 through the file store.

2. **Code-reviewer context isolation was weakened** (code-reviewer-1 MAJOR 2). Adding `Escalation` and
   `Alternative` to `AlwaysIncludedKinds` leaked open escalations — including the leads'
   recommendation — into a code reviewer's manifest, against the constraint forbidding any weakening of
   reviewer isolation. `ContextArtifactKind.Escalation` is now in `ReviewerExclusions`; `Alternative`
   deliberately stays, because a discarded approach is design history like a `Decision`, which
   reviewers already receive. Test: `ContextIsolationTests.CodeReviewerNeverSeesAnOpenEscalationButStillSeesRejectedAlternatives`.
   `docs/architecture.md` corrected — it had claimed both kinds were unconditionally eligible.

Also fixed from the minor findings: empty and duplicate escalation options are now rejected for both
kinds rather than only for `BusinessDecision`, in both rule copies, so a `TrueUnknown` can no longer
render as `Options:  | `.

Not fixed, recorded as backlog: a fresh task now emits zero constraint artifacts until the operator
runs `constraint add`. Removing the hardcoded sentence was criterion-mandated.

`dotnet build` 0 warnings; `dotnet test` 140 passed, 0 failed.
