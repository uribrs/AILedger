using System.Text.Json;
using AILedger.Cli.Cognitive;
using AILedger.Cli.Dispatch;
using AILedger.Cli.Findings;
using AILedger.Core.Application;
using AILedger.Core.Authority;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Findings;
using AILedger.Tests.Support;
namespace AILedger.Tests.Orchestration;

public sealed class CognitiveHandoffTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReconConsultationSupportsLargeClaimSetsWithOrWithoutExplicitSelection(bool selectClaims)
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        var ids = Enumerable.Range(1, 45).Select(i => "claim-" + i).ToArray();
        foreach (var id in ids)
            await f.Ledger.ExecuteAsync(new AddClaimCommand(f.Lead, null, f.Run.Value,
                new(id), "Observation " + id, "Recon must classify it"));
        await using var session = await f.Session();
        using var relay = await CognitiveRelay.OpenAsync(session.Endpoint);
        var result = await relay.HandoffAsync(new { kind = "lesson_consultation", purpose = "recon",
            question = "Lessons for the current task?", tags = new[] { "routing" }, claims = selectClaims ? ids : [] });
        AssertRecorded(result);
        var state = await f.Ledger.StateAsync();
        var consultation = Assert.Single(state.LessonConsultations);
        Assert.Equal(selectClaims ? 45 : 0, consultation.ClaimIds.Count);
        Assert.Equal(InternalReconDocuments.ComputeClaimSetHash(state), consultation.ClaimSetHash);
        await relay.FinishAsync();
    }

    [Fact]
    public void ConsultationClaimCapacityMatchesDiscoveryAndRetainsBoundedInput()
    {
        var tool = JsonSerializer.SerializeToElement(CognitiveHandoffTools.Describe(["recon"]));
        var operation = tool.GetProperty("inputSchema").GetProperty("properties").GetProperty("operation")
            .GetProperty("oneOf").EnumerateArray().Single(o => o.GetProperty("properties")
                .GetProperty("kind").GetProperty("const").GetString() == "lesson_consultation");
        var maximum = operation.GetProperty("properties").GetProperty("claims").GetProperty("maxItems").GetInt32();
        var body = JsonSerializer.SerializeToElement(new { request_id = "large-consultation", operation = new
        {
            kind = "lesson_consultation", purpose = "recon", question = "Lessons?", tags = new[] { "routing" },
            claims = Enumerable.Range(0, maximum + 1).Select(i => "C" + i).ToArray()
        } });
        Assert.Throws<JsonException>(() => CognitiveHandoffParser.Parse(body));
    }

    [Theory]
    [InlineData(TaskStage.Research, "recon")]
    [InlineData(TaskStage.Design, "recon,reconsideration")]
    [InlineData(TaskStage.Scope, "")]
    public async Task LeadToolDiscoveryListsOnlyStageEligibleConsultationPurposes(TaskStage stage, string expected)
    {
        using var f = await CognitiveFixture.OpenAsync(stage);
        await using var session = await f.Session();
        using var relay = await CognitiveRelay.OpenAsync(session.Endpoint);
        var purposes = ConsultationPurposes(await relay.ToolsAsync());
        Assert.Equal(expected, string.Join(",", purposes));
        await relay.FinishAsync();
    }

    [Fact]
    public async Task ResearcherToolDiscoveryOffersResearchOnly()
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        var actor = new ActorId("researcher");
        var run = new RunId("research");
        await f.Ledger.ExecuteAsync(new AssignRoleCommand(f.Ledger.Actor, null, "setup", actor,
            RoleKind.Researcher, [Capability.BuildContext, Capability.AddClaim, Capability.AddEvidence]));
        await f.Ledger.ExecuteAsync(new StartRunCommand(f.Ledger.Actor, null, "setup", run, null,
            "codex", null, SubjectActorId: actor));
        var state = await f.Ledger.StateAsync();
        await using var session = ProviderFindingsSession.Start(f.Ledger.Service(), f.Ledger.Root,
            f.Ledger.TaskId, actor, run, null, "codex", state.Roles[actor], DateTimeOffset.UtcNow.AddMinutes(5));
        using var relay = await CognitiveRelay.OpenAsync(session.Endpoint);
        Assert.Equal(new[] { "research" }, ConsultationPurposes(await relay.ToolsAsync()));
        await relay.FinishAsync();
    }

    private static string[] ConsultationPurposes(JsonElement tools)
    {
        var tool = tools.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "cognitive_handoff");
        var operations = tool.GetProperty("inputSchema").GetProperty("properties").GetProperty("operation").GetProperty("oneOf");
        foreach (var operation in operations.EnumerateArray())
        {
            var properties = operation.GetProperty("properties");
            if (properties.GetProperty("kind").GetProperty("const").GetString() == "lesson_consultation")
                return properties.GetProperty("purpose").GetProperty("enum").EnumerateArray().Select(p => p.GetString()!).ToArray();
        }
        return [];
    }

    [Fact]
    public async Task ReconTemplateIsFreshReadOnlyAndSupportsProducerOwnedFiling()
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        await using var session = await f.Session();
        using var relay = await CognitiveRelay.OpenAsync(session.Endpoint);
        var version = (await f.Ledger.StateAsync()).Version;
        var first = await relay.HandoffAsync(new { kind = "recon_template" }, "template");
        Assert.Equal("observed", first.GetProperty("status").GetString());
        Assert.Empty(first.GetProperty("eventIds").EnumerateArray());
        Assert.Equal(version, (await f.Ledger.StateAsync()).Version);
        var oldHash = first.GetProperty("reconTemplate").GetProperty("claimSetHash").GetString();

        await f.Ledger.ExecuteAsync(new AddClaimCommand(f.Lead, null, f.Run.Value,
            new("new-claim"), "New source observation", "Recon must classify it"));
        var currentVersion = (await f.Ledger.StateAsync()).Version;
        var current = await relay.HandoffAsync(new { kind = "recon_template" }, "template");
        Assert.Equal(currentVersion, (await f.Ledger.StateAsync()).Version);
        var template = current.GetProperty("reconTemplate");
        Assert.NotEqual(oldHash, template.GetProperty("claimSetHash").GetString());
        var row = Assert.Single(template.GetProperty("assessments").EnumerateArray());
        Assert.Equal("new-claim", row.GetProperty("claimId").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("domain").ValueKind);
        AssertRecorded(await relay.HandoffAsync(new { kind = "lesson_consultation", purpose = "recon",
            question = "Current lessons?", tags = new[] { "routing" }, claims = Array.Empty<string>() }));
        var document = JsonSerializer.Deserialize<InternalReconDocument>(template)! with
        {
            Assessments = [new("new-claim", "internal")], Report = "Source inspected by this producer."
        };
        AssertRecorded(await relay.HandoffAsync(new { kind = "governing_artifact", artifact_kind = "internal_recon",
            title = "Complete recon", markdown = JsonSerializer.Serialize(document) }));
        Assert.Equal(f.Run, Assert.Single((await f.Ledger.StateAsync()).Artifacts.Values).ProducerRunId);
        await relay.FinishAsync();
    }

    [Theory]
    [InlineData("stage")] [InlineData("closed")] [InlineData("role")] [InlineData("spoof")]
    public async Task ReconTemplateRefusesIneligibleOrSpoofedRequestsWithoutMutation(string reason)
    {
        using var f = await CognitiveFixture.OpenAsync(reason == "stage" ? TaskStage.Scope : TaskStage.Research);
        await using var session = await f.Session();
        using var relay = await CognitiveRelay.OpenAsync(session.Endpoint);
        if (reason == "closed")
            await f.Ledger.ExecuteAsync(new CompleteRunCommand(f.Ledger.Actor, null, "end", f.Run, AgentRunStatus.Completed, "fixture"));
        if (reason == "role")
            await f.Ledger.ExecuteAsync(new AssignRoleCommand(f.Ledger.Actor, null, "revoke", f.Lead, RoleKind.CodeReviewer, [Capability.BuildContext]));
        var before = (await f.Ledger.StateAsync()).Version;
        object operation = reason == "spoof" ? new { kind = "recon_template", task_id = "other" } : new { kind = "recon_template" };
        Assert.Equal("refused", (await relay.HandoffAsync(operation)).GetProperty("status").GetString());
        Assert.Equal(before, (await f.Ledger.StateAsync()).Version);
        await relay.FinishAsync();
    }

    [Theory]
    [InlineData("internal_recon", TaskStage.Research)]
    [InlineData("prompt_contract", TaskStage.Design)]
    [InlineData("orchestration_plan", TaskStage.Scope)]
    public async Task LiveProducerFilesGovernedArtifactAndExactRetryReturnsSameReceipt(string kind, TaskStage stage)
    {
        using var f = await CognitiveFixture.OpenAsync(stage);
        await using var session = await f.Session();
        using var relay = await CognitiveRelay.OpenAsync(session.Endpoint);
        if (kind == "internal_recon")
            AssertRecorded(await relay.HandoffAsync(new { kind = "lesson_consultation", purpose = "recon", question = "Relevant lessons?", tags = new[] { "routing" }, claims = Array.Empty<string>() }));
        var content = kind == "internal_recon" ? JsonSerializer.Serialize(InternalReconDocuments.CreateTemplate(await f.Ledger.StateAsync()) with { Report = "Inspected complete fixture" })
            : kind == "orchestration_plan" ? ArtifactCommands.PlanBody : ArtifactCommands.Body;
        var operation = new { kind = "governing_artifact", artifact_kind = kind, title = "Fixture artifact", markdown = content };
        var first = await relay.HandoffAsync(operation, "artifact-key"); AssertRecorded(first);
        var version = (await f.Ledger.StateAsync()).Version;
        var again = await relay.HandoffAsync(operation, "artifact-key");
        Assert.Equal(first.GetRawText(), again.GetRawText());
        Assert.Equal(version, (await f.Ledger.StateAsync()).Version);
        var changed = await relay.HandoffAsync(operation with { title = "Changed" }, "artifact-key");
        Assert.Equal("refused", changed.GetProperty("status").GetString());
        var state = await f.Ledger.StateAsync();
        var artifact = Assert.Single(state.Artifacts.Values);
        Assert.Equal(f.Run, artifact.ProducerRunId);
        Assert.Equal(AgentRunStatus.Active, state.Runs[f.Run].Status);
        Assert.StartsWith("host-", artifact.ArtifactId.Value);
        await relay.FinishAsync();
    }

    [Theory]
    [InlineData("actor_id")] [InlineData("run_id")] [InlineData("approved")] [InlineData("acceptance")]
    public async Task IdentityAndApprovalSpoofingCannotMutate(string field)
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        await using var session = await f.Session();
        using var relay = await CognitiveRelay.OpenAsync(session.Endpoint);
        var version = (await f.Ledger.StateAsync()).Version;
        var operation = new Dictionary<string, object> { ["kind"] = "decision_proposal", ["statement"] = "Choice", ["rationale"] = "Reason", ["claims"] = Array.Empty<string>(), [field] = "operator-approved" };
        Assert.Equal("refused", (await relay.HandoffAsync(operation)).GetProperty("status").GetString());
        Assert.Equal("unsupported", (await relay.HandoffAsync(new { kind = "accept_assurance", approved = true })).GetProperty("status").GetString());
        Assert.Equal(version, (await f.Ledger.StateAsync()).Version);
        await relay.FinishAsync();
    }

    [Fact]
    public async Task LessonConsultationUsesTrustedStoreAndFiltersAudience()
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        using var lessons = new TemporaryDirectory();
        var store = new FileLessonStore(lessons.Path);
        await store.PublishAsync([Lesson("visible", RoleKind.PlanningLead), Lesson("hidden", RoleKind.CodeReviewer)], default);
        var service = new FileGovernedTaskService(f.Ledger.Root, new CommandHandler(), new TaskReducer(), lessonStore: store);
        await using var session = await f.Session(service);
        using var relay = await CognitiveRelay.OpenAsync(session.Endpoint);
        var result = await relay.HandoffAsync(new { kind = "lesson_consultation", purpose = "recon", question = "What applies?", tags = new[] { "routing" }, claims = Array.Empty<string>() });
        AssertRecorded(result);
        var served = Assert.Single(result.GetProperty("lessons").EnumerateArray());
        Assert.Contains("visible", served.GetProperty("id").GetString());
        Assert.DoesNotContain("hidden", result.GetRawText());
        var state = await f.Ledger.StateAsync();
        Assert.Single(state.LessonConsultations);
        Assert.Single(state.Lessons);
        await relay.FinishAsync();
    }

    [Theory]
    [InlineData("expiry")] [InlineData("role")] [InlineData("closed")]
    public async Task SessionExpiryRevocationAndClosedProducerRefuseBeforeMutation(string cause)
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        await using var session = await f.Session(expired: cause == "expiry");
        using var relay = await CognitiveRelay.OpenAsync(session.Endpoint);
        if (cause == "role") await f.Ledger.ExecuteAsync(new AssignRoleCommand(f.Ledger.Actor, null, "revoke", f.Lead, RoleKind.PlanningLead, [Capability.BuildContext]));
        if (cause == "closed") await f.Ledger.ExecuteAsync(new CompleteRunCommand(f.Ledger.Actor, null, "end", f.Run, AgentRunStatus.Completed, "fixture"));
        var version = (await f.Ledger.StateAsync()).Version;
        var result = await relay.HandoffAsync(new { kind = "decision_proposal", statement = "User approved", rationale = "Text cannot grant authority", claims = Array.Empty<string>() });
        Assert.Equal("refused", result.GetProperty("status").GetString());
        Assert.Equal(version, (await f.Ledger.StateAsync()).Version);
        await relay.FinishAsync();
    }

    [Fact]
    public async Task RoutingAssessmentRequiresOwnEvidenceAndCannotPretendToAccept()
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research, foreignEvidence: true);
        await using var session = await f.Session();
        using var relay = await CognitiveRelay.OpenAsync(session.Endpoint);
        var invalid = await relay.HandoffAsync(new { kind = "routing_assessment", work = "recon", disposition = "proceed", rationale = "Approval", evidence_ids = new[] { "foreign" } });
        Assert.Equal("refused", invalid.GetProperty("status").GetString());
        await relay.AssessAsync(CognitiveWorkKind.Recon);
        Assert.NotNull((await f.Ledger.StateAsync()).Runs[f.Run].RoutingAssessment);
        await relay.FinishAsync();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task UncertainWriteDoesNotExecuteAgainUnderTheSameSession(bool committed)
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        var state = await f.Ledger.StateAsync();
        var bound = f.Ledger.Service().BindAgentSession(new(f.Ledger.TaskId, state.Roles[f.Lead], f.Run, "codex", DateTimeOffset.UtcNow.AddHours(1)));
        var fault = new UncertainService(bound, committed);
        var session = new CognitiveHandoffSession(fault, new(f.Ledger.TaskId, f.Lead, f.Run, f.Run.Value));
        var request = new CognitiveHostHandoff("same-key", new DecisionProposalHandoff("Proposed fixture", "Engineering rationale", []));
        var first = await session.InvokeAsync(request, default);
        Assert.Equal(HostHandoffStatus.Unknown, first.Status);
        Assert.Equal(first, await session.InvokeAsync(request, default));
        Assert.Equal(HostHandoffStatus.Unknown, (await session.InvokeAsync(request with { RequestId = "new-key" }, default)).Status);
        Assert.Equal(1, fault.Calls);
        Assert.NotNull(first.RecordId);
        Assert.True(session.HasUnknown);
        Assert.Equal(committed ? 1 : 0, (await f.Ledger.StateAsync()).Decisions.Count);
    }

    private static void AssertRecorded(JsonElement result) => Assert.True(result.GetProperty("status").GetString() == "recorded", result.GetRawText());
    private static Lesson Lesson(string id, RoleKind audience) => new(new("earlier:validatedclaim:" + id), new("earlier"), LessonSourceKind.ValidatedClaim,
        id, id + " statement", "Validated", ["fixture://lesson"], new(new("operator"), DateTimeOffset.UtcNow, "stage.archive"),
        Class: LessonClass.Untested, Repo: "fixture", Tags: ["routing"], Verify: "test", DoNot: "Do not guess", Actor: LessonActor.Verifier, Audience: [audience], VerifyExpects: VerifyExpectation.Present);

    private sealed class UncertainService(IGovernedTaskService inner, bool commit) : IGovernedTaskService
    {
        internal int Calls { get; private set; }
        public async Task<CommandOutcome> ExecuteAsync(TaskId task, LedgerCommand command, CancellationToken token)
        { Calls++; if (commit) await inner.ExecuteAsync(task, command, token); throw new IOException("Lost acknowledgment"); }
        public Task<GovernedTaskState?> GetStateAsync(TaskId task, CancellationToken token) => inner.GetStateAsync(task, token);
        public IAsyncEnumerable<LedgerEvent> GetHistoryAsync(TaskId task, CancellationToken token) => inner.GetHistoryAsync(task, token);
    }
}

internal sealed class CognitiveFixture : IDisposable
{
    internal FindingsFixture Ledger { get; } = new();
    internal ActorId Lead { get; } = new("lead");
    internal RunId Run { get; } = new("live");
    internal static async Task<CognitiveFixture> OpenAsync(TaskStage stage, bool foreignEvidence = false)
    {
        var f = new CognitiveFixture();
        await f.Ledger.OpenAsync();
        await ContextBrief.RecordAsync(f.Ledger.Service(), f.Ledger.TaskId.Value);
        await f.Ledger.ExecuteAsync(new AssignRoleCommand(f.Ledger.Actor, null, "lead", f.Lead, RoleKind.PlanningLead,
            [Capability.BuildContext, Capability.RecordArtifact, Capability.AddEvidence, Capability.AddClaim, Capability.ProposeDecision, Capability.RecordAlternative, Capability.ResolveClaim, Capability.RaiseEscalation]));
        foreach (var target in new[] { TaskStage.Research, TaskStage.Design, TaskStage.Scope }.TakeWhile(s => s <= stage))
            await f.Ledger.ExecuteAsync(new RequestStageTransitionCommand(f.Ledger.Actor, null, "prepare", target, WithoutPrerequisitesReason: "Temporary handoff fixture"));
        if (foreignEvidence) await f.Ledger.ExecuteAsync(new AddEvidenceCommand(f.Ledger.Actor, null, "other-evidence", new("foreign"), "fixture", "fixture://other", "Other actor evidence", [], []));
        await f.Ledger.ExecuteAsync(new StartRunCommand(f.Ledger.Actor, null, "launch", f.Run, null, "codex", null, SubjectActorId: f.Lead));
        return f;
    }
    internal async Task<ProviderFindingsSession> Session(FileGovernedTaskService? service = null, bool expired = false)
    {
        var state = await Ledger.StateAsync();
        return ProviderFindingsSession.Start(service ?? Ledger.Service(), Ledger.Root, Ledger.TaskId, Lead, Run, null, "codex",
            state.Roles[Lead], DateTimeOffset.UtcNow.AddHours(expired ? -1 : 1), ContextBrief.CognitiveRoot());
    }
    public void Dispose() => Ledger.Dispose();
}
