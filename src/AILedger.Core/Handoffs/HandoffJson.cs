using System.Text.Json;
using System.Text.Json.Serialization;
using AILedger.Core.Artifacts;

namespace AILedger.Core.Handoffs;

public static class HandoffJson
{
    public const int MaximumRequestBytes = 512 * 1024;
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 48
    };

    public static HandoffRequest Parse(string json) => ParseDocument<HandoffRequest>(json);
    public static AILedger.Core.Inspection.RetrievalQuery ParseRetrieval(string json) =>
        ParseDocument<AILedger.Core.Inspection.RetrievalQuery>(json);

    public static T ParseDocument<T>(string json) where T : class
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumRequestBytes)
            throw new ArgumentException("Handoff request exceeds 512 KiB.");
        try
        {
            using var document = JsonDocument.Parse(json, new() { MaxDepth = 48 });
            CheckProperties(document.RootElement);
            return JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new ArgumentException("Handoff request is required.");
        }
        catch (JsonException e) { throw new ArgumentException("Invalid handoff JSON: " + e.Message, e); }
    }

    public static PreparedHandoff Seal(HandoffPackage package)
    {
        var json = JsonSerializer.Serialize(package, Options);
        var bytes = System.Text.Encoding.UTF8.GetByteCount(json);
        if (bytes > package.Spec.Budget.MaximumPackageBytes)
            throw new ArgumentException($"Package is {bytes} bytes, above its budget. Curate omissions explicitly; nothing was truncated.");
        return new(1, ArtifactSubmissionIdentity.ContentHash(json), bytes, json);
    }

    private static void CheckProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ArgumentException("Duplicate JSON property: " + property.Name);
                CheckProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) CheckProperties(child);
    }
}
