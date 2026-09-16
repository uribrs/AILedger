namespace AILedger.Core.Contracts;

public sealed record ReviewWorkItem(
    WorkItemId WorkItemId,
    IReadOnlyList<string> ResourceScope,
    string? BaseRef);
