namespace AILedger.Core.Contracts;

public sealed record ClaimResolved(
    ClaimId ClaimId,
    ClaimStatus Status,
    IReadOnlyList<EvidenceId> EvidenceIds,
    ClaimId? SupersededByClaimId = null,
    SupersessionOutcome? Outcome = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Rationale = null) : LedgerEventData;
