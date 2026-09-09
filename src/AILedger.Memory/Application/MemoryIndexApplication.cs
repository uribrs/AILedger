using AILedger.Memory.Contracts;
using AILedger.Memory.Embeddings;
using AILedger.Memory.Evaluation;
using AILedger.Memory.Ingestion;

namespace AILedger.Memory.Application;

public sealed record MemoryIndexRequest
{
    public required IReadOnlyList<SourceDescriptor> Sources { get; init; }
    public bool GenerateEmbeddings { get; init; } = true;
}

public sealed record MemoryIndexResult
{
    public required IndexRunStatistics Statistics { get; init; }
    public required IReadOnlyList<string> Diagnostics { get; init; }
}

public sealed record MemoryEvaluationRequest
{
    public required EvaluationCorpus Corpus { get; init; }
    public required EvaluationMode Mode { get; init; }
    public IReadOnlyList<SourceDescriptor> BaselineSources { get; init; } = [];
    public long? EmbeddingCallsAvoided { get; init; }
    public string? IndexRunId { get; init; }
}

/// <summary>
/// Explicit application boundary for rebuilding and incrementally updating the disposable index.
/// It has no scheduler, watcher, or dependency on governed task services.
/// </summary>
public sealed class MemoryIndexApplication
{
    private readonly IMemoryProjectionStoreFactory _storeFactory;
    private readonly CanonicalSourceReaderSet _sourceReaders;
    private readonly IEmbeddingGenerator? _embeddingGenerator;
    private readonly DocumentChunker _chunker;
    private readonly DocumentEmbeddingPipelineOptions _embeddingOptions;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string> _runIdFactory;

    public MemoryIndexApplication(
        IMemoryProjectionStoreFactory storeFactory,
        CanonicalSourceReaderSet? sourceReaders = null,
        IEmbeddingGenerator? embeddingGenerator = null,
        DocumentChunker? chunker = null,
        DocumentEmbeddingPipelineOptions? embeddingOptions = null,
        Func<DateTimeOffset>? clock = null,
        Func<string>? runIdFactory = null)
    {
        _storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
        _sourceReaders = sourceReaders ?? new CanonicalSourceReaderSet();
        _embeddingGenerator = embeddingGenerator;
        _chunker = chunker ?? new DocumentChunker();
        _embeddingOptions = embeddingOptions ?? new DocumentEmbeddingPipelineOptions();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _runIdFactory = runIdFactory ?? (() => Guid.NewGuid().ToString("N"));
    }

    public async Task<MemoryIndexResult> RebuildAsync(
        MemoryIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var accumulator = new RunAccumulator(
            _runIdFactory(),
            IndexRunKind.Rebuild,
            _clock());

        await using var rebuild = await _storeFactory.BeginRebuildAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await ProcessSourcesAsync(
                rebuild.Store,
                request,
                useCheckpoints: false,
                accumulator,
                cancellationToken).ConfigureAwait(false);

            var statistics = accumulator.Complete(_clock());
            await RecordRunAsync(rebuild.Store, statistics, cancellationToken).ConfigureAwait(false);
            await rebuild.ReplaceCurrentAsync(cancellationToken).ConfigureAwait(false);
            return new MemoryIndexResult
            {
                Statistics = statistics,
                Diagnostics = accumulator.Diagnostics.ToArray()
            };
        }
        catch
        {
            // Disposing an uninstalled rebuild deletes only its sibling candidate database.
            throw;
        }
    }

    public async Task<MemoryIndexResult> UpdateAsync(
        MemoryIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var store = await _storeFactory.OpenCurrentAsync(cancellationToken).ConfigureAwait(false);
        var accumulator = new RunAccumulator(
            _runIdFactory(),
            IndexRunKind.Update,
            _clock());

        try
        {
            await ProcessSourcesAsync(
                store,
                request,
                useCheckpoints: true,
                accumulator,
                cancellationToken).ConfigureAwait(false);
            var statistics = accumulator.Complete(_clock());
            await RecordRunAsync(store, statistics, cancellationToken).ConfigureAwait(false);
            return new MemoryIndexResult
            {
                Statistics = statistics,
                Diagnostics = accumulator.Diagnostics.ToArray()
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var statistics = accumulator.Fail(
                IndexRunOutcome.Cancelled,
                "cancelled",
                "The index update was cancelled.",
                _clock());
            await TryRecordRunAsync(store, statistics).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            var statistics = accumulator.Fail(
                IndexRunOutcome.Failed,
                "source-update-failed",
                SafeFailureMessage(exception),
                _clock());
            await TryRecordRunAsync(store, statistics).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<MemorySearchResponse> SearchAsync(
        MemoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var store = await _storeFactory.OpenCurrentAsync(cancellationToken).ConfigureAwait(false);
        return await new Retrieval.HybridMemorySearcher(store, _embeddingGenerator)
            .SearchAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MemoryDocument?> InspectAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var store = await _storeFactory.OpenCurrentAsync(cancellationToken).ConfigureAwait(false);
        return await store.InspectAsync(documentId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MemoryDatabaseStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        var store = await _storeFactory.OpenCurrentAsync(cancellationToken).ConfigureAwait(false);
        return await store.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<EvaluationReport> EvaluateAsync(
        MemoryEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Corpus);
        ArgumentNullException.ThrowIfNull(request.BaselineSources);
        var store = await _storeFactory.OpenCurrentAsync(cancellationToken).ConfigureAwait(false);
        var searcher = new Retrieval.HybridMemorySearcher(store, _embeddingGenerator);
        IMemorySearcher? baseline = null;
        if (request.Mode == EvaluationMode.TagRecencyBaseline)
        {
            if (request.BaselineSources.Count == 0)
            {
                throw new ArgumentException(
                    "Tag-and-recency evaluation requires at least one canonical baseline source.",
                    nameof(request));
            }

            var documents = new Dictionary<string, MemoryDocument>(StringComparer.Ordinal);
            foreach (var source in request.BaselineSources)
            {
                var delta = await _sourceReaders.ReadAsync(source, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                foreach (var documentId in delta.Deletes)
                {
                    documents.Remove(documentId);
                }

                foreach (var document in delta.Upserts)
                {
                    documents[document.Id] = document;
                }
            }

            baseline = new TagRecencyBaselineSearcher(documents.Values);
        }

        return await new MemoryEvaluator(store, searcher, _clock, _runIdFactory).EvaluateAsync(
            request.Corpus,
            request.Mode,
            baseline,
            request.EmbeddingCallsAvoided,
            request.IndexRunId,
            cancellationToken).ConfigureAwait(false);
    }

    public Task DropAsync(CancellationToken cancellationToken = default) =>
        _storeFactory.DropAsync(cancellationToken);

    private async Task ProcessSourcesAsync(
        IMemoryProjectionStore store,
        MemoryIndexRequest request,
        bool useCheckpoints,
        RunAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        foreach (var source in request.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var checkpoint = useCheckpoints
                ? await store.GetCheckpointAsync(source.Id, cancellationToken).ConfigureAwait(false)
                : null;
            var delta = await _sourceReaders.ReadAsync(source, checkpoint, cancellationToken)
                .ConfigureAwait(false);
            accumulator.SourcesRead++;
            await ApplySourceAsync(store, delta, request.GenerateEmbeddings, accumulator, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task ApplySourceAsync(
        IMemoryProjectionStore store,
        SourceDelta delta,
        bool generateEmbeddings,
        RunAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        EmbeddingPreparation? preparation = null;
        if (generateEmbeddings && _embeddingGenerator is not null && delta.Upserts.Count > 0)
        {
            var requestCount = 0L;
            try
            {
                var activeIdentity = await store.GetActiveEmbeddingIdentityAsync(cancellationToken)
                    .ConfigureAwait(false);
                IReadOnlyList<DocumentEmbedding> reusable = [];
                if (activeIdentity is not null)
                {
                    var hashes = delta.Upserts
                        .Select(document => document.ContentHash)
                        .ToHashSet(StringComparer.Ordinal);
                    reusable = await store.FindReusableEmbeddingsAsync(hashes, activeIdentity, cancellationToken)
                        .ConfigureAwait(false);
                }

                var countingGenerator = CreateCountingGenerator(
                    _embeddingGenerator,
                    () => requestCount++);
                var pipeline = new DocumentEmbeddingPipeline(
                    countingGenerator,
                    _chunker,
                    _embeddingOptions);
                preparation = await pipeline.PrepareAsync(
                    delta.Upserts,
                    reusable,
                    activeIdentity,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                accumulator.EmbeddingRequests += requestCount;
                accumulator.EmbeddingFailure = true;
                accumulator.Diagnostics.Add(
                    $"Embedding unavailable for source '{delta.Source.Id}' ({SafeFailureMessage(exception)}); lexical documents were retained.");
            }
        }

        await using var transaction = await store.BeginUpdateAsync(cancellationToken).ConfigureAwait(false);
        await transaction.ApplyAsync(delta, cancellationToken).ConfigureAwait(false);
        if (preparation is not null)
        {
            if (preparation.Identity is not null && preparation.Embeddings.Count > 0)
            {
                await transaction.SetActiveEmbeddingIdentityAsync(preparation.Identity, cancellationToken)
                    .ConfigureAwait(false);
                await transaction.StoreEmbeddingsAsync(preparation.Embeddings, cancellationToken)
                    .ConfigureAwait(false);
                accumulator.EmbeddingIdentity = preparation.Identity;
            }

            accumulator.EmbeddingRequests += preparation.EmbeddingRequests;
            accumulator.EmbeddingsReused += preparation.EmbeddingsReused;
            accumulator.EmbeddingsWritten += preparation.EmbeddingsGenerated;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        accumulator.DocumentsWritten += delta.Upserts.Count;
        accumulator.DocumentsDeleted += delta.Deletes.Count;
    }

    private static async Task RecordRunAsync(
        IMemoryProjectionStore store,
        IndexRunStatistics statistics,
        CancellationToken cancellationToken)
    {
        await using var transaction = await store.BeginUpdateAsync(cancellationToken).ConfigureAwait(false);
        await transaction.RecordIndexRunAsync(statistics, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task TryRecordRunAsync(
        IMemoryProjectionStore store,
        IndexRunStatistics statistics)
    {
        try
        {
            await RecordRunAsync(store, statistics, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Preserve the original indexing failure. The database remains usable even if its
            // best-effort diagnostic row cannot be committed.
        }
    }

    private static void ValidateRequest(MemoryIndexRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Sources);
        if (request.Sources.Count == 0)
        {
            throw new ArgumentException("At least one canonical source is required.", nameof(request));
        }

        if (request.Sources.Any(source => source is null))
        {
            throw new ArgumentException("Canonical sources cannot contain null values.", nameof(request));
        }

        var duplicate = request.Sources
            .GroupBy(source => source.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Canonical source '{duplicate.Key}' is duplicated.", nameof(request));
        }
    }

    private static string SafeFailureMessage(Exception exception)
    {
        var message = $"{exception.GetType().Name}: {exception.Message}";
        return message.Length <= 2_000 ? message : message[..2_000];
    }

    private static IEmbeddingGenerator CreateCountingGenerator(
        IEmbeddingGenerator generator,
        Action onRequest) => generator is IEmbeddingIdentityResolver identityResolver
            ? new IdentityResolvingCountingEmbeddingGenerator(generator, identityResolver, onRequest)
            : new CountingEmbeddingGenerator(generator, onRequest);

    private class CountingEmbeddingGenerator : IEmbeddingGenerator
    {
        private readonly IEmbeddingGenerator _inner;
        private readonly Action _onRequest;

        public CountingEmbeddingGenerator(IEmbeddingGenerator inner, Action onRequest)
        {
            _inner = inner;
            _onRequest = onRequest;
        }

        public string Provider => _inner.Provider;
        public string Model => _inner.Model;

        public Task<EmbeddingBatch> GenerateAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default)
        {
            _onRequest();
            return _inner.GenerateAsync(request, cancellationToken);
        }
    }

    private sealed class IdentityResolvingCountingEmbeddingGenerator :
        CountingEmbeddingGenerator,
        IEmbeddingIdentityResolver
    {
        private readonly IEmbeddingIdentityResolver _identityResolver;

        public IdentityResolvingCountingEmbeddingGenerator(
            IEmbeddingGenerator generator,
            IEmbeddingIdentityResolver identityResolver,
            Action onRequest)
            : base(generator, onRequest)
        {
            _identityResolver = identityResolver;
        }

        public Task<EmbeddingIdentity> ResolveIdentityAsync(
            int dimensions,
            CancellationToken cancellationToken = default) =>
            _identityResolver.ResolveIdentityAsync(dimensions, cancellationToken);
    }

    private sealed class RunAccumulator
    {
        public RunAccumulator(string runId, IndexRunKind kind, DateTimeOffset startedAt)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
            RunId = runId;
            Kind = kind;
            StartedAt = startedAt;
        }

        public string RunId { get; }
        public IndexRunKind Kind { get; }
        public DateTimeOffset StartedAt { get; }
        public long SourcesRead { get; set; }
        public long DocumentsWritten { get; set; }
        public long DocumentsDeleted { get; set; }
        public long EmbeddingRequests { get; set; }
        public long EmbeddingsReused { get; set; }
        public long EmbeddingsWritten { get; set; }
        public bool EmbeddingFailure { get; set; }
        public EmbeddingIdentity? EmbeddingIdentity { get; set; }
        public List<string> Diagnostics { get; } = [];

        public IndexRunStatistics Complete(DateTimeOffset completedAt) => new()
        {
            RunId = RunId,
            Kind = Kind,
            Outcome = EmbeddingFailure ? IndexRunOutcome.PartiallyEmbedded : IndexRunOutcome.Succeeded,
            StartedAt = StartedAt,
            CompletedAt = completedAt,
            SourcesRead = SourcesRead,
            DocumentsWritten = DocumentsWritten,
            DocumentsDeleted = DocumentsDeleted,
            EmbeddingRequests = EmbeddingRequests,
            EmbeddingsReused = EmbeddingsReused,
            EmbeddingsWritten = EmbeddingsWritten,
            FailureCode = EmbeddingFailure ? "embedding-unavailable" : null,
            FailureMessage = EmbeddingFailure ? string.Join(" ", Diagnostics) : null,
            EmbeddingIdentity = EmbeddingIdentity
        };

        public IndexRunStatistics Fail(
            IndexRunOutcome outcome,
            string failureCode,
            string failureMessage,
            DateTimeOffset completedAt) => new()
        {
            RunId = RunId,
            Kind = Kind,
            Outcome = outcome,
            StartedAt = StartedAt,
            CompletedAt = completedAt,
            SourcesRead = SourcesRead,
            DocumentsWritten = DocumentsWritten,
            DocumentsDeleted = DocumentsDeleted,
            EmbeddingRequests = EmbeddingRequests,
            EmbeddingsReused = EmbeddingsReused,
            EmbeddingsWritten = EmbeddingsWritten,
            FailureCode = failureCode,
            FailureMessage = failureMessage,
            EmbeddingIdentity = EmbeddingIdentity
        };
    }
}
