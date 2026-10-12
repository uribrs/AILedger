namespace AILedger.Core.Assurance;

// Trusted operator requests only; these are not provider tool operations.
public sealed record ExtendAssuranceCheckBudgetRequest(int SchemaVersion, string RequestId,
    string ExpectedPolicySha256, int ExpectedConsumedBatches, int AdditionalBatches, string Reason)
{
    // Absent on historical requests, whose exhaustion prerequisite remains in force.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? ExpectedBatchLimit { get; init; }
}

public sealed record AssuranceCheckBudget(string PolicySha256, int ConsumedBatches,
    int BatchLimit, int RemainingBatches, int MaximumExtensionBatches);

public sealed record AssuranceCheckBudgetGrant(string Id, string RequestSha256,
    ExtendAssuranceCheckBudgetRequest Request, AssuranceSession Session,
    string PolicySha256, int BatchLimit, DateTimeOffset RecordedAt);

public sealed record AssuranceCheckBudgetExtension(AssuranceCheckBudgetGrant Grant, bool Replayed);
