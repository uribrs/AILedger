using AILedger.Memory.Contracts;
using AILedger.Memory.Ingestion;
using System.IO;
using System.Linq;

namespace AILedger.Memory.Normalization;

public sealed class LessonDocumentNormalizer
{
    public IReadOnlyList<MemoryDocument> Normalize(SourceDescriptor source, IReadOnlyList<LocatedLesson> lessons)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(lessons);
        var superseded = lessons
            .Where(item => item.Lesson.SupersedesLessonId is not null)
            .Select(item => item.Lesson.SupersedesLessonId!.Value.Value)
            .ToHashSet(StringComparer.Ordinal);

        return lessons.Select(item =>
        {
            var lesson = item.Lesson;
            var relations = lesson.SupersedesLessonId is { } previous
                ? new[] { new DocumentRelation { Kind = DocumentRelationKind.Supersedes, TargetId = previous.Value } }
                : [];
            return MemoryDocumentFactory.Create(
                source,
                MemoryDocumentKind.Lesson,
                $"{lesson.Repo ?? source.Repository ?? string.Empty}:{lesson.Id.Value}",
                MemoryDocumentFactory.Text(
                    ("Lesson", lesson.Statement), ("Outcome", lesson.Outcome), ("Class", lesson.Class),
                    ("Source task", lesson.SourceTaskId.Value), ("Source kind", lesson.SourceKind),
                    ("Source record", lesson.SourceRecordId), ("Verify", lesson.Verify),
                    ("Do not", lesson.DoNot), ("Established by", lesson.Actor)),
                lesson.Provenance.RecordedAt,
                lesson.Id.Value,
                MemoryAuthority.MintedLesson,
                superseded.Contains(lesson.Id.Value) ? MemoryLifecycle.Superseded : MemoryLifecycle.Active,
                [new MemoryCitation
                {
                    Kind = MemoryCitationKind.LessonRow,
                    SourcePath = Path.GetFullPath(source.CanonicalPath),
                    LineNumber = item.LineNumber,
                    RecordId = lesson.Id.Value
                }],
                actorId: lesson.Provenance.ActorId.Value,
                tags: lesson.Tags ?? [],
                relatedIds: lesson.Citations.Append(lesson.SourceRecordId),
                relations: relations,
                repository: lesson.Repo ?? source.Repository,
                taskId: lesson.SourceTaskId.Value);
        }).OrderBy(document => document.Id, StringComparer.Ordinal).ToArray();
    }
}
