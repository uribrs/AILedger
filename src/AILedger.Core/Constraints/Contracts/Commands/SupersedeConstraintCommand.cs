namespace AILedger.Core.Contracts;

public sealed record SupersedeConstraintCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ConstraintId ConstraintId) : LedgerCommand(ActorId, CausationId, CorrelationId);
