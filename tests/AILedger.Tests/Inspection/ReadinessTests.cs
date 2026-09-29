using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Inspection;
using AILedger.Core.Findings;
using AILedger.Core.Alternatives;
using AILedger.Core.ClaimDispositions;
using AILedger.Tests.Findings;
using AILedger.Tests.ClaimDispositions;
using AILedger.Tests.Artifacts.Submission;

namespace AILedger.Tests.Inspection;

public sealed class ReadinessTests
{
    [Fact]
    public async Task ReadyFindingsRevalidatesAfterRevokedGrantAndNoInspectionCountsAsMutation()
    {
        using var f = new FindingsFixture(); await f.OpenAsync();
        var b = InspectionTests.Binding(f);
        var before = await InspectionTests.Files(f.Directory);
        var state = await f.StateAsync();
        var query = new ReadinessQuery(1, "record_findings", state.Version, Findings: FindingsFixture.Request());
        var ready = await f.Service().CheckReadinessAsync(b, query, default);
        Assert.Equal("ready", ready.Status); Assert.False(ready.IsGrant);
        Assert.Equal(before, await InspectionTests.Files(f.Directory));
        var result = await f.RecordAsync(binding: f.Binding with { AllowRecordFindings = false });
        Assert.Equal("authorization_denied", result.Error!.Code);
        Assert.Empty((await f.StateAsync()).Claims);
        Assert.Equal("blocked", (await f.Service().CheckReadinessAsync(b with { AllowRecordFindings = false }, query, default)).Status);
    }

    [Fact]
    public async Task RecordingReadinessUsesActualReferencesAndDetectsChangedKeyWithoutAppending()
    {
        using var f = new FindingsFixture(); await f.OpenAsync(); var b = InspectionTests.Binding(f);
        var invalid = new FindingsRequest(1, "bad", [], [new("e", "test", "source", "summary", [new(ClaimId: "absent")], [])]);
        var query = new ReadinessQuery(1, "record_findings", (await f.StateAsync()).Version, Findings: invalid);
        var read = await f.Service().CheckReadinessAsync(b, query, default);
        var execute = await f.RecordAsync(invalid);
        Assert.Equal("blocked", read.Status); Assert.Equal(execute.Error!.Message, read.Diagnostic!.Reason);
        Assert.Equal("evidence[0]", read.Diagnostic.Reference);
        var committed = await f.RecordAsync();
        query = query with { ExpectedVersion = committed.Receipt!.LedgerVersion, Findings = FindingsFixture.Request() };
        Assert.Equal("ready", (await f.Service().CheckReadinessAsync(b, query, default)).Status);
        var changed = query with { Findings = FindingsFixture.Claims("request-1") };
        Assert.Equal("idempotency_conflict", (await f.Service().CheckReadinessAsync(b, changed, default)).Diagnostic!.Code);
    }

    [Fact]
    public async Task AlternativesReadyThenLifecycleChangeRefusesAtExecution()
    {
        using var f = new FindingsFixture(); await f.OpenAsync(); var b = InspectionTests.Binding(f);
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "worker", new("author"), RoleKind.Researcher,
            [Capability.BuildContext, Capability.RecordAlternative]));
        b = b with { ActorId = new("author") };
        var request = new AlternativesRequest(1, "alt", [new("a", "Considered", "Rejected")]);
        var query = new ReadinessQuery(1, "record_alternatives", (await f.StateAsync()).Version, Alternatives: request);
        Assert.Equal("ready", (await f.Service().CheckReadinessAsync(b, query, default)).Status);
        // Explicit capability revocation between observation and mutation.
        var caps = (await f.StateAsync()).Roles[b.ActorId].Capabilities.Where(c => c != Capability.RecordAlternative).ToArray();
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "revoke", b.ActorId, RoleKind.Researcher, caps));
        var recorded = await f.Service().RecordAlternativesAsync(new(f.TaskId, b.ActorId, null, "stable", AllowRecordAlternatives: true, AllowRunless: true), request, default);
        Assert.Equal("kernel_refused", recorded.Error!.Code);
        Assert.Equal("stale_snapshot", (await f.Service().CheckReadinessAsync(b, query, default)).Diagnostic!.Code);
        Assert.Empty((await f.StateAsync()).Alternatives);
    }

    [Fact]
    public async Task DispositionsCheckDirectionAndExpectedStatusAndExecutionRevalidates()
    {
        using var f = new ClaimDispositionsFixture(); await f.OpenAsync();
        var b = new InspectionBinding(f.TaskId, f.Actor, null, "stable", AllowInspect: true, AllowRunless: true, AllowRecordClaimDispositions: true);
        var query = new ReadinessQuery(1, "record_claim_dispositions", (await f.StateAsync()).Version, Dispositions: ClaimDispositionsFixture.Single());
        var bad = query with { Dispositions = query.Dispositions! with { Dispositions = [query.Dispositions!.Dispositions[0] with { Evidence = [new("E3")] }] } };
        var read = await f.Service().CheckReadinessAsync(b, bad, default);
        var rejected = await f.RecordAsync(bad.Dispositions);
        Assert.Equal(rejected.Error!.Message, read.Diagnostic!.Reason);
        Assert.Equal("ready", (await f.Service().CheckReadinessAsync(b, query, default)).Status);
        await f.ExecuteAsync(new ResolveClaimCommand(f.Actor, null, "other", new("C1"), ClaimStatus.Validated, [new("E1")]));
        Assert.Equal("state_conflict", (await f.RecordAsync(query.Dispositions)).Error!.Code);
        Assert.Equal("stale_snapshot", (await f.Service().CheckReadinessAsync(b, query, default)).Diagnostic!.Code);
    }

    [Fact]
    public async Task ArtifactReadinessHonorsProducerContentAndSupersessionRules()
    {
        using var f = new ArtifactSubmissionFixture(); await f.OpenAsync();
        var b = new InspectionBinding(f.TaskId, f.Actor, new("RV"), "RV", AllowInspect: true, AllowSubmitArtifact: true);
        var query = new ReadinessQuery(1, "submit_artifact", (await f.StateAsync()).Version, Artifact: ArtifactSubmissionFixture.Request());
        var before = await InspectionTests.Files(f.Directory);
        Assert.Equal("ready", (await f.Service().CheckReadinessAsync(b, query, default)).Status);
        Assert.Equal(before, await InspectionTests.Files(f.Directory));
        Assert.Equal("blocked", (await f.Service().CheckReadinessAsync(b, query with { Artifact = query.Artifact! with { ExpectedContentSha256 = new string('0', 64) } }, default)).Status);
        Assert.Null((await f.SubmitAsync()).Error);
        var version = (await f.StateAsync()).Version;
        var replacement = query with { ExpectedVersion = version, Artifact = query.Artifact! with { RequestId = "next" } };
        var check = await f.Service().CheckReadinessAsync(b, replacement, default);
        Assert.Equal("blocked", check.Status);
        Assert.Equal((await f.SubmitAsync(replacement.Artifact)).Error!.Message, check.Diagnostic!.Reason);
    }

    [Fact]
    public async Task PreparationChecksRealBriefFreshnessAndDependencyWithoutRefreshingIt()
    {
        using var f = new FindingsFixture(); await f.OpenAsync(); var b = InspectionTests.Binding(f);
        await ReadyStage(f);
        var skill = new ContextArtifact(ContextArtifactKind.Skill, "workflow-coordinator", "original", []);
        b = b with { CognitiveArtifacts = [skill] };
        var work = new WorkPreparation("W1", "Work", null, [], [Path.Combine(f.Root, "source")]);
        var query = new ReadinessQuery(1, "prepare_work", (await f.StateAsync()).Version, Work: work);
        Assert.Contains("has not built", (await f.Service().CheckReadinessAsync(b, query, default)).Diagnostic!.Reason);
        Assert.Empty((await f.StateAsync()).ContextBuilds);
        await f.ExecuteAsync(new RecordContextBuiltCommand(f.Actor, null, "brief", null, ContextSkills.From([skill])));
        query = query with { ExpectedVersion = (await f.StateAsync()).Version };
        Assert.Equal("ready", (await f.Service().CheckReadinessAsync(b, query, default)).Status);
        var stale = b with { CognitiveArtifacts = [skill with { Content = "changed" }] };
        Assert.Contains("have changed", (await f.Service().CheckReadinessAsync(stale, query, default)).Diagnostic!.Reason);
        var absent = await f.Service().CheckReadinessAsync(b with { CognitiveArtifacts = null }, query, default);
        Assert.NotEqual("ready", absent.Status); Assert.Contains("cannot be read", absent.Diagnostic!.Reason);
        var dependent = await f.Service().CheckReadinessAsync(b, query with { Work = work with { Claims = ["missing"] } }, default);
        Assert.Equal("blocked", dependent.Status);
        Assert.Empty((await f.StateAsync()).WorkItems);
    }

    [Fact]
    public async Task WorkAndStageChecksUseActualLifecycleReasons()
    {
        using var f = new FindingsFixture(); await f.OpenAsync(); var b = InspectionTests.Binding(f);
        await ReadyStage(f);
        await f.ExecuteAsync(new AddWorkItemCommand(f.Actor, null, "work", new("W1"), "Work", null, [], [Path.Combine(f.Root, "src")], WithoutBriefReason: "Disposable fixture"));
        var query = new ReadinessQuery(1, "complete_work", (await f.StateAsync()).Version, WorkId: "W1");
        var result = await f.Service().CheckReadinessAsync(b, query, default);
        var failure = await Assert.ThrowsAsync<GovernanceException>(() => f.ExecuteAsync(new CompleteWorkItemCommand(f.Actor, null, "complete", new("W1"))));
        Assert.Equal(failure.Message, result.Diagnostic!.Reason);
        using var fresh = new FindingsFixture(); await fresh.OpenAsync();
        var stage = new ReadinessQuery(1, "transition_stage", (await fresh.StateAsync()).Version, Transition: new(TaskStage.Research));
        var stageRead = await fresh.Service().CheckReadinessAsync(InspectionTests.Binding(fresh), stage, default);
        var stageError = await Assert.ThrowsAsync<GovernanceException>(() => fresh.ExecuteAsync(new RequestStageTransitionCommand(fresh.Actor, null, "stage", TaskStage.Research)));
        Assert.Equal(stageError.Message, stageRead.Diagnostic!.Reason);
        await fresh.RecordAsync(FindingsFixture.Claims());
        stage = stage with { ExpectedVersion = (await fresh.StateAsync()).Version };
        Assert.Equal("ready", (await fresh.Service().CheckReadinessAsync(InspectionTests.Binding(fresh), stage, default)).Status);
        Assert.Equal(TaskStage.Discovery, (await fresh.StateAsync()).Stage);
    }

    [Theory]
    [InlineData("dispatch_run")] [InlineData("complete_run")] [InlineData("accept_decision")] [InlineData("arbitrary_command")]
    public async Task UnsupportedIsExplicit(string action)
    {
        using var f = new FindingsFixture(); await f.OpenAsync();
        var result = await f.Service().CheckReadinessAsync(InspectionTests.Binding(f), new(1, action, (await f.StateAsync()).Version), default);
        Assert.Equal("unsupported", result.Status); Assert.False(result.IsGrant);
    }

    [Fact]
    public async Task CapacityAndUnreadableHistoryDoNotProduceReady()
    {
        using var f = new FindingsFixture(); await f.OpenAsync(); var b = InspectionTests.Binding(f);
        var version = (await f.StateAsync()).Version;
        var query = new ReadinessQuery(1, "record_findings", version, Findings: FindingsFixture.Request());
        Assert.Equal("capacity_exceeded", (await f.Service(cap: (int)version).CheckReadinessAsync(b, query, default)).Diagnostic!.Code);
        var bytes = new FileInfo(f.EventsPath).Length;
        Assert.Equal("capacity_exceeded", (await f.Service(bytes: bytes + 1).CheckReadinessAsync(b, query, default)).Diagnostic!.Code);
        await File.WriteAllTextAsync(f.EventsPath, "corrupt\n");
        var result = await f.Service().CheckReadinessAsync(b, query, default);
        Assert.Equal("unknown", result.Status); Assert.Null(result.Snapshot);
    }
    private static async Task ReadyStage(FindingsFixture f)
    {
        foreach (var stage in new[] { TaskStage.Research, TaskStage.Design, TaskStage.Scope, TaskStage.Ready })
            await f.ExecuteAsync(new RequestStageTransitionCommand(f.Actor, null, "stage", stage, WithoutPrerequisitesReason: "Disposable readiness fixture"));
    }

}
