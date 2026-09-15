namespace AILedger.Core.Contracts;

public sealed record ClaimResolved(
    ClaimId ClaimId,
    ClaimStatus Status,
    IReadOnlyList<EvidenceId> EvidenceIds,
    ClaimId? SupersededByClaimId = null,
    SupersessionOutcome? Outcome = null) : LedgerEventData;
