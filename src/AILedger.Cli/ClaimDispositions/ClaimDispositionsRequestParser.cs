using System.Text;
using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.ClaimDispositions;
using AILedger.Core.Findings;
using AILedger.Cli.Findings;

namespace AILedger.Cli.ClaimDispositions;

internal static class ClaimDispositionsRequestParser
{
    internal static ClaimDispositionsRequest Parse(JsonElement body)
    {
        if (Encoding.UTF8.GetByteCount(body.GetRawText()) > FindingsValidation.MaximumBodyBytes)
            throw new FindingsRequestException("invalid_request", "The request exceeds 256 KiB.");
        StrictJson.Validate(body);
        StrictJson.Members(body, ["schema_version", "request_id", "dispositions"]);
        if (!FindingsRequestParser.IsVersionOne(body.GetProperty("schema_version")))
            throw new JsonException("Expected schema_version 1.");
        var items = FindingsRequestParser.Array(body.GetProperty("dispositions"), 32).Select((item, i) =>
        {
            try { return Item(item); }
            catch (Exception e) when (e is JsonException or InvalidOperationException)
            { throw new FindingsRequestException("invalid_request", "Expected claim, expected_status, status, rationale and typed evidence references; no attribution or extra fields.", $"dispositions[{i}]"); }
        }).ToArray();
        return ClaimDispositionsValidation.Snapshot(new(1, StrictJson.Text(body, "request_id"), items));
    }

    private static ClaimDispositionInput Item(JsonElement item)
    {
        StrictJson.Members(item, ["key", "claim", "expected_status", "status", "rationale", "evidence"]);
        var claim = item.GetProperty("claim");
        StrictJson.Members(claim, ["claim_id"]);
        var evidence = FindingsRequestParser.Array(item.GetProperty("evidence"), 32).Select(e =>
        {
            StrictJson.Members(e, ["evidence_id"]);
            return new DispositionEvidenceReference(StrictJson.Text(e, "evidence_id"));
        }).ToArray();
        return new(StrictJson.Text(item, "key"), new(StrictJson.Text(claim, "claim_id")),
            Status(StrictJson.Text(item, "expected_status")), Status(StrictJson.Text(item, "status")),
            StrictJson.Text(item, "rationale"), evidence);
    }

    private static ClaimStatus Status(string value) => value switch
    {
        "open" => ClaimStatus.Open, "validated" => ClaimStatus.Validated, "rejected" => ClaimStatus.Rejected,
        _ => throw new JsonException("Unsupported claim status.")
    };
}
