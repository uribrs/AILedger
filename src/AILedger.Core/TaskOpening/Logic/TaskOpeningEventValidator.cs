using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Domain.ReplayValidationRules;

namespace AILedger.Core.TaskOpening;

// Replay validates only the opening event's persisted shape. Tags remain optional because their
// absence is the legal shape of every history written before task tagging existed.
internal static class TaskOpeningEventValidator
{
    internal static void ValidateOpened(TaskOpened opened)
    {
        RequireText(opened.Title, nameof(opened.Title));
        RequireText(opened.Goal, nameof(opened.Goal));
        ValidateOptionalTags(opened.Tags);
    }

    private static void ValidateOptionalTags(IReadOnlyList<string>? tags)
    {
        if (tags is null)
        {
            return;
        }

        EnsureUnique(tags, "task tags", StringComparer.Ordinal);
        if (tags.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("A task cannot carry an empty tag.");
        }
    }
}
