using System.Text.Json;
using AILedger.Cli.Dispatch;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Storage;
using AILedger.Tests.Assurance;
using AILedger.Tests.Cli;
using AILedger.Tests.HandoffAssurance;
using AILedger.Tests.Support;
namespace AILedger.Tests.Dispatch;

[Collection(StandardInput.Collection)]
public sealed class DispatchAssuranceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MissingRequiredOutputHasTypedFactAndSeparateProviderAndLedgerOutcomes(bool candidate)
    {
        using var f = await BundleFixture.CreateAsync();
        f.Adapter.FileOutput = false;
        var result = await (await RoutineAsync(f)).LaunchAsync(Request("V1") with
            { Candidate = candidate ? BundleFixture.Candidate : null, AdditionalWork = candidate ? [new("B")] : [] }, default);
        Assert.Equal(AgentRunStatus.Completed, result.ProviderResult!.Status);
        Assert.Equal(AgentRunStatus.Failed, result.Completed!.Status);
        Assert.Equal(GovernedArtifactKind.VerifierOutput, result.MissingRequiredOutput);
        Assert.Equal("RequiredRunOutput", result.Failure!.Code);
        Assert.Equal(ResultRetentionStatus.Retained, result.Retention.Status);
        Assert.Equal(WorkItemStatus.Paused, (await f.State()).WorkItems[new("A")].Status);
    }

    [Fact]
    public async Task RoutineHostPreservesIndependentVerificationAndBlindReview()
    {
        using var f = await BundleFixture.CreateAsync(dependencies: true);
        var service = await RoutineAsync(f);
        var denied = await service.LaunchAsync(Request("BAD") with { Provider = "codex" }, default);
        Assert.Equal(DispatchFailureKind.Refused, denied.Failure!.Kind);
        Assert.Equal(0, f.Adapter.Probes);
        var verified = await service.LaunchAsync(Request("V1"), default);
        Assert.Null(verified.Failure);
        await CliStageFixture.ToReviewAsync(f.App, f.Root);
        var review = await service.LaunchAsync(Request("R1") with { SubjectActorId = new("reviewer"), VerifierRun = new("V1") }, default);
        Assert.Null(review.Failure);
        var delivered = f.Adapter.Requests.Last();
        Assert.DoesNotContain("SENTINEL", delivered.StandardInput);
        Assert.Equal(new RunId("V1"), delivered.Assurance!.VerifierRunId);
        Assert.Equal(AgentRunStatus.Completed, review.Completed!.Status);
        Assert.NotEqual(WorkItemStatus.Completed, (await f.State()).WorkItems[new("A")].Status);
    }

    [Fact]
    public async Task RoutineVerifierReceivesScopedAssuranceWhileReviewIncompatibleContextRemainsExplicit()
    {
        using var f = await BundleFixture.CreateAsync();
        using var policy = await AssuranceFixture.CreateAsync();
        using var protectedRoot = new TemporaryDirectory();
        // Put the bounded policy's complete input closure inside the selected existing work scope.
        var areaRoot = Path.Combine(f.Repository, "A");
        foreach (var path in Directory.EnumerateFiles(policy.Root))
            File.Copy(path, Path.Combine(areaRoot, Path.GetFileName(path)));
        var configured = policy.Policy with { CandidateRoot = areaRoot };
        var authorityPath = Path.Combine(protectedRoot.Path, "authority.json");
        await File.WriteAllTextAsync(authorityPath, JsonSerializer.Serialize(configured, AILedger.Core.Handoffs.HandoffJson.Options));
        var options = Options(f) with { AssuranceAuthority = authorityPath, AssuranceStore = Path.Combine(protectedRoot.Path, "store") };
        var service = await RoutineAsync(f, options);
        var result = await service.LaunchAsync(Request("V1"), default);
        Assert.Null(result.Failure);
        Assert.Equal(AssurancePreparationStatus.Opened, result.AssurancePreparation);
        var request = Assert.Single(f.Adapter.Requests);
        Assert.Contains("inspect_assurance", request.FindingsEndpoint!.AssuranceTools!);
        Assert.Contains("run_assurance_checks", request.FindingsEndpoint.AssuranceTools!);
        Assert.DoesNotContain("accept_assurance", request.FindingsEndpoint.AssuranceTools!);
        Assert.Contains(authorityPath, request.Isolation!.HiddenPaths);
        await CliStageFixture.ToReviewAsync(f.App, f.Root);
        var refused = await service.LaunchAsync(Request("R1") with { SubjectActorId = new("reviewer"), VerifierRun = new("V1") }, default);
        Assert.Equal(DispatchFailureKind.PreparationUnsupported, refused.Failure!.Kind);
        Assert.Equal("incompatible_context", refused.Failure.Code);
        Assert.Equal(1, f.Adapter.Probes);
        Assert.DoesNotContain(new RunId("R1"), (await f.State()).Runs.Keys);
    }

    [Fact]
    public async Task RepeatedStorageRefusalPreservesRequiredOutputFact()
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Ok("run", "start", "--subject", "verifier", "--run", "V1", "--work", "A", "--provider", "claude");
        var command = new CompleteRunCommand(new("operator"), null, "test", new("V1"), AgentRunStatus.Completed, "observed");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var refusal = await Assert.ThrowsAsync<AILedger.Core.Domain.GovernanceException>(() =>
                f.Service.ExecuteAsync(new("T1"), command, default));
            Assert.Equal(AILedger.Core.Domain.GovernanceRefusalKind.RequiredRunOutput, refusal.Kind);
        }
    }

    private static ProviderDispatchRequest Request(string run) => new(new("T1"), new("operator"), new(run), "claude")
        { SubjectActorId = new("verifier"), WorkItemId = new("A"), Candidate = BundleFixture.Candidate };
    private static DispatchHostOptions Options(BundleFixture f) => new(f.Root, Path.Combine(f.Root, "lessons"))
        { CognitiveRoot = ContextBrief.CognitiveRoot(), Executable = "/usr/bin/true" };
    private static async Task<IProviderDispatchService> RoutineAsync(BundleFixture f, DispatchHostOptions? options = null) =>
        ProviderDispatchHost.CreateRoutine((FileGovernedTaskService)f.Service,
            new(await f.State(), new("operator"), DateTimeOffset.UtcNow.AddHours(1)),
            options ?? Options(f), _ => f.Adapter, new ContextAssembler());
}
