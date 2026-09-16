namespace AILedger.Core.Contracts;

public sealed record ContextArtifact(
    ContextArtifactKind Kind,
    string Id,
    string Content,
    IReadOnlyList<string> RelatedIds,
    IReadOnlyList<WorkItemId>? ApplicableWorkItemIds = null,
    RunId? ProducerRunId = null,
    AgentRunStatus? ProducerStatus = null);
