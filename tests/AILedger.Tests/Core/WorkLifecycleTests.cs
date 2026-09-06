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

        // The exit is real: the item takes a run and then completes.
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "codex", null));
        task.Apply(new CompleteRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, "s1"));
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

    private static TestTask Prepare(out WorkItemId workItemId)
    {
        var task = new TestTask();
        workItemId = new WorkItemId("W1");
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId, "Build it",
            task.OperatorId, [], [Path.GetFullPath("src")]));
        return task;
    }
}
