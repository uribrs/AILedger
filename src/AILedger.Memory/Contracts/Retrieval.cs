namespace AILedger.Memory.Contracts;

public sealed record MemoryQuery
{
    public required string Text { get; init; }
    public int Limit { get; init; } = 10;
    public string? Repository { get; init; }
    public string? TaskId { get; init; }
    public IReadOnlySet<MemoryDocumentKind>? Kinds { get; init; }
    public bool IncludeSuperseded { get; init; }
    public bool UseEmbeddings { get; init; } = true;
}

public sealed record MemoryScoreComponents
{
    public double? Lexical { get; init; }
    public double? Semantic { get; init; }
    public double Fusion { get; init; }
    public double Authority { get; init; }
    public double Lifecycle { get; init; }
    public double Scope { get; init; }
    public double Freshness { get; init; }
    public double Diversity { get; init; }
}

public sealed record MemorySearchResult
{
    public required int Rank { get; init; }
    public required string DocumentId { get; init; }
    public required MemoryDocumentKind Kind { get; init; }
    public required string Snippet { get; init; }
    public required double FinalScore { get; init; }
    public required MemoryScoreComponents Scores { get; init; }
    public required MemoryLifecycle Lifecycle { get; init; }
    public required MemoryAuthority Authority { get; init; }
    public string? Repository { get; init; }
    public string? TaskId { get; init; }
    public string? WorkItemId { get; init; }
    public string? RunId { get; init; }
    public required IReadOnlyList<MemoryCitation> Citations { get; init; }
    public EmbeddingIdentity? EmbeddingIdentity { get; init; }
}

public sealed record MemorySearchResponse
{
    public required MemoryQuery Query { get; init; }
    public required IReadOnlyList<MemorySearchResult> Results { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public required bool SemanticSearchUsed { get; init; }
    public string? SemanticSearchDiagnostic { get; init; }
}

public interface IMemorySearcher
{
    Task<MemorySearchResponse> SearchAsync(
        MemoryQuery query,
        CancellationToken cancellationToken = default);
}
