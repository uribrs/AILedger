namespace AILedger.Core.Contracts;

public sealed record RaiseChallengeCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ChallengeId ChallengeId,
    string TargetType,
    string TargetId,
    string Reason,
    IReadOnlyList<EvidenceId> EvidenceIds) : LedgerCommand(ActorId, CausationId, CorrelationId);
