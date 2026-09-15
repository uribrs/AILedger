namespace AILedger.Core.Contracts;

public sealed record RoleAssigned(RoleAssignment Assignment) : LedgerEventData;
