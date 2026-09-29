using System.Text.Json;
using System.Text.Json.Serialization;
using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Core.Findings;

namespace AILedger.Storage.Artifacts;

internal static class ArtifactSubmissionReceiptEnvelope
{
    internal const string PropertyName = "_ailedgerArtifactSubmissionReceipt";
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    internal static string Serialize(ArtifactSubmissionReceipt receipt) => JsonSerializer.Serialize(receipt, Json);

    internal static ArtifactSubmissionReceipt Validate(JsonElement metadata, IReadOnlyList<LedgerEvent> events, long version)
    {
        try
        {
            var r = metadata.Deserialize<ArtifactSubmissionReceipt>(Json);
            Require(r is not null && events.Count == 1 && events[0].Data is ArtifactRecorded { Artifact: not null }, "Expected one artifact and receipt.");
            var e = events[0];
            var artifact = ((ArtifactRecorded)e.Data).Artifact;
            Require(r!.SchemaVersion == 1 && r.FingerprintAlgorithm == ArtifactSubmissionIdentity.Algorithm &&
                FindingsValidation.IsRequestId(r.RequestId) && Hex(r.TransactionId, 32) &&
                ArtifactSubmissionIdentity.IsHash(r.PayloadFingerprint), "Invalid submission identity.");
            Require(r.LedgerVersion == version && r.CommittedAt == e.RecordedAt && r.EventIds is not null &&
                r.EventIds.SequenceEqual(events.Select(item => item.EventId.Value)), "Invalid submission events.");
            Require(!string.IsNullOrWhiteSpace(r.RunId) && r.RunId == r.CorrelationId &&
                r.TaskId == e.TaskId.Value && r.ActorId == e.ActorId.Value && r.CorrelationId == e.CorrelationId &&
                r.CausationId == e.CausationId?.Value && artifact.ProducerRunId?.Value == r.RunId &&
                artifact.Provenance is not null && artifact.Provenance.ActorId == e.ActorId && artifact.Provenance.RecordedAt == e.RecordedAt,
                "Submission attribution differs from its event.");
            Require(!string.IsNullOrWhiteSpace(artifact.ArtifactId.Value) && artifact.ArtifactId.Value.StartsWith("AS_", StringComparison.Ordinal) &&
                Hex(artifact.ArtifactId.Value[3..], 32), "Invalid generated artifact ID.");
            var expected = r with { Artifact = ArtifactSubmissionIdentity.Describe(r.TaskId, artifact) };
            // Recursive comparison requires every field, including nulls, rejects duplicate keys,
            // and checks the content digest and every accepted scope/version link against the event.
            using var canonical = JsonDocument.Parse(Serialize(expected));
            Require(Equal(metadata, canonical.RootElement), "Receipt content identity, links or shape differ from the event.");
            return expected with { EventIds = Array.AsReadOnly(r.EventIds!.ToArray()) };
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or FindingsRequestException)
        {
            throw new InvalidDataException("Invalid artifact submission receipt metadata.", e);
        }
    }

    private static bool Equal(JsonElement actual, JsonElement expected)
    {
        if (actual.ValueKind != expected.ValueKind) return false;
        if (expected.ValueKind == JsonValueKind.Object)
        {
            var properties = actual.EnumerateObject().ToArray();
            return properties.Length == expected.EnumerateObject().Count() &&
                properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == properties.Length &&
                properties.All(p => expected.TryGetProperty(p.Name, out var value) && Equal(p.Value, value));
        }
        if (expected.ValueKind == JsonValueKind.Array)
            return actual.GetArrayLength() == expected.GetArrayLength() &&
                actual.EnumerateArray().Zip(expected.EnumerateArray()).All(pair => Equal(pair.First, pair.Second));
        return actual.ValueKind == JsonValueKind.String ? actual.GetString() == expected.GetString() : actual.GetRawText() == expected.GetRawText();
    }
    private static bool Hex(string? value, int length) => value?.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
