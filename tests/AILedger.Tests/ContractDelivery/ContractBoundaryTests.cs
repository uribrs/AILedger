using System.Text;
using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.ContextBriefing;
using AILedger.Storage;
using AILedger.Tests.Assurance;
using AILedger.Tests.Cli;
using AILedger.Tests.Support;
using Xunit.Abstractions;

namespace AILedger.Tests.ContractDelivery;

[Collection(StandardInput.Collection)]
public sealed class ContractBoundaryTests(ITestOutputHelper output)
{
    [Fact]
    public async Task LargerContextRetainsSharedShapeWithoutRepeatingClaimsOrManual()
    {
        using var f = new DeliveryFixture(); await f.InitializeAsync();
        for (var i = 0; i < 128; i++)
            await f.Ledger.ExecuteAsync(new AddClaimCommand(f.Ledger.Actor, null, "large", new("C" + i),
                "Distinct scoped premise " + i, null));
        var manifest = await f.Context();
        var packet = manifest.GetProperty("nextActionContract");
        Assert.Equal("observed", packet.GetProperty("deliveryStatus").GetString());
        Assert.Contains("129 historical claims", packet.GetRawText());
        Assert.Contains("Preflight Evidence", packet.GetRawText());
        Assert.DoesNotContain("Distinct scoped premise", packet.GetRawText());
        Measure("129-claim initial context", packet);
    }

    [Fact]
    public async Task MemberBindingsRemainExactAndStalePairStillRefusesAfterDelivery()
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Verify("RV", "A", "B", "C", "D");
        var delivered = LastJson(f.Output.ToString()).GetProperty("nextActionContract");
        var binding = delivered.GetProperty("bindings").GetProperty("assurance");
        Assert.Equal(4, binding.GetProperty("workItemIds").GetArrayLength());
        Assert.Equal(4, binding.GetProperty("workVersions").GetArrayLength());
        Assert.Equal(BundleFixture.Candidate, binding.GetProperty("candidateId").GetString());
        Measure("four-member verifier return", delivered);
        await CliStageFixture.ToReviewAsync(f.App, f.Root);
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Repair);
        await f.Work("A", "WA2");
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Verification);
        await CliStageFixture.ToReviewAsync(f.App, f.Root);
        var requests = f.Adapter.Requests.Count;
        Assert.NotEqual(0, await f.Launch("RR", ["A", "B", "C", "D"], "reviewer", "RV"));
        Assert.Equal(requests, f.Adapter.Requests.Count);
        Assert.Contains("Assurance", f.Error.ToString());
    }

    [Fact]
    public async Task AuthoredUnresolvedDispositionDoesNotBecomeAcceptance()
    {
        using var f = await BundleFixture.CreateAsync();
        f.Adapter.FileOutput = false;
        // Existing fixture has no material attention items; include an explicit authored extra
        // row in the admitted disposition table. It remains a finding, never a pass.
        f.Adapter.BeforeResult = request => f.Service.ExecuteAsync(new("T1"),
            new RecordArtifactCommand(request.ActorId, null, "RV", new("VO"), GovernedArtifactKind.VerifierOutput,
                "Unresolved findings", VerifierOutputDocuments.AuthoringShape.Split("Dispose every dependent")[0] +
                "\n| id | final disposition | name | evidence |\n| --- | --- | --- | --- |\n| R1 | unresolved | concern | fixture:1 |\n",
                null, request.RunId, null), default);
        await f.Verify("RV", "A");
        var observed = LastJson(f.Output.ToString()).GetProperty("nextActionContract").GetProperty("return");
        Assert.Equal("Completed", observed.GetProperty("recordedTermination").GetString());
        Assert.Equal("unknown", observed.GetProperty("technicalAcceptance").GetString());
        Assert.Equal(1, observed.GetProperty("verifierDispositions")[0].GetProperty("attentionDispositions").GetProperty("unresolved").GetInt32());
    }

    [Fact]
    public async Task ExceptionalOversizeDisclosesIncompleteBindingsAndRetainsAuthoringRequirements()
    {
        using var f = await BundleFixture.CreateAsync();
        var state = await f.State();
        var work = state.Runs[new("WA")];
        var huge = Enumerable.Range(0, 5000).Select(i => new WorkItemId("member-" + i)).ToArray();
        var verifier = work with { Id = new("oversized"), SubjectRole = RoleKind.Verifier,
            Assurance = new(1, huge, huge.Select(id => new AssuranceWorkVersion(id, new("run-" + id.Value))).ToArray(), BundleFixture.Candidate) };
        state = state with { Runs = new Dictionary<RunId, AgentRun>(state.Runs) { [verifier.Id] = verifier } };
        var packet = NextActionContracts.Observe(state, new("operator"), verifier.Id)!;
        Assert.Equal("incomplete", packet.DeliveryStatus);
        Assert.Null(packet.Bindings.Assurance);
        Assert.Contains("outside bounded delivery", packet.Diagnostic);
        Assert.Contains("final disposition", string.Join("\n", packet.Actions.Select(a => a.RequiredShape)));
        Measure("oversized binding fallback", JsonSerializer.SerializeToElement(packet, LedgerJson.CreateOptions(true)));
    }

    [Fact]
    public async Task NonCoordinatorsNeverReceivePacketEvenOnDirectContextRefresh()
    {
        using var f = new DeliveryFixture(); await f.InitializeAsync();
        foreach (var actor in new[] { "worker", "researcher", "verifier", "reviewer" })
            Assert.False((await f.Context(actor)).TryGetProperty("nextActionContract", out _));
    }

    private void Measure(string name, JsonElement packet)
    {
        var bytes = Encoding.UTF8.GetByteCount(packet.GetRawText());
        Assert.InRange(bytes, 1, ContractPacketBudget.MaximumUtf8Bytes);
        output.WriteLine($"{name}: {bytes} UTF-8 bytes; {(bytes + 3) / 4} estimated tokens (bytes/4; tokenizer unavailable).");
    }

    private static JsonElement LastJson(string text)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text.Trim()));
        // CLI fixture's nested artifact response and final provider response are separate objects.
        var remaining = Encoding.UTF8.GetBytes(text.Trim()).AsSpan();
        JsonElement last = default;
        while (!remaining.IsEmpty)
        {
            reader = new Utf8JsonReader(remaining);
            if (!reader.Read()) break;
            using var document = JsonDocument.ParseValue(ref reader);
            last = document.RootElement.Clone(); remaining = remaining[(int)reader.BytesConsumed..];
        }
        return last;
    }
}
