namespace AILedger.Core.Contracts;

public sealed record WorkItemCompleted(
    WorkItemId WorkItemId,
    string? WithoutVerificationReason = null,
    WaiverProvenance? Provenance = null) : LedgerEventData;
