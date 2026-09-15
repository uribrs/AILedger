namespace AILedger.Core.Contracts;

public sealed record WorkItemInvalidated(
    WorkItemId WorkItemId,
    ClaimId RejectedClaimId,
    WorkItemStatus Status) : LedgerEventData;
