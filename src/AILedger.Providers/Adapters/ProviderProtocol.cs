using System.Text.Json;
using AILedger.Core.Contracts;

namespace AILedger.Providers.Adapters;

internal static class ProviderProtocol
{
    public static ProviderEvent ParseCodex(long sequence, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = GetString(root, "type") ?? "unknown";
        var sessionId = type == "thread.started" ? GetString(root, "thread_id") : null;
        var isTerminal = type is "turn.completed" or "turn.failed" or "error";
        var isError = type is "turn.failed" or "error";
        return new ProviderEvent(sequence, type, json, sessionId, isTerminal, isError);
    }

    public static ProviderEvent ParseClaude(long sequence, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
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
        return GetString(document.RootElement, property);
    }

    public static string? ReadNestedString(string json, string parentProperty, string property)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return root.TryGetProperty(parentProperty, out var parent) && parent.ValueKind == JsonValueKind.Object
            ? GetString(parent, property)
            : null;
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
