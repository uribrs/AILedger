using System.Text.Json;
using AILedger.Cli.Dispatch;
using AILedger.Cli.Orchestration;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Findings;
using AILedger.Tests.Support;
namespace AILedger.Tests.Orchestration;

public sealed class DriverLifecycleTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DiscoveryReconResearchAndPlanningUseLiveHandoffsThenStopAtScopePreparation(bool external)
    {
        using var ledger = new FindingsFixture();
        using var work = new TemporaryDirectory();
        await ledger.OpenAsync();
        await ledger.ExecuteAsync(new RecordArtifactCommand(ledger.Actor, null, "intake", new("request"), GovernedArtifactKind.UserRequest,
            "Trusted user request", "Investigate the prepared fixture", null, null, null));
        await ledger.ExecuteAsync(new AddClaimCommand(ledger.Actor, null, "claim", new("C1"), "Fixture claim", null));
        await AssignAsync(ledger);
        var kinds = new List<CognitiveWorkKind>();
        var researched = false;
        var adapter = new CognitiveAdapter(async request =>
        {
            var kind = Assignment(request);
            kinds.Add(kind);
            using var relay = await CognitiveRelay.OpenAsync(request.FindingsEndpoint!);
            if (kind == CognitiveWorkKind.Recon)
            {
                if (external && researched && (await ledger.StateAsync()).Claims[new("C1")].Status == ClaimStatus.Open)
                    await ResolveAsync(relay);
                await Recorded(relay, new { kind = "lesson_consultation", purpose = "recon", question = "Relevant prior lessons", tags = new[] { "routing" }, claims = Array.Empty<string>() });
                var state = await ledger.StateAsync();
                var recon = InternalReconDocuments.CreateTemplate(state) with
                { Assessments = [new("C1", external ? "external" : "internal")], Report = "Inspected fixture evidence" };
                var prior = ArtifactApplicability.Current(state).SingleOrDefault(a => a.Kind == GovernedArtifactKind.InternalRecon);
                var operation = new Dictionary<string, object> { ["kind"] = "governing_artifact", ["artifact_kind"] = "internal_recon",
                    ["title"] = "Current recon", ["markdown"] = JsonSerializer.Serialize(recon) };
                if (prior is not null) operation["supersedes"] = prior.ArtifactId.Value;
                await Recorded(relay, operation);
                var alternative = await relay.CallAsync("record_alternatives", new { schema_version = 1, request_id = Guid.NewGuid().ToString("N"),
                    alternatives = new[] { new { key = "a", statement = "Guess without inspection", rejection_rationale = "Actual evidence is available" } } });
                Assert.True(alternative.GetProperty("status").GetString() == "committed", alternative.GetRawText());
            }
            if (kind == CognitiveWorkKind.Research)
            {
                await Recorded(relay, new { kind = "lesson_consultation", purpose = "research", question = "External contract evidence", tags = new[] { "routing" }, claims = new[] { "C1" } });
                researched = true;
            }
            if (kind is CognitiveWorkKind.Design or CognitiveWorkKind.Scope)
                await Recorded(relay, new { kind = "governing_artifact", artifact_kind = kind == CognitiveWorkKind.Design ? "prompt_contract" : "orchestration_plan",
                    title = "Governed planning output", markdown = kind == CognitiveWorkKind.Design ? ArtifactCommands.Body : ArtifactCommands.PlanBody });
            await relay.AssessAsync(kind);
            await relay.FinishAsync();
        });
        var options = Options(ledger, work.Path);
        var result = await (await OrchestrationHost.CreateAsync(ledger.Service(), options, _ => adapter, new ContextAssembler(), default)).DriveAsync(new(), default);
        Assert.True(result.Status == DriverStatus.Unsupported && result.Code == "prepared_scope_required", result.Diagnostic);
        Assert.Equal(TaskStage.Ready, (await ledger.StateAsync()).Stage);
        Assert.Equal(external ? new[] { CognitiveWorkKind.Discovery, CognitiveWorkKind.Recon, CognitiveWorkKind.Research, CognitiveWorkKind.Recon, CognitiveWorkKind.Design, CognitiveWorkKind.Scope }
            : new[] { CognitiveWorkKind.Discovery, CognitiveWorkKind.Recon, CognitiveWorkKind.Design, CognitiveWorkKind.Scope }, kinds);
        Assert.All(result.Dispatches, d => Assert.NotEmpty(d.CognitiveReceipts!));
        Assert.Empty((await ledger.StateAsync()).WorkItems);
    }

    [Fact]
    public async Task CloseoutMarksLessonsAndArchivesOnlyAlreadyTerminalWork()
    {
        using var ledger = new FindingsFixture(); using var work = new TemporaryDirectory();
        await ledger.OpenAsync(); await AssignAsync(ledger);
        await ledger.ExecuteAsync(new RecordAlternativeCommand(ledger.Actor, null, "alternative", new("A1"), "Guessing", "Inspection is available", null));
        foreach (var stage in new[] { TaskStage.Research, TaskStage.Design, TaskStage.Scope, TaskStage.Ready, TaskStage.Execution, TaskStage.Verification, TaskStage.Review, TaskStage.Learn })
            await ledger.ExecuteAsync(new RequestStageTransitionCommand(ledger.Actor, null, "fixture-stage", stage, WithoutPrerequisitesReason: "Temporary closeout fixture; no product work"));
        var adapter = new CognitiveAdapter(async request =>
        {
            using var relay = await CognitiveRelay.OpenAsync(request.FindingsEndpoint!);
            await Recorded(relay, new { kind = "closeout_synthesis", title = "Fixture synthesis", markdown = "| finding | kind | severity | detection | occurrences | opportunity | repair | disposition | lesson |\n| --- | --- | --- | --- | --- | --- | --- | --- | --- |\n\n| path | decision | reason | evidence |\n| --- | --- | --- | --- |\n" });
            await Recorded(relay, new { kind = "lesson_mark", source_kind = "rejected_alternative", source_id = "A1", @class = "untested", repository = "fixture", tags = new[] { "routing" }, verify = "test", do_not = "Do not guess", lesson_actor = "verifier", verify_expects = "present" });
            await relay.AssessAsync(CognitiveWorkKind.Closeout); await relay.FinishAsync();
        });
        var result = await (await OrchestrationHost.CreateAsync(ledger.Service(), Options(ledger, work.Path), _ => adapter, new ContextAssembler(), default)).DriveAsync(new(), default);
        Assert.True(result.Status == DriverStatus.Archived, result.Diagnostic);
        var state = await ledger.StateAsync();
        Assert.Single(state.LessonMarks); Assert.Single(state.Lessons);
        Assert.Contains(state.Artifacts.Values, a => a.Kind == GovernedArtifactKind.CloseoutSynthesis && a.ProducerRunId is null);
    }

    [Theory]
    [InlineData("start")] [InlineData("completion")] [InlineData("handoff")]
    public async Task UnknownReceiptsStopAndArePreservedWithoutAnotherDispatch(string unknown)
    {
        using var ledger = new FindingsFixture(); using var work = new TemporaryDirectory();
        await ledger.OpenAsync(); await AssignAsync(ledger);
        await ledger.ExecuteAsync(new RecordArtifactCommand(ledger.Actor, null, "intake", new("request"), GovernedArtifactKind.UserRequest, "User", "Request", null, null, null));
        var options = Options(ledger, work.Path);
        var fixedDispatch = new UnknownDispatch(ledger.TaskId, unknown);
        var restricted = ledger.Service().BindRoutineOrchestration(new(await ledger.StateAsync(), ledger.Actor, DateTimeOffset.UtcNow.AddHours(1)));
        var driver = new OrchestrationDriver(new(restricted, options, new ContextAssembler()), options, fixedDispatch, fixedDispatch, fixedDispatch);
        var result = await driver.DriveAsync(new(), default);
        Assert.Equal(DriverStatus.UnknownOutcome, result.Status);
        Assert.Same(fixedDispatch.Result, Assert.Single(result.Dispatches));
        Assert.Equal(1, fixedDispatch.Calls);
        Assert.Equal(DriverStatus.Unsupported, (await driver.DriveAsync(new(), default)).Status);
        Assert.Equal(1, fixedDispatch.Calls);
    }

    private static async Task ResolveAsync(CognitiveRelay relay)
    {
        var recorded = await relay.CallAsync("record_findings", new { schema_version = 1, request_id = Guid.NewGuid().ToString("N"), findings = Array.Empty<object>(),
            evidence = new[] { new { key = "e", source_type = "fixture", citation = "fixture://external", summary = "External research settled claim", supports = new[] { new { claim_id = "C1" } }, refutes = Array.Empty<object>() } } });
        var id = recorded.GetProperty("receipt").GetProperty("evidence")[0].GetProperty("evidence_id").GetString();
        var resolved = await relay.CallAsync("record_claim_dispositions", new { schema_version = 1, request_id = Guid.NewGuid().ToString("N"),
            dispositions = new[] { new { key = "d", claim = new { claim_id = "C1" }, expected_status = "open", status = "validated", rationale = "Research evidence supports claim", evidence = new[] { new { evidence_id = id } } } } });
        Assert.True(resolved.GetProperty("status").GetString() == "committed", resolved.GetRawText());
    }
    private static async Task Recorded(CognitiveRelay relay, object operation)
    { var result = await relay.HandoffAsync(operation); Assert.True(result.GetProperty("status").GetString() == "recorded", result.GetRawText()); }
    private static async Task AssignAsync(FindingsFixture ledger)
    {
        foreach (var (name, role) in new[] { ("lead", RoleKind.PlanningLead), ("researcher", RoleKind.Researcher), ("worker", RoleKind.Worker), ("verifier", RoleKind.Verifier), ("reviewer", RoleKind.CodeReviewer) })
            await ledger.ExecuteAsync(new AssignRoleCommand(ledger.Actor, null, "staff", new(name), role,
                [Capability.BuildContext, Capability.RecordArtifact, Capability.AddEvidence, Capability.AddClaim, Capability.ProposeDecision, Capability.RecordAlternative, Capability.ResolveClaim, Capability.RaiseEscalation]));
    }
    private static DriverHostOptions Options(FindingsFixture ledger, string work) => new(ledger.TaskId, ledger.Actor, work,
        new(ledger.Root, Path.Combine(ledger.Root, "lessons")) { CognitiveRoot = ContextBrief.CognitiveRoot(), Executable = "/usr/bin/true" },
        Enum.GetValues<CognitiveWorkKind>().ToDictionary(k => k, k => new DriverAgentProfile(new(k == CognitiveWorkKind.Research ? "researcher" : "lead"), "codex")));
    private static CognitiveWorkKind Assignment(AgentLaunchRequest request)
    {
        using var manifest = JsonDocument.Parse(request.StandardInput);
        var text = manifest.RootElement.GetProperty("artifacts").EnumerateArray().Single(a => a.GetProperty("id").GetString() == "cognitive-assignment").GetProperty("content").GetString()!;
        // Fixture reads the host-authored constant for scripted behavior; production never parses this text.
        return Enum.GetValues<CognitiveWorkKind>().Single(k => text.StartsWith($"Host-assigned cognitive work: {k}."));
    }
    private sealed class CognitiveAdapter(Func<AgentLaunchRequest, Task> action) : IAgentAdapter
    {
        public string Provider => "codex";
        public Task<string> ProbeVersionAsync(string path, CancellationToken token) => Task.FromResult("fixture");
        public async Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken token)
        {
            await action(request); var now = DateTimeOffset.UtcNow;
            return new(request.RunId, request.Provider, "fixture-session", AgentRunStatus.Completed, now, now, 0, "fixture", [], "", "fixture", [], false, null);
        }
    }
    private sealed class UnknownDispatch(TaskId task, string unknown) : IProviderDispatchService
    {
        internal int Calls { get; private set; }
        internal ProviderDispatchResult? Result { get; private set; }
        public Task<ProviderDispatchResult> LaunchAsync(ProviderDispatchRequest request, CancellationToken token)
        {
            Calls++;
            Result = new(task, request.RunId, DispatchPhase.Completion, null, null, null,
                unknown == "start" ? LedgerRecordingStatus.Unknown : LedgerRecordingStatus.Recorded,
                unknown == "completion" ? LedgerRecordingStatus.Unknown : LedgerRecordingStatus.Recorded,
                new(ResultRetentionStatus.Retained, "fixture-receipt"), null, null, null, null, AssurancePreparationStatus.NotConfigured,
                unknown == "handoff", [new(HostHandoffStatus.Unknown, "original", [], Diagnostic: "Preserve original binding")]);
            return Task.FromResult(Result);
        }
    }
}
