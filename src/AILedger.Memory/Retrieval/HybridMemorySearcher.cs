using System.Diagnostics;
using AILedger.Memory.Contracts;
using AILedger.Memory.Embeddings;

namespace AILedger.Memory.Retrieval;

public sealed class HybridMemorySearcher : IMemorySearcher
{
    private const int ReciprocalRankOffset = 60;
    private const float MinimumSemanticSimilarity = 0.2f;
    private const double MaximumMetadataBonus = 0.012;
    private readonly IMemoryProjectionStore _store;
    private readonly IEmbeddingGenerator? _embeddingGenerator;

    public HybridMemorySearcher(
        IMemoryProjectionStore store,
        IEmbeddingGenerator? embeddingGenerator = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _embeddingGenerator = embeddingGenerator;
    }

    public async Task<MemorySearchResponse> SearchAsync(
        MemoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            throw new ArgumentException("Search text is required.", nameof(query));
        }

        if (query.Limit is <= 0 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Search limit must be between 1 and 1000.");
        }

        var stopwatch = Stopwatch.StartNew();
        var filter = new MemoryCandidateFilter
        {
            Repository = query.Repository,
            TaskId = query.TaskId,
            Kinds = query.Kinds,
            IncludeSuperseded = query.IncludeSuperseded
        };
        var candidateLimit = Math.Min(query.Limit * 10, 1_000);
        IReadOnlyList<LexicalCandidate> lexical;
        string? lexicalDiagnostic = null;
        if (query.Text.Any(char.IsLetterOrDigit))
        {
            lexical = await _store.FindLexicalCandidatesAsync(
                query.Text,
                filter,
                candidateLimit,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            lexical = [];
            lexicalDiagnostic = "Lexical search was unavailable (search text contains no letters or digits).";
        }

        EmbeddingIdentity? semanticIdentity = null;
        IReadOnlyList<SemanticCandidate> semantic = [];
        string? semanticDiagnostic = null;
        if (!query.UseEmbeddings)
        {
            semanticDiagnostic = CombineDiagnostics(
                lexicalDiagnostic,
                "Semantic search was disabled for this query.");
        }
        else if (_embeddingGenerator is null)
        {
            semanticDiagnostic = CombineDiagnostics(
                lexicalDiagnostic,
                "No embedding provider is configured; lexical results remain available.");
        }
        else
        {
            try
            {
                var activeIdentity = await _store.GetActiveEmbeddingIdentityAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (activeIdentity is null)
                {
                    semanticDiagnostic = "The index has no active embedding identity; lexical results remain available.";
                }
                else
                {
                    activeIdentity.Validate();
                    var queryBatch = await _embeddingGenerator.GenerateAsync(
                        new EmbeddingRequest
                        {
                            InputKind = EmbeddingInputKind.Query,
                            Inputs = [query.Text]
                        },
                        cancellationToken).ConfigureAwait(false);
                    ValidateQueryEmbedding(queryBatch, activeIdentity);
                    var stored = await _store.ReadEmbeddingCandidatesAsync(
                        activeIdentity,
                        filter,
                        cancellationToken).ConfigureAwait(false);
                    var scored = ScoreSemanticCandidates(stored, queryBatch.Vectors[0].Span, activeIdentity);
                    semantic = scored.Candidates;
                    if (semantic.Count > 0)
                    {
                        semanticIdentity = activeIdentity;
                    }
                    else if (scored.CandidatesBeforeFloor > 0)
                    {
                        semanticDiagnostic = CombineDiagnostics(
                            lexicalDiagnostic,
                            $"Semantic search contributed no candidates: the minimum similarity {MinimumSemanticSimilarity:F3} " +
                            $"filtered all {scored.CandidatesBeforeFloor} candidates (best {scored.BestSimilarity:F4}); lexical results remain available.");
                    }
                    else
                    {
                        semanticDiagnostic = CombineDiagnostics(
                            lexicalDiagnostic,
                            "The index has no matching embedding candidates; lexical results remain available.");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                semanticIdentity = null;
                semantic = [];
                semanticDiagnostic = CombineDiagnostics(
                    lexicalDiagnostic,
                    $"Semantic search was unavailable ({exception.GetType().Name}: {exception.Message}); lexical results remain available.");
            }
        }

        semanticDiagnostic ??= lexicalDiagnostic;

        var ranked = Rank(query, lexical, semantic)
            .Take(query.Limit)
            .Select((candidate, index) => ToResult(candidate, index + 1, semanticIdentity))
            .ToArray();
        stopwatch.Stop();

        return new MemorySearchResponse
        {
            Query = query,
            Results = ranked,
            Elapsed = stopwatch.Elapsed,
            SemanticSearchUsed = semanticIdentity is not null,
            SemanticSearchDiagnostic = semanticDiagnostic
        };
    }

    private static SemanticScoringResult ScoreSemanticCandidates(
        IReadOnlyList<EmbeddingCandidate> candidates,
        ReadOnlySpan<float> queryVector,
        EmbeddingIdentity activeIdentity)
    {
        var query = queryVector.ToArray();
        if (candidates.Any(candidate => candidate.Embedding.Identity != activeIdentity))
        {
            throw new InvalidOperationException("A stored embedding does not match the active index identity.");
        }

        var scored = candidates
            .GroupBy(candidate => candidate.Document.Id, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var scores = group.Select(candidate =>
                    (float)EmbeddingVector.CosineSimilarity(query, candidate.Embedding.Vector.Span));
                return new SemanticCandidate(first.Document, SemanticScoring.MaximumChunkScore(scores));
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Document.Id, StringComparer.Ordinal)
            .ToArray();

        return new SemanticScoringResult(
            scored.Where(candidate => candidate.Score >= MinimumSemanticSimilarity).ToArray(),
            scored.Length,
            scored.Length == 0 ? null : scored[0].Score);
    }

    private static string CombineDiagnostics(string? first, string second)
        => first is null ? second : $"{first} {second}";

    private static IEnumerable<RankedCandidate> Rank(
        MemoryQuery query,
        IReadOnlyList<LexicalCandidate> lexical,
        IReadOnlyList<SemanticCandidate> semantic)
    {
        var candidates = new Dictionary<string, MutableCandidate>(StringComparer.Ordinal);
        for (var index = 0; index < lexical.Count; index++)
        {
            var item = lexical[index];
            if (!ShouldInclude(item.Document, query.IncludeSuperseded))
            {
                continue;
            }

            var candidate = GetOrAdd(candidates, item.Document);
            candidate.Lexical = item.Score;
            candidate.Fusion += 1d / (ReciprocalRankOffset + index + 1);
        }

        for (var index = 0; index < semantic.Count; index++)
        {
            var item = semantic[index];
            if (!ShouldInclude(item.Document, query.IncludeSuperseded))
            {
                continue;
            }

            var candidate = GetOrAdd(candidates, item.Document);
            candidate.Semantic = item.Score;
            candidate.Fusion += 1d / (ReciprocalRankOffset + index + 1);
        }

        var metadataScale = MetadataScale(candidates.Values.Select(candidate => candidate.Fusion));
        var newestTimestamp = candidates.Count == 0
            ? DateTimeOffset.MinValue
            : candidates.Values.Max(candidate => candidate.Document.SourceTimestamp);
        foreach (var candidate in candidates.Values)
        {
            candidate.Authority = Math.Clamp((double)candidate.Document.Authority / (double)MemoryAuthority.MintedLesson, 0, 1);
            candidate.Lifecycle = LifecycleScore(candidate.Document.Lifecycle);
            candidate.Scope = ScopeScore(query, candidate.Document);
            candidate.Freshness = FreshnessScore(candidate.Document.SourceTimestamp, newestTimestamp);
            candidate.BaseScore = candidate.Fusion +
                metadataScale * ((0.004 * candidate.Authority) +
                (0.003 * candidate.Lifecycle) +
                (0.003 * candidate.Scope) +
                (0.001 * candidate.Freshness));
        }

        var remaining = candidates.Values.ToList();
        var taskCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var repositoryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        while (remaining.Count > 0)
        {
            var selected = remaining
                .Select(candidate =>
                {
                    candidate.Diversity = DiversityScore(candidate.Document, taskCounts, repositoryCounts);
                    return candidate;
                })
                .OrderByDescending(candidate => candidate.BaseScore +
                    metadataScale * 0.001 * candidate.Diversity)
                .ThenByDescending(candidate => candidate.Document.SourceTimestamp)
                .ThenBy(candidate => candidate.Document.Id, StringComparer.Ordinal)
                .First();
            remaining.Remove(selected);
            Increment(taskCounts, selected.Document.TaskId);
            Increment(repositoryCounts, selected.Document.Repository);

            yield return new RankedCandidate(
                selected.Document,
                selected.Lexical,
                selected.Semantic,
                selected.Fusion,
                selected.Authority,
                selected.Lifecycle,
                selected.Scope,
                selected.Freshness,
                selected.Diversity,
                selected.BaseScore + (metadataScale * 0.001 * selected.Diversity));
        }
    }

    private static double MetadataScale(IEnumerable<double> fusionScores)
    {
        var ordered = fusionScores.Distinct().OrderDescending().ToArray();
        if (ordered.Length < 2)
        {
            return 1;
        }

        var minimumGap = ordered
            .Zip(ordered.Skip(1), (higher, lower) => higher - lower)
            .Where(gap => gap > 0)
            .DefaultIfEmpty(MaximumMetadataBonus * 2)
            .Min();
        return Math.Min(1, minimumGap / (MaximumMetadataBonus * 2));
    }

    private static MemorySearchResult ToResult(
        RankedCandidate candidate,
        int rank,
        EmbeddingIdentity? semanticIdentity)
        => new()
        {
            Rank = rank,
            DocumentId = candidate.Document.Id,
            Kind = candidate.Document.Kind,
            Snippet = CreateSnippet(candidate.Document.Text),
            FinalScore = candidate.FinalScore,
            Scores = new MemoryScoreComponents
            {
                Lexical = candidate.Lexical,
                Semantic = candidate.Semantic,
                Fusion = candidate.Fusion,
                Authority = candidate.Authority,
                Lifecycle = candidate.Lifecycle,
                Scope = candidate.Scope,
                Freshness = candidate.Freshness,
                Diversity = candidate.Diversity
            },
            Lifecycle = candidate.Document.Lifecycle,
            Authority = candidate.Document.Authority,
            Repository = candidate.Document.Repository,
            TaskId = candidate.Document.TaskId,
            WorkItemId = candidate.Document.WorkItemId,
            RunId = candidate.Document.RunId,
            Citations = candidate.Document.Citations,
            EmbeddingIdentity = candidate.Semantic is null ? null : semanticIdentity
        };

    private static void ValidateQueryEmbedding(EmbeddingBatch batch, EmbeddingIdentity activeIdentity)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(batch.Identity);
        ArgumentNullException.ThrowIfNull(batch.Vectors);
        batch.Identity.Validate();
        if (batch.Identity != activeIdentity)
        {
            throw new InvalidOperationException("The query embedding identity does not match the active index identity.");
        }

        if (batch.Vectors.Count != 1 || batch.Vectors[0].Length != activeIdentity.Dimensions)
        {
            throw new InvalidDataException("The query embedding response has an invalid vector count or dimensionality.");
        }
    }

    private static bool ShouldInclude(MemoryDocument document, bool includeSuperseded)
        => includeSuperseded || document.Lifecycle is not (MemoryLifecycle.Superseded or MemoryLifecycle.Deleted);

    private static MutableCandidate GetOrAdd(
        IDictionary<string, MutableCandidate> candidates,
        MemoryDocument document)
    {
        if (!candidates.TryGetValue(document.Id, out var candidate))
        {
            candidate = new MutableCandidate(document);
            candidates.Add(document.Id, candidate);
        }

        return candidate;
    }

    private static double LifecycleScore(MemoryLifecycle lifecycle) => lifecycle switch
    {
        MemoryLifecycle.Active => 1,
        MemoryLifecycle.Resolved => 0.9,
        MemoryLifecycle.Rejected => 0.7,
        MemoryLifecycle.Unresolved => 0.4,
        MemoryLifecycle.Superseded => 0.1,
        MemoryLifecycle.Deleted => 0,
        _ => 0
    };

    private static double ScopeScore(MemoryQuery query, MemoryDocument document)
    {
        var requested = 0;
        var matched = 0;
        if (query.Repository is not null)
        {
            requested++;
            matched += string.Equals(query.Repository, document.Repository, StringComparison.Ordinal) ? 1 : 0;
        }

        if (query.TaskId is not null)
        {
            requested++;
            matched += string.Equals(query.TaskId, document.TaskId, StringComparison.Ordinal) ? 1 : 0;
        }

        return requested == 0 ? 0.5 : (double)matched / requested;
    }

    private static double FreshnessScore(DateTimeOffset timestamp, DateTimeOffset newest)
    {
        if (newest == DateTimeOffset.MinValue)
        {
            return 0;
        }

        var ageDays = Math.Max(0, (newest - timestamp).TotalDays);
        return 1d / (1d + (ageDays / 365d));
    }

    private static double DiversityScore(
        MemoryDocument document,
        IReadOnlyDictionary<string, int> taskCounts,
        IReadOnlyDictionary<string, int> repositoryCounts)
    {
        var score = 0d;
        if (document.TaskId is not null)
        {
            score += taskCounts.ContainsKey(document.TaskId) ? 0 : 0.5;
        }

        if (document.Repository is not null)
        {
            score += repositoryCounts.ContainsKey(document.Repository) ? 0 : 0.5;
        }

        return score;
    }

    private static void Increment(IDictionary<string, int> counts, string? key)
    {
        if (key is null)
        {
            return;
        }

        counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
    }

    private static string CreateSnippet(string text)
    {
        const int maximumLength = 240;
        var singleLine = string.Join(' ', text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return singleLine.Length <= maximumLength
            ? singleLine
            : string.Concat(singleLine.AsSpan(0, maximumLength - 1), "…");
    }

    private sealed class MutableCandidate(MemoryDocument document)
    {
        public MemoryDocument Document { get; } = document;
        public double? Lexical { get; set; }
        public double? Semantic { get; set; }
        public double Fusion { get; set; }
        public double Authority { get; set; }
        public double Lifecycle { get; set; }
        public double Scope { get; set; }
        public double Freshness { get; set; }
        public double Diversity { get; set; }
        public double BaseScore { get; set; }
    }

    private sealed record SemanticCandidate(MemoryDocument Document, double Score);

    private sealed record SemanticScoringResult(
        IReadOnlyList<SemanticCandidate> Candidates,
        int CandidatesBeforeFloor,
        double? BestSimilarity);

    private sealed record RankedCandidate(
        MemoryDocument Document,
        double? Lexical,
        double? Semantic,
        double Fusion,
        double Authority,
        double Lifecycle,
        double Scope,
        double Freshness,
        double Diversity,
        double FinalScore);
}
