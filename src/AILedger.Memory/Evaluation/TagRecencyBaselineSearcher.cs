using System.Diagnostics;
using System.Text;
using AILedger.Memory.Contracts;

namespace AILedger.Memory.Evaluation;

/// <summary>
/// Reproduces the kernel's tagged lesson recall policy over a fixed corpus: tag overlap first,
/// recency second, at most three lessons per source task, and at most ten results.
/// </summary>
public sealed class TagRecencyBaselineSearcher : IMemorySearcher
{
    private const int ResultLimit = 10;
    private const int PerSourceTaskLimit = 3;
    private readonly IReadOnlyList<MemoryDocument> _documents;

    public TagRecencyBaselineSearcher(IEnumerable<MemoryDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents
            .GroupBy(document => document.Id, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(document => document.SourceTimestamp)
                .ThenBy(document => document.Id, StringComparer.Ordinal)
                .First())
            .ToArray();
    }

    public Task<MemorySearchResponse> SearchAsync(
        MemoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return SearchAsync(query, Tokenize(query.Text), cancellationToken);
    }

    public Task<MemorySearchResponse> SearchAsync(
        MemoryQuery query,
        IEnumerable<string> openingTags,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(openingTags);
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            throw new ArgumentException("Search text is required.", nameof(query));
        }

        if (query.Limit is <= 0 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Search limit must be between 1 and 1000.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        var requestedTags = openingTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = new List<(MemoryDocument Document, int Overlap)>();
        var selectedPerTask = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var candidate in _documents
                     .Where(document => document.Kind == MemoryDocumentKind.Lesson)
                     .Where(document => query.IncludeSuperseded ||
                         document.Lifecycle is not (MemoryLifecycle.Superseded or MemoryLifecycle.Deleted))
                     .Where(document => query.Repository is null ||
                         string.Equals(document.Repository, query.Repository, StringComparison.Ordinal))
                     .Where(document => query.TaskId is null ||
                         string.Equals(document.TaskId, query.TaskId, StringComparison.Ordinal))
                     .Where(document => query.Kinds is null || query.Kinds.Contains(document.Kind))
                     .Select(document => (Document: document, Overlap: MatchedTagCount(document, requestedTags)))
                     .Where(candidate => candidate.Overlap > 0)
                     .OrderByDescending(candidate => candidate.Overlap)
                     .ThenByDescending(candidate => candidate.Document.SourceTimestamp)
                     .ThenByDescending(candidate => candidate.Document.TaskId, StringComparer.Ordinal)
                     .ThenBy(candidate => candidate.Document.Id, StringComparer.Ordinal))
        {
            var taskKey = candidate.Document.TaskId ?? string.Empty;
            selectedPerTask.TryGetValue(taskKey, out var count);
            if (count == PerSourceTaskLimit)
            {
                continue;
            }

            selectedPerTask[taskKey] = count + 1;
            selected.Add(candidate);
            if (selected.Count == Math.Min(query.Limit, ResultLimit))
            {
                break;
            }
        }

        stopwatch.Stop();
        var results = selected
            .Select((candidate, index) => new MemorySearchResult
            {
                Rank = index + 1,
                DocumentId = candidate.Document.Id,
                Kind = candidate.Document.Kind,
                Snippet = candidate.Document.Text.Length <= 240
                    ? candidate.Document.Text
                    : string.Concat(candidate.Document.Text.AsSpan(0, 237), "..."),
                FinalScore = candidate.Overlap,
                Scores = new MemoryScoreComponents
                {
                    Lexical = candidate.Overlap,
                    Fusion = 0,
                    Authority = (double)candidate.Document.Authority / (double)MemoryAuthority.MintedLesson,
                    Lifecycle = candidate.Document.Lifecycle == MemoryLifecycle.Active ? 1 : 0,
                    Scope = 0,
                    Freshness = 0,
                    Diversity = 0
                },
                Lifecycle = candidate.Document.Lifecycle,
                Authority = candidate.Document.Authority,
                Repository = candidate.Document.Repository,
                TaskId = candidate.Document.TaskId,
                WorkItemId = candidate.Document.WorkItemId,
                RunId = candidate.Document.RunId,
                Citations = candidate.Document.Citations
            }).ToArray();

        return Task.FromResult(new MemorySearchResponse
        {
            Query = query,
            Results = results,
            Elapsed = stopwatch.Elapsed,
            SemanticSearchUsed = false,
            SemanticSearchDiagnostic = "Tag-and-recency baseline; embeddings are not used."
        });
    }

    private static int MatchedTagCount(MemoryDocument document, IReadOnlySet<string> requestedTags)
    {
        var documentTags = document.Tags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return requestedTags.Count(documentTags.Contains);
    }

    private static IReadOnlySet<string> Tokenize(string value) => value
        .Normalize(NormalizationForm.FormKC)
        .Split(value.Normalize(NormalizationForm.FormKC)
            .Where(character => !char.IsLetterOrDigit(character) && character is not ('-' or '_'))
            .Distinct()
            .ToArray(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
