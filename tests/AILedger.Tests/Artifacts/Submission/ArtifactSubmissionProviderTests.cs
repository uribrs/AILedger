using System.Text;
using System.Text.Json;
using AILedger.Cli.Findings;
using AILedger.Cli.Artifacts;
using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Tests.Alternatives;
using AILedger.Tests.Findings;

namespace AILedger.Tests.Artifacts.Submission;

[Collection(AlternativesRecordingCollection.Name)]
public sealed class ArtifactSubmissionProviderTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public async Task TrustedRelayAdvertisesSubmitsReconnectsAndRevokesWithoutAgentIdentityFields(string provider)
    {
        using var f = new ArtifactSubmissionFixture(); await f.OpenAsync(provider);
        var config = Configuration(f) with { Provider = provider };
        await using var host = new ProviderFindingsSession(config, f.Service(), "unused", [], _ => Task.FromResult(config));
        JsonElement receipt;
        using (var relay = new McpProcess(host.Endpoint.Arguments))
        {
            await ProviderFindingsSessionTests.InitializeAsync(relay);
            await relay.SendAsync("""{"jsonrpc":"2.0","id":8,"method":"tools/list"}""");
            var tools = (await relay.ReadAsync()).GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
            Assert.Equal(new[] { "record_findings", "record_alternatives", "submit_artifact" }, tools.Select(t => t.GetProperty("name").GetString()));
            Assert.All(tools, t => Assert.Equal("object", t.GetProperty("outputSchema").GetProperty("type").GetString()));
            await relay.SendAsync(Call(Body()));
            var result = Payload(await relay.ReadAsync());
            receipt = result.GetProperty("receipt");
            Assert.Equal(f.Actor.Value, receipt.GetProperty("actor_id").GetString());
            Assert.Equal("RV", receipt.GetProperty("run_id").GetString());
            Assert.Equal("RV", receipt.GetProperty("correlation_id").GetString());
            Assert.Empty(await relay.FinishAsync());
        }
        using (var relay = new McpProcess(host.Endpoint.Arguments))
        {
            await ProviderFindingsSessionTests.InitializeAsync(relay);
            await relay.SendAsync(Call(Body()));
            var retry = Payload(await relay.ReadAsync());
            Assert.True(retry.GetProperty("replayed").GetBoolean());
            Assert.Equal(receipt.GetRawText(), retry.GetProperty("receipt").GetRawText());
            config = config with { AllowSubmitArtifact = false };
            await relay.SendAsync(Call(Body(), 3));
            Assert.Equal("authorization_denied", Payload(await relay.ReadAsync()).GetProperty("error").GetProperty("code").GetString());
            config = config with { AllowSubmitArtifact = true, ActorId = "operator" };
            await relay.SendAsync(Call(Body(), 4));
            Assert.Equal("authorization_denied", Payload(await relay.ReadAsync()).GetProperty("error").GetProperty("code").GetString());
            Assert.Empty(await relay.FinishAsync());
        }
        var report = await f.Service().ReadArtifactSubmissionMeasurementAsync(await f.StateAsync(), await f.HistoryAsync(), config.DiagnosticsDirectory, default);
        Assert.Equal(1, report.CommittedTransactions); Assert.Equal(4, report.ObservedToolAttempts);
        Assert.Equal(2, report.ObservedApplicationAttempts);
        Assert.Single(report.Runs.Where(r => r.RunId == "RV"));
        Assert.All(report.Attempts.Where(a => a.CanonicalTransactionId is not null), a => Assert.Equal("RV", a.JoinedRunId));
        var rows = (await Task.WhenAll(Directory.GetFiles(config.DiagnosticsDirectory, "artifact_submission-transport-*.jsonl").Select(p => File.ReadAllLinesAsync(p)))).SelectMany(x => x);
        Assert.All(rows, row => Assert.Equal(provider, JsonDocument.Parse(row).RootElement.GetProperty("provider").GetString()));
    }

    [Theory]
    [InlineData("actor_id", "operator")]
    [InlineData("run_id", "other")]
    [InlineData("artifact_id", "caller-id")]
    [InlineData("work_item_id", "other")]
    [InlineData("candidate_id", "other")]
    [InlineData("content_reference", "/tmp/untrusted.md")]
    public async Task AgentCannotInjectHostFieldsOrExternalPaths(string field, string value)
    {
        using var f = new ArtifactSubmissionFixture(); await f.OpenAsync();
        var body = System.Text.Json.Nodes.JsonNode.Parse(Body())!; body[field] = value;
        var response = await ExchangeAsync(f, body.ToJsonString());
        Assert.Equal("invalid_request", response.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain((await f.StateAsync()).Artifacts.Keys, id => id.Value.StartsWith("AS_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StrictShapeBoundsUnknownKindsAndTelemetryFailureHaveTypedResults()
    {
        using var f = new ArtifactSubmissionFixture(); await f.OpenAsync();
        foreach (var body in new[] { Body().Replace("\"schema_version\":1", "\"schema_version\":1,\"schema_version\":1"),
            Body().Replace("\"schema_version\":1", "\"schema_version\":2"), "{}" })
            Assert.Equal("invalid_request", (await ExchangeAsync(f, body)).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("unsupported_artifact_kind", (await ExchangeAsync(f, Body().Replace("verifier-output", "prompt-contract"))).GetProperty("error").GetProperty("code").GetString());
        Directory.CreateDirectory(Path.Combine(f.Directory, "artifact_submission-attempts.jsonl"));
        var accepted = await ExchangeAsync(f, Body());
        Assert.Equal("committed", accepted.GetProperty("status").GetString());
        Assert.True((await f.SubmitAsync()).Replayed);
    }

    [Fact]
    public async Task LostResponseRemainsOneCommitAndRetryRecoversReceipt()
    {
        using var f = new ArtifactSubmissionFixture(); await f.OpenAsync();
        var configuration = Configuration(f);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(AlternativesMcpFixture.Initialize + "\n" + AlternativesMcpFixture.Ready + "\n" + Call(Body()) + "\n"));
        using var output = new LostResponseStream();
        await new FindingsMcpServer(new(configuration, f.Service(), _ => Task.FromResult(configuration)), input, output, TextWriter.Null).RunAsync(default);
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var retry = await ExchangeAsync(f, Body());
        Assert.True(retry.GetProperty("replayed").GetBoolean());
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
        var report = await f.Service().ReadArtifactSubmissionMeasurementAsync(await f.StateAsync(), await f.HistoryAsync(), configuration.DiagnosticsDirectory, default);
        Assert.Equal(1, report.CommittedTransactions);
        Assert.Contains(report.Attempts, a => a.Observation.ResponseDelivery == "failed" && a.CanonicalTransactionId is not null);
    }

    private sealed class LostResponseStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            Encoding.UTF8.GetString(buffer.Span).Contains("structuredContent", StringComparison.Ordinal)
                ? throw new IOException("Response lost after commit") : base.WriteAsync(buffer, cancellationToken);
    }

    internal static FindingsHostConfiguration Configuration(ArtifactSubmissionFixture f) => new(f.Root, f.TaskId.Value,
        f.Actor.Value, "RV", Path.Combine(f.Directory, "telemetry"), RunId: "RV", AllowRecordFindings: true,
        AllowRecordAlternatives: true, AllowSubmitArtifact: true);
    internal static string Body() => JsonSerializer.Serialize(new { schema_version = 1, request_id = "output-1",
        kind = "verifier-output", title = ArtifactSubmissionFixture.Request().Title, content = ArtifactSubmissionFixture.Request().Content });
    internal static string Call(string body, int id = 2) => "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":{\"name\":\"submit_artifact\",\"arguments\":" + body + "}}";
    private static async Task<JsonElement> ExchangeAsync(ArtifactSubmissionFixture f, string body)
    {
        var configuration = Configuration(f);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(AlternativesMcpFixture.Initialize + "\n" + AlternativesMcpFixture.Ready + "\n" + Call(body) + "\n"));
        using var output = new MemoryStream();
        await new FindingsMcpServer(new(configuration, f.Service(), _ => Task.FromResult(configuration)), input, output, TextWriter.Null).RunAsync(default);
        return Payload(JsonDocument.Parse(Encoding.UTF8.GetString(output.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1]).RootElement);
    }
    internal static JsonElement Payload(JsonElement rpc)
    {
        Assert.False(rpc.TryGetProperty("error", out _), rpc.GetRawText());
        var result = rpc.GetProperty("result"); var body = result.GetProperty("structuredContent");
        FindingsResponseSchema.AssertValid(body, "Artifacts.ResponseSchema");
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(body.GetRawText()),
            System.Text.Json.Nodes.JsonNode.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!)));
        Assert.Equal(body.GetProperty("status").GetString() == "error", result.GetProperty("isError").GetBoolean());
        return body.Clone();
    }
}
