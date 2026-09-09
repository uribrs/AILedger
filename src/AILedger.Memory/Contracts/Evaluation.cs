namespace AILedger.Memory.Contracts;

public enum EvaluationMode
{
    TagRecencyBaseline,
    Lexical,
    Hybrid
}

public sealed record EvaluationCase
{
    public required string Id { get; init; }
    public required string Query { get; init; }
    public required IReadOnlyList<string> ExpectedDocumentIds { get; init; }
    public IReadOnlyList<string> ExpectedCitationRecordIds { get; init; } = [];
    public IReadOnlyList<string> OpeningTags { get; init; } = [];
    public IReadOnlyList<MemoryDocumentKind> Kinds { get; init; } = [];
    public bool ExpectNoSupportedAnswer { get; init; }
    public string? Note { get; init; }
}

public sealed record EvaluationCaseResult
{
    public required string CaseId { get; init; }
    public required int? FirstRelevantRank { get; init; }
    public required bool CitationCorrect { get; init; }
    public required int SupersededInDefaultTopSet { get; init; }
    public required int DistinctSourceTasks { get; init; }
    public required int DistinctRepositories { get; init; }
    public required TimeSpan Latency { get; init; }
}

public sealed record EvaluationMetrics
{
    public required double RecallAt1 { get; init; }
    public required double RecallAt5 { get; init; }
    public required double RecallAt10 { get; init; }
    public required double MeanReciprocalRank { get; init; }
    public required long SupersededInDefaultTopSets { get; init; }
    public required double CitationAccuracy { get; init; }
    public required double MeanSourceTaskDiversity { get; init; }
    public required double MeanRepositoryDiversity { get; init; }
    public required TimeSpan MedianSearchLatency { get; init; }
}

public sealed record EvaluationReport
{
    public required string EvaluationRunId { get; init; }
    public required string CasesVersion { get; init; }
    public required EvaluationMode Mode { get; init; }
    public required DateTimeOffset EvaluatedAt { get; init; }
    public required EvaluationMetrics Metrics { get; init; }
    public required IReadOnlyList<EvaluationCaseResult> Cases { get; init; }
    public EmbeddingIdentity? EmbeddingIdentity { get; init; }
    public string? IndexRunId { get; init; }
    public required long EmbeddingCallsAvoided { get; init; }
    public required long DatabaseBytes { get; init; }
    public required long DocumentCount { get; init; }
    public double BytesPerDocument => DocumentCount == 0 ? 0 : (double)DatabaseBytes / DocumentCount;
}
