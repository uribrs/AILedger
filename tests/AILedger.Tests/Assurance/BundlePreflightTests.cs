using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Runs.Batch;
using AILedger.Tests.Cli;
using AILedger.Tests.Support;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Assurance;

public sealed partial class BundleAssuranceTests
{
    [Fact]
    public async Task WorkingRoleCannotAcquireAssuranceCoverage()
    {
        using var f = await BundleFixture.CreateAsync();
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Execution);
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["A", "B"], subject: "worker"), "Assurance coverage:", "Verifier");
    }

    [Fact]
    public async Task LegacySingletonResumeRetainsItsSessionAndScope()
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Ok("provider", "resume", "--subject", "verifier", "--run", "LEGACY", "--work", "A", "--session", "session-LEGACY",
            "--provider", "claude", "--executable", "/usr/bin/true", "--cognitive-root", FindCognitiveRoot());
        var request = Assert.Single(f.Adapter.Requests);
        Assert.Equal(AgentLaunchMode.Resume, request.Mode);
        Assert.Equal("session-LEGACY", request.ProviderSessionId);
        Assert.Null(request.Assurance);
        Assert.Equal(AgentRunStatus.Completed, (await f.State()).Runs[new RunId("LEGACY")].Status);
    }

    [Fact]
    public async Task ThirdProviderCanIndependentlyVerifyMixedWorkingProviders()
    {
        using var f = await BundleFixture.CreateAsync(workerBProvider: "claude");
        Assert.True(await f.Launch("VAB", ["A", "B"], provider: "independent-test-provider") == 0, f.Error.ToString());
        Assert.Equal(AgentRunStatus.Completed, (await f.State()).Runs[new RunId("VAB")].Status);
        Assert.Equal("independent-test-provider", Assert.Single(f.Adapter.Requests).Provider);
    }

    [Theory]
    [InlineData("claude", true)]
    [InlineData("codex", false)]
    public async Task BatchPreviewUsesFullAssuranceAdmissionWithoutReservation(string provider, bool admissible)
    {
        using var f = await BundleFixture.CreateAsync();
        var before = await f.State();
        var body = JsonSerializer.Serialize(new { members = new[] { new { id = "bundle", providerLaunch = new {
            subjectActorId = "verifier", workItemId = "A", coveredWorkItemIds = new[] { "B", "A" },
            candidateId = BundleFixture.Candidate, runId = "VAB", provider } } } });
        f.Output.GetStringBuilder().Clear();
        using var input = new StandardInput(body);
        await f.Ok("preflight", "batch", "--body-stdin", "--cognitive-root", FindCognitiveRoot());
        using var json = JsonDocument.Parse(f.Output.ToString());
        Assert.Equal(admissible, json.RootElement.GetProperty("admissible").GetBoolean());
        Assert.Equal(before.Version, (await f.State()).Version);
        Assert.Empty(f.Adapter.Requests);
        Assert.Equal(0, f.Adapter.Probes);
        if (!admissible) Assert.Contains("Assurance member 'A':", json.RootElement.GetProperty("causes")[0].GetProperty("refusal").GetString());
    }

    [Theory]
    [InlineData("verifier")]
    [InlineData("reviewer")]
    public async Task SubsequentAssuranceBatchAndLaunchRefuseOnSameSnapshot(string subject)
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Verify("VAB", "A", "B");
        if (subject == "reviewer") await CliStageFixture.ToReviewAsync(f.App, f.Root);
        var before = await f.State();
        const string diagnostic = "Assurance member 'A': subsequent assurance requires --candidate.";
        var request = new ProviderLaunchPreflightRequest(new ActorId("operator"), new ActorId(subject),
            new WorkItemId("A"), before.ContextBuilds[new ActorId("operator")].Skills,
            RunId: new RunId("BAD"), Provider: "claude");
        var preview = BatchPreflight.Evaluate(before, new BatchPreflightRequest([new("next", null, request)]));
        Assert.False(preview.Admissible);
        Assert.Equal(diagnostic, Assert.Single(preview.Causes).Refusal);
        foreach (var incomplete in new[] { request with { RunId = null }, request with { Provider = null } })
        {
            var missingIdentity = BatchPreflight.Evaluate(before, new BatchPreflightRequest([new("next", null, incomplete)]));
            Assert.False(missingIdentity.Admissible);
            Assert.Equal("Assurance coverage: preview requires runId and provider.", Assert.Single(missingIdentity.Causes).Refusal);
        }
        var body = JsonSerializer.Serialize(new { members = new[] { new { id = "next", providerLaunch = new {
            subjectActorId = subject, workItemId = "A", runId = "BAD", provider = "claude" } } } });
        f.Output.GetStringBuilder().Clear();
        using (var input = new StandardInput(body))
            await f.Ok("preflight", "batch", "--body-stdin", "--cognitive-root", FindCognitiveRoot());
        using var json = JsonDocument.Parse(f.Output.ToString());
        Assert.False(json.RootElement.GetProperty("admissible").GetBoolean());
        Assert.Equal(diagnostic, json.RootElement.GetProperty("causes")[0].GetProperty("refusal").GetString());
        Assert.Equal(before.Version, (await f.State()).Version);
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["A"], subject: subject, candidate: null), diagnostic);
        Assert.Equal(before.Version, (await f.State()).Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NeverAssuredLegacyBatchRetainsOptionalPreviewFields(bool includeIdentity)
    {
        using var f = await BundleFixture.CreateAsync();
        var before = await f.State();
        var request = new ProviderLaunchPreflightRequest(new ActorId("operator"), new ActorId("verifier"),
            new WorkItemId("A"), before.ContextBuilds[new ActorId("operator")].Skills,
            RunId: includeIdentity ? new RunId("LEGACY") : null, Provider: includeIdentity ? "claude" : null);
        Assert.True(BatchPreflight.Evaluate(before, new BatchPreflightRequest([new("legacy", null, request)])).Admissible);
        var launch = new Dictionary<string, object> { ["subjectActorId"] = "verifier", ["workItemId"] = "A" };
        if (includeIdentity) { launch["runId"] = "LEGACY"; launch["provider"] = "claude"; }
        var body = JsonSerializer.Serialize(new { members = new[] { new { id = "legacy", providerLaunch = launch } } });
        f.Output.GetStringBuilder().Clear();
        using (var input = new StandardInput(body))
            await f.Ok("preflight", "batch", "--body-stdin", "--cognitive-root", FindCognitiveRoot());
        using var json = JsonDocument.Parse(f.Output.ToString());
        Assert.True(json.RootElement.GetProperty("admissible").GetBoolean());
        Assert.Equal(before.Version, (await f.State()).Version);
        Assert.Empty(f.Adapter.Requests);
        Assert.Equal(0, f.Adapter.Probes);
        Assert.True(await f.Launch("LEGACY", ["A"], candidate: null) == 0, f.Error.ToString());
        Assert.Equal(AgentRunStatus.Completed, (await f.State()).Runs[new RunId("LEGACY")].Status);
    }

    [Fact]
    public async Task TiedLatestWorkingRunsAreRefusedInsteadOfGuessed()
    {
        using var f = await BundleFixture.CreateAsync();
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Execution);
        await f.Work("A", "WA2");
        await CliStageFixture.ToVerificationAsync(f.App, f.Root);
        var state = await f.State();
        var timestamp = state.Runs[new RunId("WA2")].EndedAt;
        // Historical working-run timestamps can tie. Replay that legal older shape, then ask
        // the actual admission path to freeze a new assurance provenance from it.
        var history = (await f.History()).Select(e => e.Data is RunCompleted completed && completed.RunId == new RunId("WA")
            ? e with { RecordedAt = timestamp!.Value, Data = completed with { EndedAt = timestamp.Value } } : e).ToArray();
        var tied = Replay(history);
        var command = new StartRunCommand(new ActorId("operator"), null, "ambiguous", new RunId("BAD"), new WorkItemId("A"), "claude", null,
            SubjectActorId: new ActorId("verifier"), Assurance: await f.Binding("A", "B"));
        var error = Assert.Throws<GovernanceException>(() => new CommandHandler().Handle(tied, command, DateTimeOffset.UtcNow));
        Assert.Equal("Assurance member 'A': latest working run is ambiguous.", error.Message);
    }
}
