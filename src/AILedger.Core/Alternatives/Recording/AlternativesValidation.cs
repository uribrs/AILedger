using AILedger.Core.Findings;

namespace AILedger.Core.Alternatives;

public static class AlternativesValidation
{
    public static AlternativesRequest Snapshot(AlternativesRequest request)
    {
        if (request is null || request.SchemaVersion != 1 || !FindingsValidation.IsRequestId(request.RequestId))
            throw new FindingsRequestException("invalid_request", "Expected schema version 1 and a valid request ID.");
        if (request.Alternatives is null || request.Alternatives.Count is < 1 or > 32)
            throw new FindingsRequestException("invalid_request", "Expected 1–32 alternatives.", "alternatives");
        var items = request.Alternatives.ToArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            var path = $"alternatives[{i}]";
            if (item is null)
                throw new FindingsRequestException("invalid_request", "Alternative is required.", path);
            if (!FindingsValidation.IsKey(item.Key) || !keys.Add(item.Key))
                throw new FindingsRequestException("invalid_request", "Local keys must be valid and unique.", path + ".key");
            FindingsValidation.Text(item.Statement, 8192, path + ".statement");
            FindingsValidation.Text(item.RejectionRationale, 8192, path + ".rejection_rationale");
            FindingsValidation.OptionalText(item.ReplacedByDecisionId, 256, path + ".replaced_by_decision_id");
            FindingsValidation.OptionalText(item.FromLesson, 256, path + ".from_lesson");
        }
        var snapshot = request with { Alternatives = items };
        if (AlternativesFingerprint.RequestBytes(snapshot).Length > FindingsValidation.MaximumBodyBytes)
            throw new FindingsRequestException("invalid_request", "The normalized request exceeds 256 KiB.");
        return snapshot;
    }
}
