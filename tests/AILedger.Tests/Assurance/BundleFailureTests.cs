using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Tests.Cli;
using AILedger.Tests.Support;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Assurance;

public sealed partial class BundleAssuranceTests
{
    [Theory]
    [InlineData("manifest")]
    [InlineData("closure")]
    [InlineData("closure-stranded")]
    public async Task PostStartFailureRecoversEveryMember(string failure)
    {
        using var f = await BundleFixture.CreateAsync();
        var service = new CompletionFailureService(f.Service, failure == "closure-stranded" ? int.MaxValue : failure == "closure" ? 1 : 0);
        IContextAssembler assembler = failure == "manifest" ? new ThrowingAssembler() : new ContextAssembler();
        var app = new CliApplication(f.Output, f.Error, _ => service, _ => f.Adapter, assembler);
        var exit = await app.RunAsync(["provider", "launch", .. f.Common, "--actor", "operator", "--subject", "verifier", "--run", "VAB",
            "--work", "A", "--also-work", "B", "--candidate", BundleFixture.Candidate, "--provider", "claude", "--executable", "/usr/bin/true", "--cognitive-root", FindCognitiveRoot()], CancellationToken.None);
        var state = await f.State();
        if (failure == "closure")
        {
            Assert.Equal(0, exit);
            Assert.Equal(2, service.CompletionAttempts);
            Assert.Equal(AgentRunStatus.Completed, state.Runs[new RunId("VAB")].Status);
        }
        else if (failure == "closure-stranded")
        {
            Assert.Equal(1, exit);
            Assert.Equal(3, service.CompletionAttempts);
            Assert.Contains("could not be closed", f.Error.ToString(), StringComparison.Ordinal);
            Assert.Equal(AgentRunStatus.Active, state.Runs[new RunId("VAB")].Status);
            Assert.All(new[] { "A", "B" }, id => Assert.Equal(WorkItemStatus.Active, state.WorkItems[new WorkItemId(id)].Status));
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(f.Root, "T1"), "*", SearchOption.AllDirectories).Where(p => p.Contains("VAB", StringComparison.Ordinal)));
            // Recovery is a new explicit operator mutation once storage is available, never a
            // fabricated terminal state while persistence is failing.
            await f.Ok("run", "complete", "--run", "VAB", "--status", "failed", "--session", "session-VAB");
            state = await f.State();
        }
        else
        {
            Assert.Equal(1, exit);
            Assert.Equal(AgentRunStatus.Failed, state.Runs[new RunId("VAB")].Status);
            Assert.Empty(f.Adapter.Requests);
        }
        Assert.All(new[] { "A", "B" }, id => Assert.Equal(WorkItemStatus.Paused, state.WorkItems[new WorkItemId(id)].Status));
        if (failure != "manifest") Assert.Equal("session-VAB", state.Runs[new RunId("VAB")].ProviderSessionId);

    }

    [Fact]
    public async Task ExplicitBlockAndUnrefutedRefinementUseExistingRecovery()
    {
        using var f = await BundleFixture.CreateAsync(dependencies: true);
        await f.Ok("run", "start", "--run", "VAB", "--subject", "verifier", "--work", "A", "--also-work", "B", "--candidate", BundleFixture.Candidate, "--provider", "claude");
        await f.Ok("work", "block", "--id", "B", "--reason", "await refinement");
        await f.Ok("run", "complete", "--run", "VAB", "--status", "failed");
        await f.Ok("claim", "add", "--id", "CB2", "--statement", "refined requirement");
        await f.Ok("evidence", "add", "--id", "EB2", "--source-type", "fixture", "--citation", "fixture.cs:2", "--summary", "refinement supported", "--supports", "CB2");
        await f.Ok("claim", "resolve", "--id", "CB2", "--status", "validated", "--evidence", "EB2");
        await f.Ok("claim", "resolve", "--id", "CB", "--status", "superseded", "--superseded-by", "CB2");
        await f.Ok("work", "unblock", "--id", "B");
        Assert.Equal(new[] { new ClaimId("CB2") }, (await f.State()).WorkItems[new WorkItemId("B")].DependsOnClaims);
        await f.Verify("VAB2", "A", "B");
    }

    private sealed class ThrowingAssembler : IContextAssembler
    {
        private readonly ContextAssembler _inner = new();
        public ContextManifest Build(GovernedTaskState state, ActorId actorId, WorkItemId? workItemId,
            IReadOnlyList<ContextArtifact> availableArtifacts, DateTimeOffset assembledAt,
            IReadOnlyList<WorkItemId>? coveredWorkItemIds = null, AssuranceBinding? assurance = null) =>
            throw new IOException("injected manifest failure");
        public ContextManifest BuildForRun(GovernedTaskState state, ActorId actorId, RunId runId,
            IReadOnlyList<ContextArtifact> availableArtifacts, DateTimeOffset assembledAt) =>
            throw new IOException("injected manifest failure");
        public IReadOnlyList<ContextSkill> SkillsServed(RoleKind role, IReadOnlyList<ContextArtifact> availableArtifacts) =>
            _inner.SkillsServed(role, availableArtifacts);
    }
}
