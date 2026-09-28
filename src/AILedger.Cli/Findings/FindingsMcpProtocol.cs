using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AILedger.Cli.Findings;

internal static class FindingsMcpProtocol
{
    internal const string Version = "2025-11-25";
    internal static object Initialize(JsonElement parameters)
    {
        StrictJson.Members(parameters, ["protocolVersion", "capabilities", "clientInfo"], ["_meta"]);
        StrictJson.Validate(parameters);
        var version = StrictJson.Text(parameters, "protocolVersion");
        if (parameters.GetProperty("capabilities").ValueKind != JsonValueKind.Object ||
            parameters.GetProperty("clientInfo").ValueKind != JsonValueKind.Object)
            throw new JsonException("Invalid initialization parameters.");
        _ = StrictJson.Text(parameters.GetProperty("clientInfo"), "name");
        _ = StrictJson.Text(parameters.GetProperty("clientInfo"), "version");
        return new { protocolVersion = version == "2025-06-18" ? version : Version,
            capabilities = new { tools = new { listChanged = false } },
            serverInfo = new { name = "ailedger-findings", version = "1.0.0" } };
    }

    internal static object Tools(bool alternatives = false) => new { tools = (alternatives
        ? new[] { FindingsTool(), AlternativesTool() } : new[] { FindingsTool() }) };

    private static object FindingsTool() => new
    {
        name = "record_findings", title = "Record findings and evidence",
        description = "Atomically record open claims and evidence in the host-bound task. " +
            "Keep the original request and binding for retries; outcome_unknown requires the same request_id and body.",
        inputSchema = Schema("RequestSchema"), outputSchema = Schema("ResponseSchema"),
        annotations = new { readOnlyHint = false, destructiveHint = false, idempotentHint = true, openWorldHint = false }
    };

    private static object AlternativesTool() => new
    {
        name = "record_alternatives", title = "Record rejected alternatives",
        description = "Atomically record the author's rejected approaches with generated IDs. " +
            "Retain the original request_id and body for retries; this grants no approval authority.",
        inputSchema = Schema("Alternatives.RequestSchema"), outputSchema = Schema("Alternatives.ResponseSchema"),
        annotations = new { readOnlyHint = false, destructiveHint = false, idempotentHint = true, openWorldHint = false }
    };

    private static JsonElement Schema(string name)
    {
        using var stream = typeof(FindingsMcpProtocol).Assembly.GetManifestResourceStream(name.StartsWith("Alternatives.", StringComparison.Ordinal) ? name : "Findings." + name)
            ?? throw new InvalidOperationException("Missing embedded findings schema.");
        using var document = JsonDocument.Parse(stream);
        var schema = JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
        // MCP requires an object output schema. Both frozen branches already require objects;
        // this adds only the redundant root type, preserving all v1 assertions and local refs.
        schema["type"] = "object";
        return JsonSerializer.SerializeToElement(schema);
    }

    internal static JsonElement? Id(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var id)) return null;
        if (id.ValueKind == JsonValueKind.String && id.GetString() is { Length: <= 128 } ||
            id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out _)) return id.Clone();
        throw new JsonException("Invalid request id.");
    }

    internal static string IdKey(JsonElement id)
    {
        if (id.ValueKind == JsonValueKind.String && id.GetString() is { Length: <= 128 } text)
            return "s:" + text;
        if (id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number))
            return "n:" + number.ToString(CultureInfo.InvariantCulture);
        throw new JsonException("Invalid request id.");
    }

    internal static object Result(JsonElement id, object result) => new { jsonrpc = "2.0", id, result };
    internal static object Error(JsonElement? id, int code, string message) =>
        new { jsonrpc = "2.0", id, error = new { code, message } };
}
