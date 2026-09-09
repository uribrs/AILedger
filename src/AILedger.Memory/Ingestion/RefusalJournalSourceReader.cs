using System.Text.Json;
using System.IO;
using AILedger.Memory.Contracts;
using AILedger.Memory.Normalization;
using AILedger.Storage;

namespace AILedger.Memory.Ingestion;

public sealed class RefusalJournalSourceReader : ICanonicalSourceReader
{
    private readonly JsonSerializerOptions _json = LedgerJson.CreateOptions();
    private readonly RefusalDocumentNormalizer _normalizer;

    public RefusalJournalSourceReader(RefusalDocumentNormalizer? normalizer = null) =>
        _normalizer = normalizer ?? new RefusalDocumentNormalizer();

    public CanonicalSourceKind Kind => CanonicalSourceKind.RefusalJournal;

    public async Task<SourceDelta> ReadAsync(
        SourceDescriptor source,
        SourceCheckpoint? previousCheckpoint = null,
        CancellationToken cancellationToken = default)
    {
        CanonicalSourceReader.ValidateSource(source, Kind);
        CanonicalSourceReader.ValidateCheckpoint(source, previousCheckpoint, Kind);

        var snapshot = await SourceFileSnapshot.ReadAsync(source.CanonicalPath, cancellationToken).ConfigureAwait(false);
        var startOffset = 0L;
        var committedLinesBefore = 0L;
        if (previousCheckpoint is RefusalJournalCheckpoint previous &&
            previous.CommittedByteOffset <= snapshot.Bytes.LongLength &&
            previous.CommittedByteOffset <= int.MaxValue &&
            SourceFileSnapshot.Hash(snapshot.Bytes.AsSpan(0, (int)previous.CommittedByteOffset)) == previous.PrefixHash)
        {
            startOffset = previous.CommittedByteOffset;
            committedLinesBefore = previous.CommittedLineCount;
        }

        var completeEnd = LastCompleteLineEnd(snapshot.Bytes);
        if (completeEnd < startOffset)
        {
            startOffset = 0;
            committedLinesBefore = 0;
        }

        var located = new List<LocatedRefusal>();
        if (completeEnd > startOffset)
        {
            var segment = snapshot.Bytes.AsSpan((int)startOffset, (int)(completeEnd - startOffset)).ToArray();
            var segmentSnapshot = new SourceFileSnapshot(segment, SourceFileSnapshot.Hash(segment));
            foreach (var line in segmentSnapshot.ReadCompleteLines(source.CanonicalPath, allowIncompleteFinalLine: false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var actualLine = committedLinesBefore + line.Number;
                var actualOffset = startOffset + line.ByteOffset;
                if (string.IsNullOrWhiteSpace(line.Text))
                {
                    throw new InvalidDataException($"Blank refusal at line {actualLine} in '{source.CanonicalPath}'.");
                }

                try
                {
                    var refusal = JsonSerializer.Deserialize<RefusalRecord>(line.Text, _json)
                        ?? throw new JsonException("The refusal row is null.");
                    located.Add(new LocatedRefusal(refusal, actualLine, actualOffset));
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException(
                        $"Invalid refusal JSON at line {actualLine} in '{source.CanonicalPath}'.",
                        exception);
                }
            }
        }

        var prefixHash = SourceFileSnapshot.Hash(snapshot.Bytes.AsSpan(0, (int)completeEnd));
        var fingerprint = MemoryIdentity.HashParts("refusal-journal-v1", prefixHash, MemoryIdentity.NormalizerVersion);
        var checkpoint = new RefusalJournalCheckpoint
        {
            SourceId = source.Id,
            Kind = Kind,
            SourceFingerprint = fingerprint,
            ObservedAt = DateTimeOffset.UtcNow,
            CommittedByteOffset = completeEnd,
            CommittedLineCount = committedLinesBefore + located.Count,
            PrefixHash = prefixHash
        };

        if (previousCheckpoint is RefusalJournalCheckpoint unchanged &&
            startOffset == unchanged.CommittedByteOffset && completeEnd == startOffset)
        {
            return CanonicalSourceReader.Unchanged(source, unchanged);
        }

        return new SourceDelta
        {
            Source = source,
            PreviousCheckpoint = previousCheckpoint,
            NextCheckpoint = checkpoint,
            Upserts = _normalizer.Normalize(source, located),
            Deletes = []
        };
    }

    private static long LastCompleteLineEnd(byte[] bytes)
    {
        for (var index = bytes.Length - 1; index >= 0; index--)
        {
            if (bytes[index] == (byte)'\n')
            {
                return index + 1L;
            }
        }

        return 0;
    }
}

public sealed record LocatedRefusal(RefusalRecord Refusal, long LineNumber, long ByteOffset);
