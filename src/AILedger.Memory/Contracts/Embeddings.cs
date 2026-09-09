namespace AILedger.Memory.Contracts;

public enum EmbeddingInputKind
{
    Document,
    Query
}

public sealed record EmbeddingIdentity
{
    public const int MinimumDimensions = 1;
    public const int MaximumDimensions = 4096;

    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required int Dimensions { get; init; }
    public required string Version { get; init; }

    public void Validate()
    {
        if (Dimensions is < MinimumDimensions or > MaximumDimensions)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Dimensions),
                $"Embedding dimensions must be between {MinimumDimensions} and {MaximumDimensions}.");
        }
    }
}

public sealed record EmbeddingRequest
{
    public required EmbeddingInputKind InputKind { get; init; }
    public required IReadOnlyList<string> Inputs { get; init; }
}

public sealed record EmbeddingBatch
{
    public required EmbeddingIdentity Identity { get; init; }
    public required IReadOnlyList<ReadOnlyMemory<float>> Vectors { get; init; }
}

public interface IEmbeddingGenerator
{
    string Provider { get; }
    string Model { get; }

    Task<EmbeddingBatch> GenerateAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record DocumentEmbedding
{
    public required string DocumentId { get; init; }
    public required string ContentHash { get; init; }
    public required EmbeddingIdentity Identity { get; init; }
    public required int ChunkIndex { get; init; }
    public required int ChunkCount { get; init; }
    public required ReadOnlyMemory<float> Vector { get; init; }
    public required DateTimeOffset EmbeddedAt { get; init; }
}

public static class SemanticScoring
{
    public static float MaximumChunkScore(IEnumerable<float> chunkScores)
    {
        ArgumentNullException.ThrowIfNull(chunkScores);

        var found = false;
        var maximum = float.NegativeInfinity;
        foreach (var score in chunkScores)
        {
            if (float.IsNaN(score))
            {
                throw new ArgumentException("Chunk scores cannot contain NaN.", nameof(chunkScores));
            }

            found = true;
            maximum = Math.Max(maximum, score);
        }

        return found
            ? maximum
            : throw new ArgumentException("At least one chunk score is required.", nameof(chunkScores));
    }
}
