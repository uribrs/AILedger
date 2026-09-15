namespace AILedger.Core.Contracts;

public sealed record CompleteCoordinatorSessionCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    CoordinatorSessionId SessionId) : LedgerCommand(ActorId, CausationId, CorrelationId);
