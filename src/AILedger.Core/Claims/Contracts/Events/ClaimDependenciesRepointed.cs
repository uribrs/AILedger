namespace AILedger.Core.Contracts;

public sealed record ClaimDependenciesRepointed(
    ClaimId SupersededClaimId,
    ClaimId ReplacementClaimId) : LedgerEventData;
