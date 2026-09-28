using System.Globalization;
using System.Text;
using System.Text.Json;
using AILedger.Core.Findings;

namespace AILedger.Cli.Findings;

// JsonDocument retains duplicate members. Inspect them before constructing any typed request.
internal static class FindingsRequestParser
{
    internal static FindingsRequest Parse(JsonElement body)
    {
        if (Encoding.UTF8.GetByteCount(body.GetRawText()) > FindingsValidation.MaximumBodyBytes)
            throw new FindingsRequestException("invalid_request", "The request exceeds 256 KiB.");
        StrictJson.Validate(body);
        StrictJson.Members(body, ["schema_version", "request_id", "findings", "evidence"]);
        var version = body.GetProperty("schema_version");
        if (!IsVersionOne(version))
            throw new JsonException("Expected schema_version 1.");
        var findings = Array(body.GetProperty("findings"), 32).Select(Finding).ToArray();
        var evidence = Array(body.GetProperty("evidence"), 64).Select(Evidence).ToArray();
        return FindingsValidation.Snapshot(new(1, StrictJson.Text(body, "request_id"), findings, evidence));
    }

    private static bool IsVersionOne(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number) return false;
        var number = value.GetRawText();
        var exponentAt = number.IndexOfAny(['e', 'E']);
        var mantissa = exponentAt < 0 ? number : number[..exponentAt];
        var exponent = 0;
        if (exponentAt >= 0 && !int.TryParse(number.AsSpan(exponentAt + 1), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out exponent)) return false;
        var dot = mantissa.IndexOf('.');
        var scale = dot < 0 ? 0 : mantissa.Length - dot - 1;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        var significant = digits.TrimEnd('0');
        // Do not round a value such as 1.00000000000000000000000000001 to version 1.
        return significant == "1" && (long)exponent + digits.Length - significant.Length - scale == 0;
    }

    private static FindingInput Finding(JsonElement item)
    {
        StrictJson.Members(item, ["key", "statement"], ["consequence_if_wrong", "from_lesson"]);
        return new(StrictJson.Text(item, "key"), StrictJson.Text(item, "statement"),
            OptionalText(item, "consequence_if_wrong"), OptionalText(item, "from_lesson"));
    }

    private static EvidenceInput Evidence(JsonElement item)
    {
        StrictJson.Members(item, ["key", "source_type", "citation", "summary", "supports", "refutes"]);
        return new(StrictJson.Text(item, "key"), StrictJson.Text(item, "source_type"),
            StrictJson.Text(item, "citation"), StrictJson.Text(item, "summary"),
            Array(item.GetProperty("supports"), 64).Select(Reference).ToArray(),
            Array(item.GetProperty("refutes"), 64).Select(Reference).ToArray());
    }

    private static FindingReference Reference(JsonElement item)
    {
        StrictJson.Members(item, [], ["finding", "claim_id"]);
        if (item.EnumerateObject().Count() != 1) throw new JsonException("Expected exactly one reference target.");
        return item.TryGetProperty("finding", out _) ? new(Finding: StrictJson.Text(item, "finding"))
            : new(ClaimId: StrictJson.Text(item, "claim_id"));
    }

    private static JsonElement.ArrayEnumerator Array(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximum)
            throw new JsonException($"Expected an array with at most {maximum} items.");
        return value.EnumerateArray();
    }

    private static string? OptionalText(JsonElement item, string name) =>
        !item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null
            ? null : StrictJson.Text(item, name);
}

internal static class StrictJson
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        // System.Text.Json can otherwise defer invalid UTF-8 handling until strings are decoded.
        Utf8.GetCharCount(bytes.Span);
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
    }

    internal static void Validate(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new JsonException("Duplicate JSON property.");
                    Validate(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray()) Validate(item);
                break;
            case JsonValueKind.String:
                _ = value.GetString(); // Reject invalid escaped surrogate sequences, without replacement.
                break;
        }
    }

    internal static void Members(JsonElement value, string[] required, string[]? optional = null)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException("Expected an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name) || !required.Contains(property.Name) &&
                !(optional?.Contains(property.Name) ?? false))
                throw new JsonException("Unknown or duplicate JSON property.");
        if (required.Any(name => !names.Contains(name))) throw new JsonException("Missing required property.");
    }

    internal static string Text(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var item) || item.ValueKind != JsonValueKind.String)
            throw new JsonException("Expected a string property.");
        return item.GetString()!;
    }
}
