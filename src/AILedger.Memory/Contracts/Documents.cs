namespace AILedger.Memory.Contracts;

public enum MemoryDocumentKind
{
    Lesson,
    Decision,
    Constraint,
    ClaimEvidence,
    Alternative,
    RefusalChain,
    WorkSummary,
    RunSummary,
    Artifact
}

public enum MemoryLifecycle
{
    Active,
    Resolved,
    Superseded,
    Rejected,
    Deleted,
    Unresolved
}

public enum MemoryAuthority
{
    UnresolvedRecord = 0,
    HistoricalRecord = 10,
    RunOrWorkSummary = 20,
    ResolvedRefusal = 30,
    ResolvedAlternative = 40,
    ResolvedClaim = 50,
    AcceptedDecision = 60,
    ActiveConstraint = 70,
    MintedLesson = 80
}

public enum MemoryCitationKind
{
    Event,
    RefusalRow,
    LessonRow,
    File
}

public enum DocumentRelationKind
{
    Supports,
    Refutes,
    DependsOn,
    Supersedes,
    Replaces,
    Resolves,
    Satisfies,
    ProducedBy,
    BelongsTo
}

public sealed record MemoryCitation
{
    public required MemoryCitationKind Kind { get; init; }
    public required string SourcePath { get; init; }
    public long? LineNumber { get; init; }
    public long? ByteOffset { get; init; }
    public string? EventId { get; init; }
    public string? RecordId { get; init; }
}

public sealed record DocumentRelation
{
    public required DocumentRelationKind Kind { get; init; }
    public required string TargetId { get; init; }
}

public sealed record MemoryDocument
{
    public required string Id { get; init; }
    public required MemoryDocumentKind Kind { get; init; }
    public required string Text { get; init; }
    public string? Repository { get; init; }
    public string? TaskId { get; init; }
    public string? WorkItemId { get; init; }
    public string? RunId { get; init; }
    public string? ActorId { get; init; }
    public required DateTimeOffset SourceTimestamp { get; init; }
    public required string SourceVersion { get; init; }
    public required MemoryAuthority Authority { get; init; }
    public required MemoryLifecycle Lifecycle { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> RelatedIds { get; init; } = [];
    public required IReadOnlyList<MemoryCitation> Citations { get; init; }
    public IReadOnlyList<DocumentRelation> Relations { get; init; } = [];
    public required string ContentHash { get; init; }
    public required string NormalizerVersion { get; init; }
}
