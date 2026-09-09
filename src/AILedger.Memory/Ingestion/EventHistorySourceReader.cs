using System.Text.Json;
using System.IO;
using System.Linq;
using AILedger.Core.Contracts;
using AILedger.Memory.Contracts;
using AILedger.Memory.Normalization;
using AILedger.Storage;

namespace AILedger.Memory.Ingestion;

public sealed class EventHistorySourceReader : ICanonicalSourceReader
{
    private readonly JsonSerializerOptions _json;
    private readonly LedgerDocumentNormalizer _normalizer;

    public EventHistorySourceReader(LedgerDocumentNormalizer? normalizer = null)
    {
        _normalizer = normalizer ?? new LedgerDocumentNormalizer();
        _json = LedgerJson.CreateOptions();
    }

    public CanonicalSourceKind Kind => CanonicalSourceKind.EventHistory;

    public async Task<SourceDelta> ReadAsync(
        SourceDescriptor source,
        SourceCheckpoint? previousCheckpoint = null,
        CancellationToken cancellationToken = default)
    {
        CanonicalSourceReader.ValidateSource(source, Kind);
        CanonicalSourceReader.ValidateCheckpoint(source, previousCheckpoint, Kind);

        var snapshot = await SourceFileSnapshot.ReadAsync(source.CanonicalPath, cancellationToken).ConfigureAwait(false);
        var fingerprint = MemoryIdentity.HashParts("event-history-v1", snapshot.ContentHash, MemoryIdentity.NormalizerVersion);
        if (previousCheckpoint is EventHistoryCheckpoint previous &&
            previous.FileContentHash == snapshot.ContentHash &&
            previous.SourceFingerprint == fingerprint)
        {
            return CanonicalSourceReader.Unchanged(source, previous);
        }

        var sourceLines = snapshot.ReadCompleteLines(source.CanonicalPath, allowIncompleteFinalLine: false);
        if (sourceLines.Count == 0)
        {
            throw new InvalidDataException($"Event history '{source.CanonicalPath}' contains no task-opening event.");
        }

        var isAppend = previousCheckpoint is EventHistoryCheckpoint appendCheckpoint &&
            appendCheckpoint.SourceFingerprint == MemoryIdentity.HashParts(
                "event-history-v1", appendCheckpoint.FileContentHash, MemoryIdentity.NormalizerVersion) &&
            appendCheckpoint.FileLength <= snapshot.Bytes.LongLength &&
            appendCheckpoint.FileLength <= int.MaxValue &&
            appendCheckpoint.EventCount <= sourceLines.Count &&
            SourceFileSnapshot.Hash(snapshot.Bytes.AsSpan(0, (int)appendCheckpoint.FileLength)) ==
                appendCheckpoint.FileContentHash;
        var events = new List<LocatedLedgerEvent>(sourceLines.Count);
        foreach (var line in sourceLines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line.Text))
            {
                throw new InvalidDataException($"Blank event at line {line.Number} in '{source.CanonicalPath}'.");
            }

            try
            {
                var @event = JsonSerializer.Deserialize<LedgerEvent>(line.Text, _json)
                    ?? throw new JsonException("The event row is null.");
                events.Add(new LocatedLedgerEvent(@event, line.Number));
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    $"Invalid event JSON at line {line.Number} in '{source.CanonicalPath}'.",
                    exception);
            }
        }

        var checkpoint = new EventHistoryCheckpoint
        {
            SourceId = source.Id,
            Kind = Kind,
            SourceFingerprint = fingerprint,
            ObservedAt = DateTimeOffset.UtcNow,
            FileLength = snapshot.Bytes.LongLength,
            FileContentHash = snapshot.ContentHash,
            EventCount = events.Count,
            LastEventId = events.Count == 0 ? null : events[^1].Event.EventId.Value
        };

        var documents = _normalizer.Normalize(source, events);
        if (isAppend && previousCheckpoint is EventHistoryCheckpoint appendedAfter)
        {
            var appendedDocuments = documents
                .Where(document => document.Citations.Any(citation => citation.LineNumber > appendedAfter.EventCount))
                .ToArray();
            var affectedDocumentIds = appendedDocuments
                .Select(document => document.Id)
                .Concat(appendedDocuments.SelectMany(document => document.Relations
                    .Where(relation => relation.Kind == DocumentRelationKind.Supersedes)
                    .Select(relation => MemoryIdentity.CreateDocumentId(source.Id, document.Kind, relation.TargetId))))
                .ToHashSet(StringComparer.Ordinal);

            documents = documents.Where(document => affectedDocumentIds.Contains(document.Id)).ToArray();
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

public sealed record LocatedLedgerEvent(LedgerEvent Event, long LineNumber);
