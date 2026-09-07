using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// "Completed" used to mean only that someone said so. Nothing in the ledger recorded whether a
// verifier had ever looked at the work, and nothing stopped a code reviewer from reviewing work
// that had not been verified yet. The sequence is now imposed by the kernel: a verifier run has to
// finish against a work item before that item can be completed or reviewed, and a run records the
// role its subject held so the ledger can tell a verifier's pass from anybody else's.
public sealed class VerificationSequenceTests
{
    [Fact]
    public void AWorkItemCannotBeCompletedUntilAVerifierRunHasPassedOverIt()
    {
        var task = Prepare(out var workItemId);
        // A run by the actor that did the work is not a verification, however cleanly it ended.
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "codex", null));
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, "session-1"));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));

        // The refusal has to name what is missing, because the operator's next move depends on it.
        Assert.Contains("verifier run", error.Message, StringComparison.Ordinal);
        Assert.Equal(WorkItemStatus.Paused, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void ACompletedVerifierRunIsWhatUnlocksCompletion()
    {
        var task = Prepare(out var workItemId);
        task.RecordRequiredRuns(workItemId);

        task.Apply(new CompleteWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId));

        Assert.Equal(WorkItemStatus.Completed, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void AVerifierFromTheWorkingProviderDoesNotUnlockCompletion()
    {
        var task = Prepare(out var workItemId);
        task.RecordWorkingPass(workItemId);
        var verifier = new ActorId("verifier");
        task.Assign(verifier, RoleKind.Verifier, Capability.BuildContext);
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("RV-same"),
            workItemId, "codex", null, null, null, null, verifier));
        task.Apply(new CompleteRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("RV-same"),
            AgentRunStatus.Completed, "session-same"));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));

        Assert.Contains("same provider", error.Message, StringComparison.Ordinal);
        task.RecordVerifierPass(workItemId, "RV-different");
        task.Apply(new CompleteWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId));
        Assert.Equal(WorkItemStatus.Completed, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void AVerifierRunThatDidNotFinishIsNotAVerification()
    {
        var task = Prepare(out var workItemId);
        // The work itself was done; only the verification is in question.
        task.RecordWorkingPass(workItemId);
        var verifier = new ActorId("verifier");
        task.Assign(verifier, RoleKind.Verifier, Capability.BuildContext);
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("RV"),
            workItemId, "codex", null, null, null, null, verifier));
        // The verifier died before it answered. Nobody read a verdict, so nothing was verified.
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("RV"), AgentRunStatus.Failed, null));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));

        Assert.Contains("verifier run", error.Message, StringComparison.Ordinal);
        Assert.NotEqual(WorkItemStatus.Completed, task.State.WorkItems[workItemId].Status);
    }

    // F3a. A verifier's pass says the work was checked. It does not say anyone did the work, and
    // the kernel used to have no way to tell the difference: a work item completed with no run at
    // all means it never saw who worked on it, or whether anyone did.
    [Fact]
    public void AWorkItemCannotBeCompletedUntilARunUnderAWorkingRoleHasFinishedIt()
    {
        var task = Prepare(out var workItemId);
        // A verifier run is present and completed, and it is still not enough.
        task.RecordVerifierPass(workItemId);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));

        Assert.Contains("no completed run", error.Message, StringComparison.Ordinal);
        Assert.NotEqual(WorkItemStatus.Completed, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void ACodeReviewersRunIsNotSomebodyHavingDoneTheWork()
    {
        var task = Prepare(out var workItemId);
        var reviewer = new ActorId("reviewer");
        task.Assign(reviewer, RoleKind.CodeReviewer, Capability.BuildContext);
        task.RecordVerifierPass(workItemId);
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("R2"),
            workItemId, "claude", null, null, null, null, reviewer));
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R2"), AgentRunStatus.Completed, "session-r2"));

        // Both roles that only inspect have now run against it. Neither of them built anything.
        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));

        Assert.Contains("no completed run", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWorkingRunThatDidNotFinishIsNotWorkDone()
    {
        var task = Prepare(out var workItemId);
        task.RecordVerifierPass(workItemId);
        var worker = new ActorId("worker");
        task.Assign(worker, RoleKind.Worker, Capability.BuildContext);
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("RW"),
            workItemId, "codex", null, null, null, null, worker));
        // The agent died partway. Whatever it left behind, nobody finished it.
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("RW"), AgentRunStatus.Failed, null));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));

        Assert.Contains("no completed run", error.Message, StringComparison.Ordinal);
    }

    // A verifier pass is a statement about the code as it stood when the verifier read it. Counting
    // one that finished before the work did would let any stale pass license every later change,
    // which is the same "someone once looked at this" the gate exists to replace. The verifier run
    // has to have ended after the most recent completed working run.
    [Fact]
    public void AVerifierRunThatEndedBeforeTheWorkWasDoneDoesNotSatisfyCompletion()
    {
        var task = Prepare(out var workItemId);
        task.RecordVerifierPass(workItemId, "RV-early");
        task.RecordWorkingPass(workItemId);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));
        Assert.Contains("verifier run", error.Message, StringComparison.Ordinal);

        // A verifier that reads the work as it now stands is what the gate was always asking for.
        task.RecordVerifierPass(workItemId, "RV-after");
        task.Apply(new CompleteWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId));

        Assert.Equal(WorkItemStatus.Completed, task.State.WorkItems[workItemId].Status);
    }

    // Which working run the verifier is measured against: the latest, not the first. Measuring
    // against the earliest would let an agent get verified, keep working, and complete — the same
    // hole in a different shape.
    [Fact]
    public void AVerifierPassIsMeasuredAgainstTheLatestWorkNotTheFirst()
    {
        var task = Prepare(out var workItemId);
        task.RecordWorkingPass(workItemId, "RW-first");
        task.RecordVerifierPass(workItemId);
        // More work landed after the verifier signed off, so the pass no longer covers the item.
        task.RecordWorkingPass(workItemId, "RW-second");

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));

        Assert.Contains("verifier run", error.Message, StringComparison.Ordinal);
        Assert.NotEqual(WorkItemStatus.Completed, task.State.WorkItems[workItemId].Status);
    }

    // The reviewer gate is the same claim about the same staleness: reviewing work whose verifier
    // pass predates it spends the review on code nobody has checked.
    [Fact]
    public void ACodeReviewerCannotStartWhenTheVerifierPassPredatesTheLatestWork()
    {
        var task = Prepare(out var workItemId);
        var reviewer = new ActorId("reviewer");
        task.Assign(reviewer, RoleKind.CodeReviewer, Capability.BuildContext);
        task.RecordVerifierPass(workItemId, "RV-early");
        task.RecordWorkingPass(workItemId);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "claude",
            null, null, null, null, reviewer)));
        Assert.Contains("after a verifier run", error.Message, StringComparison.Ordinal);

        task.RecordVerifierPass(workItemId, "RV-after");
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "claude",
            null, null, null, null, reviewer));

        Assert.Equal(AgentRunStatus.Active, task.State.Runs[new RunId("R1")].Status);
    }

    // The two gates have to be told apart from the message alone, because the operator's next move
    // is different for each: dispatch a worker, or dispatch a verifier. One phrase each.
    [Fact]
    public void TheMissingWorkAndMissingVerifierRefusalsNameDifferentGates()
    {
        var task = Prepare(out var workItemId);

        var noRunsAtAll = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));

        task.RecordWorkingPass(workItemId);
        var noVerifier = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));

        Assert.Contains("no completed run", noRunsAtAll.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("verifier run", noRunsAtAll.Message, StringComparison.Ordinal);
        Assert.Contains("verifier run", noVerifier.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("no completed run", noVerifier.Message, StringComparison.Ordinal);
    }

    // One waiver, both requirements. This item has no run of any kind against it, and the operator
    // still completes it — which is exactly why the reason has to be on the record.
    [Fact]
    public void AnOperatorMayWaiveTheRequiredRunsAndTheReasonIsRecordedOnTheEvent()
    {
        var task = Prepare(out var workItemId);

        var outcome = task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId, "  No verifier is attached to this task  "));

        // The waiver is the audit trail. A completion that skipped verification must say so in the
        // ledger, in the operator's own words, or the skip is invisible a week later.
        var completed = outcome.Events.Select(item => item.Data).OfType<WorkItemCompleted>().Single();
        Assert.Equal("No verifier is attached to this task", completed.WithoutVerificationReason);
        Assert.Equal(WorkItemStatus.Completed, task.State.WorkItems[workItemId].Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankWaiverIsNotAWaiver(string reason)
    {
        var task = Prepare(out var workItemId);
        // The verifier pass is staged, so this completion would otherwise succeed. That makes the
        // blank waiver the only variable: without the staging, the missing-run refusals fire
        // instead and the test passes whether or not a blank waiver is rejected at all.
        task.RecordRequiredRuns(workItemId);

        // An empty string is the shape a caller reaches for when it wants the gate gone without
        // owning the decision. It buys nothing, and it must not read as "no waiver was offered".
        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId, reason)));

        Assert.Contains("blank waiver", error.Message, StringComparison.Ordinal);
        Assert.NotEqual(WorkItemStatus.Completed, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void OnlyAnOperatorCanWaiveTheVerifierPass()
    {
        var task = Prepare(out var workItemId);
        var lead = new ActorId("lead");
        // The lead holds ManageWork, so it may complete work items. It is refused here for who it
        // is, not for what it is allowed to do: skipping verification is the operator's call.
        task.Assign(lead, RoleKind.ImplementationLead, Capability.ManageWork);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            lead, null, task.NextCorrelation(), workItemId, "I could not find a verifier")));

        Assert.Contains("Only an operator can complete work without the required runs", error.Message,
            StringComparison.Ordinal);
        Assert.NotEqual(WorkItemStatus.Completed, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void ACodeReviewerCannotStartOnAWorkItemUntilAVerifierHasFinishedWithIt()
    {
        var task = Prepare(out var workItemId);
        var reviewer = new ActorId("reviewer");
        task.Assign(reviewer, RoleKind.CodeReviewer, Capability.BuildContext);

        // Reviewing unverified work spends the scarcest pass in the pipeline on a question the
        // verifier was going to answer anyway.
        var error = Assert.Throws<GovernanceException>(() => task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "claude",
            null, null, null, null, reviewer)));
        Assert.Contains("after a verifier run", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(new RunId("R1"), task.State.Runs.Keys);

        task.RecordVerifierPass(workItemId);
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "claude",
            null, null, null, null, reviewer));

        Assert.Equal(AgentRunStatus.Active, task.State.Runs[new RunId("R1")].Status);
        Assert.Equal(RoleKind.CodeReviewer, task.State.Runs[new RunId("R1")].SubjectRole);
    }

    // The ordering rule is about reviewing one work item. A reviewer run that names no work item
    // has no verifier pass to wait for, so the rule must not reach it.
    [Fact]
    public void ACodeReviewerRunThatNamesNoWorkItemIsNotHeldBack()
    {
        var task = Prepare(out _);
        var reviewer = new ActorId("reviewer");
        task.Assign(reviewer, RoleKind.CodeReviewer, Capability.BuildContext);

        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), null, "claude",
            null, null, null, null, reviewer));

        Assert.Equal(RoleKind.CodeReviewer, task.State.Runs[new RunId("R1")].SubjectRole);
    }

    [Fact]
    public void ARunRecordsTheRoleItsSubjectHeldWhenItStartedAndAReassignmentDoesNotRewriteIt()
    {
        var task = Prepare(out var workItemId);
        var subject = new ActorId("subject");
        task.Assign(subject, RoleKind.Verifier, Capability.BuildContext);
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "codex",
            null, null, null, null, subject));
        Assert.Equal(RoleKind.Verifier, task.State.Runs[new RunId("R1")].SubjectRole);

        // Roles are reassigned as a task moves on. The run is a record of what happened, so what it
        // ran as is fixed at the moment it started; otherwise a later reassignment would silently
        // turn yesterday's worker run into a verifier pass and unlock a completion.
        task.Assign(subject, RoleKind.Worker, Capability.AddClaim);

        Assert.Equal(RoleKind.Worker, task.State.Roles[subject].Role);
        Assert.Equal(RoleKind.Verifier, task.State.Runs[new RunId("R1")].SubjectRole);
    }

    // Replay safety, the mistake this repository has already made twice, and the most important
    // test in this file. All seven work items completed in the live ledger were completed before
    // either gate existed: no verifier run, and no working run — no run at all. Both gates are
    // therefore command-time only. Adding either to the replay validator would reject the real log
    // and make the task unreadable, and no amount of care afterwards un-writes history to match.
    // Command-time rules may tighten; replay-time rules may not.
    [Fact]
    public void ReplayAcceptsAWorkItemCompletedWithNoRunOfAnyKind()
    {
        var state = Opened("legacy-completion", out var reducer, out var next, out var actor, out _);
        state = reducer.Apply(state, next(state, actor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Finished long ago", actor, WorkItemStatus.Proposed, [],
            [Path.GetFullPath("src")]))));

        // Exactly the shape of every completion already on disk: no run at all, and no waiver.
        state = reducer.Apply(state, next(state, actor, new WorkItemCompleted(new WorkItemId("W1"))));

        Assert.Equal(WorkItemStatus.Completed, state.WorkItems[new WorkItemId("W1")].Status);
        Assert.Null(state.WorkItems[new WorkItemId("W1")].AbandonReason);
    }

    // The same principle on the other new field: a run recorded before runs carried the role they
    // ran under has none, and replay may never require one to be present.
    [Fact]
    public void ReplayAcceptsARunRecordedBeforeRunsCarriedASubjectRole()
    {
        var state = Opened("legacy-run-role", out var reducer, out var next, out var actor, out var recordedAt);

        state = reducer.Apply(state, next(state, actor, new RunStarted(new AgentRun(
            new RunId("R1"), actor, null, "codex", null, AgentRunStatus.Active, recordedAt, null))));
        state = reducer.Apply(state, next(state, actor, new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, "session-1", recordedAt)));

        Assert.Equal(AgentRunStatus.Completed, state.Runs[new RunId("R1")].Status);
        Assert.Null(state.Runs[new RunId("R1")].SubjectRole);
    }

    // The code-reviewer ordering rule is deliberately written once, at command time only. The test
    // below asserts the omission itself: it passes today and must keep passing. If a later refactor
    // "completes the pair" by adding that rule to the replay validator, this fails — which is the
    // whole point, because logs already on disk contain exactly this shape.
    [Fact]
    public void ReplayAcceptsACodeReviewerRunStartedWithNoPrecedingVerifierRun()
    {
        var state = Opened("legacy-review-order", out var reducer, out var next, out var actor, out var recordedAt);
        var reviewer = new ActorId("reviewer");
        state = reducer.Apply(state, next(state, actor, new RoleAssigned(new RoleAssignment(
            reviewer, RoleKind.CodeReviewer, [Capability.BuildContext],
            new Provenance(actor, recordedAt, "actor.assign-role")))));
        state = reducer.Apply(state, next(state, actor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Reviewed work", actor, WorkItemStatus.Proposed, [],
            [Path.GetFullPath("src")]))));

        // No verifier run precedes this, and replay must not care. The ordering is a coordination
        // rule about what to spend a review pass on, not a structural property of the event.
        state = reducer.Apply(state, next(state, actor, new RunStarted(new AgentRun(
            new RunId("R1"), reviewer, new WorkItemId("W1"), "claude", null, AgentRunStatus.Active,
            recordedAt, null, null, null, null, actor, RoleKind.CodeReviewer))));

        Assert.Equal(RoleKind.CodeReviewer, state.Runs[new RunId("R1")].SubjectRole);
        Assert.Equal(AgentRunStatus.Active, state.Runs[new RunId("R1")].Status);
    }

    // The waiver rule is the opposite case, and it is safe to write twice. The waiver field is new,
    // so no event already on disk carries one: a rule that fires only when it is present cannot
    // reject any history that was ever legal. Both copies therefore exist, and a forged log in
    // which a lead waived verification is refused at replay as well as at command time.
    [Fact]
    public void ReplayRefusesACompletionWaivedByANonOperator()
    {
        var state = Opened("waiver-authority-replay", out var reducer, out var next, out var actor, out var recordedAt);
        var lead = new ActorId("lead");
        state = reducer.Apply(state, next(state, actor, new RoleAssigned(new RoleAssignment(
            lead, RoleKind.ImplementationLead, [Capability.ManageWork],
            new Provenance(actor, recordedAt, "actor.assign-role")))));
        state = reducer.Apply(state, next(state, actor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Waived work", actor, WorkItemStatus.Proposed, [],
            [Path.GetFullPath("src")]))));

        var forged = next(state, lead, new WorkItemCompleted(
            new WorkItemId("W1"), "No verifier was attached to this task"));

        var error = Assert.Throws<GovernanceException>(() => reducer.Apply(state, forged));
        Assert.Contains("Only an operator", error.Message, StringComparison.Ordinal);
    }

    // The control that keeps the rule above honest: the same completion by the same lead, with no
    // waiver on it, is the shape older logs do carry, and replay still has to accept it.
    [Fact]
    public void ReplayAcceptsAnUnwaivedCompletionByANonOperator()
    {
        var state = Opened("legacy-lead-completion", out var reducer, out var next, out var actor, out var recordedAt);
        var lead = new ActorId("lead");
        state = reducer.Apply(state, next(state, actor, new RoleAssigned(new RoleAssignment(
            lead, RoleKind.ImplementationLead, [Capability.ManageWork],
            new Provenance(actor, recordedAt, "actor.assign-role")))));
        state = reducer.Apply(state, next(state, actor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Finished long ago", actor, WorkItemStatus.Proposed, [],
            [Path.GetFullPath("src")]))));

        state = reducer.Apply(state, next(state, lead, new WorkItemCompleted(new WorkItemId("W1"))));

        Assert.Equal(WorkItemStatus.Completed, state.WorkItems[new WorkItemId("W1")].Status);
    }

    // An opened task with one operator, ready for hand-built events. The reducer re-validates every
    // event, so a history assembled here is held to the replay rules and nothing else.
    private static GovernedTaskState Opened(
        string taskId,
        out TaskReducer reducer,
        out Func<GovernedTaskState, ActorId, LedgerEventData, LedgerEvent> next,
        out ActorId actor,
        out DateTimeOffset recordedAt)
    {
        var id = new TaskId(taskId);
        var localActor = new ActorId("operator");
        var localRecordedAt = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var localReducer = new TaskReducer();
        actor = localActor;
        recordedAt = localRecordedAt;
        reducer = localReducer;
        next = (current, eventActor, data) => new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{id.Value}:{(current?.Version ?? 0) + 1:D10}"),
            id, eventActor, localRecordedAt, null, "replay", data);

        var state = localReducer.Apply(null, next(null!, localActor, new TaskOpened("Legacy", "Goal")));
        return localReducer.Apply(state, next(state, localActor, new RoleAssigned(new RoleAssignment(
            localActor, RoleKind.Operator, Enum.GetValues<Capability>(),
            new Provenance(localActor, localRecordedAt, "task.open")))));
    }

    private static TestTask Prepare(out WorkItemId workItemId)
    {
        var task = new TestTask();
        workItemId = new WorkItemId("W1");
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId, "Build it",
            task.OperatorId, [], [Path.GetFullPath("src")]));
        return task;
    }
}
