using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.WorkItems;

namespace AILedger.Core.Stages;

internal static class StageSerialExecutionRules
{
    internal static void EnsureExplained(
        GovernedTaskState state,
        AlternativeId? serialJustification)
    {
        var workedItems = state.WorkItems.Values
            .Where(item => WorkItemVerificationRules.HasCompletedWorkingRun(state, item.Id))
            .ToArray();
        if (workedItems.Length < 2 || !ArePairwiseDisjoint(workedItems))
        {
            return;
        }

        var workingRuns = state.Runs.Values
            .Where(WorkItemVerificationRules.DidWorkUnderAWorkingRole)
            .Where(run => run.WorkItemId is not null)
            .ToArray();
        if (HasCrossItemOverlap(workingRuns) || serialJustification is not null)
        {
            return;
        }

        throw new GovernanceException(
            "Verification follows two or more disjoint work items whose working runs were entirely " +
            "serial. Record an alternative explaining why they were not dispatched concurrently, " +
            "then pass its ID as the serial justification.");
    }

    private static bool ArePairwiseDisjoint(IReadOnlyList<WorkItem> items)
    {
        for (var first = 0; first < items.Count; first++)
        {
            for (var second = first + 1; second < items.Count; second++)
            {
                if (!WorkItemScopeRules.AreDisjoint(items[first], items[second]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasCrossItemOverlap(IReadOnlyList<AgentRun> runs)
    {
        for (var first = 0; first < runs.Count; first++)
        {
            for (var second = first + 1; second < runs.Count; second++)
            {
                if (runs[first].WorkItemId == runs[second].WorkItemId)
                {
                    continue;
                }

                if (runs[first].StartedAt < runs[second].EndedAt &&
                    runs[second].StartedAt < runs[first].EndedAt)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
