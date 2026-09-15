namespace AILedger.Core.Contracts;

public sealed record WorkItemUnblocked(WorkItemId WorkItemId) : LedgerEventData;
