using AILedger.Memory.Contracts;

namespace AILedger.Memory.Embeddings;

public sealed record DocumentEmbeddingPipelineOptions
{
    public int BatchSize { get; init; } = 32;
}

public sealed record EmbeddingPreparation
{
    public required EmbeddingIdentity? Identity { get; init; }
    public required IReadOnlyList<DocumentEmbedding> Embeddings { get; init; }
    public required long EmbeddingRequests { get; init; }
    public required long EmbeddingsReused { get; init; }
    public required long EmbeddingsGenerated { get; init; }
}

/// <summary>Plans reusable chunks and generates only missing document vectors.</summary>
public sealed class DocumentEmbeddingPipeline
{
    private readonly IEmbeddingGenerator _generator;
    private readonly DocumentChunker _chunker;
    private readonly int _batchSize;

    public DocumentEmbeddingPipeline(
        IEmbeddingGenerator generator,
        DocumentChunker? chunker = null,
        DocumentEmbeddingPipelineOptions? options = null)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _chunker = chunker ?? new DocumentChunker();
        _batchSize = (options ?? new DocumentEmbeddingPipelineOptions()).BatchSize;
        if (_batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "BatchSize must be positive.");
        }
    }

    public async Task<EmbeddingPreparation> PrepareAsync(
        IReadOnlyList<MemoryDocument> documents,
        IReadOnlyList<DocumentEmbedding> reusableEmbeddings,
        EmbeddingIdentity? requiredIdentity = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(reusableEmbeddings);
        requiredIdentity?.Validate();

        if (requiredIdentity is not null)
        {
            if (_generator is not IEmbeddingIdentityResolver identityResolver)
            {
                throw new InvalidOperationException(
                    "The embedding generator cannot verify the active model-build identity; rebuild with an identity-resolving generator.");
            }

            var currentIdentity = await identityResolver.ResolveIdentityAsync(
                requiredIdentity.Dimensions,
                cancellationToken).ConfigureAwait(false);
            if (currentIdentity != requiredIdentity)
            {
                throw new InvalidOperationException(
                    "The embedding model build does not match the active index identity; rebuild before reusing vectors.");
            }
        }

        var reusableByHash = requiredIdentity is null
            ? new Dictionary<string, DocumentEmbedding[]>(StringComparer.Ordinal)
            : reusableEmbeddings
                .Where(item => item.Identity == requiredIdentity)
                .GroupBy(item => item.ContentHash, StringComparer.Ordinal)
                .Select(group => (group.Key, Chunks: FindCompleteChunkSet(group)))
                .Where(item => item.Chunks.Length > 0)
                .ToDictionary(item => item.Key, item => item.Chunks, StringComparer.Ordinal);

        var output = new List<DocumentEmbedding>();
        var pending = new List<PendingChunk>();
        var maximumInputUtf8Bytes = _generator is IEmbeddingInputLimitResolver inputLimitResolver
            ? await inputLimitResolver.ResolveMaximumInputUtf8BytesAsync(
                EmbeddingInputKind.Document,
                cancellationToken).ConfigureAwait(false)
            : (int?)null;
        long reused = 0;
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reusableByHash.TryGetValue(document.ContentHash, out var reusable) && reusable.Length > 0)
            {
                foreach (var item in reusable)
                {
                    output.Add(item with { DocumentId = document.Id, ContentHash = document.ContentHash });
                    reused++;
                }

                continue;
            }

            var chunks = _chunker.Split(document.Text, maximumInputUtf8Bytes);
            for (var index = 0; index < chunks.Count; index++)
            {
                pending.Add(new PendingChunk(document, chunks[index], index, chunks.Count));
            }
        }

        EmbeddingIdentity? observedIdentity = requiredIdentity;
        long requests = 0;
        for (var start = 0; start < pending.Count; start += _batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(_batchSize, pending.Count - start);
            var batchItems = pending.GetRange(start, count);
            var batch = await _generator.GenerateAsync(
                new EmbeddingRequest
                {
                    InputKind = EmbeddingInputKind.Document,
                    Inputs = batchItems.Select(item => item.Text).ToArray()
                },
                cancellationToken).ConfigureAwait(false);
            requests++;

            ValidateBatch(batch, count, observedIdentity);
            observedIdentity ??= batch.Identity;
            for (var index = 0; index < count; index++)
            {
                var item = batchItems[index];
                output.Add(new DocumentEmbedding
                {
                    DocumentId = item.Document.Id,
                    ContentHash = item.Document.ContentHash,
                    Identity = batch.Identity,
                    ChunkIndex = item.Index,
                    ChunkCount = item.Count,
                    Vector = EmbeddingVector.Normalize(batch.Vectors[index].Span),
                    EmbeddedAt = DateTimeOffset.UtcNow
                });
            }
        }

        return new EmbeddingPreparation
        {
            Identity = observedIdentity,
            Embeddings = output,
            EmbeddingRequests = requests,
            EmbeddingsReused = reused,
            EmbeddingsGenerated = pending.Count
        };
    }

    private static DocumentEmbedding[] FindCompleteChunkSet(IEnumerable<DocumentEmbedding> items)
    {
        foreach (var documentGroup in items.GroupBy(item => item.DocumentId, StringComparer.Ordinal))
        {
            var ordered = documentGroup.OrderBy(item => item.ChunkIndex).ToArray();
            if (ordered.Length > 0 && ordered[0].ChunkCount == ordered.Length &&
                ordered.Select((item, index) => (item, index))
                    .All(pair =>
                        pair.item.ChunkIndex == pair.index &&
                        pair.item.ChunkCount == ordered.Length &&
                        pair.item.Vector.Length == pair.item.Identity.Dimensions))
            {
                return ordered;
            }
        }

        return [];
    }

    private static void ValidateBatch(
        EmbeddingBatch batch,
        int expectedCount,
        EmbeddingIdentity? requiredIdentity)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(batch.Identity);
        ArgumentNullException.ThrowIfNull(batch.Vectors);
        batch.Identity.Validate();
        if (batch.Vectors.Count != expectedCount)
        {
            throw new InvalidDataException(
                $"The embedding provider returned {batch.Vectors.Count} vectors for {expectedCount} inputs.");
        }

        if (requiredIdentity is not null && batch.Identity != requiredIdentity)
        {
            throw new InvalidOperationException("The embedding provider identity does not match the active index identity.");
        }

        foreach (var vector in batch.Vectors)
        {
            if (vector.Length != batch.Identity.Dimensions)
            {
                throw new InvalidDataException("An embedding vector does not match its declared dimensionality.");
            }
        }
    }

    private sealed record PendingChunk(MemoryDocument Document, string Text, int Index, int Count);
}
