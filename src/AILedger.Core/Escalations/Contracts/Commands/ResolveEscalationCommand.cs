namespace AILedger.Core.Contracts;

public sealed record ResolveEscalationCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    EscalationId EscalationId,
    EscalationStatus Status,
    string? Resolution) : LedgerCommand(ActorId, CausationId, CorrelationId);
