namespace AILedger.Core.Contracts;

public sealed record AddConstraintCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ConstraintId ConstraintId,
    string Statement,
    string Source,
    IReadOnlyList<string> Scope) : LedgerCommand(ActorId, CausationId, CorrelationId);
