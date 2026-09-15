namespace AILedger.Core.Contracts;

public sealed record BlockWorkItemCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    WorkItemId WorkItemId,
    string Reason,
    EscalationId? EscalationId) : LedgerCommand(ActorId, CausationId, CorrelationId);
