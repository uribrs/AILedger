namespace AILedger.Core.Contracts;

public sealed record ClaimAdded(Claim Claim) : LedgerEventData;
