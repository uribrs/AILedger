using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class InvalidationTests
{
    [Fact]
    public void RejectingClaimInvalidatesDecisionAndBlocksActiveDependentWork()
    {
        var task = new TestTask();
        var claimId = new ClaimId("C1");
        var evidenceId = new EvidenceId("E1");
        var decisionId = new DecisionId("D1");
        var workItemId = new WorkItemId("W1");

        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), claimId, "Provider supports JSONL", "Run cannot be normalized"));
        task.Apply(new ProposeDecisionCommand(task.OperatorId, null, task.NextCorrelation(), decisionId, "Use JSONL", "It is deterministic", [claimId], null));
        task.Apply(new ResolveDecisionCommand(task.OperatorId, null, task.NextCorrelation(), decisionId, DecisionStatus.Accepted));
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId, "Adapter", task.OperatorId, [claimId], [Path.GetFullPath("src/providers")]));
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "codex", null));
        task.Apply(new AddEvidenceCommand(task.OperatorId, null, task.NextCorrelation(), evidenceId, "probe", "local help", "Flag absent", [], [claimId]));

        var outcome = task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), claimId, ClaimStatus.Rejected, [evidenceId]));

        Assert.Collection(
            outcome.Events.Select(item => item.Data),
            item => Assert.IsType<ClaimResolved>(item),
            item => Assert.IsType<DecisionInvalidated>(item),
            item => Assert.IsType<WorkItemInvalidated>(item));
        Assert.Equal(DecisionStatus.Invalidated, task.State.Decisions[decisionId].Status);
        Assert.Equal(WorkItemStatus.Blocked, task.State.WorkItems[workItemId].Status);

        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, "session"));

        Assert.Equal(AgentRunStatus.Completed, task.State.Runs[new RunId("R1")].Status);
        Assert.Equal(WorkItemStatus.Blocked, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void SupersedingOpenClaimMarksInactiveDependentWorkStale()
    {
        var task = new TestTask();
        var claimId = new ClaimId("C1");
        var workItemId = new WorkItemId("W1");
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), claimId, "Old assumption", null));
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId, "Work", null, [claimId], [Path.GetFullPath("src")]));

        task.Apply(new ResolveClaimCommand(task.OperatorId, null, task.NextCorrelation(), claimId, ClaimStatus.Superseded, []));

        Assert.Equal(WorkItemStatus.Stale, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void RejectedClaimCannotAcquireNewDecisionOrWorkDependents()
    {
        var task = new TestTask();
        var claimId = new ClaimId("C1");
        var evidenceId = new EvidenceId("E1");
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), claimId, "Rejected", null));
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), evidenceId, "test", "citation", "refutes", [], [claimId]));
        task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), claimId, ClaimStatus.Rejected, [evidenceId]));

        Assert.Throws<GovernanceException>(() => task.Apply(new ProposeDecisionCommand(
            task.OperatorId, null, task.NextCorrelation(), new DecisionId("D1"), "Decision", "Reason", [claimId], null)));
        Assert.Throws<GovernanceException>(() => task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", task.OperatorId, [claimId], [Path.GetFullPath("src")])));
    }
}
