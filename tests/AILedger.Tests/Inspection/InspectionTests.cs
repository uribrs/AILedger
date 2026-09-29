using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Inspection;
using AILedger.Core.Findings;
using AILedger.Tests.Findings;
using AILedger.Storage;

namespace AILedger.Tests.Inspection;

public sealed class InspectionTests
{
    internal static InspectionBinding Binding(FindingsFixture f) => new(f.TaskId, f.Actor, null, "stable",
        AllowInspect: true, AllowRunless: true, AllowRecordFindings: true, AllowRecordAlternatives: true,
        AllowSubmitArtifact: true, AllowRecordClaimDispositions: true);

    [Fact]
    public async Task TypedRetrievalPreservesClaimsEvidenceDecisionsAndOwnReceiptsWithoutRepairingAnything()
    {
        using var f = new FindingsFixture(); await f.OpenAsync();
        var receipt = (await f.RecordAsync()).Receipt!;
        await f.ExecuteAsync(new ProposeDecisionCommand(f.Actor, null, "decision", new("D1"), "Decision", "Rationale",
            [new(receipt.Findings[0].ClaimId)], null));
        var b = Binding(f);
        var service = f.Service();
        await File.WriteAllTextAsync(Path.Combine(f.Directory, "task.md"), "DAMAGED VIEW");
        File.Delete(Path.Combine(f.Directory, "state.json"));
        var before = await Files(f.Directory);
        var index = await service.InspectAsync(b, new(Limit: 32), default);
        Assert.Equal("ok", index.Status); Assert.Equal("missing", index.Snapshot!.BriefFreshness);
        Assert.Contains(index.Records, r => r.Kind == "Decision" && r.Id == "D1");
        var claim = await service.RetrieveAsync(b, index.Records.Single(r => r.Kind == "Claim").Retrieve, default);
        Assert.Equal("ok", claim.Status);
        var typedClaim = claim.Data!.Value.Deserialize<Claim>(LedgerJson.CreateOptions())!;
        Assert.Equal(ClaimStatus.Open, typedClaim.Status);
        Assert.Equal("consequence", typedClaim.ConsequenceIfWrong);
        var evidence = await service.RetrieveAsync(b, index.Records.Single(r => r.Kind == "Evidence").Retrieve, default);
        var typedEvidence = evidence.Data!.Value.Deserialize<Evidence>(LedgerJson.CreateOptions())!;
        Assert.Equal("fixture://source", typedEvidence.Citation);
        Assert.Equal(typedClaim.Id, Assert.Single(typedEvidence.Supports));
        var readReceipt = await service.RetrieveAsync(b, index.Records.Single(r => r.Kind == "FindingsReceipt").Retrieve, default);
        Assert.Equal(receipt.TransactionId, readReceipt.Data!.Value.Deserialize<FindingsReceipt>(LedgerJson.CreateOptions())!.TransactionId);
        Assert.Equal(before, await Files(f.Directory));
    }

    [Fact]
    public async Task PaginationAndChunksExposeEverySelectedRecordWithoutMixingVersions()
    {
        using var f = new FindingsFixture(); await f.OpenAsync();
        var text = string.Concat(Enumerable.Repeat("quoted \" $HOME `literal` שלום 😀\n", 200));
        await f.ExecuteAsync(new AddClaimCommand(f.Actor, null, "claim", new("LARGE"), text, "Consequence"));
        var b = Binding(f); var service = f.Service();
        var page = await service.InspectAsync(b, new(Limit: 1), default);
        var references = page.Records.ToList();
        while (page.NextOffset is { } next)
        {
            page = await service.InspectAsync(b, new(Offset: next, Limit: 1, ExpectedVersion: page.Snapshot!.LedgerVersion), default);
            references.AddRange(page.Records);
        }
        Assert.Equal(page.TotalSelected, references.Count);
        var reference = references.Single(r => r.Id == "LARGE"); Assert.True(reference.SummaryTruncated);
        var query = reference.Retrieve with { Length = 37 };
        var content = new StringBuilder();
        while (true)
        {
            var chunk = await service.RetrieveAsync(b, query, default);
            Assert.Equal("ok", chunk.Status); Assert.Null(chunk.Data); content.Append(chunk.JsonChunk);
            if (chunk.NextOffset is not { } offset) break;
            query = query with { Offset = offset };
        }
        Assert.Equal(reference.Sha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString()))).ToLowerInvariant());
        Assert.Equal(text.Trim(), JsonSerializer.Deserialize<Claim>(content.ToString(), LedgerJson.CreateOptions())!.Statement);
        await f.RecordAsync(FindingsFixture.Claims("new"));
        Assert.Equal("stale_snapshot", (await service.RetrieveAsync(b, query, default)).Diagnostic!.Code);
        Assert.Equal("stale_snapshot", (await service.InspectAsync(b, new(Offset: 1, ExpectedVersion: page.Snapshot!.LedgerVersion), default)).Diagnostic!.Code);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(-1, 1)] [InlineData(0, 33)]
    public async Task BoundsRefuseWithoutSideEffects(int offset, int limit)
    {
        using var f = new FindingsFixture(); await f.OpenAsync(); var before = await Files(f.Directory);
        Assert.Equal("invalid_request", (await f.Service().InspectAsync(Binding(f), new(Offset: offset, Limit: limit), default)).Diagnostic!.Code);
        Assert.Equal(before, await Files(f.Directory));
    }

    [Fact]
    public async Task MissingGrantRoleCapabilityAndForeignRunFailClosed()
    {
        using var f = new FindingsFixture(); await f.OpenAsync(); var service = f.Service();
        foreach (var binding in new[] { Binding(f) with { AllowInspect = false }, Binding(f) with { ActorId = new("absent") },
            Binding(f) with { RunId = new("foreign"), CorrelationId = "foreign" } })
        {
            var result = await service.InspectAsync(binding, new(), default);
            Assert.Equal("error", result.Status); Assert.Null(result.Snapshot); Assert.Empty(result.Records);
        }
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "role", new("worker"), RoleKind.Worker, [Capability.AddClaim]));
        var denied = await service.InspectAsync(Binding(f) with { ActorId = new("worker") }, new(), default);
        Assert.Contains("BuildContext", denied.Diagnostic!.Reason);
    }

    [Fact]
    public async Task ReviewerAndReceiptIsolationApplyToIndexAndDirectRetrieval()
    {
        using var f = new FindingsFixture(); await f.OpenAsync(); await f.RecordAsync();
        await f.ExecuteAsync(new RaiseEscalationCommand(f.Actor, null, "x", new("X1"), EscalationKind.BusinessDecision,
            "SECRET RECOMMENDATION", null, ["A", "B"], "A", []));
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "role", new("reviewer"), RoleKind.CodeReviewer,
            [Capability.BuildContext, Capability.AddClaim, Capability.AddEvidence]));
        var b = Binding(f) with { ActorId = new("reviewer") };
        var index = await f.Service().InspectAsync(b, new(Selection: "task", Limit: 32), default);
        Assert.Equal("ok", index.Status);
        Assert.DoesNotContain(index.Records, r => r.Kind.Contains("Receipt", StringComparison.Ordinal) || r.Kind == "Escalation");
        var result = await f.Service().RetrieveAsync(b, new(1, "task", "Escalation", "X1", index.Snapshot!.LedgerVersion), default);
        Assert.Equal("not_visible", result.Diagnostic!.Code);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(index));
    }

    [Fact]
    public async Task CognitiveChangeAtSameLedgerVersionCannotMixContentChunks()
    {
        using var f = new FindingsFixture(); await f.OpenAsync();
        var b = Binding(f) with { CognitiveArtifacts = [new(ContextArtifactKind.Skill, "workflow-coordinator", "first", [])] };
        var index = await f.Service().InspectAsync(b, new(), default);
        var reference = index.Records.Single(r => r.Kind == "Skill");
        var changed = b with { CognitiveArtifacts = [new(ContextArtifactKind.Skill, "workflow-coordinator", "second", [])] };
        var result = await f.Service().RetrieveAsync(changed, reference.Retrieve, default);
        Assert.Equal("stale_content", result.Diagnostic!.Code);
    }

    [Fact]
    public async Task TornTailIsNeitherRepairedNorReturnedAndLostHistoryIsNotHealed()
    {
        using var f = new FindingsFixture(); await f.OpenAsync();
        await f.RecordAsync();
        await File.AppendAllTextAsync(f.EventsPath, "{partial");
        var before = await Files(f.Directory);
        Assert.Equal("ok", (await f.Service().InspectAsync(Binding(f), new(), default)).Status);
        Assert.Equal(before, await Files(f.Directory));
        var lines = await File.ReadAllLinesAsync(f.EventsPath);
        await File.WriteAllTextAsync(f.EventsPath, string.Join("\n", lines.Take(2)) + "\n");
        var result = await f.Service().InspectAsync(Binding(f), new(), default);
        Assert.Equal("error", result.Status); Assert.Null(result.Snapshot);
        Assert.Contains("History has been lost", result.Diagnostic!.Reason);
    }

    internal static async Task<string[]> Files(string directory)
    {
        var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal);
        var rows = new List<string>();
        foreach (var path in files) rows.Add(Path.GetRelativePath(directory, path) + ":" + Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))));
        return rows.ToArray();
    }
}
