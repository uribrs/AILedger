using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Tests.Support;

namespace AILedger.Tests.ContextBriefing;

public sealed class ContextSkillSelectionTests
{
    [Fact]
    public async Task TheOperatorIsServedTheCoordinatorFirstAndTheOrchestratorSecond()
    {
        using var root = new TemporaryDirectory();
        var manifest = await ContextBuildTestHost.BuildAsync(root.Path, "T1", "operator");

        var skills = manifest.Artifacts
            .Where(artifact => artifact.Kind == ContextArtifactKind.Skill)
            .Select(artifact => artifact.Id)
            .ToArray();

        Assert.Equal("workflow-coordinator", skills[0]);
        Assert.Equal("task-orchestrator", skills[1]);
        Assert.All(skills, id => Assert.True(
            id is "workflow-coordinator" or "task-orchestrator" ||
            id.EndsWith(":workflow-coordinator", StringComparison.Ordinal) ||
            id.EndsWith(":task-orchestrator", StringComparison.Ordinal),
            $"'{id}' belongs to neither skill the operator is served."));
    }

    [Fact]
    public async Task TheEventRecordsOneRowPerSkillServedWithItsContentHash()
    {
        using var root = new TemporaryDirectory();
        var manifest = await ContextBuildTestHost.BuildAsync(root.Path, "T1", "operator");
        var state = await ContextBuildTestHost.Service(root.Path)
            .GetStateAsync(new TaskId("T1"), CancellationToken.None);

        var build = state!.ContextBuilds[new ActorId("operator")];
        Assert.Equal(RoleKind.Operator, build.Role);
        Assert.Equal(
            manifest.Artifacts.Count(artifact => artifact.Kind == ContextArtifactKind.Skill),
            build.Skills.Count);
        foreach (var skill in build.Skills)
        {
            var served = manifest.Artifacts.Single(artifact =>
                artifact.Kind == ContextArtifactKind.Skill && artifact.Id == skill.SkillId);
            Assert.Equal(ContextSkills.Hash(served.Content), skill.ContentHash);
        }
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
