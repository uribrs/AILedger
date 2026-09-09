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

    public IReadOnlyList<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length <= _options.MaximumCharacters)
        {
            return [text];
        }

        var chunks = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var remaining = text.Length - start;
            var length = Math.Min(_options.MaximumCharacters, remaining);
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

            start += Math.Max(1, length - _options.OverlapCharacters);
        }

        return chunks;
    }
}
