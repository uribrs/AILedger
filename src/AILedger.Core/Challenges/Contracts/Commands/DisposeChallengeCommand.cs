namespace AILedger.Core.Contracts;

public sealed record DisposeChallengeCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ChallengeId ChallengeId,
    ChallengeStatus Status) : LedgerCommand(ActorId, CausationId, CorrelationId);
