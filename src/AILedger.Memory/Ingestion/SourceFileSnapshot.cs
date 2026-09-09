using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace AILedger.Memory.Ingestion;

internal sealed record SourceFileSnapshot(byte[] Bytes, string ContentHash)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task<SourceFileSnapshot> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length > int.MaxValue)
        {
            throw new InvalidDataException($"Canonical source '{fullPath}' is too large to index in one pass.");
        }

        var bytes = new byte[(int)stream.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException($"Canonical source '{fullPath}' changed while it was being read.");
            }

            offset += read;
        }

        return new SourceFileSnapshot(bytes, Hash(bytes));
    }

    public static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string Decode(ReadOnlySpan<byte> bytes, string path, long lineNumber)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"Invalid UTF-8 at line {lineNumber} in '{path}'.", exception);
        }
    }

    public IReadOnlyList<SourceLine> ReadCompleteLines(string path, bool allowIncompleteFinalLine)
    {
        var lines = new List<SourceLine>();
        var start = 0;
        long lineNumber = 1;

        for (var index = 0; index < Bytes.Length; index++)
        {
            if (Bytes[index] != (byte)'\n')
            {
                continue;
            }

            var length = index - start;
            if (length > 0 && Bytes[index - 1] == (byte)'\r')
            {
                length--;
            }

            lines.Add(new SourceLine(
                lineNumber++,
                start,
                index + 1,
                Decode(Bytes.AsSpan(start, length), path, lineNumber - 1)));
            start = index + 1;
        }

        if (start != Bytes.Length && !allowIncompleteFinalLine)
        {
            throw new InvalidDataException($"Canonical JSONL source '{path}' has an incomplete final row.");
        }

        return lines;
    }
}

internal sealed record SourceLine(long Number, long ByteOffset, long EndByteOffset, string Text);
