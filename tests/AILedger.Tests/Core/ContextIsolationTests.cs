using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class ContextIsolationTests
{
    [Fact]
    public void R4_CodeReviewerManifestExcludesForbiddenArtifacts()
    {
        var task = new TestTask();
        var reviewer = new ActorId("reviewer");
        task.Assign(reviewer, RoleKind.CodeReviewer, Capability.BuildContext);
        var artifacts = Enum.GetValues<ContextArtifactKind>()
            .Select(kind => new ContextArtifact(kind, kind.ToString(), $"content-{kind}", []))
            .Append(new ContextArtifact(ContextArtifactKind.Skill, "code-reviewer", "review instructions", []))
            .Append(new ContextArtifact(ContextArtifactKind.Skill, "workflow-coordinator", "orchestrator instructions", []))
            .ToArray();

        var manifest = new ContextAssembler().Build(
            task.State, reviewer, null, artifacts, DateTimeOffset.UnixEpoch);

        var forbidden = new[]
        {
            ContextArtifactKind.UserRequest,
            ContextArtifactKind.PromptContract,
            ContextArtifactKind.OrchestrationPlan,
            ContextArtifactKind.VerifierOutput
        };
        Assert.DoesNotContain(manifest.Artifacts, artifact => forbidden.Contains(artifact.Kind));
        Assert.Contains(manifest.Artifacts, artifact => artifact.Kind == ContextArtifactKind.Skill && artifact.Id == "code-reviewer");
        Assert.DoesNotContain(manifest.Artifacts, artifact => artifact.Kind == ContextArtifactKind.Skill && artifact.Id == "workflow-coordinator");
    }

    [Fact]
    public void ManifestIsDeterministicAndScopedToWorkItemDependencies()
    {
        var task = new TestTask();
        var actor = new ActorId("worker");
        task.Assign(actor, RoleKind.Worker, Capability.BuildContext);
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), new ClaimId("C2"), "Unrelated", null));
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), new ClaimId("C1"), "Relevant", null));
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", actor, [new ClaimId("C1")], [Path.GetFullPath("src")]));
        var available = new[]
        {
            new ContextArtifact(ContextArtifactKind.StopCondition, "Z", "scope-change", []),
            new ContextArtifact(ContextArtifactKind.Rules, "B", "rules", []),
            new ContextArtifact(ContextArtifactKind.Skill, "contract-driven-execution", "skill", [])
        };

        var manifest = new ContextAssembler().Build(task.State, actor, new WorkItemId("W1"), available, DateTimeOffset.UnixEpoch);

        Assert.Contains(manifest.Artifacts, artifact => artifact.Kind == ContextArtifactKind.Claim && artifact.Id == "C1");
        Assert.DoesNotContain(manifest.Artifacts, artifact => artifact.Kind == ContextArtifactKind.Claim && artifact.Id == "C2");
        Assert.Equal(manifest.Artifacts.OrderBy(item => item.Kind).ThenBy(item => item.Id), manifest.Artifacts);
        Assert.Equal(["scope-change"], manifest.StopConditions);
    }

    [Fact]
    public void CodeReviewerNeverSeesAnOpenEscalationButStillSeesRejectedAlternatives()
    {
        var task = new TestTask();
        var reviewer = new ActorId("reviewer");
        task.Assign(reviewer, RoleKind.CodeReviewer, Capability.BuildContext);
        task.Apply(new RaiseEscalationCommand(
            task.OperatorId, null, task.NextCorrelation(), new EscalationId("X1"),
            EscalationKind.BusinessDecision, "Ship now or harden first?", null,
            ["ship", "harden"], "ship", []));
        task.Apply(new RecordAlternativeCommand(
            task.OperatorId, null, task.NextCorrelation(), new AlternativeId("ALT1"),
            "Use a database", "Files stay inspectable", null));

        var manifest = new ContextAssembler().Build(task.State, reviewer, null, [], DateTimeOffset.UnixEpoch);

        // An escalation carries the leads' recommendation, which is intent, not code.
        Assert.DoesNotContain(manifest.Artifacts, item => item.Kind == ContextArtifactKind.Escalation);
        Assert.DoesNotContain(manifest.Artifacts, item => item.Content.Contains("Recommended", StringComparison.Ordinal));
        Assert.Contains(manifest.Artifacts,
            item => item.Kind == ContextArtifactKind.Alternative && item.Id == "ALT1");
    }

    [Fact]
    public void ContextRequiresAssignedRoleAndCapability()
    {
        var task = new TestTask();
        var actor = new ActorId("lead");
        task.Assign(actor, RoleKind.PlanningLead, Capability.AddClaim);

        Assert.Throws<GovernanceException>(() =>
            new ContextAssembler().Build(task.State, actor, null, [], DateTimeOffset.UnixEpoch));
        Assert.Throws<GovernanceException>(() =>
            new ContextAssembler().Build(task.State, new ActorId("unknown"), null, [], DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void VerifierReceivesTaskOrchestratorVerifierProtocol()
    {
        var task = new TestTask();
        var verifier = new ActorId("verifier");
        task.Assign(verifier, RoleKind.Verifier, Capability.BuildContext);
        var artifacts = new[]
        {
            new ContextArtifact(ContextArtifactKind.Skill, "task-orchestrator", "contains verifier protocol", []),
            new ContextArtifact(ContextArtifactKind.Skill, "code-reviewer", "review protocol", [])
        };

        var manifest = new ContextAssembler().Build(
            task.State, verifier, null, artifacts, DateTimeOffset.UnixEpoch);

        Assert.Contains(manifest.Artifacts,
            artifact => artifact.Kind == ContextArtifactKind.Skill && artifact.Id == "task-orchestrator");
        Assert.DoesNotContain(manifest.Artifacts,
            artifact => artifact.Kind == ContextArtifactKind.Skill && artifact.Id == "code-reviewer");
    }
}
