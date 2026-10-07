using AILedger.Core.Contracts;
using AILedger.Core.ContextBriefing;
using AILedger.Tests.Assurance;
using AILedger.Tests.Cli;
using AILedger.Tests.Support;

namespace AILedger.Tests.ContractDelivery;

// Current command admission closes the historical Part 1 completion gaps.
// BundleFixture uses a temporary ledger and an in-process fake adapter; no provider is launched.
[Collection(StandardInput.Collection)]
public sealed class OrchestrationInventoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentCompletionRefusesNeverTestedVerifierDisposition(bool candidateBound)
    {
        using var fixture = await BundleFixture.CreateAsync(dependencies: true);
        fixture.Adapter.FileOutput = false;
        fixture.Adapter.BeforeResult = request => RecordUntestedOutputAsync(fixture, request);
        var candidate = candidateBound ? BundleFixture.Candidate : null;

        Assert.Equal(0, await fixture.Launch("V-untested", ["A"], candidate: candidate));
        var verified = await fixture.State();
        Assert.Equal(AgentRunStatus.Completed, verified.Runs[new("V-untested")].Status);
        Assert.Equal(ClaimStatus.Validated, verified.Claims[new("CA")].Status);
        Assert.Contains("NEVER-TESTED", verified.Artifacts[new("V-output")].Content);
        Assert.Equal("unknown", NextActionContracts.Observe(verified, new("operator"),
            new RunId("V-untested"))!.Return!.TechnicalAcceptance);

        fixture.Adapter.BeforeResult = null;
        fixture.Adapter.FileOutput = true;
        await CliStageFixture.ToReviewAsync(fixture.App, fixture.Root);
        Assert.Equal(0, await fixture.Launch("R-untested", ["A"], "reviewer",
            candidateBound ? "V-untested" : null, candidate));
        Assert.Equal(1, await fixture.Run("work", "complete", "--id", "A"));

        // Honest negative output remains recordable; required runs cannot manufacture acceptance.
        Assert.NotEqual(WorkItemStatus.Completed, (await fixture.State()).WorkItems[new("A")].Status);
        Assert.Contains("NEVER-TESTED", fixture.Error.ToString());
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public async Task CurrentCompletionRefusesUnsuccessfulLaterWork(string termination)
    {
        using var fixture = await BundleFixture.CreateAsync();
        await fixture.Verify("V-before-repair", "A");
        await fixture.Review("R-before-repair", "V-before-repair", "A");
        await CliStageFixture.BackAsync(fixture.App, fixture.Root, TaskStage.Repair);
        await fixture.Ok("run", "start", "--run", "W-interrupted", "--subject", "worker",
            "--work", "A", "--provider", "codex");
        await fixture.Ok("run", "complete", "--run", "W-interrupted", "--status", termination);

        // A failed/cancelled worker may have changed files. The kernel's latest-completed-work
        // predicate alone cannot establish that the previously inspected candidate still applies.
        Assert.Equal(1, await fixture.Run("work", "complete", "--id", "A"));
        var state = await fixture.State();
        Assert.NotEqual(WorkItemStatus.Completed, state.WorkItems[new("A")].Status);
        Assert.Contains("uncertain working execution", fixture.Error.ToString());
        Assert.Equal(new RunId("WA"), state.Runs[new("V-before-repair")].Assurance!
            .WorkVersions.Single().WorkingRunId);
        Assert.NotEqual(AgentRunStatus.Completed, state.Runs[new("W-interrupted")].Status);
    }

    [Theory]
    [InlineData("untested")] [InlineData("failed")] [InlineData("cancelled")]
    public async Task HistoricalLegalCompletionStillReplays(string history)
    {
        using var fixture = await BundleFixture.CreateAsync(dependencies: history == "untested");
        if (history == "untested")
        {
            fixture.Adapter.FileOutput = false;
            fixture.Adapter.BeforeResult = request => RecordUntestedOutputAsync(fixture, request);
        }
        await fixture.Verify("V", "A");
        fixture.Adapter.FileOutput = true;
        fixture.Adapter.BeforeResult = null;
        await fixture.Review("R", "V", "A");
        if (history != "untested")
        {
            await CliStageFixture.BackAsync(fixture.App, fixture.Root, TaskStage.Repair);
            await fixture.Ok("run", "start", "--run", "later", "--subject", "worker", "--work", "A", "--provider", "codex");
            await fixture.Ok("run", "complete", "--run", "later", "--status", history);
        }
        var state = await fixture.State();
        var replayed = new AILedger.Core.Domain.TaskReducer().Apply(state, new LedgerEvent(1, new("old-completion"), state.TaskId,
            new("operator"), DateTimeOffset.UtcNow, null, "historical", new WorkItemCompleted(new("A"))));
        Assert.Equal(WorkItemStatus.Completed, replayed.WorkItems[new("A")].Status);
    }

    private static Task RecordUntestedOutputAsync(BundleFixture fixture, AgentLaunchRequest request)
    {
        var content = ArtifactCommands.VerifierBody.Replace(
            "| --- | --- | --- | --- | --- |\n\n",
            "| --- | --- | --- | --- | --- |\n" +
            "| CA | NEVER-TESTED | dependent premise | fixture:1 | verifier |\n\n",
            StringComparison.Ordinal);
        return fixture.Service.ExecuteAsync(new("T1"), new RecordArtifactCommand(
            request.ActorId, null, "inventory", new("V-output"), GovernedArtifactKind.VerifierOutput,
            "Untested dependent premise", content, request.WorkItemId, request.RunId, null),
            CancellationToken.None);
    }
}
