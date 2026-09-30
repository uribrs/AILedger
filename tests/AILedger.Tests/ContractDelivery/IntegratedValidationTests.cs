using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Storage;
using AILedger.Tests.Findings;
using AILedger.Tests.Inspection;
using AILedger.Tests.Support;
using Xunit.Abstractions;

namespace AILedger.Tests.ContractDelivery;

[Collection(StandardInput.Collection)]
public sealed class IntegratedValidationTests(ITestOutputHelper output)
{
    private const string Candidate = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Plan = "| id | name | failure mode | causal path and impact | planned handling | source |\n" +
        "| --- | --- | --- | --- | --- | --- |\n" +
        "| R1 | PLAN_SECRET | wrong output | input drift changes output | test: fixture check | fixture:1 |\n";

    [Theory]
    [InlineData("codex", "claude")]
    [InlineData("claude", "codex")]
    public async Task ExternalResearchRevisionAndEveryLaunchRetainDeliveredContracts(string workerProvider, string verifierProvider)
    {
        using var f = new DeliveryFixture();
        await f.InitializeAsync();
        var initial = await f.Context();
        var preceding = initial.GetProperty("nextActionContract");
        var expectedRun = "RN";
        var trace = new List<string> { "response:initial" };
        f.Adapter.OnProbe = async () =>
        {
            Assert.StartsWith("response:", trace[^1]);
            Assert.False((await f.Ledger.StateAsync()).Runs.ContainsKey(new RunId(expectedRun)));
            Assert.Equal("observed", preceding.GetProperty("deliveryStatus").GetString());
            Assert.NotEmpty(preceding.GetProperty("actions").EnumerateArray());
            trace.Add("probe:" + expectedRun);
        };
        f.Adapter.BeforeAct = async request =>
        {
            Assert.Equal("probe:" + request.RunId.Value, trace[^1]);
            var active = await f.Ledger.StateAsync();
            var manifest = JsonSerializer.Deserialize<ContextManifest>(request.StandardInput, LedgerJson.CreateOptions())!;
            Assert.Equal(AgentRunStatus.Active, active.Runs[request.RunId].Status);
            Assert.Equal(active.Version, manifest.TaskVersion);
            Assert.True(manifest.TaskVersion > preceding.GetProperty("observedTaskVersion").GetInt64());
            Assert.Equal(active.Roles[request.ActorId].Role, manifest.Role);
            Assert.Contains(manifest.Artifacts, a => a.Kind == ContextArtifactKind.Rules);
            Assert.Contains(manifest.Artifacts, a => a.Kind == ContextArtifactKind.Skill);
            if (manifest.Role is RoleKind.PlanningLead)
                Assert.Equal(manifest.TaskVersion, manifest.NextActionContract!.ObservedTaskVersion);
            else
                Assert.Null(manifest.NextActionContract);
            trace.Add("execute:" + request.RunId.Value);
        };

        Assert.Contains("InternalRecon strict JSON", preceding.GetRawText());
        await f.ReconAsync(workerProvider, "external");
        preceding = await Observe(f, "recon", trace);
        Assert.Contains("open external claim", preceding.GetRawText());
        AssertPlanShape(preceding);

        expectedRun = "RS";
        f.Adapter.Act = async request =>
        {
            await f.Ledger.ExecuteAsync(new ConsultLessonsCommand(request.ActorId, null, "research", request.RunId,
                LessonConsultationPurpose.Research, "Check external premise", ["contract-delivery"], [new("C")]));
            await f.Ledger.ExecuteAsync(new AddEvidenceCommand(request.ActorId, null, "research", new("RE"),
                "local-probe", "fixture://external", "External premise confirmed by stub fixture", [new("C")], []));
            await f.Ledger.ExecuteAsync(new DeclareProducerOutcomeCommand(request.ActorId, null, "research",
                request.RunId, new("reported-complete", [new("RE")], [])));
        };
        Assert.Equal(0, await f.Launch(expectedRun, "researcher", verifierProvider));
        preceding = await Observe(f, "research", trace);
        AssertPlanShape(preceding);
        Assert.Equal("referenced-self-report", preceding.GetProperty("return").GetProperty("requiredOutputPresence").GetString());
        await f.Ok("claim", "resolve", "--id", "C", "--status", "validated", "--evidence", "RE");
        expectedRun = "RN2";
        f.Adapter.Act = request => RefreshRecon(f, request);
        Assert.Equal(0, await f.Launch(expectedRun, "planner", workerProvider));
        preceding = await Observe(f, "refreshed-recon", trace);
        Assert.Contains("RECON2", preceding.GetRawText());
        preceding = (await f.Stage(TaskStage.Design)).GetProperty("nextActionContract");
        Assert.Equal("satisfied", Requirement(preceding, "stage-Design").GetProperty("status").GetString());
        trace.Add("response:Design");

        expectedRun = "RP";
        f.Adapter.Act = async request =>
        {
            AssertPlanShape(preceding); // The actual author sees the shape delivered before dispatch.
            await FileThroughCli(f, request, "PROMPT", GovernedArtifactKind.PromptContract, "PROMPT_SECRET");
            await RejectMalformedPlan(f, request);
            var first = await FileThroughCli(f, request, "PLAN1", GovernedArtifactKind.OrchestrationPlan, Plan);
            var revised = await FileThroughCli(f, request, "PLAN2", GovernedArtifactKind.OrchestrationPlan,
                Plan.Replace("fixture:1", "fixture:2"), "PLAN1");
            Assert.True(revised.GetProperty("version").GetInt64() > first.GetProperty("version").GetInt64());
            Assert.Contains("PLAN2", revised.GetProperty("nextActionContract").GetRawText());
            Assert.DoesNotContain("\"PLAN1\"", revised.GetProperty("nextActionContract").GetRawText());
        };
        Assert.Equal(0, await f.Launch(expectedRun, "planner", workerProvider));
        preceding = await Observe(f, "planning", trace);
        Assert.Contains("prepare work and dispatch worker", preceding.GetRawText());
        await f.PrepareWorkerAsync();
        preceding = f.LastJson().GetProperty("nextActionContract");
        trace.Add("response:Execution");

        expectedRun = "RW";
        f.Adapter.Act = request =>
        {
            Assert.Contains("PLAN_SECRET", request.StandardInput);
            Assert.Contains("REQUEST_SECRET", request.StandardInput);
            return Task.CompletedTask;
        };
        Assert.Equal(0, await f.Launch(expectedRun, "worker", workerProvider, "W"));
        preceding = await Observe(f, "worker", trace);
        Assert.Contains(VerifierOutputDocuments.AuthoringShape, Shapes(preceding));
        preceding = (await f.Stage(TaskStage.Verification)).GetProperty("nextActionContract");
        trace.Add("response:Verification");

        expectedRun = "RV";
        f.Adapter.Act = async request =>
        {
            Assert.Contains("PLAN_SECRET", request.StandardInput);
            await FileThroughCli(f, request, "V", GovernedArtifactKind.VerifierOutput,
                ArtifactCommands.VerifierBody + "| R1 | handled | fixture | fixture:2 |\n\nVERIFIER_SECRET");
        };
        Assert.Equal(0, await f.Launch(expectedRun, "verifier", verifierProvider, "W", "--candidate", Candidate));
        preceding = await Observe(f, "verifier", trace);
        AssertBinding(preceding);
        preceding = (await f.Stage(TaskStage.Review)).GetProperty("nextActionContract");
        AssertBinding(preceding);
        trace.Add("response:Review");

        expectedRun = "RR";
        f.Adapter.Act = async request =>
        {
            Assert.Equal(Candidate, request.Assurance!.CandidateId);
            Assert.Equal(new RunId("RV"), request.Assurance.VerifierRunId);
            AssertIsolated(JsonSerializer.Serialize(request, LedgerJson.CreateOptions()));
            await AssertRelayIsolation(request);
            await FileThroughCli(f, request, "CR", GovernedArtifactKind.CodeReviewOutput, "Code review findings");
        };
        Assert.Equal(0, await f.Launch(expectedRun, "reviewer", workerProvider, "W",
            "--candidate", Candidate, "--verifier-run", "RV"));
        await Observe(f, "reviewer", trace);
        var final = await f.Ledger.StateAsync();
        Assert.Equal(7, f.Adapter.Requests.Count);
        foreach (var request in f.Adapter.Requests)
        {
            var run = final.Runs[request.RunId];
            Assert.Equal(AgentRunStatus.Completed, run.Status);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.StandardInput))).ToLowerInvariant(), run.ManifestHash);
            Assert.Equal("probe:" + request.RunId.Value, trace[trace.IndexOf("execute:" + request.RunId.Value) - 1]);
        }
        // Filed reports and successful terminations do not complete the work or establish task-13 acceptance.
        Assert.NotEqual(WorkItemStatus.Completed, final.WorkItems[new("W")].Status);
        Assert.Equal("unknown", f.LastJson().GetProperty("nextActionContract").GetProperty("return").GetProperty("technicalAcceptance").GetString());
        output.WriteLine(string.Join(" -> ", trace));
    }

    [Theory]
    [InlineData("claude", false)]
    [InlineData("codex", false)]
    [InlineData("claude", true)]
    [InlineData("codex", true)]
    public async Task CancelledReturnRetainsPartialEvidenceAndClosesRun(string provider, bool throws)
    {
        using var f = new DeliveryFixture();
        await f.InitializeAsync(); await f.Context(); await f.Stage(TaskStage.Research);
        f.Adapter.Status = AgentRunStatus.Cancelled;
        using var cancellation = new CancellationTokenSource();
        f.RunCancellation = cancellation.Token;
        f.Adapter.Act = async request =>
        {
            await f.Ledger.ExecuteAsync(new AddEvidenceCommand(request.ActorId, null, "partial", new("E"),
                "local-probe", "fixture://partial", "Evidence survives cancellation", [], []));
            await f.Ledger.ExecuteAsync(new DeclareProducerOutcomeCommand(request.ActorId, null, "partial", request.RunId,
                new("partial", [new("E")], [])));
            if (throws)
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }
        };
        Assert.NotEqual(0, await f.Launch("CANCEL", "researcher", provider));
        var response = f.LastJson();
        var returned = response.GetProperty("nextActionContract").GetProperty("return");
        Assert.Equal("Cancelled", returned.GetProperty("recordedTermination").GetString());
        Assert.Equal("partial", returned.GetProperty("attributedOutcome").GetString());
        Assert.Equal("referenced-self-report", returned.GetProperty("requiredOutputPresence").GetString());
        Assert.Equal("unknown", returned.GetProperty("technicalAcceptance").GetString());
        Assert.Equal(!throws, response.TryGetProperty("status", out _));
        var state = await f.Ledger.StateAsync();
        Assert.Single(state.Evidence);
        Assert.Equal(AgentRunStatus.Cancelled, state.Runs[new("CANCEL")].Status);
        var history = new List<LedgerEvent>();
        await foreach (var entry in f.Ledger.Service().GetHistoryAsync(f.Ledger.TaskId, default)) history.Add(entry);
        Assert.Single(history.Where(e => e.Data is RunCompleted c && c.RunId == new RunId("CANCEL")));
    }

    private async Task<JsonElement> Observe(DeliveryFixture f, string name, List<string> trace)
    {
        var packet = f.LastJson().GetProperty("nextActionContract");
        Assert.Equal((await f.Ledger.StateAsync()).Version, packet.GetProperty("observedTaskVersion").GetInt64());
        Assert.Equal("observed", packet.GetProperty("deliveryStatus").GetString());
        var bytes = Encoding.UTF8.GetByteCount(packet.GetRawText());
        output.WriteLine($"{name}: {bytes} UTF-8 bytes; {(bytes + 3) / 4} estimated tokens (bytes/4, not tokenizer counts).");
        trace.Add("response:" + name);
        return packet;
    }

    private static async Task RefreshRecon(DeliveryFixture f, AgentLaunchRequest request)
    {
        await f.Ledger.ExecuteAsync(new ConsultLessonsCommand(request.ActorId, null, "refresh", request.RunId,
            LessonConsultationPurpose.Recon, "Refresh after external claim resolution", ["contract-delivery"], []));
        var template = InternalReconDocuments.CreateTemplate(await f.Ledger.StateAsync());
        await FileThroughCli(f, request, "RECON2", GovernedArtifactKind.InternalRecon, JsonSerializer.Serialize(template with
        {
            Assessments = template.Assessments.Select(a => a with { Domain = "external" }).ToArray(), Report = "RECON_SECRET"
        }), "RECON");
    }

    private static async Task<JsonElement> FileThroughCli(DeliveryFixture f, AgentLaunchRequest request, string id,
        GovernedArtifactKind kind, string content, string? predecessor = null)
    {
        var (exit, response, error) = await ArtifactCli(f, request, id, kind, content, predecessor);
        Assert.True(exit == 0, error);
        var result = JsonDocument.Parse(response).RootElement.Clone();
        Assert.True(result.TryGetProperty("events", out _));
        if (request.ActorId == new ActorId("planner"))
            Assert.Equal(result.GetProperty("version").GetInt64(), result.GetProperty("nextActionContract").GetProperty("observedTaskVersion").GetInt64());
        else
            Assert.False(result.TryGetProperty("nextActionContract", out _));
        return result;
    }

    private static async Task RejectMalformedPlan(DeliveryFixture f, AgentLaunchRequest request)
    {
        var before = await File.ReadAllTextAsync(f.Ledger.EventsPath);
        var (exit, _, error) = await ArtifactCli(f, request, "BAD", GovernedArtifactKind.OrchestrationPlan, "Missing attention table");
        Assert.Equal(1, exit);
        Assert.Contains("Orchestration plan 'BAD'", error);
        Assert.Equal(before, await File.ReadAllTextAsync(f.Ledger.EventsPath));
    }

    private static async Task<(int Exit, string Response, string Error)> ArtifactCli(DeliveryFixture f,
        AgentLaunchRequest request, string id, GovernedArtifactKind kind, string content, string? predecessor = null)
    {
        using var response = new StringWriter(); using var error = new StringWriter(); using var input = new StandardInput(content);
        var app = new CliApplication(response, error, _ => f.Ledger.Service(), _ => f.Adapter, new ContextAssembler());
        var exit = await app.RunAsync(["artifact", "record", "--root", f.Ledger.Root, "--task", f.Ledger.TaskId.Value,
            "--actor", request.ActorId.Value, "--run", request.RunId.Value, "--id", id, "--kind", kind.ToString(),
            "--title", "Fixture artifact", "--body-stdin",
            .. predecessor is null ? Array.Empty<string>() : ["--supersedes", predecessor]], default);
        return (exit, response.ToString(), error.ToString());
    }

    private static async Task AssertRelayIsolation(AgentLaunchRequest request)
    {
        using var relay = new McpProcess(request.FindingsEndpoint!.Arguments.Skip(1).ToArray());
        await ProviderFindingsSessionTests.InitializeAsync(relay);
        await relay.SendAsync(InspectionMcpTests.Call("inspect_task", """{"schema_version":1,"selection":"task"}""", 2));
        var inspected = InspectionMcpTests.Body(await relay.ReadAsync());
        AssertIsolated(inspected.GetRawText());
        var version = inspected.GetProperty("snapshot").GetProperty("ledger_version").GetInt64();
        var index = 3;
        foreach (var (kind, id) in new[] { ("UserRequest", "U"), ("InternalRecon", "RECON2"),
                     ("PromptContract", "PROMPT"), ("OrchestrationPlan", "PLAN2"), ("VerifierOutput", "V") })
        {
            await relay.SendAsync(InspectionMcpTests.Call("retrieve_context", JsonSerializer.Serialize(new
            { schema_version = 1, selection = "task", kind, id, expected_version = version, expected_sha256 = new string('a', 64) }), index++));
            var result = InspectionMcpTests.Body(await relay.ReadAsync());
            Assert.Equal("not_visible", result.GetProperty("diagnostic").GetProperty("code").GetString());
            AssertIsolated(result.GetRawText());
        }
        Assert.Empty(await relay.FinishAsync());
    }

    private static void AssertIsolated(string text)
    {
        foreach (var forbidden in new[] { "REQUEST_SECRET", "RECON_SECRET", "PROMPT_SECRET", "PLAN_SECRET", "VERIFIER_SECRET", "nextActionContract" })
            Assert.DoesNotContain(forbidden, text);
    }

    private static void AssertBinding(JsonElement packet)
    {
        var binding = packet.GetProperty("bindings");
        Assert.Equal("RV", binding.GetProperty("verifierRunId").GetString());
        var assurance = binding.GetProperty("assurance");
        Assert.Equal(Candidate, assurance.GetProperty("candidateId").GetString());
        Assert.Equal("W", Assert.Single(assurance.GetProperty("workItemIds").EnumerateArray()).GetString());
        var work = Assert.Single(assurance.GetProperty("workVersions").EnumerateArray());
        Assert.Equal("W", work.GetProperty("workItemId").GetString());
        Assert.Equal("RW", work.GetProperty("workingRunId").GetString());
        Assert.Equal("satisfied", Requirement(packet, "verifier-pair").GetProperty("status").GetString());
    }

    private static JsonElement Requirement(JsonElement packet, string id) => packet.GetProperty("actions").EnumerateArray()
        .SelectMany(a => a.GetProperty("requirements").EnumerateArray()).First(r => r.GetProperty("id").GetString() == id);
    private static string Shapes(JsonElement packet) => string.Join("\n", packet.GetProperty("actions").EnumerateArray()
        .Select(a => a.GetProperty("requiredShape").GetString()));
    private static void AssertPlanShape(JsonElement packet) => Assert.Contains(OrchestrationPlanDocuments.AttentionTableTemplate, Shapes(packet));
}
