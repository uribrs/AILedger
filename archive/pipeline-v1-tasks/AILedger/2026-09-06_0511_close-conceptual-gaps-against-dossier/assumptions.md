# Assumptions

- A1 VALIDATED — actor: executor — The four gaps were implemented without touching `AILedger.Providers`. Evidence: `git diff --stat -- src/AILedger.Providers` is empty at implementation completion.
- A2 VALIDATED — actor: executor — Exactly one test broke: `CliApplicationTests.CancellationAfterSuccessfulProviderReturnStillPersistsTerminalRun` (`Failed: 1, Passed: 121` on the first run after the reducer change). No production path required the transition.
- A3 VALIDATED — actor: executor — Evidence: `tests/AILedger.Tests/Storage/NewCommandReplayTests.cs::AnEventLogWithoutTheNewEventTypesStillReplays` writes a log containing no new event types, deletes `state.json`, and replays into empty new collections.
- A4 VALIDATED — actor: executor — Evidence: `tests/AILedger.Tests/Core/WorkLifecycleTests.cs::CompletingWorkIsRefusedWhileAnEscalationOnItIsOpen`; completion is refused until the escalation is resolved.
- A5 VALIDATED — actor: executor — Evidence: `tests/AILedger.Tests/Core/ConstraintContextTests.cs::ActiveConstraintsEnterContextAndSupersededOnesDoNot`. `ContextArtifactKind.Constraint` was already in `AlwaysIncludedKinds`, so no narrowing change was needed.
- A6 VALIDATED — actor: executor — Evidence: `tests/AILedger.Tests/Core/AlternativeTests.cs::AnAlternativeRejectedBeforeAnyDecisionExistsStillReachesLaterContext`; an alternative recorded before any decision or work item exists still reaches a work-item-scoped manifest.

## Prior Art

Matched 4 rows for tags: ailedger, governance, event-sourcing, lifecycle, dotnet. None superseded or retracted. Followed 1 task directory.

- A7 OPEN — A green test suite using the existing fake provider adapters is evidence about work-item lifecycle semantics after a real provider run. source: lessons.md#L-71758bac (verifier, 2026-08-27)
- A8 REJECTED — actor: executor — Adding three aggregates forced changes in all three places the belief denied: `TaskTransitionValidator.Validate` (replay; an unregistered event type throws and would make a task permanently unreadable), `MarkdownTaskProjectionWriter.RenderTask`/`RenderDecisions` (projections), and `ContextAssembler.BuildStateArtifacts` plus `AlwaysIncludedKinds` (context). Evidence: `TaskTransitionValidator.cs:60-61` default arm; `research/internal-recon.md` Landmines 1 and 2. source: lessons.md#L-d13589c6 (verifier, 2026-08-27)
- A9 REJECTED — actor: executor — Causal invalidation transitions a `Completed` work item to `Stale` when one of its claims is rejected, so a terminal work item is not inert. This predates the task: `Completed` was never in the skip list at `87ca8b1:CommandHandler.cs:561`. The R3 change removed only `Blocked`. Evidence: `CommandHandler.cs` `AddDependencyInvalidations`, `TaskTransitionValidator.ValidateWorkItemInvalidated`. source: lessons.md#L-bb0d01f6 (verifier, 2026-08-27)

## Classified Prior Art

Delta lookup for classes: ailedger-kernel (new), persisted-state (existing), lifecycle-semantics (new). Matched 8 `persisted-state` rows; one is newly relevant beyond the designer's recall.

- A10 REJECTED — actor: executor — `TaskReducer.CompleteRun` previously branched on run status to pick `Completed` or `Paused` for the work item. Splitting the two outcomes into separate commands (`work.complete`, and the run's own `Paused`) was required; the single branching predicate was exactly the conflation gap 3 existed to remove. Evidence: `TaskReducer.cs` `CompleteRun`; `tests/AILedger.Tests/Core/WorkLifecycleTests.cs::ASuccessfulRunPausesItsWorkItemAndOnlyAnExplicitCommandCompletesIt`. source: lessons.md#L-5838490b (verifier, 2026-08-27)

## Amendment Assumptions

- A11 OPEN — Returning an unblocked work item to `Paused` is correct for both a manual block and a repaired one; no prior status needs to be recorded and restored.
- A12 OPEN — Refusing `work unblock` while a dependency claim is `Rejected` or `Superseded` leaves a usable repair path, because a rejected claim is terminal and the intended route is a replacement work item depending on a new claim.
