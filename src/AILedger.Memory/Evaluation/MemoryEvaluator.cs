using AILedger.Memory.Contracts;

namespace AILedger.Memory.Evaluation;

public sealed class MemoryEvaluator
{
    private readonly IMemoryProjectionStore _store;
    private readonly IMemorySearcher _indexSearcher;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string> _runIdFactory;

    public MemoryEvaluator(
        IMemoryProjectionStore store,
        IMemorySearcher indexSearcher,
        Func<DateTimeOffset>? clock = null,
        Func<string>? runIdFactory = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _indexSearcher = indexSearcher ?? throw new ArgumentNullException(nameof(indexSearcher));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _runIdFactory = runIdFactory ?? (() => Guid.NewGuid().ToString("N"));
    }

    public async Task<EvaluationReport> EvaluateAsync(
        EvaluationCorpus corpus,
        EvaluationMode mode,
        IMemorySearcher? baselineSearcher = null,
        long? embeddingCallsAvoided = null,
        string? indexRunId = null,
        CancellationToken cancellationToken = default)
    {
        EvaluationCorpusLoader.Validate(corpus);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (embeddingCallsAvoided is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(embeddingCallsAvoided));
        }

        var searcher = mode == EvaluationMode.TagRecencyBaseline
            ? baselineSearcher ?? throw new ArgumentNullException(
                nameof(baselineSearcher),
                "Tag-and-recency evaluation requires a baseline searcher.")
            : _indexSearcher;
        var caseResults = new List<EvaluationCaseResult>(corpus.Cases.Count);
        var semanticSearchUsed = false;
        foreach (var evaluationCase in corpus.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = new MemoryQuery
            {
                Text = evaluationCase.Query,
                Limit = 10,
                Kinds = evaluationCase.Kinds.Count == 0
                    ? null
                    : evaluationCase.Kinds.ToHashSet(),
                IncludeSuperseded = false,
                UseEmbeddings = mode == EvaluationMode.Hybrid
            };
            var response = mode == EvaluationMode.TagRecencyBaseline &&
                searcher is TagRecencyBaselineSearcher tagRecencyBaseline
                    ? await tagRecencyBaseline.SearchAsync(
                        query,
                        evaluationCase.OpeningTags,
                        cancellationToken).ConfigureAwait(false)
                    : await searcher.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            semanticSearchUsed |= response.SemanticSearchUsed;
            caseResults.Add(Score(evaluationCase, response));
        }

        var statistics = await _store.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);
        return new EvaluationReport
        {
            EvaluationRunId = _runIdFactory(),
            CasesVersion = corpus.CorpusVersion,
            Mode = mode,
            EvaluatedAt = _clock(),
            Metrics = CalculateMetrics(corpus.Cases, caseResults),
            Cases = caseResults,
            EmbeddingIdentity = mode == EvaluationMode.Hybrid && semanticSearchUsed
                ? statistics.ActiveEmbeddingIdentity
                : null,
            IndexRunId = indexRunId ?? statistics.LatestRun?.RunId,
            EmbeddingCallsAvoided = embeddingCallsAvoided ?? statistics.LatestRun?.EmbeddingsReused ?? 0,
            DatabaseBytes = statistics.DatabaseBytes,
            DocumentCount = statistics.DocumentCount
        };
    }

    private static EvaluationCaseResult Score(
        EvaluationCase evaluationCase,
        MemorySearchResponse response)
    {
        var expectedIds = evaluationCase.ExpectedDocumentIds.ToHashSet(StringComparer.Ordinal);
        var relevant = response.Results.FirstOrDefault(result => expectedIds.Contains(result.DocumentId));
        var citationCorrect = evaluationCase.ExpectNoSupportedAnswer
            ? response.Results.Count == 0
            : relevant is not null && HasExpectedCitation(relevant, evaluationCase.ExpectedCitationRecordIds);
        return new EvaluationCaseResult
        {
            CaseId = evaluationCase.Id,
            FirstRelevantRank = relevant?.Rank,
            CitationCorrect = citationCorrect,
            SupersededInDefaultTopSet = response.Results.Count(result =>
                result.Lifecycle is MemoryLifecycle.Superseded or MemoryLifecycle.Deleted),
            DistinctSourceTasks = response.Results
                .Select(result => result.TaskId)
                .Where(taskId => !string.IsNullOrWhiteSpace(taskId))
                .Distinct(StringComparer.Ordinal)
                .Count(),
            DistinctRepositories = response.Results
                .Select(result => result.Repository)
                .Where(repository => !string.IsNullOrWhiteSpace(repository))
                .Distinct(StringComparer.Ordinal)
                .Count(),
            Latency = response.Elapsed
        };
    }

    private static bool HasExpectedCitation(
        MemorySearchResult result,
        IReadOnlyList<string> expectedRecordIds)
    {
        if (result.Citations.Count == 0)
        {
            return false;
        }

        return expectedRecordIds.Count == 0 || expectedRecordIds.All(expected =>
            result.Citations.Any(citation =>
                string.Equals(citation.RecordId, expected, StringComparison.Ordinal) ||
                string.Equals(citation.EventId, expected, StringComparison.Ordinal)));
    }

    private static EvaluationMetrics CalculateMetrics(
        IReadOnlyList<EvaluationCase> cases,
        IReadOnlyList<EvaluationCaseResult> results)
    {
        var supportedIds = cases
            .Where(evaluationCase => !evaluationCase.ExpectNoSupportedAnswer)
            .Select(evaluationCase => evaluationCase.Id)
            .ToHashSet(StringComparer.Ordinal);
        var supported = results.Where(result => supportedIds.Contains(result.CaseId)).ToArray();
        var latencies = results.Select(result => result.Latency).OrderBy(value => value).ToArray();
        return new EvaluationMetrics
        {
            RecallAt1 = RecallAt(supported, 1),
            RecallAt5 = RecallAt(supported, 5),
            RecallAt10 = RecallAt(supported, 10),
            MeanReciprocalRank = supported.Length == 0
                ? 0
                : supported.Average(result => result.FirstRelevantRank is { } rank ? 1d / rank : 0),
            SupersededInDefaultTopSets = results.Sum(result => (long)result.SupersededInDefaultTopSet),
            CitationAccuracy = results.Count == 0
                ? 0
                : results.Count(result => result.CitationCorrect) / (double)results.Count,
            MeanSourceTaskDiversity = results.Count == 0
                ? 0
                : results.Average(result => result.DistinctSourceTasks),
            MeanRepositoryDiversity = results.Count == 0
                ? 0
                : results.Average(result => result.DistinctRepositories),
            MedianSearchLatency = Median(latencies)
        };
    }

    private static double RecallAt(IReadOnlyList<EvaluationCaseResult> results, int rank) =>
        results.Count == 0
            ? 0
            : results.Count(result => result.FirstRelevantRank is { } first && first <= rank) /
              (double)results.Count;

    private static TimeSpan Median(IReadOnlyList<TimeSpan> values)
    {
        if (values.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : TimeSpan.FromTicks((values[middle - 1].Ticks / 2) + (values[middle].Ticks / 2));
    }
}
