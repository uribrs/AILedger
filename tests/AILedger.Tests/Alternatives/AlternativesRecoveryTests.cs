using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Core.Contracts;
using AILedger.Core.Alternatives;
using AILedger.Storage.Alternatives;
using AILedger.Storage.Findings;

namespace AILedger.Tests.Alternatives;

[Collection(AlternativesRecordingCollection.Name)]
public sealed class AlternativesRecoveryTests
{
    [Theory]
    [InlineData("mid-line")]
    [InlineData("complete-prefix")]
    [InlineData("missing-newline")]
    public async Task TornGroupsRemainInvisibleAndRetryRepairsWithoutDuplicates(string tear)
    {
        using var f = new AlternativesFixture();
        await f.OpenAsync();
        var old = await File.ReadAllBytesAsync(f.EventsPath);
        var faults = new FindingsStorageFaults(Write: async (stream, bytes, token) =>
        {
            var split = tear switch
            {
                "mid-line" => 37,
                "complete-prefix" => Array.IndexOf(bytes.ToArray(), (byte)'\n') + 1,
                _ => bytes.Length - 1
            };
            await stream.WriteAsync(bytes[..split], token);
            stream.Flush(true);
            throw new IOException("Simulated interrupted append");
        });
        var failed = await f.Faulted(faults).RecordAlternativesAsync(f.Binding, AlternativesFixture.Request(), default);
        Assert.Equal("outcome_unknown", failed.Error?.Code);
        Assert.Equal("unknown", failed.Error?.CommitState);
        Assert.Empty((await f.StateAsync()).Alternatives);
        Assert.Equal(2, (await f.StateAsync()).Version);
        var retry = await f.RecordAsync();
        Assert.Null(retry.Error);
        Assert.False(retry.Replayed);
        Assert.Equal(2, (await f.StateAsync()).Alternatives.Count);
        Assert.Equal(4, (await File.ReadAllLinesAsync(f.EventsPath)).Length);
        Assert.Equal(old, (await File.ReadAllBytesAsync(f.EventsPath))[..old.Length]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteWriteWithFailedFlushRecoversOriginalReceiptAndReflushes(bool cancel)
    {
        using var f = new AlternativesFixture();
        await f.OpenAsync();
        var faults = new FindingsStorageFaults(Flush: _ =>
        {
            if (cancel) throw new OperationCanceledException("Cancelled after write");
            throw new IOException("Flush failed after write");
        });
        var failed = await f.Faulted(faults).RecordAlternativesAsync(f.Binding, AlternativesFixture.Request(), default);
        Assert.Equal("outcome_unknown", failed.Error?.Code);
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        using var doc = JsonDocument.Parse((await File.ReadAllLinesAsync(f.EventsPath))[2]);
        var original = doc.RootElement.GetProperty("_ailedgerAlternativesReceipt").GetRawText();
        var failedRetry = await f.Faulted(faults).RecordAlternativesAsync(f.Binding, AlternativesFixture.Request(), default);
        Assert.Equal("outcome_unknown", failedRetry.Error?.Code);
        Assert.Null(failedRetry.Receipt);
        var retry = await f.RecordAsync();
        Assert.True(retry.Replayed);
        Assert.Equal(original, AlternativesReceiptEnvelope.Serialize(retry.Receipt!));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
    }

    [Fact]
    public async Task CancellationBeforeWriteProvesNoNewCommitAndCanRetry()
    {
        using var f = new AlternativesFixture();
        await f.OpenAsync();
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var faults = new FindingsStorageFaults(BeforeWrite: () => throw new OperationCanceledException("Before write"));
        var result = await f.Faulted(faults).RecordAlternativesAsync(f.Binding, AlternativesFixture.Request(), default);
        Assert.Equal("storage_unavailable", result.Error?.Code);
        Assert.Equal("not_committed", result.Error?.CommitState);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
        Assert.Null((await f.RecordAsync()).Error);
    }

    [Fact]
    public async Task CancellationBeforeLookupCannotClaimNoEarlierCommit()
    {
        using var f = new AlternativesFixture();
        await f.OpenAsync();
        await f.RecordAsync();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var result = await f.Service().RecordAlternativesAsync(f.Binding, AlternativesFixture.Request(), cancelled.Token);
        Assert.Equal("unknown", result.Error?.CommitState);
        Assert.Equal("unavailable", result.CollectionStatus);
        Assert.True((await f.RecordAsync()).Replayed);
    }

    [Fact]
    public async Task ProjectionAndTelemetryFailureDoNotChangeCommittedTruth()
    {
        using var f = new AlternativesFixture();
        await f.OpenAsync();
        System.IO.Directory.CreateDirectory(Path.Combine(f.Directory, "alternatives-attempts.jsonl"));
        File.Delete(Path.Combine(f.Directory, "state.json"));
        System.IO.Directory.CreateDirectory(Path.Combine(f.Directory, "state.json"));
        var result = await f.RecordAsync();
        Assert.Null(result.Error);
        Assert.Equal("unavailable", result.CollectionStatus);
        var retry = await f.RecordAsync();
        Assert.True(retry.Replayed);
        Assert.Equal(result.Receipt!.TransactionId, retry.Receipt!.TransactionId);
        Assert.Equal("unavailable", retry.CollectionStatus);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("algorithm")]
    [InlineData("fingerprint")]
    [InlineData("event")]
    [InlineData("map")]
    [InlineData("timestamp")]
    [InlineData("actor")]
    [InlineData("task")]
    [InlineData("run")]
    [InlineData("correlation")]
    [InlineData("causation")]
    [InlineData("ledger-version")]
    [InlineData("missing-field")]
    [InlineData("duplicate-field")]
    [InlineData("moved")]
    [InlineData("duplicate-receipt")]
    [InlineData("unmarked")]
    [InlineData("null")]
    public async Task CorruptCommittedMetadataFailsClosedWithoutRecommit(string corruption)
    {
        using var f = new AlternativesFixture();
        await f.OpenAsync();
        await f.RecordAsync();
        var lines = await File.ReadAllLinesAsync(f.EventsPath);
        var first = JsonNode.Parse(lines[2])!.AsObject();
        var receipt = first["_ailedgerAlternativesReceipt"]!.AsObject();
        switch (corruption)
        {
            case "version": receipt["schema_version"] = 9; break;
            case "algorithm": receipt["fingerprint_algorithm"] = "future"; break;
            case "fingerprint": receipt["payload_fingerprint"] = "bad"; break;
            case "event": receipt["event_ids"]![0] = "other"; break;
            case "map": receipt["alternatives"]![0]!["alternative_id"] = "AF_" + new string('a', 32); break;
            case "timestamp": receipt["committed_at"] = DateTimeOffset.UnixEpoch; break;
            case "actor": receipt["actor_id"] = "other"; break;
            case "task": receipt["task_id"] = "other"; break;
            case "run": receipt["run_id"] = "stable"; break; // matches correlation, but no such run
            case "correlation": receipt["correlation_id"] = "other"; break;
            case "causation": receipt["causation_id"] = "alternatives:0000000001"; break;
            case "ledger-version": receipt["ledger_version"] = 999; break;
            case "missing-field": receipt.Remove("run_id"); break;
            case "null": first["_ailedgerAlternativesReceipt"] = null; break;
            case "unmarked": first.Remove("_ailedgerCommandEventIndex"); first.Remove("_ailedgerCommandEventCount"); break;
            case "moved":
            case "duplicate-receipt":
                var last = JsonNode.Parse(lines[3])!.AsObject();
                last["_ailedgerAlternativesReceipt"] = receipt.DeepClone();
                lines[3] = last.ToJsonString();
                if (corruption == "moved") first.Remove("_ailedgerAlternativesReceipt");
                break;
        }
        lines[2] = first.ToJsonString();
        if (corruption == "duplicate-field") lines[2] = lines[2].Replace("\"schema_version\":1", "\"schema_version\":1,\"schema_version\":1");
        await File.WriteAllTextAsync(f.EventsPath, string.Join("\n", lines) + "\n");
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var retry = await f.RecordAsync();
        Assert.Equal("history_corrupt", retry.Error?.Code);
        Assert.Null(retry.Receipt);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
    }

    [Fact]
    public async Task DuplicateReceiptKeysAcrossOtherwiseValidGroupsFailClosed()
    {
        using var f = new AlternativesFixture();
        await f.OpenAsync();
        await f.RecordAsync(AlternativesFixture.Single("one"));
        await f.RecordAsync(AlternativesFixture.Single("two"));
        var lines = await File.ReadAllLinesAsync(f.EventsPath);
        var last = JsonNode.Parse(lines[3])!;
        last["_ailedgerAlternativesReceipt"]!["request_id"] = "one";
        lines[3] = last.ToJsonString();
        await File.WriteAllTextAsync(f.EventsPath, string.Join("\n", lines) + "\n");
        var result = await f.RecordAsync(AlternativesFixture.Single("one"));
        Assert.Equal("history_corrupt", result.Error?.Code);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("not-json")]
    public async Task InvalidInteriorEventShapesReturnHistoryCorrupt(string invalid)
    {
        using var f = new AlternativesFixture();
        await f.OpenAsync();
        await File.AppendAllTextAsync(f.EventsPath, invalid + "\n");
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        Assert.Equal("history_corrupt", (await f.RecordAsync()).Error?.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
    }

    [Fact]
    public async Task InteriorTornGroupIsCorruptionNotRecoverableTail()
    {
        using var f = new AlternativesFixture();
        await f.OpenAsync();
        await f.RecordAsync();
        await f.RecordAsync(AlternativesFixture.Single());
        var lines = (await File.ReadAllLinesAsync(f.EventsPath)).ToList();
        lines.RemoveAt(3);
        await File.WriteAllTextAsync(f.EventsPath, string.Join("\n", lines) + "\n");
        Assert.Equal("history_corrupt", (await f.RecordAsync()).Error?.Code);
    }
}
