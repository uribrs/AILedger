using AILedger.Memory.Contracts;
using AILedger.Memory.Ingestion;
using System.IO;
using System.Linq;

namespace AILedger.Memory.Normalization;

public sealed class RefusalDocumentNormalizer
{
    public IReadOnlyList<MemoryDocument> Normalize(SourceDescriptor source, IReadOnlyList<LocatedRefusal> refusals)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(refusals);

        return refusals.Select(item =>
        {
            var refusal = item.Refusal;
            var recordIdentity = $"byte:{item.ByteOffset}";
            return MemoryDocumentFactory.Create(
                source,
                MemoryDocumentKind.RefusalChain,
                recordIdentity,
                MemoryDocumentFactory.Text(
                    ("Refusal", "unmatched"), ("Attempted command", refusal.Command),
                    ("Site", refusal.Site), ("Message", refusal.Message),
                    ("Task version", refusal.TaskVersion), ("Actor", refusal.ActorId.Value)),
                refusal.RecordedAt,
                $"{item.ByteOffset}",
                MemoryAuthority.HistoricalRecord,
                MemoryLifecycle.Unresolved,
                [new MemoryCitation
                {
                    Kind = MemoryCitationKind.RefusalRow,
                    SourcePath = Path.GetFullPath(source.CanonicalPath),
                    LineNumber = item.LineNumber,
                    ByteOffset = item.ByteOffset,
                    RecordId = recordIdentity
                }],
                actorId: refusal.ActorId.Value,
                tags: [refusal.Site],
                relatedIds: [$"task-version:{refusal.TaskVersion}"]);
        }).OrderBy(document => document.Id, StringComparer.Ordinal).ToArray();
    }
}
