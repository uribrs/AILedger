using System.Text.Json;
using System.Text.Json.Serialization;
using AILedger.Core.Contracts;
using AILedger.Core.Alternatives;
using AILedger.Core.Findings;

namespace AILedger.Storage.Alternatives;

internal static class AlternativesReceiptEnvelope
{
    internal const string PropertyName = "_ailedgerAlternativesReceipt";
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static string Serialize(AlternativesReceipt receipt) => JsonSerializer.Serialize(receipt, Json);

    internal static AlternativesReceipt Validate(JsonElement metadata, IReadOnlyList<LedgerEvent> events, long version)
    {
        try
        {
            ValidateJsonShape(metadata);
            var receipt = metadata.Deserialize<AlternativesReceipt>(Json);
            Require(receipt is not null, "Missing receipt.");
            var r = receipt!;
            Require(r.SchemaVersion == 1 && r.FingerprintAlgorithm == AlternativesFingerprint.Algorithm,
                "Unsupported alternatives receipt version or algorithm.");
            Require(FindingsValidation.IsRequestId(r.RequestId) && Hex(r.TransactionId, 32) &&
                Hex(r.PayloadFingerprint, 64), "Invalid receipt identity or fingerprint.");
            Require(r.LedgerVersion == version && r.CommittedAt != default &&
                r.EventIds is not null && r.Alternatives is not null,
                "Invalid receipt version or maps.");
            Require(r.EventIds!.SequenceEqual(events.Select(e => e.EventId.Value)) &&
                r.Alternatives!.Count is >= 1 and <= 32 && r.Alternatives.Count == events.Count, "Receipt events do not match group.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var map in r.Alternatives!)
            {
                Require(map is not null && FindingsValidation.IsKey(map.Key) && keys.Add(map.Key) &&
                    DurableId(map.AlternativeId, "AF_") && ids.Add(map.AlternativeId), "Invalid alternative map.");
                var e = events[index++];
                Require(e.EventId.Value == map!.EventId && e.Data is AlternativeRecorded c &&
                    c.Alternative.Id.Value == map.AlternativeId && c.Alternative.Provenance.ActorId == e.ActorId && c.Alternative.Provenance.RecordedAt == e.RecordedAt,
                    "Alternative map does not match its event.");
            }
            Require(!string.IsNullOrWhiteSpace(r.TaskId) && !string.IsNullOrWhiteSpace(r.ActorId) &&
                !string.IsNullOrWhiteSpace(r.CorrelationId) &&
                (r.RunId is null || !string.IsNullOrWhiteSpace(r.RunId) && r.CorrelationId == r.RunId),
                "Invalid receipt binding.");
            Require(events.All(e => e.TaskId.Value == r.TaskId && e.ActorId.Value == r.ActorId &&
                e.CorrelationId == r.CorrelationId && e.CausationId?.Value == r.CausationId &&
                e.RecordedAt == r.CommittedAt), "Receipt attribution does not match group.");
            return r with { EventIds = Array.AsReadOnly(r.EventIds!.ToArray()),
                Alternatives = Array.AsReadOnly(r.Alternatives!.ToArray()) };
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("Invalid alternatives receipt metadata.", e);
        }
    }

    // Required null-valued members are still required. A missing field must not quietly become
    // a deserializer default and turn a damaged binding into another valid receipt.
    private static void ValidateJsonShape(JsonElement value)
    {
        Require(value.ValueKind == JsonValueKind.Object, "Receipt must be an object.");
        string[] expected = ["schema_version", "request_id", "transaction_id", "task_id", "actor_id",
            "run_id", "correlation_id", "causation_id", "payload_fingerprint", "fingerprint_algorithm",
            "committed_at", "ledger_version", "event_ids", "alternatives"];
        var names = value.EnumerateObject().Select(p => p.Name).ToArray();
        Require(names.Length == expected.Length && names.ToHashSet().SetEquals(expected), "Invalid receipt members.");
        foreach (var name in new[] { "alternatives" })
        {
            Require(value.GetProperty(name).ValueKind == JsonValueKind.Array, "Invalid receipt maps.");
            foreach (var map in value.GetProperty(name).EnumerateArray())
            {
                Require(map.ValueKind == JsonValueKind.Object, "Invalid receipt map.");
                string[] fields = ["key", "alternative_id", "event_id"];
                var actual = map.EnumerateObject().Select(p => p.Name).ToArray();
                Require(actual.Length == 3 && actual.ToHashSet().SetEquals(fields), "Invalid map members.");
            }
        }
    }

    private static bool DurableId(string? value, string prefix) =>
        value is not null && value.StartsWith(prefix, StringComparison.Ordinal) && Hex(value[prefix.Length..], 32);
    private static bool Hex(string? value, int length) => value?.Length == length &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
