namespace AILedger.Core.Contracts;

public sealed record ConstraintSuperseded(ConstraintId ConstraintId) : LedgerEventData;
