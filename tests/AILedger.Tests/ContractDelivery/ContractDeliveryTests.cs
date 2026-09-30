using System.Text;
using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.ContextBriefing;
using AILedger.Tests.Findings;
using AILedger.Tests.Inspection;
using AILedger.Tests.Support;
using Xunit.Abstractions;

namespace AILedger.Tests.ContractDelivery;

[Collection(StandardInput.Collection)]
public sealed class ContractDeliveryTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("codex", "claude")]
    [InlineData("claude", "codex")]
    public async Task CompletedResponsesDeliverBeforeEveryNextDispatchAndIsolateReview(string workingProvider, string verifyingProvider)
    {
        using var f = new DeliveryFixture();
        await f.InitializeAsync();
        var initial = await f.Context();
        PlanShape(initial.GetProperty("nextActionContract"));
        await f.ReconAsync(workingProvider);
        var recon = Packet(f);
        PlanShape(recon);
        Assert.Equal("Completed", recon.GetProperty("return").GetProperty("recordedTermination").GetString());
        Assert.Equal("present", recon.GetProperty("return").GetProperty("requiredOutputPresence").GetString());
        Assert.Equal("unknown", recon.GetProperty("return").GetProperty("attributedOutcome").GetString());
        await f.PlanAsync(workingProvider);
        Assert.Contains("prepare work", Packet(f).GetRawText());
        await f.PrepareWorkerAsync();
        f.Adapter.Act = request => DeclareThroughSuppliedSession(request, "reported-complete");
        Assert.Equal(0, await f.Launch("RW", "worker", workingProvider, "W"));
        var worked = Packet(f);
        Assert.Contains("verify or recover", worked.GetRawText());
        Assert.Equal("reported-complete", worked.GetProperty("return").GetProperty("attributedOutcome").GetString());
        Assert.Equal("unknown", worked.GetProperty("return").GetProperty("technicalAcceptance").GetString());
        await f.Stage(TaskStage.Verification);
        f.Adapter.Act = request => f.FileAsync(request, "V", GovernedArtifactKind.VerifierOutput, ArtifactCommands.VerifierBody);
        Assert.Equal(0, await f.Launch("RV", "verifier", verifyingProvider, "W", "--candidate", new string('a', 64)));
        var verified = Packet(f);
        Assert.Contains("isolated review", verified.GetRawText());
        Assert.Equal("RV", verified.GetProperty("bindings").GetProperty("verifierRunId").GetString());
        Assert.Equal("RW", verified.GetProperty("bindings").GetProperty("assurance").GetProperty("workVersions")[0].GetProperty("workingRunId").GetString());
        Assert.Equal("present", verified.GetProperty("return").GetProperty("requiredOutputPresence").GetString());
        await f.Stage(TaskStage.Review);
        f.Adapter.Act = async request =>
        {
            Assert.DoesNotContain("nextActionContract", request.StandardInput);
            Assert.DoesNotContain("PROMPT_SECRET", request.StandardInput);
            Assert.DoesNotContain("RECON_SECRET", request.StandardInput);
            Assert.DoesNotContain("Assumption dispositions", request.StandardInput);
            using var relay = new McpProcess(request.FindingsEndpoint!.Arguments.Skip(1).ToArray());
            await ProviderFindingsSessionTests.InitializeAsync(relay);
            await relay.SendAsync(InspectionMcpTests.Call("inspect_task", """{"schema_version":1,"selection":"task"}""", 2));
            var inspected = InspectionMcpTests.Body(await relay.ReadAsync());
            Assert.DoesNotContain("nextActionContract", inspected.GetRawText());
            Assert.DoesNotContain("PROMPT_SECRET", inspected.GetRawText());
            Assert.Empty(await relay.FinishAsync());
            await f.FileAsync(request, "CR", GovernedArtifactKind.CodeReviewOutput, "Review findings, no inferred acceptance");
        };
        Assert.Equal(0, await f.Launch("RR", "reviewer", workingProvider, "W", "--candidate", new string('a', 64), "--verifier-run", "RV"));
        foreach (var run in new[] { "RN", "RP", "RW", "RV", "RR" })
        {
            var index = f.Order.IndexOf("dispatch:" + run);
            Assert.True(index > 0);
            Assert.StartsWith("response:", f.Order[index - 1]);
            Assert.Equal("response:provider launch", f.Order[index + 1]);
        }
        Measure("initial", initial.GetProperty("nextActionContract"));
        Measure("worker", worked); Measure("verifier", verified);
    }

    [Theory]
    [InlineData(AgentRunStatus.Completed, false)]
    [InlineData(AgentRunStatus.Failed, false)]
    [InlineData(AgentRunStatus.Failed, true)]
    public async Task ResearchReturnsBlockedPartialFindingsWithoutPromotingTermination(AgentRunStatus status, bool throws)
    {
        using var f = new DeliveryFixture();
        await f.InitializeAsync(); await f.Context(); await f.Stage(TaskStage.Research);
        f.Adapter.Status = status; f.Adapter.Throw = throws;
        f.Adapter.Act = request => DeclareThroughSuppliedSession(request, "blocked");
        var exit = await f.Launch("R", "researcher");
        Assert.Equal(status == AgentRunStatus.Completed ? 0 : throws ? 1 : 3, exit);
        var packet = Packet(f);
        PlanShape(packet);
        Assert.Equal(status.ToString(), packet.GetProperty("return").GetProperty("recordedTermination").GetString());
        Assert.Equal("blocked", packet.GetProperty("return").GetProperty("attributedOutcome").GetString());
        Assert.Equal("missing", packet.GetProperty("return").GetProperty("requiredOutputPresence").GetString());
        Assert.Equal("unknown", packet.GetProperty("return").GetProperty("technicalAcceptance").GetString());
        Assert.Single((await f.Ledger.StateAsync()).Evidence);
    }

    [Fact]
    public async Task UndeclaredResearchReturnIsUnknownDespiteBlockedText()
    {
        using var f = new DeliveryFixture();
        await f.InitializeAsync(); await f.Context(); await f.Stage(TaskStage.Research);
        Assert.Equal(0, await f.Launch("R", "researcher"));
        Assert.Equal("unknown", Packet(f).GetProperty("return").GetProperty("attributedOutcome").GetString());
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData("partial")]
    public async Task WorkerSelfReportLeavesRepairBranchAndAcceptanceUnknown(string declared)
    {
        using var f = new DeliveryFixture(); await f.InitializeAsync(); await f.Context();
        await f.ReconAsync(); await f.PlanAsync(); await f.PrepareWorkerAsync();
        f.Adapter.Act = request => DeclareThroughSuppliedSession(request, declared);
        Assert.Equal(0, await f.Launch("RW", "worker", "codex", "W"));
        var packet = Packet(f);
        Assert.Equal(declared, packet.GetProperty("return").GetProperty("attributedOutcome").GetString());
        Assert.Equal("unknown", packet.GetProperty("return").GetProperty("technicalAcceptance").GetString());
        Assert.Contains("repair/replanning", packet.GetRawText());
        Assert.Equal("W", packet.GetProperty("bindings").GetProperty("workId").GetString());
    }

    [Fact]
    public async Task MissingVerifierOutputPreservesProviderCompletedAndRecordedFailed()
    {
        using var f = await WorkerCompletedAsync();
        await f.Stage(TaskStage.Verification); f.Adapter.Act = null;
        Assert.NotEqual(0, await f.Launch("RV", "verifier", "claude", "W"));
        var response = f.LastJson();
        Assert.Equal("completed", response.GetProperty("status").GetString());
        var observed = Packet(f).GetProperty("return");
        Assert.Equal("Failed", observed.GetProperty("recordedTermination").GetString());
        Assert.Equal("missing", observed.GetProperty("requiredOutputPresence").GetString());
        Assert.Contains("recover missing verifier output", Packet(f).GetRawText());
    }

    [Fact]
    public async Task ArtifactRevisionAndStageResponsesRefreshAndPreserveFields()
    {
        using var f = new DeliveryFixture(); await f.InitializeAsync(); await f.Context(); await f.ReconAsync();
        await f.Stage(TaskStage.Design);
        await f.Ok("run", "start", "--run", "P", "--subject", "planner", "--provider", "codex");
        foreach (var (id, predecessor) in new[] { ("P1", (string?)null), ("P2", "P1") })
        {
            using var input = new StandardInput("No material attention items — fixture plan.");
            await f.Ok(["artifact", "record", "--actor", "planner", "--run", "P", "--id", id,
                "--kind", "OrchestrationPlan", "--title", "Plan", "--body-stdin",
                .. predecessor is null ? Array.Empty<string>() : ["--supersedes", predecessor]]);
            var response = f.LastJson();
            Assert.True(response.TryGetProperty("events", out _));
            Assert.Equal(response.GetProperty("version").GetInt64(), Packet(f).GetProperty("observedTaskVersion").GetInt64());
            Assert.Contains(id, Packet(f).GetRawText());
            if (predecessor is not null) Assert.DoesNotContain("\"P1\"", Packet(f).GetRawText());
        }
        var previous = Packet(f).GetProperty("observedTaskVersion").GetInt64();
        var stage = await f.Stage(TaskStage.Research, "Reconsider plan");
        Assert.True(stage.GetProperty("version").GetInt64() > previous);
        PlanShape(Packet(f));
    }

    [Fact]
    public async Task FileContextCarriesContractAndLaterAuthorityStillRefusesLaunch()
    {
        using var f = new DeliveryFixture(); await f.InitializeAsync();
        using var output = new TemporaryDirectory();
        var manifest = await f.Context(output: Path.Combine(output.Path, "manifest.json"));
        PlanShape(manifest.GetProperty("nextActionContract"));
        await f.ReconAsync(); await f.PlanAsync(); await f.PrepareWorkerAsync();
        var observed = Packet(f).GetProperty("observedTaskVersion").GetInt64();
        await f.Ledger.ExecuteAsync(new AssignRoleCommand(f.Ledger.Actor, null, "revoke", new("worker"),
            RoleKind.CodeReviewer, [Capability.BuildContext]));
        var count = f.Adapter.Requests.Count;
        Assert.NotEqual(0, await f.Launch("RW", "worker", "codex", "W"));
        Assert.Equal(count, f.Adapter.Requests.Count);
        Assert.True((await f.Ledger.StateAsync()).Version > observed);
    }

    private void Measure(string name, JsonElement packet)
    {
        var bytes = Encoding.UTF8.GetByteCount(packet.GetRawText());
        Assert.InRange(bytes, 1, ContractPacketBudget.MaximumUtf8Bytes);
        output.WriteLine($"{name}: {bytes} UTF-8 bytes; {(bytes + 3) / 4} estimated tokens (bytes/4, no tokenizer).");
    }

    private static JsonElement Packet(DeliveryFixture f) => f.LastJson().GetProperty("nextActionContract");
    private static void PlanShape(JsonElement packet)
    {
        Assert.Contains(OrchestrationPlanDocuments.AttentionTableTemplate,
            string.Join("\n", packet.GetProperty("actions").EnumerateArray().Select(a => a.GetProperty("requiredShape").GetString())));
    }

    private static async Task<DeliveryFixture> WorkerCompletedAsync()
    {
        var f = new DeliveryFixture(); await f.InitializeAsync(); await f.Context(); await f.ReconAsync();
        await f.PlanAsync(); await f.PrepareWorkerAsync(); f.Adapter.Act = null;
        Assert.Equal(0, await f.Launch("RW", "worker", "codex", "W")); return f;
    }

    private static async Task DeclareThroughSuppliedSession(AgentLaunchRequest request, string outcome)
    {
        using var relay = new McpProcess(request.FindingsEndpoint!.Arguments.Skip(1).ToArray());
        await ProviderFindingsSessionTests.InitializeAsync(relay);
        var evidence = new AILedger.Core.Findings.FindingsRequest(1, "output-evidence", [],
            [new("output", "test-run", "fixture://output", "Observed output or blocker", [], [])]);
        await relay.SendAsync(FindingsMcpFixture.Call(FindingsMcpFixture.Body(evidence)));
        var receipt = FindingsMcpFixture.Payload(await relay.ReadAsync()).GetProperty("receipt");
        var id = receipt.GetProperty("evidence")[0].GetProperty("evidence_id").GetString()!;
        var body = JsonSerializer.Serialize(new { outcome,
            output_evidence_ids = outcome == "blocked" ? Array.Empty<string>() : [id],
            blocker_evidence_ids = outcome == "blocked" ? new[] { id } : [] });
        await relay.SendAsync(InspectionMcpTests.Call("declare_producer_outcome", body, 3));
        Assert.Equal("ok", InspectionMcpTests.Body(await relay.ReadAsync()).GetProperty("status").GetString());
        Assert.Empty(await relay.FinishAsync());
    }
}
