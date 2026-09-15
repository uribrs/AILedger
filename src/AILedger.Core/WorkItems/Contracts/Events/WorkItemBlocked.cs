namespace AILedger.Core.Contracts;

public sealed record WorkItemBlocked(
    WorkItemId WorkItemId,
    string Reason,
    EscalationId? EscalationId) : LedgerEventData;
