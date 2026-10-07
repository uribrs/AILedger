namespace AILedger.Core.Contracts;

public sealed record Evidence(
    EvidenceId Id,
    string SourceType,
    string Citation,
    string Summary,
    IReadOnlyList<ClaimId> Supports,
    IReadOnlyList<ClaimId> Refutes,
    Provenance Provenance)
{
    // Optional attribution minted at command admission, not inferred from a later role assignment.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? TrustedTerminationVersion { get; init; }
}
