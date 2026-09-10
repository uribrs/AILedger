using System.Text;

namespace AILedger.Memory.Embeddings;

public sealed record DocumentChunkerOptions
{
    public int MaximumCharacters { get; init; } = 2_000;
    public int OverlapCharacters { get; init; } = 200;
}

public sealed class DocumentChunker
{
    private readonly DocumentChunkerOptions _options;

    public DocumentChunker(DocumentChunkerOptions? options = null)
    {
        _options = options ?? new DocumentChunkerOptions();
        if (_options.MaximumCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumCharacters must be positive.");
        }

        if (_options.OverlapCharacters < 0 || _options.OverlapCharacters >= _options.MaximumCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "OverlapCharacters must be non-negative and smaller than MaximumCharacters.");
        }
    }

    public IReadOnlyList<string> Split(string text, int? maximumUtf8Bytes = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maximumUtf8Bytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumUtf8Bytes), "Maximum UTF-8 bytes must be positive.");
        }

        if (text.Length <= _options.MaximumCharacters &&
            (maximumUtf8Bytes is null || Encoding.UTF8.GetByteCount(text) <= maximumUtf8Bytes))
        {
            return [text];
        }

        var chunks = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var remaining = text.Length - start;
            var length = Math.Min(_options.MaximumCharacters, remaining);
            if (maximumUtf8Bytes is { } byteLimit)
            {
                length = MaximumCharacterLengthWithinUtf8ByteLimit(text, start, length, byteLimit);
                if (length == 0)
                {
                    throw new InvalidDataException(
                        $"The embedding input byte limit {byteLimit} cannot hold the next Unicode scalar value.");
                }
            }

            if (length < remaining)
            {
                var lowerBound = Math.Max(1, length / 2);
                var boundary = text.LastIndexOfAny(['\n', ' ', '\t'], start + length - 1, length);
                if (boundary >= start + lowerBound)
                {
                    length = boundary - start + 1;
                }
            }

            chunks.Add(text.Substring(start, length));
            if (start + length >= text.Length)
            {
                break;
            }

            // OverlapCharacters defines the ratio at MaximumCharacters (200/2000 = 10%
            // by default). Preserve that ratio when a provider byte bound makes chunks
            // smaller; retaining a fixed 200-character overlap would duplicate almost
            // half of every 448-byte chunk.
            var proportionalOverlap = (int)((long)length * _options.OverlapCharacters / _options.MaximumCharacters);
            var overlap = Math.Min(proportionalOverlap, length - 1);
            start += Math.Max(1, length - overlap);
            if (start < text.Length && char.IsLowSurrogate(text[start]))
            {
                // The preceding chunk already contains the complete scalar. When overlap would
                // restart inside it, move past the scalar rather than emitting invalid UTF-16.
                start++;
            }
        }

        return chunks;
    }

    private static int MaximumCharacterLengthWithinUtf8ByteLimit(
        string text,
        int start,
        int maximumCharacters,
        int maximumUtf8Bytes)
    {
        var length = 0;
        var bytes = 0;
        while (length < maximumCharacters)
        {
            var scalarLength = char.IsHighSurrogate(text[start + length]) &&
                start + length + 1 < text.Length &&
                char.IsLowSurrogate(text[start + length + 1])
                ? 2
                : 1;
            if (length + scalarLength > maximumCharacters)
            {
                break;
            }

            var scalarBytes = Encoding.UTF8.GetByteCount(text.AsSpan(start + length, scalarLength));
            if (bytes + scalarBytes > maximumUtf8Bytes)
            {
                break;
            }

            length += scalarLength;
            bytes += scalarBytes;
        }

        return length;
    }
}
