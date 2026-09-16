namespace AILedger.Core.Contracts;

public sealed record RecordArtifactCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ArtifactId ArtifactId,
    GovernedArtifactKind Kind,
    string Title,
    string Content,
    WorkItemId? WorkItemId,
    RunId? ProducerRunId,
    ArtifactId? SupersedesArtifactId,
    IReadOnlyList<WorkItemId>? CoveredWorkItemIds = null) : LedgerCommand(ActorId, CausationId, CorrelationId);
