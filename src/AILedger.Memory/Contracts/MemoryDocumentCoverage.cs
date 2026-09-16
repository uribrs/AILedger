namespace AILedger.Memory.Contracts;

internal static class MemoryDocumentCoverage
{
    // Only original coverage has a legacy singular fallback. Empty applicability is meaningful.
    public static IReadOnlyList<string> Original(MemoryDocument document) =>
        document.CoveredWorkItemIds is { Count: > 0 } coverage ? coverage
            : document.WorkItemId is { } workItemId ? [workItemId] : [];
}
