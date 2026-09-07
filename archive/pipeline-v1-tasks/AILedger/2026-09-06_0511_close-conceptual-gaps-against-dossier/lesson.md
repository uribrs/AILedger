# Lessons — 2026-09-06_0511_close-conceptual-gaps-against-dossier (2026-09-06)

## L-ebe1d09c — A8 — a new aggregate is not contained to handler, reducer and state
belief:  Adding a new state aggregate to an event-sourced kernel is a storage change with no lifecycle consequences for replay, projections or context assembly.
counter: Each of the three new aggregates forced edits in all three places the belief denies. TaskTransitionValidator re-validates every event at replay and its default arm throws, so an unregistered event type makes a task permanently unreadable; MarkdownTaskProjectionWriter needed new sections; ContextAssembler needed a ToArtifact overload and an AlwaysIncludedKinds entry or the aggregate silently vanishes from narrowed manifests. Adding one command, work.unblock, touched six files.
source:  src/AILedger.Core/Domain/TaskTransitionValidator.cs default arm; ai/done/2026-09-06_0511_close-conceptual-gaps-against-dossier/research/internal-recon.md Landmines 1 and 2 (verifier, 2026-09-06)
verify:  grep -n 'Unsupported event data' ~/Dev/Uri/localprojects/AILedger/src/AILedger.Core/Domain/TaskTransitionValidator.cs
do not:  do not scope a new aggregate to handler, reducer and state without first grepping for every exhaustive switch over the event hierarchy

## L-cd74b5ae — A9 — a terminal work item is not inert
belief:  A work item in a terminal status is inert, so nothing later mutates it.
counter: Causal invalidation transitions a Completed work item to Stale when one of its claims is rejected. The skip list at CommandHandler.cs never contained Completed, so this predated the task and was found only by reading the filter rather than by trusting the status name.
source:  87ca8b1:src/AILedger.Core/Application/CommandHandler.cs:561 skip list reads 'not (Blocked or Stale)' (verifier, 2026-09-06)
verify:  grep -n 'WorkItemStatus.Stale' ~/Dev/Uri/localprojects/AILedger/src/AILedger.Core/Application/CommandHandler.cs
do not:  do not infer from a status named terminal that no later path writes it; read the filters that exclude it

## L-55212e51 — A10 — one predicate cannot serve two work-item outcomes
belief:  One command and one predicate can serve two different work-item outcomes by branching on a status column.
counter: TaskReducer.CompleteRun branched on AgentRunStatus to pick Completed or Paused for the work item, which conflated a provider process exiting zero with a claim that the governed work was done. Removing the branch and adding an explicit work.complete command was the fix; the branch was the defect, not the mechanism.
source:  src/AILedger.Core/Domain/TaskReducer.cs CompleteRun; tests/AILedger.Tests/Core/WorkLifecycleTests.cs ASuccessfulRunPausesItsWorkItemAndOnlyAnExplicitCommandCompletesIt (verifier, 2026-09-06)
verify:  grep -n 'WorkItemStatus.Paused' ~/Dev/Uri/localprojects/AILedger/src/AILedger.Core/Domain/TaskReducer.cs
do not:  do not let a subsystem's terminal signal stand in for a domain assertion; give the domain event its own command
