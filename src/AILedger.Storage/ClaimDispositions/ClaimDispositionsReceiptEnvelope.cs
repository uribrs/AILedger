using System.Text.Json;
using System.Text.Json.Serialization;
using AILedger.Core.Contracts;
using AILedger.Core.ClaimDispositions;
using AILedger.Core.Findings;

namespace AILedger.Storage.ClaimDispositions;

internal static class ClaimDispositionsReceiptEnvelope
{
    internal const string PropertyName = "_ailedgerClaimDispositionsReceipt";
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) }
    };

    internal static string Serialize(ClaimDispositionsReceipt receipt) => JsonSerializer.Serialize(receipt, Json);

    internal static ClaimDispositionsReceipt Validate(JsonElement metadata, IReadOnlyList<LedgerEvent> events, long version)
    {
        try
        {
            var receipt = metadata.Deserialize<ClaimDispositionsReceipt>(Json);
            Require(receipt is not null, "Missing disposition receipt.");
            var r = receipt!;
            // Recursively require every serialized member, including explicit nulls, and reject duplicates.
            using var canonical = JsonDocument.Parse(Serialize(r));
            RequireShape(metadata, canonical.RootElement);
            Require(r.SchemaVersion == 1 && r.FingerprintAlgorithm == ClaimDispositionsFingerprint.Algorithm &&
                FindingsValidation.IsRequestId(r.RequestId) && Hex(r.TransactionId, 32) && Hex(r.PayloadFingerprint, 64),
                "Invalid disposition receipt identity.");
            Require(r.LedgerVersion == version && r.CommittedAt != default && r.EventIds is not null &&
                r.Dispositions is not null && r.Dispositions.Count is >= 1 and <= 32 &&
                r.EventIds.SequenceEqual(events.Select(e => e.EventId.Value)), "Invalid receipt version or events.");
            Require(!string.IsNullOrWhiteSpace(r.TaskId) && !string.IsNullOrWhiteSpace(r.ActorId) &&
                !string.IsNullOrWhiteSpace(r.CorrelationId) &&
                (r.RunId is null || !string.IsNullOrWhiteSpace(r.RunId) && r.RunId == r.CorrelationId), "Invalid receipt binding.");
            Require(events.All(e => e.TaskId.Value == r.TaskId && e.ActorId.Value == r.ActorId &&
                e.CorrelationId == r.CorrelationId && e.RecordedAt == r.CommittedAt), "Receipt attribution mismatch.");
            var position = 0;
            foreach (var change in r.Dispositions!) ValidateChange(change, r, events, ref position);
            Require(position == events.Count, "Unmapped disposition events.");
            var request = ClaimDispositionsValidation.Snapshot(new(1, r.RequestId, r.Dispositions!.Select(c =>
                new ClaimDispositionInput(c.Key, new(c.ClaimId), c.PreviousStatus, c.Status, c.Rationale, c.Evidence)).ToArray()));
            var binding = new ClaimDispositionsBinding(new(r.TaskId), new(r.ActorId),
                r.RunId is null ? null : new RunId(r.RunId), r.CorrelationId,
                r.CausationId is null ? null : new EventId(r.CausationId));
            Require(ClaimDispositionsFingerprint.Compute(binding, request) == r.PayloadFingerprint,
                "Receipt content does not match its fingerprint.");
            return r with
            {
                EventIds = Array.AsReadOnly(r.EventIds!.ToArray()),
                Dispositions = Array.AsReadOnly(r.Dispositions!.Select(c => c with
                {
                    Evidence = Array.AsReadOnly(c.Evidence.ToArray()),
                    Dependencies = Array.AsReadOnly(c.Dependencies.ToArray())
                }).ToArray())
            };
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or FindingsRequestException)
        {
            throw new InvalidDataException("Invalid claim dispositions receipt metadata.", e);
        }
    }

    private static void ValidateChange(ClaimDispositionChange change, ClaimDispositionsReceipt receipt,
        IReadOnlyList<LedgerEvent> events, ref int position)
    {
        Require(change is not null && change.Evidence is not null && change.Dependencies is not null &&
            position < events.Count, "Invalid disposition map.");
        var e = events[position++];
        Require(e.Data is ClaimResolved, "Expected claim resolution event.");
        var resolved = (ClaimResolved)e.Data;
        Require(e.EventId.Value == change!.EventId && e.CausationId?.Value == receipt.CausationId &&
            resolved.ClaimId.Value == change.ClaimId && resolved.Status == change.Status &&
            resolved.Rationale == change.Rationale && resolved.SupersededByClaimId is null && resolved.Outcome is null &&
            change.Evidence!.All(x => x is not null) &&
            resolved.EvidenceIds is not null &&
            resolved.EvidenceIds.Select(x => x.Value).SequenceEqual(change.Evidence!.Select(x => x.EvidenceId)),
            "Disposition map does not match resolution.");
        Require(change.PreviousStatus != change.Status, "Disposition must change the claim status.");
        foreach (var dependency in change.Dependencies!)
        {
            Require(change.Status == ClaimStatus.Rejected && position < events.Count && dependency is not null,
                "Invalid disposition consequences.");
            var next = events[position++];
            Require(next.CausationId == e.EventId && Dependency(next) == dependency &&
                (next.Data is DecisionInvalidated d && d.RejectedClaimId.Value == change.ClaimId ||
                 next.Data is WorkItemInvalidated w && w.RejectedClaimId.Value == change.ClaimId),
                "Dependency map does not match its event or cause.");
            e = next;
        }
    }

    internal static DispositionDependencyChange Dependency(LedgerEvent e) => e.Data switch
    {
        DecisionInvalidated d => new(e.EventId.Value, "decision", d.DecisionId.Value, "invalidated"),
        WorkItemInvalidated w => new(e.EventId.Value, "work_item", w.WorkItemId.Value, w.Status.ToString().ToLowerInvariant()),
        _ => throw new InvalidDataException("Unexpected disposition consequence event.")
    };

    private static void RequireShape(JsonElement actual, JsonElement canonical)
    {
        Require(actual.ValueKind == canonical.ValueKind, "Invalid receipt field kind.");
        if (canonical.ValueKind == JsonValueKind.Object)
        {
            var properties = actual.EnumerateObject().ToArray();
            var expected = canonical.EnumerateObject().ToArray();
            Require(properties.Length == expected.Length && properties.Select(p => p.Name).Distinct().Count() == properties.Length,
                "Invalid receipt members.");
            foreach (var property in expected)
            {
                Require(actual.TryGetProperty(property.Name, out var value), "Missing receipt member.");
                RequireShape(value, property.Value);
            }
        }
        else if (canonical.ValueKind == JsonValueKind.Array)
        {
            Require(actual.GetArrayLength() == canonical.GetArrayLength(), "Invalid receipt array.");
            for (var i = 0; i < actual.GetArrayLength(); i++) RequireShape(actual[i], canonical[i]);
        }
    }

    private static bool Hex(string? value, int length) => value?.Length == length &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
