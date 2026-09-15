namespace AILedger.Core.Contracts;

public sealed record DecisionResolved(DecisionId DecisionId, DecisionStatus Status) : LedgerEventData;
