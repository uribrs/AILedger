using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Findings;
using AILedger.Storage;
using AILedger.Storage.Findings;

namespace AILedger.Tests.Findings;

public sealed class FindingsOperationTests
{
    [Fact]
    public async Task BatchUsesRealDomainRulesAndPreservesTextReferencesAndProvenance()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        await f.ExecuteAsync(new AddClaimCommand(f.Actor, null, "cli", new("C1"), "Existing", null));
        var request = FindingsFixture.Request();
        request = request with { Evidence = [request.Evidence[0] with
            { Refutes = [new(ClaimId: "C1")] }] };
        var result = await f.RecordAsync(request);
        Assert.Null(result.Error);
        var receipt = Assert.IsType<FindingsReceipt>(result.Receipt);
        Assert.Equal(5, receipt.LedgerVersion);
        var state = await f.StateAsync();
        var claim = state.Claims[new(receipt.Findings[0].ClaimId)];
        Assert.Equal(request.Findings[0].Statement.Trim(), claim.Statement);
        Assert.Equal(ClaimStatus.Open, claim.Status);
        Assert.Empty(claim.EvidenceIds);
        Assert.Equal("consequence", claim.ConsequenceIfWrong);
        var evidence = state.Evidence[new(receipt.Evidence[0].EvidenceId)];
        Assert.Equal("summary\nsecond line", evidence.Summary);
        Assert.Equal([claim.Id], evidence.Supports);
        Assert.Equal([new ClaimId("C1")], evidence.Refutes);
        Assert.Equal(f.Actor, claim.Provenance.ActorId);
        Assert.Equal(receipt.CommittedAt, evidence.Provenance.RecordedAt);
        var lines = await File.ReadAllLinesAsync(f.EventsPath);
        Assert.Contains("_ailedgerFindingsReceipt", lines[3]);
        Assert.DoesNotContain("_ailedgerFindingsReceipt", lines[4]);
        // The historical typed event reader ignores the additive storage fields.
        Assert.All(lines, l => Assert.NotNull(JsonSerializer.Deserialize<LedgerEvent>(l, LedgerJson.CreateOptions())));
    }

    [Fact]
    public async Task ReceiptSurvivesLostResponseRestartAdvancementAndDeletedProjections()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var first = await f.RecordAsync();
        await f.ExecuteAsync(new AddClaimCommand(f.Actor, null, "cli", new("C-next"), "Next", null));
        foreach (var path in System.IO.Directory.EnumerateFiles(f.Directory))
            if (Path.GetFileName(path) is not ("events.jsonl" or ".mutation.lock")) File.Delete(path);
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var retry = await f.RecordAsync();
        Assert.True(retry.Replayed);
        Assert.NotEqual(first.AttemptId, retry.AttemptId);
        Assert.Equal(FindingsReceiptEnvelope.Serialize(first.Receipt!), FindingsReceiptEnvelope.Serialize(retry.Receipt!));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
        Assert.Equal(first.Receipt!.LedgerVersion + 1, (await f.StateAsync()).Version);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("order")]
    [InlineData("correlation")]
    [InlineData("causation")]
    [InlineData("whitespace")]
    [InlineData("reference")]
    public async Task ChangedMeaningUnderACommittedKeyConflicts(string change)
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var request = FindingsFixture.Request() with { Findings = [new("f1", "One"), new("f2", "Two")] };
        var first = await f.RecordAsync(request);
        Assert.Null(first.Error);
        var binding = f.Binding;
        request = change switch
        {
            "text" => request with { Findings = [new("f1", "Changed"), request.Findings[1]] },
            "whitespace" => request with { Findings = [new("f1", " One "), request.Findings[1]] },
            "order" => request with { Findings = request.Findings.Reverse().ToArray() },
            "reference" => request with { Evidence = [request.Evidence[0] with
                { Supports = [new(ClaimId: first.Receipt!.Findings[0].ClaimId)] }] },
            _ => request
        };
        if (change == "correlation") binding = binding with { CorrelationId = "other" };
        if (change == "causation") binding = binding with { CausationId = new EventId("findings:0000000001") };
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var result = await f.RecordAsync(request, binding);
        Assert.Equal("idempotency_conflict", result.Error?.Code);
        Assert.Equal("committed", result.Error?.CommitState);
        Assert.Null(result.Receipt);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("opposed")]
    public async Task LastCommandRefusalLeavesNoPrefixAndOneOriginalRefusal(string error)
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var request = FindingsFixture.Request();
        var evidence = request.Evidence[0];
        var bad = error switch
        {
            "missing" => evidence with { Key = "last", Supports = [new(ClaimId: "absent")] },
            "duplicate" => evidence with { Key = "last", Supports = [new(Finding: "f1"), new(Finding: "f1")] },
            _ => evidence with { Key = "last", Refutes = [new(Finding: "f1")] }
        };
        request = request with { Evidence = [evidence, bad] };
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var result = await f.RecordAsync(request);
        Assert.Equal("kernel_refused", result.Error?.Code);
        Assert.Equal("not_committed", result.Error?.CommitState);
        Assert.Equal("evidence[1]", result.Error?.ItemPath);
        Assert.Null(result.Error?.RuleId);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
        Assert.Empty((await f.StateAsync()).Claims);
        var row = Assert.Single(await File.ReadAllLinesAsync(Path.Combine(f.Directory, "refusals.jsonl")));
        var refusal = JsonSerializer.Deserialize<RefusalRecord>(row, LedgerJson.CreateOptions())!;
        Assert.Equal("AddEvidenceCommand", refusal.Command);
        Assert.Equal(2, refusal.TaskVersion);
        Assert.Equal(result.Error!.Message, refusal.Message);
        Assert.NotNull(refusal.KernelIdentity);
    }

    [Fact]
    public async Task SameKeyConcurrencyAndCompetingCliWritesSerializeWithoutLostEvents()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var same = Enumerable.Range(0, 10).Select(_ => f.RecordAsync()).ToArray();
        var different = Enumerable.Range(0, 8).Select(i => f.RecordAsync(FindingsFixture.Claims($"other-{i}"))).ToArray();
        var cli = Enumerable.Range(0, 8).Select(i => f.ExecuteAsync(new AddClaimCommand(f.Actor,
            null, "cli", new($"CLI-{i}"), "CLI claim", null))).ToArray();
        await Task.WhenAll(same.Cast<Task>().Concat(different).Concat(cli));
        var sameResults = await Task.WhenAll(same);
        var differentResults = await Task.WhenAll(different);
        Assert.All(sameResults.Concat(differentResults), r => Assert.Null(r.Error));
        Assert.Single(sameResults.Where(r => !r.Replayed));
        Assert.Single(sameResults.Select(r => r.Receipt!.TransactionId).Distinct());
        var state = await f.StateAsync();
        Assert.Equal(20, state.Version);
        Assert.Equal(17, state.Claims.Count);
        Assert.Single(state.Evidence);
        Assert.Equal(20, (await File.ReadAllLinesAsync(f.EventsPath)).Length);
    }

    [Fact]
    public async Task ClaimsOnlyAndEvidenceOnlyMarkSingletonGroupsAndCanRetryAtTheCap()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var claims = await f.RecordAsync(FindingsFixture.Claims());
        var evidenceRequest = new FindingsRequest(1, "evidence-only", [],
            [new("e", "probe", "fixture", "observed", [new(ClaimId: claims.Receipt!.Findings[0].ClaimId)], [])]);
        var evidence = await f.RecordAsync(evidenceRequest);
        Assert.Null(evidence.Error);
        var lines = await File.ReadAllLinesAsync(f.EventsPath);
        foreach (var line in lines.Skip(2))
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal(0, doc.RootElement.GetProperty("_ailedgerCommandEventIndex").GetInt32());
            Assert.Equal(1, doc.RootElement.GetProperty("_ailedgerCommandEventCount").GetInt32());
        }
        // Even lowering configured mutation budgets below historical size cannot revoke a receipt.
        var smaller = f.Service(cap: 1, bytes: 1);
        Assert.True((await smaller.RecordAsync(f.Binding, evidenceRequest, default)).Replayed);
        var capped = f.Service(cap: 4, bytes: new FileInfo(f.EventsPath).Length);
        Assert.True((await capped.RecordAsync(f.Binding, evidenceRequest, default)).Replayed);
        var failure = await capped.RecordAsync(f.Binding, FindingsFixture.Claims("new"), default);
        Assert.Equal("capacity_exceeded", failure.Error?.Code);
        Assert.Equal("not_committed", failure.Error?.CommitState);
        Assert.Equal(lines, await File.ReadAllLinesAsync(f.EventsPath));
    }

    [Fact]
    public async Task ByteCapIncludesReceiptMetadataAndRejectsTheWholeBatch()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        using var reference = new FindingsFixture();
        await reference.OpenAsync();
        var accepted = await reference.RecordAsync();
        var totalLength = new FileInfo(reference.EventsPath).Length;
        var metadataLength = System.Text.Encoding.UTF8.GetByteCount(FindingsReceiptEnvelope.Serialize(accepted.Receipt!));
        // Event bytes alone fit; adding the receipt crosses this cap.
        var result = await f.Service(bytes: totalLength - metadataLength / 2)
            .RecordAsync(f.Binding, FindingsFixture.Request(), default);
        Assert.Equal("capacity_exceeded", result.Error?.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
    }

    [Fact]
    public async Task AttemptJournalDistinguishesCommitReplayAndRefusalWithoutCopyingProse()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        Assert.Equal("collected", (await f.RecordAsync()).CollectionStatus);
        await f.RecordAsync();
        var bad = FindingsFixture.Request("bad") with { Evidence = [new("e", "test", "source", "SECRET PROSE",
            [new(ClaimId: "missing")], [])] };
        await f.RecordAsync(bad);
        var rows = await File.ReadAllLinesAsync(Path.Combine(f.Directory, "findings-attempts.jsonl"));
        Assert.Equal(3, rows.Length);
        Assert.DoesNotContain("SECRET PROSE", string.Join("", rows));
        using var commit = JsonDocument.Parse(rows[0]);
        using var retry = JsonDocument.Parse(rows[1]);
        using var refusal = JsonDocument.Parse(rows[2]);
        Assert.Equal("committed", commit.RootElement.GetProperty("outcome").GetString());
        Assert.True(retry.RootElement.GetProperty("replayed").GetBoolean());
        Assert.Equal(commit.RootElement.GetProperty("transaction_id").GetString(), retry.RootElement.GetProperty("transaction_id").GetString());
        Assert.Equal(JsonValueKind.Null, retry.RootElement.GetProperty("validation_ms").ValueKind);
        Assert.Equal("kernel_refused", refusal.RootElement.GetProperty("code").GetString());
        Assert.Equal("not_committed", refusal.RootElement.GetProperty("commit_state").GetString());
    }
}
