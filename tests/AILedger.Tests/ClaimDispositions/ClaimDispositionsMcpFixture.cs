using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AILedger.Cli;
using AILedger.Cli.Findings;
using AILedger.Core.Findings;
using AILedger.Core.ClaimDispositions;
using AILedger.Tests.Findings;

namespace AILedger.Tests.ClaimDispositions;

internal sealed class ClaimDispositionsMcpFixture : IDisposable
{
    internal ClaimDispositionsFixture Ledger { get; } = new();
    internal FindingsHostConfiguration Configuration { get; set; }
    internal string ConfigurationPath => Path.Combine(Ledger.Root, "host.json");
    internal ClaimDispositionsMcpFixture()
    {
        Configuration = new(Ledger.Root, Ledger.TaskId.Value, Ledger.Actor.Value, "stable",
            Path.Combine(Ledger.Root, "transport"), AllowRecordFindings: true, AllowRunless: true, AllowRecordClaimDispositions: true);
    }
    internal Task OpenAsync() => Ledger.OpenAsync();
    internal static string Body(ClaimDispositionsRequest? request = null) => JsonSerializer.Serialize(request ?? ClaimDispositionsFixture.Request(),
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    internal static string Call(string body, int id = 2) =>
        "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":{\"name\":\"record_claim_dispositions\",\"arguments\":" + body + "}}";
    internal const string Initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""";
    internal const string Ready = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";

    internal Task SaveConfigurationAsync() => File.WriteAllTextAsync(ConfigurationPath,
        JsonSerializer.Serialize(Configuration, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));

    internal async Task<McpProcess> StartAsync()
    {
        await SaveConfigurationAsync();
        var process = new McpProcess(ConfigurationPath);
        await process.SendAsync(Initialize);
        var initialized = await process.ReadAsync();
        Assert.Equal("2025-11-25", initialized.GetProperty("result").GetProperty("protocolVersion").GetString());
        await process.SendAsync(Ready);
        return process;
    }

    internal async Task<JsonElement[]> ExchangeAsync(string messages, IFindingsRecorder? recorder = null,
        Stream? output = null, CancellationToken cancellationToken = default)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(Initialize + "\n" + Ready + "\n" + messages));
        using var ownOutput = new MemoryStream();
        var host = new FindingsMcpHost(Configuration, recorder ?? Ledger.Service(), _ => Task.FromResult(Configuration));
        await new FindingsMcpServer(host, input, output ?? ownOutput, TextWriter.Null).RunAsync(cancellationToken);
        return Encoding.UTF8.GetString(ownOutput.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(Parse).Skip(1).ToArray();
    }
    internal async Task<JsonElement> RecordAsync(string? body = null, IFindingsRecorder? recorder = null) =>
        Payload(Assert.Single(await ExchangeAsync(Call(body ?? Body()) + "\n", recorder)));
    internal static JsonElement Payload(JsonElement response)
    {
        Assert.False(response.TryGetProperty("error", out _), response.GetRawText());
        var result = response.GetProperty("result");
        var body = result.GetProperty("structuredContent");
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(body.GetRawText()),
            System.Text.Json.Nodes.JsonNode.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!)));
        Assert.Equal(body.GetProperty("status").GetString() == "error", result.GetProperty("isError").GetBoolean());
        FindingsResponseSchema.AssertValid(body, "ClaimDispositions.ResponseSchema");
        return body;
    }
    internal static JsonElement Parse(string text)
    {
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }
    internal async Task<JsonElement[]> AttemptsAsync() => (await Task.WhenAll(
        Directory.EnumerateFiles(Configuration.DiagnosticsDirectory).Select(path => File.ReadAllLinesAsync(path))))
        .SelectMany(lines => lines).Select(Parse).ToArray();
    public void Dispose() => Ledger.Dispose();
}
