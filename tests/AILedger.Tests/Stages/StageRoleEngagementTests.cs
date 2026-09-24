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
        // It consults as a research pass would, so only the missing cognition is left to refuse (A2).
        task.ConsultLessons(researcher, new RunId("RN"), LessonConsultationPurpose.Research,
            task.State.Claims.Keys.ToArray());
        task.Apply(new CompleteRunCommand(task.OperatorId, null, task.NextCorrelation(),
            new RunId("RN"), AgentRunStatus.Completed, null));
        f.Resolve();
        f.Eligible("external");

        var error = Assert.Throws<GovernanceException>(() => task.Apply(
            new RequestStageTransitionCommand(
                task.OperatorId, null, task.NextCorrelation(), TaskStage.Design)));

        Assert.Contains("by a completed Researcher run in the current research episode", error.Message,
            StringComparison.Ordinal);
    }
}
