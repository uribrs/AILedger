using AILedger.Memory.Contracts;
using System.Globalization;
using System.Linq;

namespace AILedger.Memory.Normalization;

internal static class MemoryDocumentFactory
{
    public static MemoryDocument Create(
        SourceDescriptor source,
        MemoryDocumentKind kind,
        string recordIdentity,
        string text,
        DateTimeOffset sourceTimestamp,
        string sourceVersion,
        MemoryAuthority authority,
        MemoryLifecycle lifecycle,
        IReadOnlyList<MemoryCitation> citations,
        string? workItemId = null,
        string? runId = null,
        string? actorId = null,
        IEnumerable<string?>? tags = null,
        IEnumerable<string?>? relatedIds = null,
        IEnumerable<DocumentRelation>? relations = null,
        string? repository = null,
        string? taskId = null)
    {
        var normalizedText = NormalizeText(text);
        return new MemoryDocument
        {
            Id = MemoryIdentity.CreateDocumentId(source.Id, kind, recordIdentity),
            Kind = kind,
            Text = normalizedText,
            Repository = repository ?? source.Repository,
            TaskId = taskId ?? source.TaskId,
            WorkItemId = workItemId,
            RunId = runId,
            ActorId = actorId,
            SourceTimestamp = sourceTimestamp,
            SourceVersion = sourceVersion,
            Authority = authority,
            Lifecycle = lifecycle,
            Tags = NormalizeValues(tags),
            RelatedIds = NormalizeValues(relatedIds),
            Citations = citations,
            Relations = (relations ?? []).
                Distinct().
                OrderBy(item => item.Kind).
                ThenBy(item => item.TargetId, StringComparer.Ordinal).
                ToArray(),
            ContentHash = MemoryIdentity.CreateContentHash(normalizedText),
            NormalizerVersion = MemoryIdentity.NormalizerVersion
        };
    }

    public static string Text(params (string Label, object? Value)[] fields) =>
        string.Join('\n', fields
            .Where(field => field.Value is not null)
            .Select(field => $"{field.Label}: {Format(field.Value!)}"));

    private static string Format(object value) => value switch
    {
        string text => text.Trim(),
        IEnumerable<string> values => string.Join(", ", values.Order(StringComparer.Ordinal)),
        DateTimeOffset timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty,
        _ => value.ToString()?.Trim() ?? string.Empty
    };

    private static string NormalizeText(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim() + "\n";

    private static IReadOnlyList<string> NormalizeValues(IEnumerable<string?>? values) =>
        (values ?? [])
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim())
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
}
