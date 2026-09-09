namespace AILedger.Memory.Contracts;

public enum IndexRunKind
{
    Rebuild,
    Update,
    EmbeddingRepair
}

public enum IndexRunOutcome
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
    PartiallyEmbedded
}

public sealed record IndexRunStatistics
{
    public required string RunId { get; init; }
    public required IndexRunKind Kind { get; init; }
    public required IndexRunOutcome Outcome { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public long SourcesRead { get; init; }
    public long DocumentsWritten { get; init; }
    public long DocumentsDeleted { get; init; }
    public long EmbeddingRequests { get; init; }
    public long EmbeddingsReused { get; init; }
    public long EmbeddingsWritten { get; init; }
    public string? FailureCode { get; init; }
    public string? FailureMessage { get; init; }
    public EmbeddingIdentity? EmbeddingIdentity { get; init; }
}

public sealed record MemoryDatabaseStatistics
{
    public required int SchemaVersion { get; init; }
    public required long DatabaseBytes { get; init; }
    public required long DocumentCount { get; init; }
    public required long EmbeddingChunkCount { get; init; }
    public required long SourceCount { get; init; }
    public EmbeddingIdentity? ActiveEmbeddingIdentity { get; init; }
    public IndexRunStatistics? LatestRun { get; init; }
}

public sealed record MemoryCandidateFilter
{
    public string? Repository { get; init; }
    public string? TaskId { get; init; }
    public IReadOnlySet<MemoryDocumentKind>? Kinds { get; init; }
    public bool IncludeSuperseded { get; init; }
}

public sealed record LexicalCandidate
{
    public required MemoryDocument Document { get; init; }
    public required double Score { get; init; }
}

public sealed record EmbeddingCandidate
{
    public required MemoryDocument Document { get; init; }
    public required DocumentEmbedding Embedding { get; init; }
}

public interface IMemoryProjectionTransaction : IAsyncDisposable
{
    Task ApplyAsync(SourceDelta delta, CancellationToken cancellationToken = default);
    Task StoreEmbeddingsAsync(
        IReadOnlyList<DocumentEmbedding> embeddings,
        CancellationToken cancellationToken = default);
    Task SetActiveEmbeddingIdentityAsync(
        EmbeddingIdentity identity,
        CancellationToken cancellationToken = default);
    Task RecordIndexRunAsync(
        IndexRunStatistics statistics,
        CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface IMemoryProjectionStore
{
    int SupportedSchemaVersion { get; }

    Task<IMemoryProjectionTransaction> BeginUpdateAsync(
        CancellationToken cancellationToken = default);

    Task<SourceCheckpoint?> GetCheckpointAsync(
        string sourceId,
        CancellationToken cancellationToken = default);

    Task<MemoryDocument?> InspectAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    Task<MemoryDatabaseStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default);

    Task<EmbeddingIdentity?> GetActiveEmbeddingIdentityAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentEmbedding>> FindReusableEmbeddingsAsync(
        IReadOnlySet<string> contentHashes,
        EmbeddingIdentity identity,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LexicalCandidate>> FindLexicalCandidatesAsync(
        string query,
        MemoryCandidateFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmbeddingCandidate>> ReadEmbeddingCandidatesAsync(
        EmbeddingIdentity identity,
        MemoryCandidateFilter filter,
        CancellationToken cancellationToken = default);

    Task VerifyIntegrityAsync(CancellationToken cancellationToken = default);
}

public interface IMemoryProjectionRebuild : IAsyncDisposable
{
    IMemoryProjectionStore Store { get; }

    Task ReplaceCurrentAsync(CancellationToken cancellationToken = default);
}

public interface IMemoryProjectionStoreFactory
{
    Task<IMemoryProjectionStore> OpenCurrentAsync(
        CancellationToken cancellationToken = default);

    Task<IMemoryProjectionRebuild> BeginRebuildAsync(
        CancellationToken cancellationToken = default);

    Task DropAsync(CancellationToken cancellationToken = default);
}
