using System.Text.Json;
using AILedger.Core.Contracts;

namespace AILedger.Providers.Adapters;

internal static class ProviderProtocol
{
    public static ProviderEvent ParseCodex(long sequence, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = RequireObject(document);
        var type = GetString(root, "type") ?? "unknown";
        var sessionId = type == "thread.started" ? GetString(root, "thread_id") : null;
        var isTerminal = type is "turn.completed" or "turn.failed" or "error";
        var isError = type is "turn.failed" or "error";
        return new ProviderEvent(sequence, type, json, sessionId, isTerminal, isError);
    }

    public static ProviderEvent ParseClaude(long sequence, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = RequireObject(document);
        var type = GetString(root, "type") ?? "unknown";
        var sessionId = GetString(root, "session_id");
        var isTerminal = type == "result";
        var subtype = GetString(root, "subtype");
        var isError = GetBoolean(root, "is_error") || subtype?.Contains("error", StringComparison.OrdinalIgnoreCase) == true;
        return new ProviderEvent(sequence, type, json, sessionId, isTerminal, isError);
    }

    public static string? ReadString(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        return GetString(RequireObject(document), property);
    }

    public static string? ReadNestedString(string json, string parentProperty, string property)
    {
        using var document = JsonDocument.Parse(json);
        var root = RequireObject(document);
        return root.TryGetProperty(parentProperty, out var parent) && parent.ValueKind == JsonValueKind.Object
            ? GetString(parent, property)
            : null;
    }

    /// <summary>
    /// A protocol line has to be a JSON object. A line that parses as a number, a string, an array
    /// or a literal is as invalid as one that does not parse at all, and this reports it the same
    /// way — as a <see cref="JsonException"/>, the exception the drain already treats as a
    /// malformed line.
    /// </summary>
    /// <remarks>
    /// Without this, <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/> threw
    /// <see cref="InvalidOperationException"/> from inside the line callback, which catches only
    /// <see cref="JsonException"/>. Nothing between there and the launcher's catch-all held it, so
    /// one such line killed the child and discarded every event already collected — seven runs in
    /// this ledger. Strictness is unchanged: the line is still rejected and still reported. What
    /// changes is that rejecting one line no longer destroys the run around it.
    /// </remarks>
    private static JsonElement RequireObject(JsonDocument document)
    {
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException(
                $"A provider protocol line must be a JSON object; this line is a {root.ValueKind} value.");
        }

        return root;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();
}
