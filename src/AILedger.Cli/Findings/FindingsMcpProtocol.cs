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

    internal static object Tools(bool alternatives = false, bool artifacts = false, bool dispositions = false, bool inspection = false, IReadOnlyList<string>? assurance = null, bool includeFindings = true, bool producerOutcome = false)
    {
        var tools = new List<object>();
        if (includeFindings) tools.Add(FindingsTool());
        if (producerOutcome) tools.Add(ProducerOutcomeTool());
        if (assurance is not null) tools.AddRange(assurance.Select(AILedger.Cli.Assurance.AssuranceTools.Describe));
        if (alternatives) tools.Add(AlternativesTool());
        if (dispositions) tools.Add(DispositionsTool());
        if (artifacts) tools.Add(ArtifactTool());
        if (inspection)
            foreach (var name in new[] { "inspect_task", "retrieve_context", "check_readiness" })
                tools.Add(new { name, description = name == "check_readiness"
                    ? "Check a bounded proposed action against existing kernel validators without mutation. ready is a snapshot observation, never a grant; execution revalidates. Unsupported actions are explicit."
                    : "Read role-filtered task context and own receipts with ledger version, omissions and exact retrieval paths. Does not refresh the governed brief or repair projections.",
                    inputSchema = Schema("Inspection." + name),
                    annotations = new { readOnlyHint = true, destructiveHint = false, idempotentHint = true, openWorldHint = false } });
        return new { tools };
    }

    private static object ProducerOutcomeTool() => new
    {
        name = "declare_producer_outcome",
        description = "Worker/Researcher only: declare reported-complete, blocked or partial with existing own evidence IDs. One immutable declaration per host-bound run; retry identical arguments. Self-report only; never closes or accepts work.",
        inputSchema = JsonSerializer.Deserialize<JsonElement>("""
            {"type":"object","additionalProperties":false,"required":["outcome","output_evidence_ids","blocker_evidence_ids"],"properties":{"outcome":{"type":"string","enum":["reported-complete","blocked","partial"]},"output_evidence_ids":{"type":"array","maxItems":16,"items":{"type":"string","minLength":1,"maxLength":256}},"blocker_evidence_ids":{"type":"array","maxItems":16,"items":{"type":"string","minLength":1,"maxLength":256}}}}
            """),
        annotations = new { readOnlyHint = false, destructiveHint = false, idempotentHint = true, openWorldHint = false }
    };

    private static object ArtifactTool() => new
    {
        name = "submit_artifact", title = "Submit verification or review output",
        description = "Atomically submit one Markdown verifier-output or code-review-output with host-generated identity, " +
            "trusted producer and scope, content digest and version links. Keep the original key/body for retries. " +
            "Submission records authored output; it does not approve work or complete a run.",
        inputSchema = Schema("Artifacts.RequestSchema"), outputSchema = Schema("Artifacts.ResponseSchema"),
        annotations = new { readOnlyHint = false, destructiveHint = false, idempotentHint = true, openWorldHint = false }
    };

    private static object FindingsTool() => new
    {
        name = "record_findings", title = "Record findings and evidence",
        description = "Atomically record open claims and evidence in the host-bound task. " +
            "Keep the original request and binding for retries; outcome_unknown requires the same request_id and body.",
        inputSchema = Schema("RequestSchema"), outputSchema = Schema("ResponseSchema"),
        annotations = new { readOnlyHint = false, destructiveHint = false, idempotentHint = true, openWorldHint = false }
    };

    private static object DispositionsTool() => new
    {
        name = "record_claim_dispositions", title = "Record explicit claim dispositions",
        description = "Atomically record 1–32 explicit claim validations/rejections with rationale and existing evidence. Requires ResolveClaim authority; never proves judgment correctness. Preserve body/key for retries.",
        inputSchema = Schema("ClaimDispositions.RequestSchema"), outputSchema = Schema("ClaimDispositions.ResponseSchema"),
        annotations = new { readOnlyHint = false, destructiveHint = true, idempotentHint = true, openWorldHint = false }
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
        using var stream = typeof(FindingsMcpProtocol).Assembly.GetManifestResourceStream((name.StartsWith("Inspection.", StringComparison.Ordinal) || name.StartsWith("Alternatives.", StringComparison.Ordinal) || name.StartsWith("Artifacts.", StringComparison.Ordinal) || name.StartsWith("ClaimDispositions.", StringComparison.Ordinal)) ? name : "Findings." + name)
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
