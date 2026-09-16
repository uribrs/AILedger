using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Tests.Cli;
using AILedger.Tests.Support;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Assurance;

[Collection(StandardInput.Collection)]
public sealed class LegacySpacedWorkCliTests
{
    private const string Member = "legacy work";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacySpacedWorkFilesAndClosesThroughManualAndProviderCli(bool provider)
    {
        using var f = await CreateAsync();
        if (provider)
        {
            Assert.True(await f.Launch("VL", [Member], candidate: null) == 0, f.Error.ToString());
            var request = Assert.Single(f.Adapter.Requests);
            Assert.Equal(Member, request.WorkItemId!.Value.Value);
            Assert.Null(request.Assurance);
            Assert.Equal(Path.Combine(f.Repository, "legacy"), request.WorkingDirectory);
            Assert.Equal(WorkItemStatus.Active, Assert.Single(f.Adapter.ActiveStates).WorkItems[new WorkItemId(Member)].Status);
        }
        else
        {
            await f.Ok("run", "start", "--subject", "verifier", "--run", "VL", "--work", Member, "--provider", "claude");
            Assert.Equal(WorkItemStatus.Active, (await f.State()).WorkItems[new WorkItemId(Member)].Status);
            Assert.True(await f.File("verifier", "VL", "OUT-VL", "verifier-output", "--work", Member) == 0, f.Error.ToString());
            await f.Ok("run", "complete", "--run", "VL", "--status", "completed", "--session", "manual-legacy-session");
            Assert.Empty(f.Adapter.Requests);
        }
        var state = await f.State();
        var run = state.Runs[new RunId("VL")];
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.NotNull(run.ProviderSessionId);
        Assert.Null(run.Assurance);
        Assert.Equal(Member, run.WorkItemId!.Value.Value);
        var output = state.Artifacts[new ArtifactId("OUT-VL")];
        Assert.Equal(run.Id, output.ProducerRunId);
        Assert.Equal(run.WorkItemId, output.WorkItemId);
        Assert.Null(output.Assurance);
        Assert.Equal(WorkItemStatus.Paused, state.WorkItems[new WorkItemId(Member)].Status);

        await f.Verify("VA", "A");
        f.Output.GetStringBuilder().Clear();
        await f.Ok("artifact", "list", "--work", Member);
        Assert.Contains("OUT-VL", f.Output.ToString());
        Assert.DoesNotContain("OUT-VA", f.Output.ToString());
        var path = Path.Combine(f.Root, "legacy-context.json");
        await f.Ok("context", "build", "--actor", "verifier", "--work", Member,
            "--cognitive-root", FindCognitiveRoot(), "--output", path);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(Member, manifest.RootElement.GetProperty("workItemId").GetString());
        var artifacts = manifest.RootElement.GetProperty("artifacts").EnumerateArray().ToArray();
        Assert.Contains(artifacts, a => a.GetProperty("id").GetString() == "OUT-VL");
        Assert.DoesNotContain(artifacts, a => a.GetProperty("id").GetString() == "OUT-VA");
    }

    [Theory]
    [InlineData("legacy", true)]
    [InlineData("candidate", false)]
    [InlineData("coverage", false)]
    public async Task LegacySpacedWorkBatchDistinguishesExplicitAssurance(string selection, bool admissible)
    {
        using var f = await CreateAsync();
        var before = (await f.State()).Version;
        var launch = new Dictionary<string, object?> { ["subjectActorId"] = "verifier", ["workItemId"] = Member };
        if (selection != "legacy") launch["candidateId"] = BundleFixture.Candidate;
        if (selection == "coverage") launch["coveredWorkItemIds"] = new[] { Member, "A" };
        using var input = new StandardInput(JsonSerializer.Serialize(new { members = new[] { new { id = "legacy", providerLaunch = launch } } }));
        f.Output.GetStringBuilder().Clear();
        await f.Ok("preflight", "batch", "--body-stdin", "--cognitive-root", FindCognitiveRoot());
        using var result = JsonDocument.Parse(f.Output.ToString());
        Assert.Equal(admissible, result.RootElement.GetProperty("admissible").GetBoolean());
        if (!admissible) Assert.Contains("whitespace", f.Output.ToString());
        Assert.Equal(before, (await f.State()).Version);
        Assert.Equal(0, f.Adapter.Probes);
        Assert.Empty(f.Adapter.Requests);
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("also-work")]
    public async Task LegacySpacedWorkExplicitSelectionStillRefusesBeforeProbe(string selection)
    {
        using var f = await CreateAsync();
        var before = (await f.State()).Version;
        var members = selection == "candidate" ? new[] { Member } : new[] { "A", Member };
        Assert.Equal(1, await f.Launch("BAD", members));
        Assert.Contains("Assurance coverage:", f.Error.ToString());
        Assert.Contains("whitespace", f.Error.ToString());
        Assert.Equal(before, (await f.State()).Version);
        Assert.Equal(0, f.Adapter.Probes);
        Assert.Empty(f.Adapter.Requests);
    }

    [Fact]
    public async Task LegacyAnchorSyntaxStillCannotAssertSingletonAgainstBundleArtifact()
    {
        using var f = await BundleFixture.CreateAsync();
        f.Adapter.BeforeResult = async _ =>
        {
            var before = (await f.State()).Version;
            Assert.Equal(1, await f.File("verifier", "VAB", "BAD", "verifier-output", "--work", "A"));
            Assert.Contains("Assurance artifact coverage:", f.Error.ToString());
            Assert.Equal(before, (await f.State()).Version);
        };
        await f.Verify("VAB", "A", "B");
        var state = await f.State();
        Assert.DoesNotContain(new ArtifactId("BAD"), state.Artifacts.Keys);
        Assert.Equal(new[] { "A", "B" }, state.Artifacts[new ArtifactId("OUT-VAB")].Assurance!.WorkItemIds.Select(id => id.Value));
    }

    private static async Task<BundleFixture> CreateAsync()
    {
        var f = await BundleFixture.CreateAsync();
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Execution);
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Scope);
        await CliStageFixture.AdvanceAsync(f.App, f.Root, "T1", TaskStage.Ready);
        var scope = Directory.CreateDirectory(Path.Combine(f.Repository, "legacy")).FullName;
        await f.Ok("work", "add", "--id", Member, "--title", "legacy spaced ID", "--owner", "worker", "--scope", scope);
        await CliStageFixture.ToExecutionAsync(f.App, f.Root);
        await f.Work(Member, "WL");
        await CliStageFixture.ToVerificationAsync(f.App, f.Root);
        return f;
    }
}
