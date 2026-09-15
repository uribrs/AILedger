namespace AILedger.Core.Contracts;

public sealed record WorkItemAbandoned(WorkItemId WorkItemId, string Reason) : LedgerEventData;
