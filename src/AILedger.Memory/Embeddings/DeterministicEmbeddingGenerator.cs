using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using AILedger.Memory.Contracts;

namespace AILedger.Memory.Embeddings;

/// <summary>A stable, in-process generator for tests and offline retrieval evaluation.</summary>
public sealed class DeterministicEmbeddingGenerator : IEmbeddingGenerator, IEmbeddingIdentityResolver
{
    private readonly int _dimensions;

    public DeterministicEmbeddingGenerator(int dimensions = 64, string model = "deterministic-hash-v1")
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("A model name is required.", nameof(model));
        }

        _dimensions = dimensions;
        Model = model;
        Identity.Validate();
    }

    public string Provider => "deterministic";

    public string Model { get; }

    public EmbeddingIdentity Identity => new()
    {
        Provider = Provider,
        Model = Model,
        Dimensions = _dimensions,
        Version = "1"
    };

    public Task<EmbeddingIdentity> ResolveIdentityAsync(
        int dimensions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Identity);
    }

    public Task<EmbeddingBatch> GenerateAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Inputs);

        var vectors = new ReadOnlyMemory<float>[request.Inputs.Count];
        for (var inputIndex = 0; inputIndex < request.Inputs.Count; inputIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = request.Inputs[inputIndex]
                ?? throw new ArgumentException("Embedding inputs cannot contain null values.", nameof(request));
            vectors[inputIndex] = CreateVector(request.InputKind, input);
        }

        return Task.FromResult(new EmbeddingBatch
        {
            Identity = Identity,
            Vectors = vectors
        });
    }

    private float[] CreateVector(EmbeddingInputKind inputKind, string input)
    {
        var vector = new float[_dimensions];
        var normalizedText = input.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var tokens = normalizedText.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Feature hashing makes semantically identical tokens overlap while remaining deterministic.
        foreach (var token in tokens.Prepend($"input-kind:{inputKind}"))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var bucket = (int)(BinaryPrimitives.ReadUInt32LittleEndian(hash) % (uint)_dimensions);
            var sign = (hash[4] & 1) == 0 ? 1f : -1f;
            vector[bucket] += sign;
        }

        // Empty input still receives a stable kind feature and therefore has non-zero magnitude.
        return EmbeddingVector.Normalize(vector);
    }
}
