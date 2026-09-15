namespace AILedger.Core.Contracts;

public sealed record ConstraintAdded(Constraint Constraint) : LedgerEventData;
