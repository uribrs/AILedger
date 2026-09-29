namespace AILedger.Cli.Findings;

// The bound is applied while reading bytes, before allocating JSON/string graphs. Oversized frames
// are drained to the next newline so a following independent request can still be decoded.
internal sealed class FindingsFrameReader(Stream input)
{
    internal const int MaximumFrameBytes = 256 * 1024 + 16 * 1024;
    private readonly byte[] _buffer = new byte[4096];
    private int _offset;
    private int _count;

    internal async Task<FindingsFrame?> ReadAsync(CancellationToken cancellationToken)
    {
        using var frame = new MemoryStream();
        var oversized = false;
        while (true)
        {
            if (_offset == _count)
            {
                _count = await input.ReadAsync(_buffer, cancellationToken).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
                _offset = 0;
                if (_count == 0)
                    return frame.Length == 0 && !oversized ? null : new(frame.ToArray(), oversized, false);
            }
            var end = System.Array.IndexOf(_buffer, (byte)'\n', _offset, _count - _offset);
            var length = (end < 0 ? _count : end) - _offset;
            if (!oversized)
            {
                if (frame.Length + length > MaximumFrameBytes) oversized = true;
                else frame.Write(_buffer, _offset, length);
            }
            _offset += length;
            if (end < 0) continue;
            _offset++;
            return new(frame.ToArray(), oversized, true);
        }
    }
}

internal sealed record FindingsFrame(byte[] Bytes, bool Oversized, bool Terminated);
