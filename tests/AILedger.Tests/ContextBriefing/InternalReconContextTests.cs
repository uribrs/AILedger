using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Tests.Artifacts.Recon;

namespace AILedger.Tests.ContextBriefing;

public sealed class InternalReconContextTests
{
    [Theory]
    [InlineData(RoleKind.PlanningLead, true)]
    [InlineData(RoleKind.Worker, true)]
    [InlineData(RoleKind.Verifier, true)]
    [InlineData(RoleKind.CodeReviewer, false)]
    public void R5_ReconVisibleExceptToReviewers(RoleKind role, bool visible)
    {
        var f = new InternalReconFixture();
        f.Eligible();
        var actor = new ActorId("reader");
        f.Task.Assign(actor, role, Capability.BuildContext);
        var context = new ContextAssembler().Build(f.Task.State, actor, null, [], DateTimeOffset.UtcNow);
        Assert.Equal(visible, context.Artifacts.Any(a => a.Kind == ContextArtifactKind.InternalRecon));
        Assert.Equal(visible, context.Artifacts.Any(a => a.Content.Contains("RECON-PRIVATE-NARRATIVE", StringComparison.Ordinal)));
        if (visible)
            Assert.Equal(AgentRunStatus.Completed, Assert.Single(context.Artifacts,
                a => a.Kind == ContextArtifactKind.InternalRecon).ProducerStatus);
    }
    [Fact]
    public void CandidateBoundReviewerExcludesReconAndAssessmentNarrative()
    {
        var f = new InternalReconFixture();
        f.Eligible();
        f.Task.Transition(TaskStage.Design);
        f.Task.RecordPromptContract();
        f.Task.Transition(TaskStage.Scope);
        f.Task.RecordOrchestrationPlan();
        f.Task.StaffRemainingStages();
        f.Task.Transition(TaskStage.Ready);
        var work = f.Task.StageWorkItem();
        var binding = new AssuranceBinding(1, [work],
            [new AssuranceWorkVersion(work, new RunId("working-candidate"))], "candidate", new RunId("verified-candidate"));
        // The assembler consumes the already frozen binding; this tests routing, not admission of a run.
        var manifest = new ContextAssembler().Build(f.Task.State, new ActorId("reviewer"), work, [],
            DateTimeOffset.UtcNow, [work], binding);
        Assert.DoesNotContain(manifest.Artifacts, a => a.Kind == ContextArtifactKind.InternalRecon);
        Assert.DoesNotContain(manifest.Artifacts, a => a.Content.Contains("RECON-PRIVATE-NARRATIVE", StringComparison.Ordinal));
        Assert.DoesNotContain(manifest.Artifacts, a => a.Content.Contains("claimSetHash", StringComparison.Ordinal));
        Assert.Equal(binding.CandidateId, manifest.Assurance!.CandidateId);
    }

}
