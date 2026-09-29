using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AILedger.Cli.Findings;
using AILedger.Core.Findings;

namespace FindingsPilot;

internal sealed record CallObservation(string RequestId, double ElapsedMs, bool ResponseLost, JsonElement? Response);

internal sealed class PilotTransport(PilotLedger ledger)
{
    internal List<CallObservation> Calls { get; } = [];
    private const string Initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"prepared-pilot","version":"1"}}}""";
    private const string Ready = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";

    internal async Task<JsonElement?> CallAsync(FindingsRequest request, CancellationToken token, bool loseResponse = false)
    {
        var wire = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 2, method = "tools/call",
            @params = new { name = "record_findings", arguments = request } }, PilotJson.Wire);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(Initialize + "\n" + Ready + "\n" + wire + "\n"));
        using var output = new ResponseStream(loseResponse);
        using var error = new StringWriter();
        var configuration = ledger.Configuration;
        // A fresh real server/recorder on every exchange; no test-side receipt cache.
        var host = new FindingsMcpHost(configuration, ledger.Service(), _ => Task.FromResult(configuration));
        var started = Stopwatch.GetTimestamp();
        await new FindingsMcpServer(host, input, output, error).RunAsync(token);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var responses = Encoding.UTF8.GetString(output.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line)).ToArray();
        JsonElement? payload = null;
        try
        {
            PilotJson.Require(!responses[0].RootElement.TryGetProperty("error", out _), "MCP initialization failed.");
            foreach (var response in responses.Skip(1))
            {
                PilotJson.Require(!response.RootElement.TryGetProperty("error", out _), "Unexpected protocol error.");
                payload = response.RootElement.GetProperty("result").GetProperty("structuredContent").Clone();
            }
            PilotJson.Require(loseResponse ? output.Lost && payload is null : payload is not null, "Unexpected response delivery.");
        }
        finally { foreach (var response in responses) response.Dispose(); }
        Calls.Add(new(request.RequestId, elapsed, loseResponse, payload));
        await PilotJson.SaveAsync(Path.Combine(ledger.Root, "calls.json"), Calls, token);
        await File.AppendAllTextAsync(Path.Combine(ledger.Root, "transport-errors.log"), error.ToString(), token);
        return payload;
    }

    internal static void Committed(JsonElement? response, bool replay = false)
    {
        PilotJson.Require(response is { } value && value.GetProperty("status").GetString() == "committed" &&
            value.GetProperty("replayed").GetBoolean() == replay, "Expected committed response with correct replay flag.");
    }

    internal static void Refused(JsonElement? response, string code)
    {
        PilotJson.Require(response is { } value && value.GetProperty("status").GetString() == "error" &&
            value.GetProperty("error").GetProperty("code").GetString() == code, "Expected refusal " + code);
    }

    private sealed class ResponseStream(bool lose) : MemoryStream
    {
        internal bool Lost { get; private set; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (lose && Encoding.UTF8.GetString(buffer.Span).Contains("structuredContent", StringComparison.Ordinal))
            {
                Lost = true;
                throw new IOException("Controlled pilot response loss after application completion.");
            }
            return base.WriteAsync(buffer, cancellationToken);
        }
    }
}
