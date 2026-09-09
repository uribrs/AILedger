using System.Text.Json.Serialization;

namespace AILedger.Memory.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<CanonicalSourceKind>))]
public enum CanonicalSourceKind
{
    EventHistory,
    RefusalJournal,
    PublishedLessons
}

public sealed record SourceDescriptor
{
    public required string Id { get; init; }
    public required CanonicalSourceKind Kind { get; init; }
    public required string CanonicalPath { get; init; }
    public string? Repository { get; init; }
    public string? TaskId { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "checkpointType")]
[JsonDerivedType(typeof(EventHistoryCheckpoint), "eventHistory")]
[JsonDerivedType(typeof(RefusalJournalCheckpoint), "refusalJournal")]
[JsonDerivedType(typeof(LessonStoreCheckpoint), "lessonStore")]
public abstract record SourceCheckpoint
{
    public required string SourceId { get; init; }
    public required CanonicalSourceKind Kind { get; init; }
    public required string SourceFingerprint { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
}

public sealed record EventHistoryCheckpoint : SourceCheckpoint
{
    public required long FileLength { get; init; }
    public required string FileContentHash { get; init; }
    public required int EventCount { get; init; }
    public string? LastEventId { get; init; }
}

public sealed record RefusalJournalCheckpoint : SourceCheckpoint
{
    public required long CommittedByteOffset { get; init; }
    public required long CommittedLineCount { get; init; }
    public required string PrefixHash { get; init; }
}

public sealed record LessonStoreCheckpoint : SourceCheckpoint
{
    public required long FileLength { get; init; }
    public required string FileContentHash { get; init; }
    public required int LessonCount { get; init; }
    public string? LastLessonId { get; init; }
}

public sealed record SourceDelta
{
    public required SourceDescriptor Source { get; init; }
    public SourceCheckpoint? PreviousCheckpoint { get; init; }
    public required SourceCheckpoint NextCheckpoint { get; init; }
    public required IReadOnlyList<MemoryDocument> Upserts { get; init; }
    public IReadOnlyList<string> Deletes { get; init; } = [];
}
