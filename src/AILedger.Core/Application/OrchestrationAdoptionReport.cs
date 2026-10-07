using AILedger.Core.Contracts;

namespace AILedger.Core.Application;

// Read-only observations, not an admission/acceptance model. References point to existing
// journals and events. Sequence associations do not establish human intent or defect causation.
public sealed record OrchestrationAdoptionReport(
    int SchemaVersion, long TaskVersion, string JournalStatus, long? JournalRevision, long? Epoch,
    IReadOnlyList<OrchestrationObservation> Observations,
    IReadOnlyList<OrchestrationProviderUsage> ProviderUsage,
    IReadOnlyDictionary<string, int>? RefusalsByCause,
    IReadOnlyList<string> UnknownMeasurements);

public sealed record OrchestrationObservation(string Kind, string Source, string Outcome,
    string? RunId = null, string? RelatedRunId = null, string? Detail = null);

public sealed record OrchestrationProviderUsage(string RunId, string Provider, string? Model,
    long? OutputTokens, long? UncachedInputTokens, long? CacheWriteTokens, long? CacheReadTokens,
    decimal? ReportedUsd, string Source);
