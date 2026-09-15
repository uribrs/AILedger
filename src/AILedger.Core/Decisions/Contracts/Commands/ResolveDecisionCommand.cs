namespace AILedger.Core.Contracts;

public sealed record ResolveDecisionCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    DecisionId DecisionId,
    DecisionStatus Status) : LedgerCommand(ActorId, CausationId, CorrelationId);
