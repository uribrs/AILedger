using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Storage;
using AILedger.Tests.Cli;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Assurance;

public sealed partial class BundleAssuranceTests
{
    [Fact]
    public async Task CoverageRoundTripsThroughMemoryDebtAndViews()
    {
        // Memory ingestion/SQL/search parity is separately owned by BundleMemoryTests. This test
        // pins canonical debt and the on-disk/CLI boundary feeding that read model.
        using var f = await BundleFixture.CreateAsync();
        var before = TaskDebt.Compute(await f.State());
        await f.Verify("VAB", "A", "B");
        var verified = TaskDebt.Compute(await f.State());
        Assert.Equal(before.WorkItemsAwaitingVerification - 2, verified.WorkItemsAwaitingVerification);
        Assert.Equal(2, verified.WorkItemsAwaitingCodeReview);
        await f.Review("RAB", "VAB", "B", "A");
        Assert.Equal(0, TaskDebt.Compute(await f.State()).WorkItemsAwaitingCodeReview);
        var state = await f.State();
        var serialized = JsonSerializer.Serialize(state, LedgerJson.CreateProjectionOptions());
        using var json = JsonDocument.Parse(serialized);
        var artifact = json.RootElement.GetProperty("artifacts").EnumerateObject().Single(p => p.Name == "OUT-VAB").Value;
        Assert.False(artifact.TryGetProperty("content", out _));
        Assert.Equal(new[] { "A", "B" }, artifact.GetProperty("assurance").GetProperty("workItemIds").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(2, artifact.GetProperty("memberReplacements").GetArrayLength());
        await f.Ok("artifact", "list", "--work", "B");
        Assert.Contains("OUT-VAB", f.Output.ToString());
        Assert.Contains("OUT-RAB", f.Output.ToString());
        var markdown = await File.ReadAllTextAsync(Path.Combine(f.Root, "T1", "task.md"));
        Assert.Contains("VAB", markdown);
        Assert.Contains("A", markdown);
        Assert.Contains("B", markdown);
        Assert.Equal(2, state.Runs.Values.Count(r => r.Assurance is not null));
    }

    [Fact]
    public async Task PrivateCandidateUsesItsOwnCliAndDetectsWholeTree()
    {
        // Physical whole-tree comparisons are the coordinator's release procedure (PD7).
        // This executable boundary proves the child receives a private CLI path, not global tool.
        using var f = await BundleFixture.CreateAsync();
        await f.Verify("VAB", "A", "B");
        var request = Assert.Single(f.Adapter.Requests);
        Assert.Contains(typeof(AILedger.Cli.CliApplication).Assembly.Location, request.LedgerCommandLine, StringComparison.Ordinal);
        Assert.Contains("dotnet", request.LedgerCommandLine, StringComparison.Ordinal);
        Assert.Equal(f.Root, request.LedgerRoot);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("blocked")]
    [InlineData("abandoned")]
    [InlineData("stale")]
    [InlineData("completed")]
    public async Task InvalidMemberRefusalNamesTheMemberAndHasNoEffects(string defect)
    {
        using var f = await BundleFixture.CreateAsync(dependencies: defect == "stale");
        var id = defect == "unknown" ? "UNKNOWN" : "B";
        if (defect == "blocked") await f.Ok("work", "block", "--id", id, "--reason", "fixture");
        if (defect == "abandoned") await f.Ok("work", "abandon", "--id", id, "--reason", "fixture");
        if (defect == "stale")
        {
            await f.Ok("evidence", "add", "--id", "REFUTE", "--source-type", "fixture", "--citation", "fixture.cs:1", "--summary", "invalid after work", "--refutes", "CB");
            await f.Ok("claim", "resolve", "--id", "CB", "--status", "rejected", "--evidence", "REFUTE");
            Assert.Equal(WorkItemStatus.Stale, (await f.State()).WorkItems[new WorkItemId("B")].Status);
        }
        if (defect == "completed")
        {
            await f.Verify("VB", "B");
            await f.Review("RB", "VB", "B");
            await f.Ok("work", "complete", "--id", "B");
            await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Repair);
            await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Verification);
        }
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["A", id]), $"Assurance member '{id}':", defect);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("no-anchor")]
    [InlineData("no-candidate")]
    public async Task CliCoverageSyntaxRefusesBeforeProbe(string defect)
    {
        using var f = await BundleFixture.CreateAsync();
        string[] selection = defect switch
        {
            "duplicate" => ["--work", "A", "--also-work", "A", "--candidate", BundleFixture.Candidate],
            "no-anchor" => ["--also-work", "B", "--candidate", BundleFixture.Candidate],
            _ => ["--work", "A", "--also-work", "B"]
        };
        await RefusesWithoutMutation(f, () => f.Run(["provider", "launch", "--subject", "verifier", "--run", "BAD", .. selection, "--provider", "claude", "--executable", "/usr/bin/true", "--cognitive-root", FindCognitiveRoot()]), defect == "duplicate" ? 1 : 2, "Assurance coverage:");
    }
}
