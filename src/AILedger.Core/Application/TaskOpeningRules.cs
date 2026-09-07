using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Command-time counterparts: ValidateTaskOpened, ValidateOpeningRole, ValidateLessonRecalled.
internal static class TaskOpeningRules
{
    internal static IReadOnlyList<LedgerEventData> HandleOpening(LedgerCommand command, DateTimeOffset now)
    {
        if (command is not OpenTaskCommand open)
        {
            throw new GovernanceException("A task must be opened before other commands are handled.");
        }
    
        RequireId(open.TaskId.Value, nameof(open.TaskId));
        RequireText(open.Title, nameof(open.Title));
        RequireText(open.Goal, nameof(open.Goal));
    
        var operatorRole = new RoleAssignment(
            open.ActorId,
            RoleKind.Operator,
            Enum.GetValues<Capability>(),
            new Provenance(open.ActorId, now, "task.open"));
    
        var events = new List<LedgerEventData>
        {
            new TaskOpened(open.Title.Trim(), open.Goal.Trim(), OpeningTags(open.Tags)),
            new RoleAssigned(operatorRole)
        };
    
        var recalledLessons = open.RecalledLessons ?? [];
        EnsureUnique(recalledLessons.Select(lesson => lesson.Id).ToArray(), "Recalled lesson IDs");
        foreach (var lesson in recalledLessons.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            ValidateRecalledLesson(open.TaskId, lesson);
            events.Add(new LessonRecalled(lesson));
        }
    
        return events;
    }
    
    // Mirrors the normalisation the lesson tags already get: trimmed, refused when duplicated or
    // empty. An explicitly empty list collapses to null so that "untagged" has one representation
    // for recall to read, rather than two that behave the same.

    private static IReadOnlyList<string>? OpeningTags(IReadOnlyList<string>? tags)
    {
        if (tags is null)
        {
            return null;
        }
    
        var trimmed = tags.Select(tag => tag.Trim()).ToArray();
        EnsureUnique(trimmed, "Task tags", StringComparer.Ordinal);
        if (trimmed.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("A task cannot carry an empty tag.");
        }
    
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static void ValidateRecalledLesson(TaskId openedTaskId, Lesson lesson)
    {
        RequireId(lesson.Id.Value, nameof(lesson.Id));
        RequireId(lesson.SourceTaskId.Value, nameof(lesson.SourceTaskId));
        RequireDefined(lesson.SourceKind, nameof(lesson.SourceKind));
        RequireId(lesson.SourceRecordId, nameof(lesson.SourceRecordId));
        RequireText(lesson.Statement, nameof(lesson.Statement));
        RequireText(lesson.Outcome, nameof(lesson.Outcome));
        if (lesson.SourceTaskId == openedTaskId)
        {
            throw new GovernanceException("A task cannot recall a lesson from itself while it is being opened.");
        }
    }
}
