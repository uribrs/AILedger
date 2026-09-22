using System.Security.Cryptography;
using System.Text.Json;

namespace AILedger.Core.Contracts;

/// <summary>Shared authoring and version-binding boundary for internal recon.</summary>
public static class InternalReconDocuments
{
    /// <summary>The returned template is deliberately inadmissible until classified and completed.</summary>
    public static InternalReconDocument CreateTemplate(GovernedTaskState state) =>
        new(1, state.TaskId.Value, ComputeClaimSetHash(state),
            state.Claims.Values.OrderBy(claim => claim.Id.Value, StringComparer.Ordinal)
                .Select(claim => new InternalReconAssessment(claim.Id.Value, null)).ToArray(), "");

    public static string ComputeClaimSetHash(GovernedTaskState state)
    {
        // R2 (stale-claim-certification): bind every historical claim, not task event version.
        var rows = state.Claims.Values.OrderBy(claim => claim.Id.Value, StringComparer.Ordinal)
            .Select(claim => new object?[]
            {
                claim.Id.Value,
                claim.Statement,
                claim.Status.ToString(),
                claim.EvidenceIds.Select(id => id.Value).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                claim.SupersededByClaimId?.Value
            }).ToArray();
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows)))
            .ToLowerInvariant();
    }
}
