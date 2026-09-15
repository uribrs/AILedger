using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Stages;

public sealed class StageRoleEngagementTests
{
    [Fact]
    public void ANoProviderRunStaffsNoRoleForAStageArm()
    {
        var task = new TestTask();
        var workItemId = new WorkItemId("W1");
        task.Apply(new AddWorkItemCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            workItemId,
            "Build it",
            task.OperatorId,
            [],
            [Path.GetFullPath("src")]));
        var researcher = new ActorId("researcher");
        task.Assign(researcher, RoleKind.Researcher, Capability.AddClaim, Capability.BuildContext);
        task.Apply(new StartRunCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            new RunId("RN"),
            workItemId,
            AgentRun.NoProvider,
            ProviderSessionId: null,
            SubjectActorId: researcher));
        task.Apply(new CompleteRunCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            new RunId("RN"),
            AgentRunStatus.Completed,
            null));

        task.Apply(new AddClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), new ClaimId("C9"), "Something to research", null));
        task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), TaskStage.Research));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(
            new RequestStageTransitionCommand(
                task.OperatorId, null, task.NextCorrelation(), TaskStage.Design)));

        Assert.Contains("Design requires a completed Researcher run", error.Message, StringComparison.Ordinal);
    }
}
