using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Cli;
using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Storage;
using AILedger.Tests.Alternatives;
using AILedger.Tests.Support;

namespace AILedger.Tests.Artifacts.Submission;

[Collection(AlternativesRecordingCollection.Name)]
public sealed class ArtifactSubmissionBoundaryTests
{
    [Fact]
    public async Task ContentLimitAndEventCapacityAreCheckedBeforeCommitButDoNotRevokeReceipts()
    {
        using var f = new ArtifactSubmissionFixture(); await f.OpenAsync();
        var prefix = ArtifactCommands.VerifierBody;
        var request = ArtifactSubmissionFixture.Request() with { Content = prefix + new string('a', 128 * 1024 - System.Text.Encoding.UTF8.GetByteCount(prefix)) };
        var version = (await f.StateAsync()).Version;
        Assert.Equal("capacity_exceeded", (await f.Service((int)version).SubmitArtifactAsync(f.Binding, request, default)).Error?.Code);
        Assert.Equal("capacity_exceeded", (await f.Service(bytes: new FileInfo(f.EventsPath).Length).SubmitArtifactAsync(f.Binding, request, default)).Error?.Code);
        var accepted = await f.SubmitAsync(request); Assert.Null(accepted.Error);
        Assert.Equal(128 * 1024, accepted.Receipt!.Artifact.ContentBytes);
        Assert.True((await f.Service((int)version, 1).SubmitArtifactAsync(f.Binding, request, default)).Replayed);
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        Assert.Equal("invalid_request", (await f.SubmitAsync(request with { RequestId = "too-large", Content = request.Content + "a" })).Error?.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", ArtifactSubmissionIdentity.ContentHash("abc"));
    }

    [Fact]
    public async Task DuplicateKeysAcrossValidRevisionsFailClosed()
    {
        using var f = new ArtifactSubmissionFixture(); await f.OpenAsync();
        var first = (await f.SubmitAsync()).Receipt!;
        Assert.Null((await f.SubmitAsync(ArtifactSubmissionFixture.Request("second") with { SupersedesArtifactId = first.Artifact.ArtifactId })).Error);
        var lines = await File.ReadAllLinesAsync(f.EventsPath); var last = JsonNode.Parse(lines[^1])!;
        last["_ailedgerArtifactSubmissionReceipt"]!["request_id"] = "output-1"; lines[^1] = last.ToJsonString();
        await File.WriteAllTextAsync(f.EventsPath, string.Join("\n", lines) + "\n");
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        Assert.Equal("history_corrupt", (await f.SubmitAsync()).Error?.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
    }

    [Fact]
    public async Task OptInReportLeavesDefaultReportAndLedgerUntouchedAndMarksMissingTelemetry()
    {
        using var f = new ArtifactSubmissionFixture(); await f.OpenAsync(); await f.SubmitAsync(); await f.SubmitAsync();
        var state = await f.StateAsync(); var history = await f.HistoryAsync();
        File.Delete(Path.Combine(f.Directory, "artifact_submission-attempts.jsonl"));
        var report = await f.Service().ReadArtifactSubmissionMeasurementAsync(state, history, null, default);
        Assert.Equal(1, report.CommittedTransactions); Assert.Null(report.ObservedApplicationAttempts);
        Assert.NotEmpty(report.CoverageGaps);
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var output = new StringWriter(); var error = new StringWriter(); var app = new CliApplication(output, error, _ => f.Service(),
            _ => throw new InvalidOperationException("No provider needed"), new AILedger.Core.Application.ContextAssembler());
        string[] command = ["retrospective", "build", "--root", f.Root, "--task", f.TaskId.Value];
        Assert.Equal(0, await app.RunAsync(command, default));
        var plain = JsonNode.Parse(output.ToString())!;
        output.GetStringBuilder().Clear();
        Assert.True(await app.RunAsync([.. command, "--artifacts"], default) == 0, error.ToString());
        var extended = JsonNode.Parse(output.ToString())!.AsObject();
        Assert.Equal(1, extended["artifactSubmissions"]!["committedTransactions"]!.GetValue<int>());
        Assert.True(extended.Remove("artifactSubmissions"));
        Assert.True(JsonNode.DeepEquals(plain, extended));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
    }
}
