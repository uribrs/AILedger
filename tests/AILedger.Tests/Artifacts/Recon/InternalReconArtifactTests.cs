using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Artifacts.Recon;

public sealed class InternalReconArtifactTests
{
    [Theory]
    [InlineData(AgentRunStatus.Active, "codex", false)]
    [InlineData(AgentRunStatus.Failed, "codex", false)]
    [InlineData(AgentRunStatus.Cancelled, "codex", false)]
    [InlineData(AgentRunStatus.Completed, "none", false)]
    [InlineData(AgentRunStatus.Completed, "codex", true)]
    public void R3_OnlyCurrentCompletedOwnerQualifies(AgentRunStatus status, string provider, bool accepted)
    {
        var f = new InternalReconFixture();
        f.Start(provider: provider);
        f.File();
        if (status != AgentRunStatus.Active) f.Complete(status);
        if (accepted) f.Task.Transition(TaskStage.Design);
        else Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Design));
    }

    [Fact]
    public void SupersedingActiveProducerDoesNotFallBackToCompletedRevision()
    {
        var f = new InternalReconFixture();
        f.Eligible();
        f.Start("revision-run");
        f.File(id: "revision", supersedes: "recon");
        Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Design));
        f.Complete();
        f.Task.Transition(TaskStage.Design);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("completed")]
    [InlineData("owner")]
    public void FilingRequiresActiveMatchingProducer(string invalid)
    {
        var f = new InternalReconFixture();
        f.Start();
        if (invalid == "completed") f.Complete();
        var command = ArtifactCommands.Record(f.Task,
            invalid == "owner" ? f.Task.OperatorId : f.Lead,
            "recon", GovernedArtifactKind.InternalRecon, f.Body(),
            producerRun: invalid == "missing" ? new RunId("absent") : f.Run);
        Assert.Throws<GovernanceException>(() => f.Task.Apply(command));
        Assert.Empty(f.Task.State.Artifacts);
    }
    [Fact]
    public void ResearcherCannotFileReconEvenWithRecordArtifactCapability()
    {
        var f = new InternalReconFixture();
        var researcher = new ActorId("external-researcher");
        f.Task.Assign(researcher, RoleKind.Researcher, Capability.BuildContext, Capability.RecordArtifact);
        var run = new RunId("external-run");
        f.Task.Apply(new StartRunCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(), run,
            null, "codex", null, null, null, null, researcher));
        var command = ArtifactCommands.Record(f.Task, researcher, "recon", GovernedArtifactKind.InternalRecon,
            f.Body(), producerRun: run);
        Assert.Throws<GovernanceException>(() => f.Task.Apply(command));
        Assert.Empty(f.Task.State.Artifacts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void R3_LeadCannotAcquireWorkOrAssuranceProducerProvenance(bool assurance)
    {
        var task = new TestTask(placeEntryStages: false);
        task.ReachStage(TaskStage.Ready);
        var work = task.StageWorkItem();
        var lead = task.GoverningLead();
        var binding = new AssuranceBinding(1, [work],
            [new AssuranceWorkVersion(work, new RunId("working"))], "candidate");
        var error = Assert.Throws<GovernanceException>(() => task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("ineligible"),
            work, "codex", null, SubjectActorId: lead,
            Assurance: assurance ? binding : null)));
        Assert.Contains(assurance ? "only Verifier or CodeReviewer" : "coordinating role", error.Message);
        Assert.DoesNotContain(new RunId("ineligible"), task.State.Runs.Keys);
    }

}
