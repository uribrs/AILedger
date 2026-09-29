using System.Text;
using System.Text.Json;
using AILedger.Core.Alternatives;
using AILedger.Core.Findings;
using AILedger.Cli.Findings;

namespace AILedger.Cli.Alternatives;

internal static class AlternativesRequestParser
{
    internal static AlternativesRequest Parse(JsonElement body)
    {
        if (Encoding.UTF8.GetByteCount(body.GetRawText()) > FindingsValidation.MaximumBodyBytes)
            throw new FindingsRequestException("invalid_request", "The request exceeds 256 KiB.");
        StrictJson.Validate(body);
        StrictJson.Members(body, ["schema_version", "request_id", "alternatives"]);
        if (!FindingsRequestParser.IsVersionOne(body.GetProperty("schema_version")))
            throw new JsonException("Expected schema_version 1.");
        var items = FindingsRequestParser.Array(body.GetProperty("alternatives"), 32).Select(Item).ToArray();
        return AlternativesValidation.Snapshot(new(1, StrictJson.Text(body, "request_id"), items));
    }

    private static AlternativeInput Item(JsonElement item)
    {
        StrictJson.Members(item, ["key", "statement", "rejection_rationale"],
            ["replaced_by_decision_id", "from_lesson"]);
        return new(StrictJson.Text(item, "key"), StrictJson.Text(item, "statement"),
            StrictJson.Text(item, "rejection_rationale"),
            FindingsRequestParser.OptionalText(item, "replaced_by_decision_id"),
            FindingsRequestParser.OptionalText(item, "from_lesson"));
    }
}
