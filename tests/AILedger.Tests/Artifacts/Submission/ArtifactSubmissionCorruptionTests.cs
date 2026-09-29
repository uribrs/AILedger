using System.Text.Json.Nodes;
using AILedger.Tests.Alternatives;

namespace AILedger.Tests.Artifacts.Submission;

[Collection(AlternativesRecordingCollection.Name)]
public sealed class ArtifactSubmissionCorruptionTests
{
    [Theory]
    [InlineData("null-artifact")]
    [InlineData("null-provenance")]
    [InlineData("missing-artifact-id")]
    [InlineData("digest")]
    [InlineData("body")]
    [InlineData("size")]
    [InlineData("producer")]
    [InlineData("scope")]
    [InlineData("candidate")]
    [InlineData("event")]
    [InlineData("binding")]
    [InlineData("null")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unmarked")]
    public async Task DamagedContentOrReceiptFailsClosed(string corruption)
    {
        using var f = new ArtifactSubmissionFixture(); await f.OpenAsync(); await f.SubmitAsync();
        var lines = await File.ReadAllLinesAsync(f.EventsPath);
        var node = JsonNode.Parse(lines[^1])!;
        var receipt = node["_ailedgerArtifactSubmissionReceipt"]!;
        var artifact = receipt["artifact"]!;
        switch (corruption)
        {
            case "null-artifact": node["data"]!["artifact"] = null; break;
            case "null-provenance": node["data"]!["artifact"]!["provenance"] = null; break;
            case "missing-artifact-id": node["data"]!["artifact"]!.AsObject().Remove("artifactId"); break;
            case "digest": artifact["content_sha256"] = new string('0', 64); break;
            case "size": artifact["content_bytes"] = 1; break;
            case "producer": artifact["producer_run_id"] = "other"; break;
            case "scope": artifact["covered_work_item_ids"]![0] = "other"; break;
            case "candidate": artifact["candidate_id"] = new string('a', 64); break;
            case "event": receipt["event_ids"]![0] = "other"; break;
            case "binding": receipt["actor_id"] = "operator"; break;
            case "null": node["_ailedgerArtifactSubmissionReceipt"] = null; break;
            case "missing": artifact.AsObject().Remove("candidate_id"); break;
            case "unmarked": node.AsObject().Remove("_ailedgerCommandEventIndex"); node.AsObject().Remove("_ailedgerCommandEventCount"); break;
        }
        lines[^1] = node.ToJsonString();
        if (corruption == "duplicate") lines[^1] = lines[^1].Replace("\"content_bytes\":", "\"candidate_id\":null,\"content_bytes\":");
        if (corruption == "body") lines[^1] = lines[^1].Replace("Literal", "Changed");
        await File.WriteAllTextAsync(f.EventsPath, string.Join("\n", lines) + "\n");
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var result = await f.SubmitAsync();
        Assert.Equal("history_corrupt", result.Error?.Code);
        Assert.Null(result.Receipt);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
    }
}
