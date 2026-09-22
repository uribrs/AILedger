namespace AILedger.Core.Contracts;

public sealed record ContextManifest(
    int SchemaVersion,
    TaskId TaskId,
    ActorId ActorId,
    RoleKind Role,
    WorkItemId? WorkItemId,
    long TaskVersion,
    IReadOnlyList<Capability> Capabilities,
    IReadOnlyList<ContextArtifact> Artifacts,
    IReadOnlyList<string> StopConditions,
    DateTimeOffset AssembledAt,
    IReadOnlyList<WorkItemId>? CoveredWorkItemIds = null,
    AssuranceBinding? Assurance = null,
    IReadOnlyList<ReviewWorkItem>? ReviewWorkItems = null,
    ContextBudgetReport? Budget = null);
