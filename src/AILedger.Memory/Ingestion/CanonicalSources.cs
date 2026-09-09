using AILedger.Memory.Contracts;
using System.IO;
using System.Linq;

namespace AILedger.Memory.Ingestion;

public static class CanonicalSources
{
    public static SourceDescriptor EventHistory(
        string canonicalRootIdentity,
        string taskId,
        string eventsPath,
        string? repository = null) =>
        TaskSource(canonicalRootIdentity, taskId, eventsPath, CanonicalSourceKind.EventHistory, repository);

    public static SourceDescriptor RefusalJournal(
        string canonicalRootIdentity,
        string taskId,
        string journalPath,
        string? repository = null) =>
        TaskSource(canonicalRootIdentity, taskId, journalPath, CanonicalSourceKind.RefusalJournal, repository);

    public static SourceDescriptor PublishedLessons(
        string storeIdentity,
        string lessonsPath) => new()
        {
            Id = MemoryIdentity.HashParts("canonical-source-v1", storeIdentity, CanonicalSourceKind.PublishedLessons.ToString()),
            Kind = CanonicalSourceKind.PublishedLessons,
            CanonicalPath = Path.GetFullPath(lessonsPath)
        };

    private static SourceDescriptor TaskSource(
        string canonicalRootIdentity,
        string taskId,
        string path,
        CanonicalSourceKind kind,
        string? repository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalRootIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new SourceDescriptor
        {
            Id = MemoryIdentity.HashParts("canonical-source-v1", canonicalRootIdentity, taskId, kind.ToString()),
            Kind = kind,
            CanonicalPath = Path.GetFullPath(path),
            Repository = repository,
            TaskId = taskId
        };
    }
}

public sealed class CanonicalSourceReaderSet
{
    private readonly IReadOnlyDictionary<CanonicalSourceKind, ICanonicalSourceReader> _readers;

    public CanonicalSourceReaderSet(IEnumerable<ICanonicalSourceReader>? readers = null)
    {
        var selected = readers?.ToArray() ??
        [
            new EventHistorySourceReader(),
            new RefusalJournalSourceReader(),
            new LessonStoreSourceReader()
        ];
        _readers = selected.ToDictionary(reader => reader.Kind);
        if (_readers.Count != Enum.GetValues<CanonicalSourceKind>().Length)
        {
            throw new ArgumentException("Exactly one reader is required for every canonical source kind.", nameof(readers));
        }
    }

    public Task<SourceDelta> ReadAsync(
        SourceDescriptor source,
        SourceCheckpoint? previousCheckpoint = null,
        CancellationToken cancellationToken = default) =>
        _readers[source.Kind].ReadAsync(source, previousCheckpoint, cancellationToken);
}
