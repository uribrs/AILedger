using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class WorkLifecycleTests
{
    [Fact]
    public void CompletingWorkIsRefusedWhileARunIsStillActive()
    {
        var task = Prepare(out var workItemId);
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "codex", null));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));

        Assert.Contains("run is still active", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASuccessfulRunPausesItsWorkItemAndOnlyAnExplicitCommandCompletesIt()
    {
        var task = Prepare(out var workItemId);
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "codex", null));

        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, "session-1"));

        // A provider exiting zero is a finished process, not finished work.
        Assert.Equal(WorkItemStatus.Paused, task.State.WorkItems[workItemId].Status);

        task.RecordVerifierPass(workItemId);
        task.Apply(new CompleteWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId));
        Assert.Equal(WorkItemStatus.Completed, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void CompletingWorkIsRefusedWhileAnEscalationOnItIsOpen()
    {
        var task = Prepare(out var workItemId);
        EscalationTests.Raise(task, task.OperatorId, "X1", EscalationKind.BusinessDecision,
            workItemId: workItemId, options: ["a", "b"], recommendation: "a");

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));
        Assert.Contains("escalation on it is open", error.Message, StringComparison.Ordinal);

        task.Apply(new ResolveEscalationCommand(
            task.OperatorId, null, task.NextCorrelation(), new EscalationId("X1"), EscalationStatus.Resolved, "a"));
        task.RecordRequiredRuns(workItemId);
        task.Apply(new CompleteWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId));

        Assert.Equal(WorkItemStatus.Completed, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void BlockingRecordsAReasonAndMayCiteAnOpenEscalation()
    {
        var task = Prepare(out var workItemId);
        EscalationTests.Raise(task, task.OperatorId, "X1", EscalationKind.BusinessDecision,
            options: ["a", "b"], recommendation: "a");

        task.Apply(new BlockWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId, "Waiting on the operator", new EscalationId("X1")));

        var workItem = task.State.WorkItems[workItemId];
        Assert.Equal(WorkItemStatus.Blocked, workItem.Status);
        Assert.Equal("Waiting on the operator", workItem.BlockReason);
        Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));
    }

    // R3 (workitem-blocked-conflation): an operator block and a claim-caused block are the
    // same status member, so invalidation must not treat a manual block as already handled.
    [Fact]
    public void R3_ManuallyBlockedItemStillReactsWhenItsClaimIsRejected()
    {
        var task = new TestTask();
        var claimId = new ClaimId("C1");
        var workItemId = new WorkItemId("W1");
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), claimId, "The API is stable", "Work is wasted"));
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId, "Build it",
            task.OperatorId, [claimId], [Path.GetFullPath("src")]));
        task.Apply(new BlockWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId, "Operator paused this", null));
        Assert.Equal(WorkItemStatus.Blocked, task.State.WorkItems[workItemId].Status);

        task.Apply(new AddEvidenceCommand(task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E1"),
            "probe", "vendor changelog", "The API changed", [], [claimId]));
        var outcome = task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), claimId, ClaimStatus.Rejected, [new EvidenceId("E1")]));

        Assert.Contains(outcome.Events.Select(item => item.Data), item => item is WorkItemInvalidated);
        Assert.Equal(WorkItemStatus.Stale, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void AManuallyBlockedItemUnblocksAndCanThenRunAndComplete()
    {
        var task = Prepare(out var workItemId);
        EscalationTests.Raise(task, task.OperatorId, "X1", EscalationKind.BusinessDecision,
            workItemId: workItemId, options: ["a", "b"], recommendation: "a");
        task.Apply(new BlockWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId, "Waiting on the operator", new EscalationId("X1")));
        task.Apply(new ResolveEscalationCommand(
            task.OperatorId, null, task.NextCorrelation(), new EscalationId("X1"), EscalationStatus.Resolved, "a"));

        task.Apply(new UnblockWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId));

        var workItem = task.State.WorkItems[workItemId];
        Assert.Equal(WorkItemStatus.Paused, workItem.Status);
        Assert.Null(workItem.BlockReason);

        // The exit is real: the item takes a run, is verified, and then completes.
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "codex", null));
        task.Apply(new CompleteRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, "s1"));
        task.RecordVerifierPass(workItemId);
        task.Apply(new CompleteWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId));
        Assert.Equal(WorkItemStatus.Completed, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void UnblockingCannotUndoCausalInvalidation()
    {
        var task = new TestTask();
        var claimId = new ClaimId("C1");
        var workItemId = new WorkItemId("W1");
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), claimId, "The API is stable", null));
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId, "Build it",
            task.OperatorId, [claimId], [Path.GetFullPath("src")]));
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "codex", null));
        task.Apply(new AddEvidenceCommand(task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E1"),
            "probe", "vendor changelog", "The API changed", [], [claimId]));
        task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), claimId, ClaimStatus.Rejected, [new EvidenceId("E1")]));
        Assert.Equal(WorkItemStatus.Blocked, task.State.WorkItems[workItemId].Status);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new UnblockWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));

        Assert.Contains("replacement work item", error.Message, StringComparison.Ordinal);
        Assert.Equal(WorkItemStatus.Blocked, task.State.WorkItems[workItemId].Status);
    }

    // The first live governed run closed its own run with no session id, erasing the identity that
    // exact-session resume depends on. A completed run must stay resumable; a failed one need not.
    [Fact]
    public void ARunCannotBeRecordedAsCompletedWithoutASessionIdentity()
    {
        var task = Prepare(out var workItemId);
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "claude", null));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, null)));
        Assert.Contains("must stay resumable", error.Message, StringComparison.Ordinal);
        Assert.Equal(AgentRunStatus.Active, task.State.Runs[new RunId("R1")].Status);

        // A run that died before its session existed has no identity to record.
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Failed, null));
        Assert.Equal(AgentRunStatus.Failed, task.State.Runs[new RunId("R1")].Status);
    }

    // Three live agents closed their own runs despite a briefing forbidding it. A launched agent
    // shares the run's actor identity, so only a secret the launcher holds can tell them apart.
    [Fact]
    public void AnAgentInsideALauncherManagedRunCannotCompleteIt()
    {
        var task = Prepare(out var workItemId);
        var token = "launcher-secret";
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("R1"),
            workItemId, "claude", null, null, "2.1.261", CommandHandler.HashLaunchToken(token)));

        // The agent knows its own actor and session, and neither is enough.
        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, "s1")));
        Assert.Contains("closed by that launcher", error.Message, StringComparison.Ordinal);
        Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, "s1", "guessed")));
        Assert.Equal(AgentRunStatus.Active, task.State.Runs[new RunId("R1")].Status);

        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, "s1", token));
        Assert.Equal(AgentRunStatus.Completed, task.State.Runs[new RunId("R1")].Status);
    }

    // The launching process can die and take its secret with it. An operator may then close the
    // orphan, but only as a failure: a run nobody watched finish is not a success.
    [Fact]
    public void AnOrphanedLauncherManagedRunMayBeClosedByAnOperatorOnlyAsAFailure()
    {
        var task = Prepare(out var workItemId);
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("R1"),
            workItemId, "claude", null, null, "2.1.261", CommandHandler.HashLaunchToken("lost")));

        Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, "s1")));

        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Cancelled, null));
        Assert.Equal(AgentRunStatus.Cancelled, task.State.Runs[new RunId("R1")].Status);
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
