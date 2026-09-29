using System.Text.Json;
using System.Text.Json.Serialization;
using AILedger.Core.Contracts;
using AILedger.Core.Findings;

namespace AILedger.Storage.Findings;

internal static class FindingsReceiptEnvelope
{
    internal const string PropertyName = "_ailedgerFindingsReceipt";
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static string Serialize(FindingsReceipt receipt) => JsonSerializer.Serialize(receipt, Json);

    internal static FindingsReceipt Validate(JsonElement metadata, IReadOnlyList<LedgerEvent> events, long version)
    {
        try
        {
            ValidateJsonShape(metadata);
            var receipt = metadata.Deserialize<FindingsReceipt>(Json);
            Require(receipt is not null, "Missing receipt.");
            var r = receipt!;
            Require(r.SchemaVersion == 1 && r.FingerprintAlgorithm == FindingsFingerprint.Algorithm,
                "Unsupported findings receipt version or algorithm.");
            Require(FindingsValidation.IsRequestId(r.RequestId) && Hex(r.TransactionId, 32) &&
                Hex(r.PayloadFingerprint, 64), "Invalid receipt identity or fingerprint.");
            Require(r.LedgerVersion == version && r.CommittedAt != default &&
                r.EventIds is not null && r.Findings is not null && r.Evidence is not null,
                "Invalid receipt version or maps.");
            Require(r.EventIds!.SequenceEqual(events.Select(e => e.EventId.Value)) &&
                r.Findings!.Count <= 32 && r.Evidence!.Count <= 64 &&
                r.Findings.Count + r.Evidence.Count == events.Count, "Receipt events do not match group.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var map in r.Findings!)
            {
                Require(map is not null && FindingsValidation.IsKey(map.Key) && keys.Add(map.Key) &&
                    DurableId(map.ClaimId, "CF_") && ids.Add(map.ClaimId), "Invalid finding map.");
                var e = events[index++];
                Require(e.EventId.Value == map!.EventId && e.Data is ClaimAdded c &&
                    c.Claim.Id.Value == map.ClaimId && c.Claim.Status == ClaimStatus.Open &&
                    c.Claim.Provenance.ActorId == e.ActorId && c.Claim.Provenance.RecordedAt == e.RecordedAt,
                    "Finding map does not match its event.");
            }
            foreach (var map in r.Evidence!)
            {
                Require(map is not null && FindingsValidation.IsKey(map.Key) && keys.Add(map.Key) &&
                    DurableId(map.EvidenceId, "EF_") && ids.Add(map.EvidenceId), "Invalid evidence map.");
                var e = events[index++];
                Require(e.EventId.Value == map!.EventId && e.Data is EvidenceAdded a &&
                    a.Evidence.Id.Value == map.EvidenceId && a.Evidence.Provenance.ActorId == e.ActorId &&
                    a.Evidence.Provenance.RecordedAt == e.RecordedAt, "Evidence map does not match its event.");
            }
            Require(!string.IsNullOrWhiteSpace(r.TaskId) && !string.IsNullOrWhiteSpace(r.ActorId) &&
                !string.IsNullOrWhiteSpace(r.CorrelationId) &&
                (r.RunId is null || !string.IsNullOrWhiteSpace(r.RunId) && r.CorrelationId == r.RunId),
                "Invalid receipt binding.");
            Require(events.All(e => e.TaskId.Value == r.TaskId && e.ActorId.Value == r.ActorId &&
                e.CorrelationId == r.CorrelationId && e.CausationId?.Value == r.CausationId &&
                e.RecordedAt == r.CommittedAt), "Receipt attribution does not match group.");
            return r with { EventIds = Array.AsReadOnly(r.EventIds!.ToArray()),
                Findings = Array.AsReadOnly(r.Findings!.ToArray()), Evidence = Array.AsReadOnly(r.Evidence!.ToArray()) };
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("Invalid findings receipt metadata.", e);
        }
    }

    // Required null-valued members are still required. A missing field must not quietly become
    // a deserializer default and turn a damaged binding into another valid receipt.
    private static void ValidateJsonShape(JsonElement value)
    {
        Require(value.ValueKind == JsonValueKind.Object, "Receipt must be an object.");
        string[] expected = ["schema_version", "request_id", "transaction_id", "task_id", "actor_id",
            "run_id", "correlation_id", "causation_id", "payload_fingerprint", "fingerprint_algorithm",
            "committed_at", "ledger_version", "event_ids", "findings", "evidence"];
        var names = value.EnumerateObject().Select(p => p.Name).ToArray();
        Require(names.Length == expected.Length && names.ToHashSet().SetEquals(expected), "Invalid receipt members.");
        foreach (var name in new[] { "findings", "evidence" })
        {
            Require(value.GetProperty(name).ValueKind == JsonValueKind.Array, "Invalid receipt maps.");
            foreach (var map in value.GetProperty(name).EnumerateArray())
            {
                Require(map.ValueKind == JsonValueKind.Object, "Invalid receipt map.");
                string[] fields = ["key", name == "findings" ? "claim_id" : "evidence_id", "event_id"];
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
