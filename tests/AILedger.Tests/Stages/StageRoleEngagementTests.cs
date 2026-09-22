using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Stages;

public sealed class StageRoleEngagementTests
{
    [Fact]
    public void ANoProviderRunStaffsNoRoleForAStageArm()
    {
        var f = new AILedger.Tests.Artifacts.Recon.InternalReconFixture();
        var task = f.Task;
        var researcher = new ActorId("researcher");
        task.Assign(researcher, RoleKind.Researcher, Capability.BuildContext);
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(),
            new RunId("RN"), null, AgentRun.NoProvider, null, SubjectActorId: researcher));
        task.Apply(new CompleteRunCommand(task.OperatorId, null, task.NextCorrelation(),
            new RunId("RN"), AgentRunStatus.Completed, null));
        f.Resolve();
        f.Eligible("external");

        var error = Assert.Throws<GovernanceException>(() => task.Apply(
            new RequestStageTransitionCommand(
                task.OperatorId, null, task.NextCorrelation(), TaskStage.Design)));

        Assert.Contains("Design requires a completed Researcher run", error.Message, StringComparison.Ordinal);
    }
}
