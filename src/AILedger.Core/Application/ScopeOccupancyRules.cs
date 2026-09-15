using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Command-time scope policy paired with the deliberately permissive ValidateWorkItemAdded replay boundary.
//
// A scope may name a file or a directory, which is accepted decision D1. Nothing here has to branch
// on which: the predicate below compares paths as text, so a file inside a held directory overlaps
// it, a directory containing a held file overlaps it, and two files in one directory do not.
//
// Do not make it ask the filesystem. Occupancy itself is command-time only, but the rule about what
// shape a scope may take is written twice, and the replay copy in ValidateWorkItemAdded re-reads
// events recorded long ago, whose paths may since have moved. It can only test the text of a path,
// so a command-time copy that tested the disk as well would refuse work the log still accepts.
//
// The check that still holds a scope to a directory is not in the kernel at all — it is
// CliApplication.ResolveExistingDirectory, which the operator surface applies to every --scope. D1
// is unlanded until that one is lifted.
internal static class ScopeOccupancyRules
{
    internal static void EnsureScopeIsNotAlreadyOccupied(
        GovernedTaskState state,
        WorkItemId workItemId,
        IReadOnlyList<string> requestedScope)
    {
        foreach (var existing in state.WorkItems.Values
                     .Where(item => item.Id != workItemId)
                     // Which statuses release an area is this rule written twice: the second copy is
                     // the `occupied` query in CliApplication.WriteWhoAsync. They must change
                     // together. Apart, `who` shows an operator an area as taken that `work add`
                     // hands to someone else in the next command, or the reverse.
                     .Where(item => item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Stale
                         or WorkItemStatus.Abandoned))
                     .OrderBy(item => item.Id.Value, StringComparer.Ordinal))
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

    // The same question the occupancy check asks, put to two work items that both already exist: do
    // these two hold areas that do not touch? It belongs here and not at its caller because a second
    // copy of PathsOverlap would be a second definition of what overlap means, and the two copies
    // would drift the way the status filter above is warned about.
    //
    // It compares text, exactly as the occupancy check does, and it must. Work items recorded before
    // AddWorkItem demanded absolute paths store relative ones — ledger-selfhost holds
    // 'src/AILedger.Cli' — so nothing here may normalise or root a path. Path.IsPathFullyQualified is
    // false for those, and any rooting built on it would compare a stored relative scope against an
    // absolute one and answer wrongly.
    //
    // It does not trim either side, and that is what keeps it identical to the occupancy check rather
    // than merely similar to it. That check trims one side only — the scope it has been handed
    // straight off a command — because the other side is already stored, and AddWorkItem trims every
    // entry before recording it. Both sides here are stored, so there is nothing left to trim, and
    // trimming anyway would make this the stricter of the two on a scope with surrounding whitespace.
    //
    // An item holding no areas is disjoint from every item, including itself: it takes nothing, so it
    // touches nothing.
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
    
        var prefix = container.EndsWith(Path.DirectorySeparatorChar) ? container : container + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.Ordinal);
    }
}
