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
        var findings = Array(body.GetProperty("findings"), 32, "findings")
            .Select((item, index) => Finding(item, $"findings[{index}]")).ToArray();
        var evidence = Array(body.GetProperty("evidence"), 64, "evidence")
            .Select((item, index) => Evidence(item, $"evidence[{index}]")).ToArray();
        return FindingsValidation.Snapshot(new(1, StrictJson.Text(body, "request_id"), findings, evidence));
    }

    internal static bool IsVersionOne(JsonElement value)
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

    private static FindingInput Finding(JsonElement item, string path)
    {
        StrictJson.Members(item, ["key", "statement"], ["consequence_if_wrong", "from_lesson"], path);
        return new(StrictJson.Text(item, "key", path), StrictJson.Text(item, "statement", path),
            OptionalText(item, "consequence_if_wrong", path), OptionalText(item, "from_lesson", path));
    }

    private static EvidenceInput Evidence(JsonElement item, string path)
    {
        StrictJson.Members(item, ["key", "source_type", "citation", "summary", "supports", "refutes"], path: path);
        return new(StrictJson.Text(item, "key", path), StrictJson.Text(item, "source_type", path),
            StrictJson.Text(item, "citation", path), StrictJson.Text(item, "summary", path),
            Array(item.GetProperty("supports"), 64, path + ".supports")
                .Select((value, index) => Reference(value, $"{path}.supports[{index}]")).ToArray(),
            Array(item.GetProperty("refutes"), 64, path + ".refutes")
                .Select((value, index) => Reference(value, $"{path}.refutes[{index}]")).ToArray());
    }

    private static FindingReference Reference(JsonElement item, string path)
    {
        StrictJson.Members(item, [], ["finding", "claim_id"], path);
        if (item.EnumerateObject().Count() != 1)
            throw new JsonException("Expected exactly one reference target.", path, null, null);
        return item.TryGetProperty("finding", out _) ? new(Finding: StrictJson.Text(item, "finding", path))
            : new(ClaimId: StrictJson.Text(item, "claim_id", path));
    }

    internal static JsonElement.ArrayEnumerator Array(JsonElement value, int maximum, string path = "$")
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximum)
            throw new JsonException($"Expected an array with at most {maximum} items.", path, null, null);
        return value.EnumerateArray();
    }

    internal static string? OptionalText(JsonElement item, string name, string path = "") =>
        !item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null
            ? null : StrictJson.Text(item, name, path);
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

    internal static void Validate(JsonElement value, string path = "")
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    var location = PropertyPath(path, property.Name);
                    if (!names.Add(property.Name)) throw new JsonException("Duplicate JSON property.", location, null, null);
                    Validate(property.Value, location);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in value.EnumerateArray()) Validate(item, $"{path}[{index++}]");
                break;
            case JsonValueKind.String:
                _ = value.GetString(); // Reject invalid escaped surrogate sequences, without replacement.
                break;
        }
    }

    internal static void Members(JsonElement value, string[] required, string[]? optional = null, string path = "")
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException("Expected an object.", path, null, null);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name) || !required.Contains(property.Name) &&
                !(optional?.Contains(property.Name) ?? false))
                throw new JsonException($"Unknown or duplicate JSON property. Allowed properties: {string.Join(", ", required.Concat(optional ?? []))}.",
                    PropertyPath(path, property.Name), null, null);
        }
        foreach (var name in required)
            if (!names.Contains(name)) throw new JsonException("Missing required property.", PropertyPath(path, name), null, null);
    }

    internal static string Text(JsonElement value, string name, string path = "")
    {
        if (!value.TryGetProperty(name, out var item) || item.ValueKind != JsonValueKind.String)
            throw new JsonException("Expected a string property.", PropertyPath(path, name), null, null);
        return item.GetString()!;
    }

    // Diagnostics identify fields, never values, and cannot echo unbounded or control-bearing keys.
    private static string PropertyPath(string parent, string name)
    {
        var safe = name.Length is > 0 and <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            ? name : "[unrecognized-property]";
        return parent.Length == 0 ? safe : parent + "." + safe;
    }
}
