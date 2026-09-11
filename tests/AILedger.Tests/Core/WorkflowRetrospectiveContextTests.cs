using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// Contract constraint 3, channel (a). The retrospective scores the agents that worked the task, so
// it is withheld from every role rather than from some of them. The assembler drops it before
// ToContextKind is reached, and the point of testing 'does not throw' alongside 'is absent' is that
// the obvious other repair — giving the kind a ContextArtifactKind counterpart — would pass the
// throw check and fail the absence one.
public sealed class WorkflowRetrospectiveContextTests
{
    [Theory]
    [InlineData(RoleKind.Operator)]
    [InlineData(RoleKind.PlanningLead)]
    [InlineData(RoleKind.ImplementationLead)]
    [InlineData(RoleKind.Researcher)]
    [InlineData(RoleKind.Worker)]
    [InlineData(RoleKind.Verifier)]
    [InlineData(RoleKind.CodeReviewer)]
    public void R1_AFiledWorkflowRetrospectiveIsAbsentFromEveryRoleManifest(RoleKind role)
    {
        var task = WorkflowRetrospectiveArtifactTests.Archived();
        task.Apply(WorkflowRetrospectiveArtifactTests.Retrospective(task, task.OperatorId, "A-retro"));
        var actor = new ActorId("reader-" + role);
        task.Assign(actor, role, Capability.BuildContext);

        var manifest = new ContextAssembler().Build(
            task.State, actor, null, [], DateTimeOffset.UnixEpoch);

        Assert.DoesNotContain(manifest.Artifacts, artifact => artifact.Id == "A-retro");
        Assert.DoesNotContain(manifest.Artifacts,
            artifact => artifact.Content.Contains("| dimension | score |", StringComparison.Ordinal));
    }

    // The throw arm stays reachable. It is what makes the next governed artifact kind added without
    // a decision about who may read it fail loudly instead of appearing quietly in a manifest.
    [Fact]
    public void TheUnknownKindArmIsStillReachable()
    {
        var method = typeof(ContextAssembler).GetMethod(
            "ToContextKind",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            method!.Invoke(null, [(GovernedArtifactKind)999]));
        Assert.Contains("Unknown governed artifact kind", error.InnerException!.Message,
            StringComparison.Ordinal);
    }
}
