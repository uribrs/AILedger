using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Core.WorkItems;

// Command-time scope policy paired with the deliberately permissive WorkItemEventValidator.ValidateAdded
// replay boundary. A scope may name a file or directory; overlap is derived from path text so replay
// never depends on today's filesystem.
internal static class WorkItemScopeRules
{
    internal static void EnsureAvailable(
        GovernedTaskState state,
        WorkItemId workItemId,
        IReadOnlyList<string> requestedScope)
    {
        foreach (var existing in OccupiedItems(state, workItemId))
        {
            foreach (var held in existing.ResourceScope)
            {
                var clash = requestedScope.FirstOrDefault(requested => PathsOverlap(requested.Trim(), held));
                if (clash is not null)
                {
                    throw new GovernanceException(
                        $"Scope '{clash}' overlaps '{held}', already held by work item '{existing.Id}' in status " +
                        $"'{existing.Status}'. Complete or supersede that item, or narrow this scope.");
                }
            }
        }
    }

    private static IEnumerable<WorkItem> OccupiedItems(GovernedTaskState state, WorkItemId workItemId) =>
        state.WorkItems.Values
            .Where(item => item.Id != workItemId)
            // This status set is mirrored by the `occupied` query in CliApplication.WriteWhoAsync.
            .Where(item => item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Stale
                or WorkItemStatus.Abandoned))
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal);

    // The same overlap policy is used when stage rules decide whether two recorded items were
    // independently dispatchable. Stored paths are already normalized by creation and older relative
    // paths must remain comparable as text, so this deliberately does not consult the filesystem.
    internal static bool AreDisjoint(WorkItem first, WorkItem second) =>
        !first.ResourceScope.Any(held =>
            second.ResourceScope.Any(other => PathsOverlap(held, other)));

    private static bool PathsOverlap(string first, string second) =>
        IsSameOrInside(first, second) || IsSameOrInside(second, first);

    private static bool IsSameOrInside(string candidate, string container)
    {
        if (string.Equals(candidate, container, StringComparison.Ordinal))
        {
            return true;
        }

        var prefix = container.EndsWith(Path.DirectorySeparatorChar)
            ? container
            : container + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.Ordinal);
    }
}
