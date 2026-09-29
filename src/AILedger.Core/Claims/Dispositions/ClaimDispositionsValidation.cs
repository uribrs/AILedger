using AILedger.Core.Contracts;
using AILedger.Core.Findings;

namespace AILedger.Core.ClaimDispositions;

public static class ClaimDispositionsValidation
{
    public static ClaimDispositionsRequest Snapshot(ClaimDispositionsRequest request)
    {
        if (request is null || request.SchemaVersion != 1 || !FindingsValidation.IsRequestId(request.RequestId))
            throw new FindingsRequestException("invalid_request", "Expected schema version 1 and a valid request ID.");
        if (request.Dispositions is null || request.Dispositions.Count is < 1 or > 32)
            throw new FindingsRequestException("invalid_request", "Expected 1–32 dispositions.", "dispositions");
        var items = request.Dispositions.ToArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var claims = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < items.Length; i++)
            items[i] = SnapshotItem(items[i], $"dispositions[{i}]", keys, claims);
        var snapshot = request with { Dispositions = Array.AsReadOnly(items) };
        if (ClaimDispositionsFingerprint.RequestBytes(snapshot).Length > FindingsValidation.MaximumBodyBytes)
            throw new FindingsRequestException("invalid_request", "The normalized request exceeds 256 KiB.");
        return snapshot;
    }

    private static ClaimDispositionInput SnapshotItem(ClaimDispositionInput item, string path,
        HashSet<string> keys, HashSet<string> claims)
    {
        if (item is null) throw new FindingsRequestException("invalid_request", "Disposition is required.", path);
        if (!FindingsValidation.IsKey(item.Key) || !keys.Add(item.Key))
            throw new FindingsRequestException("invalid_request", "Local keys must be valid and unique.", path + ".key");
        Id(item.Claim?.ClaimId, path + ".claim.claim_id");
        if (!claims.Add(item.Claim!.ClaimId))
            throw new FindingsRequestException("invalid_request", "Each claim may appear only once in a batch.", path + ".claim");
        if (item.ExpectedStatus is not (ClaimStatus.Open or ClaimStatus.Validated))
            throw new FindingsRequestException("invalid_request", "expected_status must be open or validated.", path + ".expected_status");
        if (item.Status is not (ClaimStatus.Validated or ClaimStatus.Rejected))
            throw new FindingsRequestException("invalid_request", "status must be validated or rejected.", path + ".status");
        FindingsValidation.Text(item.Rationale, 8192, path + ".rationale");
        if (item.Evidence is null || item.Evidence.Count is < 1 or > 32)
            throw new FindingsRequestException("invalid_request", "Expected 1–32 existing evidence references.", path + ".evidence");
        var evidence = item.Evidence.ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var j = 0; j < evidence.Length; j++)
        {
            var referencePath = path + $".evidence[{j}].evidence_id";
            Id(evidence[j]?.EvidenceId, referencePath);
            if (!ids.Add(evidence[j]!.EvidenceId))
                throw new FindingsRequestException("invalid_request", "Evidence references must be unique.", referencePath);
        }
        return item with { Evidence = Array.AsReadOnly(evidence) };
    }

    private static void Id(string? value, string path)
    {
        FindingsValidation.Text(value!, 256, path);
        if (value != value!.Trim())
            throw new FindingsRequestException("invalid_request", "References must use exact IDs without outer whitespace.", path);
    }
}
