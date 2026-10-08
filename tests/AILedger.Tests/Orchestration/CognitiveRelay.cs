using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Tests.Findings;
namespace AILedger.Tests.Orchestration;

internal sealed class CognitiveRelay : IDisposable
{
    private readonly McpProcess _relay;
    private int _id = 2;
    private CognitiveRelay(McpProcess relay) => _relay = relay;
    internal static async Task<CognitiveRelay> OpenAsync(ProviderFindingsEndpoint endpoint)
    {
        var relay = new McpProcess(endpoint.Arguments.Skip(1).ToArray());
        await ProviderFindingsSessionTests.InitializeAsync(relay);
        return new(relay);
    }
    internal async Task<JsonElement> CallAsync(string name, object arguments)
    {
        await _relay.SendAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = _id++, method = "tools/call", @params = new { name, arguments } }));
        var response = await _relay.ReadAsync();
        Assert.False(response.TryGetProperty("error", out _), response.GetRawText());
        return response.GetProperty("result").GetProperty("structuredContent").Clone();
    }
    internal async Task<JsonElement> ToolsAsync()
    {
        await _relay.SendAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = _id++, method = "tools/list" }));
        var response = await _relay.ReadAsync();
        Assert.False(response.TryGetProperty("error", out _), response.GetRawText());
        return response.GetProperty("result").GetProperty("tools").Clone();
    }
    internal Task<JsonElement> HandoffAsync(object operation, string? key = null) =>
        CallAsync("cognitive_handoff", new { request_id = key ?? Guid.NewGuid().ToString("N"), operation });
    internal async Task<string> EvidenceAsync()
    {
        var reply = await CallAsync("record_findings", new { schema_version = 1, request_id = Guid.NewGuid().ToString("N"),
            findings = Array.Empty<object>(), evidence = new[] { new { key = "e", source_type = "fixture", citation = "fixture://observed",
                summary = "Bounded fixture observation", supports = Array.Empty<object>(), refutes = Array.Empty<object>() } } });
        Assert.Equal("committed", reply.GetProperty("status").GetString());
        return reply.GetProperty("receipt").GetProperty("evidence")[0].GetProperty("evidence_id").GetString()!;
    }
    internal async Task AssessAsync(CognitiveWorkKind work, string disposition = "proceed")
    {
        var evidence = await EvidenceAsync();
        var result = await HandoffAsync(new { kind = "routing_assessment", work = JsonNamingPolicy.SnakeCaseLower.ConvertName(work.ToString()),
            disposition, rationale = "Evidence-backed fixture judgment; not acceptance", evidence_ids = new[] { evidence } });
        Assert.True(result.GetProperty("status").GetString() == "recorded", result.GetRawText());
    }
    internal async Task FinishAsync() => Assert.Empty(await _relay.FinishAsync());
    public void Dispose() => _relay.Dispose();
}
