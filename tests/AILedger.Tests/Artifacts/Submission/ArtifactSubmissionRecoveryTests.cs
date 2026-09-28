using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Core.Contracts;
using AILedger.Core.Artifacts;
using AILedger.Storage.Artifacts;
using AILedger.Storage.Findings;

namespace AILedger.Tests.Artifacts.Submission;

using AILedger.Tests.Alternatives;

[Collection(AlternativesRecordingCollection.Name)]
public sealed class ArtifactSubmissionRecoveryTests
{
    [Theory]
    [InlineData("mid-line")]
    [InlineData("missing-newline")]
    public async Task TornGroupsRemainInvisibleAndRetryRepairsWithoutDuplicates(string tear)
    {
        using var f = new ArtifactSubmissionFixture();
        await f.OpenAsync();
        var old = await File.ReadAllBytesAsync(f.EventsPath);
        var initial = await f.StateAsync();
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
        var failed = await f.Faulted(faults).SubmitArtifactAsync(f.Binding, ArtifactSubmissionFixture.Request(), default);
        Assert.Equal("outcome_unknown", failed.Error?.Code);
        Assert.Equal("unknown", failed.Error?.CommitState);
        Assert.Equal(initial.Artifacts.Count, (await f.StateAsync()).Artifacts.Count);
        Assert.Equal(initial.Version, (await f.StateAsync()).Version);
        var retry = await f.SubmitAsync();
        Assert.Null(retry.Error);
        Assert.False(retry.Replayed);
        Assert.Equal(initial.Artifacts.Count + 1, (await f.StateAsync()).Artifacts.Count);
        Assert.Equal(initial.Version + 1, (await File.ReadAllLinesAsync(f.EventsPath)).Length);
        Assert.Equal(old, (await File.ReadAllBytesAsync(f.EventsPath))[..old.Length]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteWriteWithFailedFlushRecoversOriginalReceiptAndReflushes(bool cancel)
    {
        using var f = new ArtifactSubmissionFixture();
        await f.OpenAsync();
        var faults = new FindingsStorageFaults(Flush: _ =>
        {
            if (cancel) throw new OperationCanceledException("Cancelled after write");
            throw new IOException("Flush failed after write");
        });
        var failed = await f.Faulted(faults).SubmitArtifactAsync(f.Binding, ArtifactSubmissionFixture.Request(), default);
        Assert.Equal("outcome_unknown", failed.Error?.Code);
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        using var doc = JsonDocument.Parse((await File.ReadAllLinesAsync(f.EventsPath))[^1]);
        var original = doc.RootElement.GetProperty("_ailedgerArtifactSubmissionReceipt").GetRawText();
        var failedRetry = await f.Faulted(faults).SubmitArtifactAsync(f.Binding, ArtifactSubmissionFixture.Request(), default);
        Assert.Equal("outcome_unknown", failedRetry.Error?.Code);
        Assert.Null(failedRetry.Receipt);
        var retry = await f.SubmitAsync();
        Assert.True(retry.Replayed);
        Assert.Equal(original, ArtifactSubmissionReceiptEnvelope.Serialize(retry.Receipt!));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
    }

    [Fact]
    public async Task CancellationBeforeWriteProvesNoNewCommitAndCanRetry()
    {
        using var f = new ArtifactSubmissionFixture();
        await f.OpenAsync();
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var faults = new FindingsStorageFaults(BeforeWrite: () => throw new OperationCanceledException("Before write"));
        var result = await f.Faulted(faults).SubmitArtifactAsync(f.Binding, ArtifactSubmissionFixture.Request(), default);
        Assert.Equal("storage_unavailable", result.Error?.Code);
        Assert.Equal("not_committed", result.Error?.CommitState);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
        Assert.Null((await f.SubmitAsync()).Error);
    }

    [Fact]
    public async Task CancellationBeforeLookupCannotClaimNoEarlierCommit()
    {
        using var f = new ArtifactSubmissionFixture();
        await f.OpenAsync();
        await f.SubmitAsync();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var result = await f.Service().SubmitArtifactAsync(f.Binding, ArtifactSubmissionFixture.Request(), cancelled.Token);
        Assert.Equal("unknown", result.Error?.CommitState);
        Assert.Equal("unavailable", result.CollectionStatus);
        Assert.True((await f.SubmitAsync()).Replayed);
    }

    [Fact]
    public async Task ProjectionAndTelemetryFailureDoNotChangeCommittedTruth()
    {
        using var f = new ArtifactSubmissionFixture();
        await f.OpenAsync();
        System.IO.Directory.CreateDirectory(Path.Combine(f.Directory, "artifact_submission-attempts.jsonl"));
        File.Delete(Path.Combine(f.Directory, "state.json"));
        System.IO.Directory.CreateDirectory(Path.Combine(f.Directory, "state.json"));
        var result = await f.SubmitAsync();
        Assert.Null(result.Error);
        Assert.Equal("unavailable", result.CollectionStatus);
        var retry = await f.SubmitAsync();
        Assert.True(retry.Replayed);
        Assert.Equal(result.Receipt!.TransactionId, retry.Receipt!.TransactionId);
        Assert.Equal("unavailable", retry.CollectionStatus);
    }

}
