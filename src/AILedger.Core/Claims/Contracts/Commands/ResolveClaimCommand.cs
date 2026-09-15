namespace AILedger.Core.Contracts;

public sealed record ResolveClaimCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ClaimId ClaimId,
    ClaimStatus Status,
    IReadOnlyList<EvidenceId> EvidenceIds,
    ClaimId? SupersededByClaimId = null) : LedgerCommand(ActorId, CausationId, CorrelationId);
