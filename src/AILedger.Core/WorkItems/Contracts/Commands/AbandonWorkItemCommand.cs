namespace AILedger.Core.Contracts;

public sealed record AbandonWorkItemCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    WorkItemId WorkItemId,
    string Reason) : LedgerCommand(ActorId, CausationId, CorrelationId);
