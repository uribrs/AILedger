namespace AILedger.Core.Contracts;

public sealed record WorkItemAdded(WorkItem WorkItem) : LedgerEventData;
