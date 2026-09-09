using AILedger.Memory.Contracts;

namespace AILedger.Memory.Ingestion;

public interface ICanonicalSourceReader
{
    CanonicalSourceKind Kind { get; }

    Task<SourceDelta> ReadAsync(
        SourceDescriptor source,
        SourceCheckpoint? previousCheckpoint = null,
        CancellationToken cancellationToken = default);
}

internal static class CanonicalSourceReader
{
    public static void ValidateSource(SourceDescriptor source, CanonicalSourceKind expectedKind)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.CanonicalPath);

        if (source.Kind != expectedKind)
        {
            throw new ArgumentException(
                $"Reader for '{expectedKind}' cannot read source kind '{source.Kind}'.",
                nameof(source));
        }
    }

    public static void ValidateCheckpoint(
        SourceDescriptor source,
        SourceCheckpoint? checkpoint,
        CanonicalSourceKind expectedKind)
    {
        if (checkpoint is null)
        {
            return;
        }

        if (checkpoint.SourceId != source.Id || checkpoint.Kind != expectedKind)
        {
            throw new ArgumentException("The checkpoint does not belong to the requested source.", nameof(checkpoint));
        }
    }

    public static SourceDelta Unchanged(SourceDescriptor source, SourceCheckpoint checkpoint) => new()
    {
        Source = source,
        PreviousCheckpoint = checkpoint,
        NextCheckpoint = checkpoint,
        Upserts = [],
        Deletes = []
    };
}
