namespace AILedger.Core.Contracts;

public sealed record WorkItemCompleted(
    WorkItemId WorkItemId,
    string? WithoutVerificationReason = null,
    WaiverProvenance? Provenance = null) : LedgerEventData
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AILedger.Core.Assurance.GovernedAcceptanceReceipt? Acceptance { get; init; }
}
