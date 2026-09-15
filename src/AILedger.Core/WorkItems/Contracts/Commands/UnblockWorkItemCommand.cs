namespace AILedger.Core.Contracts;

public sealed record UnblockWorkItemCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    WorkItemId WorkItemId) : LedgerCommand(ActorId, CausationId, CorrelationId);
