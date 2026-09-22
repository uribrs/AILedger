namespace AILedger.Core.Contracts;

public sealed record ContextBudgetReport(
    int MaximumBytes,
    int OmittedArtifacts,
    string? Notice = null);
