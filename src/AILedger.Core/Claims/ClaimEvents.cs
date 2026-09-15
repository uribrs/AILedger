namespace AILedger.Core.Contracts;

public sealed record ClaimAdded(Claim Claim) : LedgerEventData;

public sealed record ClaimResolved(
    ClaimId ClaimId,
    ClaimStatus Status,
    IReadOnlyList<EvidenceId> EvidenceIds,
    ClaimId? SupersededByClaimId = null,
    SupersessionOutcome? Outcome = null) : LedgerEventData;

public sealed record ClaimDependenciesRepointed(
    ClaimId SupersededClaimId,
    ClaimId ReplacementClaimId) : LedgerEventData;
