using System.Text.Json;
using System.IO;
using System.Linq;
using AILedger.Core.Contracts;
using AILedger.Memory.Contracts;
using AILedger.Memory.Normalization;
using AILedger.Storage;

namespace AILedger.Memory.Ingestion;

public sealed class LessonStoreSourceReader : ICanonicalSourceReader
{
    private readonly JsonSerializerOptions _json = LedgerJson.CreateOptions();
    private readonly LessonDocumentNormalizer _normalizer;

    public LessonStoreSourceReader(LessonDocumentNormalizer? normalizer = null) =>
        _normalizer = normalizer ?? new LessonDocumentNormalizer();

    public CanonicalSourceKind Kind => CanonicalSourceKind.PublishedLessons;

    public async Task<SourceDelta> ReadAsync(
        SourceDescriptor source,
        SourceCheckpoint? previousCheckpoint = null,
        CancellationToken cancellationToken = default)
    {
        CanonicalSourceReader.ValidateSource(source, Kind);
        CanonicalSourceReader.ValidateCheckpoint(source, previousCheckpoint, Kind);

        var snapshot = await SourceFileSnapshot.ReadAsync(source.CanonicalPath, cancellationToken).ConfigureAwait(false);
        var fingerprint = MemoryIdentity.HashParts("lesson-store-v1", snapshot.ContentHash, MemoryIdentity.NormalizerVersion);
        if (previousCheckpoint is LessonStoreCheckpoint previous &&
            previous.FileContentHash == snapshot.ContentHash &&
            previous.SourceFingerprint == fingerprint)
        {
            return CanonicalSourceReader.Unchanged(source, previous);
        }

        var lines = snapshot.ReadCompleteLines(source.CanonicalPath, allowIncompleteFinalLine: false);
        var isAppend = previousCheckpoint is LessonStoreCheckpoint appendCheckpoint &&
            appendCheckpoint.SourceFingerprint == MemoryIdentity.HashParts(
                "lesson-store-v1", appendCheckpoint.FileContentHash, MemoryIdentity.NormalizerVersion) &&
            appendCheckpoint.FileLength <= snapshot.Bytes.LongLength &&
            appendCheckpoint.FileLength <= int.MaxValue &&
            appendCheckpoint.LessonCount <= lines.Count &&
            SourceFileSnapshot.Hash(snapshot.Bytes.AsSpan(0, (int)appendCheckpoint.FileLength)) ==
                appendCheckpoint.FileContentHash;
        var lessons = new List<LocatedLesson>(lines.Count);
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line.Text))
            {
                throw new InvalidDataException($"Blank lesson at line {line.Number} in '{source.CanonicalPath}'.");
            }

            try
            {
                var lesson = JsonSerializer.Deserialize<Lesson>(line.Text, _json)
                    ?? throw new JsonException("The lesson row is null.");
                lessons.Add(new LocatedLesson(lesson, line.Number));
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    $"Invalid lesson JSON at line {line.Number} in '{source.CanonicalPath}'.",
                    exception);
            }
        }

        var checkpoint = new LessonStoreCheckpoint
        {
            SourceId = source.Id,
            Kind = Kind,
            SourceFingerprint = fingerprint,
            ObservedAt = DateTimeOffset.UtcNow,
            FileLength = snapshot.Bytes.LongLength,
            FileContentHash = snapshot.ContentHash,
            LessonCount = lessons.Count,
            LastLessonId = lessons.Count == 0 ? null : lessons[^1].Lesson.Id.Value
        };

        var documents = _normalizer.Normalize(source, lessons);
        if (isAppend && previousCheckpoint is LessonStoreCheckpoint appendedAfter)
        {
            var appended = lessons.Skip(appendedAfter.LessonCount).Select(item => item.Lesson).ToArray();
            var affectedIds = appended.Select(item => item.Id.Value)
                .Concat(appended.Where(item => item.SupersedesLessonId is not null)
                    .Select(item => item.SupersedesLessonId!.Value.Value))
                .ToHashSet(StringComparer.Ordinal);
            documents = documents
                .Where(document => document.Citations.Any(citation =>
                    citation.RecordId is { } recordId && affectedIds.Contains(recordId)))
                .ToArray();
        }

        return new SourceDelta
        {
            Source = source,
            PreviousCheckpoint = previousCheckpoint,
            NextCheckpoint = checkpoint,
            Upserts = documents,
            Deletes = []
        };
    }
}

public sealed record LocatedLesson(Lesson Lesson, long LineNumber);
