namespace AILedger.Core.Contracts;

public sealed record DecisionProposed(Decision Decision) : LedgerEventData;
