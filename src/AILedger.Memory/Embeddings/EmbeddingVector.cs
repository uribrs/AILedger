using AILedger.Memory.Contracts;

namespace AILedger.Memory.Embeddings;

internal static class EmbeddingVector
{
    public static float[] Normalize(ReadOnlySpan<float> vector)
    {
        if (vector.Length is < EmbeddingIdentity.MinimumDimensions or > EmbeddingIdentity.MaximumDimensions)
        {
            throw new InvalidDataException(
                $"Embedding dimensions must be between {EmbeddingIdentity.MinimumDimensions} and {EmbeddingIdentity.MaximumDimensions}.");
        }

        double squaredMagnitude = 0;
        for (var index = 0; index < vector.Length; index++)
        {
            if (!float.IsFinite(vector[index]))
            {
                throw new InvalidDataException("Embedding vectors must contain only finite values.");
            }

            squaredMagnitude += vector[index] * (double)vector[index];
        }

        if (squaredMagnitude <= 0)
        {
            throw new InvalidDataException("Embedding vectors must have a non-zero magnitude.");
        }

        var scale = 1d / Math.Sqrt(squaredMagnitude);
        var normalized = new float[vector.Length];
        for (var index = 0; index < vector.Length; index++)
        {
            normalized[index] = (float)(vector[index] * scale);
        }

        return normalized;
    }

    public static double CosineSimilarity(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length || left.Length == 0)
        {
            throw new InvalidDataException("Embedding vectors must have the same non-zero dimensionality.");
        }

        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;
        for (var index = 0; index < left.Length; index++)
        {
            if (!float.IsFinite(left[index]) || !float.IsFinite(right[index]))
            {
                throw new InvalidDataException("Embedding vectors must contain only finite values.");
            }

            dot += left[index] * (double)right[index];
            leftMagnitude += left[index] * (double)left[index];
            rightMagnitude += right[index] * (double)right[index];
        }

        if (leftMagnitude <= 0 || rightMagnitude <= 0)
        {
            throw new InvalidDataException("Embedding vectors must have a non-zero magnitude.");
        }

        return Math.Clamp(dot / Math.Sqrt(leftMagnitude * rightMagnitude), -1d, 1d);
    }
}
