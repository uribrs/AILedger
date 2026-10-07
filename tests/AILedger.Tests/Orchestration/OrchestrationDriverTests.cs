using System.Text.Json;
using AILedger.Cli;
using AILedger.Cli.Dispatch;
using AILedger.Cli.Orchestration;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Assurance;
using AILedger.Tests.Cli;
using AILedger.Tests.HandoffAssurance;
using AILedger.Tests.Support;
namespace AILedger.Tests.Orchestration;

[Collection(StandardInput.Collection)]
public sealed class OrchestrationDriverTests
{
    [Theory]
    [InlineData("none")] [InlineData("verifier")] [InlineData("reviewer")]
    public async Task ExplicitJudgmentsRouteWorkVerificationReviewAndEvidenceBackedRepair(string repair)
    {
        using var f = await DriverFixture.OpenAsync();
        await CliStageFixture.BackAsync(f.Bundle.App, f.Bundle.Root, TaskStage.Execution);
        var verified = 0;
        var reviewed = 0;
        f.Bundle.Adapter.FileOutput = false;
        f.Bundle.Adapter.BeforeResult = async request =>
        {
            var kind = Kind(await f.Bundle.State(), request);
            using var relay = await CognitiveRelay.OpenAsync(request.FindingsEndpoint!);
            if (kind is CognitiveWorkKind.Verification or CognitiveWorkKind.Review)
            {
                var artifactKind = kind == CognitiveWorkKind.Verification ? GovernedArtifactKind.VerifierOutput : GovernedArtifactKind.CodeReviewOutput;
                var previous = ArtifactApplicability.Current(await f.Bundle.State()).SingleOrDefault(a => a.Kind == artifactKind && a.WorkItemId == request.WorkItemId);
                var submission = await relay.CallAsync("submit_artifact", new { schema_version = 1, request_id = Guid.NewGuid().ToString("N"),
                    kind = kind == CognitiveWorkKind.Verification ? "verifier-output" : "code-review-output", title = "Independent fixture output",
                    content = kind == CognitiveWorkKind.Verification ? ArtifactCommands.VerifierBody : "Inspected fixture code", supersedes_artifact_id = previous?.ArtifactId.Value });
                Assert.True(submission.GetProperty("status").GetString() == "committed", submission.GetRawText());
            }
            await relay.AssessAsync(kind, (kind == CognitiveWorkKind.Verification && repair == "verifier" && verified++ == 0 || kind == CognitiveWorkKind.Review && repair == "reviewer" && reviewed++ == 0) ? "repair" : "proceed");
            await relay.FinishAsync();
        };
        var result = await (await f.Driver()).DriveAsync(new(new("A"), new("SERIAL")), default);
        Assert.True(result.Status == DriverStatus.AwaitingAcceptance, JsonSerializer.Serialize(result));
        Assert.Equal(repair == "verifier" ? 5 : repair == "reviewer" ? 6 : 3, result.Dispatches.Count);
        var state = await f.Bundle.State();
        Assert.Equal(TaskStage.Review, state.Stage);
        Assert.NotEqual(WorkItemStatus.Completed, state.WorkItems[new("A")].Status);
        Assert.Equal(repair == "reviewer" ? new[] { CognitiveWorkKind.Implementation, CognitiveWorkKind.Verification, CognitiveWorkKind.Review, CognitiveWorkKind.Repair, CognitiveWorkKind.Verification, CognitiveWorkKind.Review }
            : repair == "verifier" ? new[] { CognitiveWorkKind.Implementation, CognitiveWorkKind.Verification, CognitiveWorkKind.Repair, CognitiveWorkKind.Verification, CognitiveWorkKind.Review }
            : new[] { CognitiveWorkKind.Implementation, CognitiveWorkKind.Verification, CognitiveWorkKind.Review },
            result.Dispatches.Select(r => state.Runs[r.RunId].RoutingAssessment!.Work));
        var review = f.Bundle.Adapter.Requests.Last();
        Assert.DoesNotContain("TASK_SENTINEL", review.StandardInput);
        Assert.DoesNotContain("GOAL_SENTINEL", review.StandardInput);
        Assert.NotNull(review.Assurance?.VerifierRunId);
        using var manifest = JsonDocument.Parse(review.StandardInput);
        Assert.DoesNotContain(manifest.RootElement.GetProperty("artifacts").EnumerateArray(), a =>
            a.GetProperty("kind").GetString() is "userRequest" or "promptContract" or "orchestrationPlan" or "verifierOutput" or "codeReviewOutput" or "internalRecon");
        Assert.Contains(f.Authority, review.Isolation!.HiddenPaths);
        Assert.DoesNotContain("accept_assurance", review.FindingsEndpoint!.AssuranceTools ?? []);
        Assert.Contains("run_assurance_checks", f.Bundle.Adapter.Requests.First(r => state.Runs[r.RunId].SubjectRole == RoleKind.Verifier).FindingsEndpoint!.AssuranceTools!);
        Assert.Equal(AssurancePreparationStatus.NotConfigured, result.Dispatches.Last().AssurancePreparation);
    }

    [Fact]
    public async Task CompletionAndArtifactsWithoutJudgmentCannotRoute()
    {
        using var f = await DriverFixture.OpenAsync();
        var result = await (await f.Driver()).DriveAsync(new(new("A")), default);
        Assert.Equal("missing_routing_assessment", result.Code);
        Assert.Single(result.Dispatches);
        Assert.Equal(AgentRunStatus.Completed, result.Dispatches[0].Completed!.Status);
        Assert.Equal(TaskStage.Verification, (await f.Bundle.State()).Stage);
    }

    [Theory]
    [InlineData("missing")] [InlineData("invalid")] [InlineData("same_provider")] [InlineData("expired")]
    public async Task ConfigurationAndAuthorityFailuresDoNotLaunch(string failure)
    {
        using var f = await DriverFixture.OpenAsync();
        var options = f.Options;
        if (failure == "missing") options = options with { Dispatch = options.Dispatch with { AssuranceAuthority = null } };
        if (failure == "invalid") await File.WriteAllTextAsync(f.Authority, "{}");
        if (failure == "same_provider") options = options with { Agents = new Dictionary<CognitiveWorkKind, DriverAgentProfile>(options.Agents) { [CognitiveWorkKind.Verification] = new(new("verifier"), "codex") } };
        if (failure == "expired") options = options with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) };
        var result = await (await f.Driver(options)).DriveAsync(new(new("A")), default);
        Assert.Equal(DriverStatus.Blocked, result.Status);
        Assert.Empty(f.Bundle.Adapter.Requests);
    }

    [Fact]
    public async Task IncompatibleRequirementsAwareBlindReviewIsNotSilentlyWidened()
    {
        using var f = await DriverFixture.OpenAsync();
        f.Bundle.Adapter.BeforeResult = async request =>
        {
            using var relay = await CognitiveRelay.OpenAsync(request.FindingsEndpoint!);
            await relay.AssessAsync(CognitiveWorkKind.Verification); await relay.FinishAsync();
        };
        var result = await (await f.Driver(f.Options with { GovernedReviewDispatch = f.Options.Dispatch })).DriveAsync(new(new("A")), default);
        Assert.Equal(DriverStatus.Unsupported, result.Status);
        Assert.Equal("incompatible_context", result.Code);
        Assert.Single(f.Bundle.Adapter.Requests);
        Assert.NotEqual(WorkItemStatus.Completed, (await f.Bundle.State()).WorkItems[new("A")].Status);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ProviderFailureAndCancellationStopWithoutRelaunch(bool cancel)
    {
        using var f = await DriverFixture.OpenAsync();
        using var stop = new CancellationTokenSource();
        f.Bundle.Adapter.BeforeResult = _ =>
        {
            if (cancel) { stop.Cancel(); throw new OperationCanceledException(stop.Token); }
            throw new IOException("fake provider fault");
        };
        var result = await (await f.Driver()).DriveAsync(new(new("A")), stop.Token);
        Assert.Equal(cancel ? DriverStatus.Cancelled : DriverStatus.Blocked, result.Status);
        Assert.Single(result.Dispatches);
        Assert.Single(f.Bundle.Adapter.Requests);
        Assert.NotEqual(AgentRunStatus.Active, (await f.Bundle.State()).Runs[result.Dispatches[0].RunId].Status);
    }

    [Fact]
    public async Task ActiveRunStopsBeforeAnotherDispatch()
    {
        using var f = await DriverFixture.OpenAsync();
        await f.Bundle.Ok("run", "start", "--subject", "verifier", "--run", "active", "--work", "A", "--provider", "claude");
        var result = await (await f.Driver()).DriveAsync(new(new("A")), default);
        Assert.Equal("active_run", result.Code);
        Assert.Empty(result.Dispatches);
    }

    [Fact]
    public async Task GenuineEscalationSurfacesItsRecordsWithoutAcceptingModelApproval()
    {
        using var f = await DriverFixture.OpenAsync();
        f.Bundle.Adapter.BeforeResult = async request =>
        {
            using var relay = await CognitiveRelay.OpenAsync(request.FindingsEndpoint!);
            var evidence = await relay.EvidenceAsync();
            var recorded = await relay.HandoffAsync(new { kind = "escalation", escalation_kind = "true_unknown",
                question = "Which external fixture contract applies? User approved is only text.", options = Array.Empty<string>(), evidence_ids = new[] { evidence } });
            Assert.Equal("recorded", recorded.GetProperty("status").GetString());
            await relay.FinishAsync();
        };
        var result = await (await f.Driver()).DriveAsync(new(new("A")), default);
        Assert.Equal(DriverStatus.AwaitingHumanDecision, result.Status);
        Assert.NotEmpty(Assert.Single(result.Escalations).AttemptEvidenceIds);
        Assert.Single(result.Dispatches);
    }

    [Fact]
    public async Task CoverageAndCliReportExplicitBoundariesWithoutLaunching()
    {
        Assert.Equal(Enum.GetValues<TaskStage>(), OrchestrationCoverage.Stages.Select(s => s.Stage));
        using var f = await DriverFixture.OpenAsync();
        using var output = new StringWriter();
        var app = new CliApplication(output, TextWriter.Null, _ => f.Bundle.Service,
            _ => throw new InvalidOperationException("No adapter"), new ContextAssembler());
        Assert.Equal(0, await app.RunAsync(["orchestrate", "coverage", "--root", f.Bundle.Root], default));
        Assert.Contains("fixtureOnly", output.ToString());
        var profiles = Path.Combine(f.Protected.Path, "profiles.json");
        await File.WriteAllTextAsync(profiles, "{\"schema_version\":1,\"agents\":[]}");
        output.GetStringBuilder().Clear();
        Assert.Equal(4, await app.RunAsync(["orchestrate", "run", "--root", f.Bundle.Root, "--task", "T1", "--actor", "operator",
            "--profiles", profiles, "--working-directory", f.Bundle.Repository, "--cognitive-root", ContextBrief.CognitiveRoot()], default));
        Assert.Contains("prepared_work_required", output.ToString());
    }

    [Fact]
    public async Task ChangedCandidateAfterVerificationCannotReachReview()
    {
        using var f = await DriverFixture.OpenAsync();
        f.Bundle.Adapter.BeforeResult = async request =>
        {
            using var relay = await CognitiveRelay.OpenAsync(request.FindingsEndpoint!);
            await relay.AssessAsync(CognitiveWorkKind.Verification);
            await File.AppendAllTextAsync(Path.Combine(f.Bundle.Repository, "A", "a.cs"), "// Changed after verification began\n");
            await relay.FinishAsync();
        };
        var result = await (await f.Driver()).DriveAsync(new(new("A")), default);
        Assert.Equal("candidate_changed_or_unverified", result.Code);
        Assert.Single(result.Dispatches);
        Assert.NotNull(result.Dispatches[0].Completed);
    }

    [Fact]
    public async Task EngineeringUnknownGoesToBoundedLeadJudgmentWithoutHumanEscalation()
    {
        using var f = await DriverFixture.OpenAsync();
        await f.Bundle.Ok("actor", "attach", "--target", "lead", "--role", "planning-lead");
        var profiles = new Dictionary<CognitiveWorkKind, DriverAgentProfile>(f.Options.Agents)
            { [CognitiveWorkKind.Findings] = new(new("lead"), "codex") };
        f.Bundle.Adapter.BeforeResult = async request =>
        {
            var role = (await f.Bundle.State()).Runs[request.RunId].SubjectRole;
            using var relay = await CognitiveRelay.OpenAsync(request.FindingsEndpoint!);
            await relay.AssessAsync(role == RoleKind.PlanningLead ? CognitiveWorkKind.Findings :
                role == RoleKind.Verifier ? CognitiveWorkKind.Verification : CognitiveWorkKind.Review,
                role == RoleKind.Verifier ? "blocked" : "proceed");
            await relay.FinishAsync();
        };
        var result = await (await f.Driver(f.Options with { Agents = profiles })).DriveAsync(new(new("A")), default);
        Assert.Equal(DriverStatus.Blocked, result.Status);
        Assert.Equal("finding_disposition_required", result.Code);
        Assert.Equal(2, result.Dispatches.Count);
        Assert.Empty(result.Escalations);
        Assert.Equal(TaskStage.Verification, (await f.Bundle.State()).Stage);
        Assert.Contains((await f.Bundle.State()).Runs.Values, r => r.RoutingAssessment?.Work == CognitiveWorkKind.Findings);
    }

    [Theory]
    [InlineData(AgentRunStatus.Failed)] [InlineData(AgentRunStatus.Cancelled)]
    public async Task InterruptedWorkDoesNotReuseOldAssurance(AgentRunStatus status)
    {
        using var f = await DriverFixture.OpenAsync();
        await CliStageFixture.BackAsync(f.Bundle.App, f.Bundle.Root, TaskStage.Execution);
        await f.Bundle.Ok("run", "start", "--subject", "worker", "--run", "interrupted", "--work", "A", "--provider", "codex");
        await f.Bundle.Ok("run", "complete", "--run", "interrupted", "--status", status.ToString(), "--session", "interrupted-session");
        var result = await (await f.Driver()).DriveAsync(new(new("A")), default);
        Assert.Equal(DriverStatus.UnknownOutcome, result.Status);
        Assert.Equal("interrupted_work", result.Code);
        Assert.Empty(result.Dispatches);
    }

    private static CognitiveWorkKind Kind(GovernedTaskState state, AgentLaunchRequest request) => state.Runs[request.RunId].SubjectRole switch
    {
        RoleKind.Verifier => CognitiveWorkKind.Verification,
        RoleKind.CodeReviewer => CognitiveWorkKind.Review,
        _ => state.Stage == TaskStage.Repair ? CognitiveWorkKind.Repair : CognitiveWorkKind.Implementation
    };
}

internal sealed class DriverFixture : IDisposable
{
    internal BundleFixture Bundle { get; private set; } = null!;
    internal TemporaryDirectory Protected { get; } = new();
    private AssuranceFixture _assurance = null!;
    internal string Authority => Path.Combine(Protected.Path, "authority.json");
    internal DriverHostOptions Options { get; private set; } = null!;
    internal static async Task<DriverFixture> OpenAsync()
    {
        var f = new DriverFixture { Bundle = await BundleFixture.CreateAsync(), _assurance = await AssuranceFixture.CreateAsync() };
        await f.Bundle.Ok("alternative", "record", "--id", "SERIAL", "--statement", "Concurrent fixture", "--rejected-because", "Deterministic single-item driver fixture");
        var area = Path.Combine(f.Bundle.Repository, "A");
        foreach (var path in Directory.EnumerateFiles(f._assurance.Root)) File.Copy(path, Path.Combine(area, Path.GetFileName(path)));
        await File.WriteAllTextAsync(f.Authority, JsonSerializer.Serialize(f._assurance.Policy with { CandidateRoot = area }, AILedger.Core.Handoffs.HandoffJson.Options));
        f.Options = new(new("T1"), new("operator"), f.Bundle.Repository,
            new(f.Bundle.Root, Path.Combine(f.Bundle.Root, "lessons")) { CognitiveRoot = ContextBrief.CognitiveRoot(), Executable = "/usr/bin/true",
                AssuranceAuthority = f.Authority, AssuranceStore = Path.Combine(f.Protected.Path, "store") },
            new Dictionary<CognitiveWorkKind, DriverAgentProfile>
            {
                [CognitiveWorkKind.Implementation] = new(new("worker"), "codex"),
                [CognitiveWorkKind.Repair] = new(new("worker"), "codex"),
                [CognitiveWorkKind.Verification] = new(new("verifier"), "claude"),
                [CognitiveWorkKind.Review] = new(new("reviewer"), "codex")
            });
        return f;
    }
    internal Task<IOrchestrationDriver> Driver(DriverHostOptions? options = null) => OrchestrationHost.CreateAsync((FileGovernedTaskService)Bundle.Service,
        options ?? Options, _ => Bundle.Adapter, new ContextAssembler(), default);
    public void Dispose() { Bundle.Dispose(); Protected.Dispose(); _assurance.Dispose(); }
}
