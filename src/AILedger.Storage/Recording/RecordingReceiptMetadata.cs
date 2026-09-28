using System.Text.Json;
using AILedger.Storage.Alternatives;
using AILedger.Storage.Findings;
using AILedger.Storage.Artifacts;
using AILedger.Storage.ClaimDispositions;

namespace AILedger.Storage;

// Both operations share an envelope inspection; replay must not parse the same JSON separately
// for every supported receipt. The typed envelope validators still own their distinct contracts.
internal static class RecordingReceiptMetadata
{
    internal static (JsonElement? Findings, JsonElement? Alternatives, JsonElement? Artifact, JsonElement? Dispositions) Read(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Expected event object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!names.Add(property.Name)) throw new JsonException("Duplicate event envelope property.");
        return (root.TryGetProperty(FindingsReceiptEnvelope.PropertyName, out var findings) ? findings.Clone() : null,
            root.TryGetProperty(AlternativesReceiptEnvelope.PropertyName, out var alternatives) ? alternatives.Clone() : null,
            root.TryGetProperty(ArtifactSubmissionReceiptEnvelope.PropertyName, out var artifact) ? artifact.Clone() : null,
            root.TryGetProperty(ClaimDispositionsReceiptEnvelope.PropertyName, out var dispositions) ? dispositions.Clone() : null);
    }
}
