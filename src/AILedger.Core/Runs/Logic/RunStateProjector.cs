using AILedger.Core.Contracts;
using AILedger.Core.WorkItems;

namespace AILedger.Core.Runs;

// Applies Run events to aggregate state. Work Item activation and pause remain delegated to the
// Work Items concept; the completion field mapping remains shared with the memory projection.
internal static class RunStateProjector
{
    internal static GovernedTaskState Start(
        GovernedTaskState state,
        RunStarted started,
        IReadOnlyList<ContextBriefWaiver> contextBriefWaivers)
    {
        var projected = WorkItemStateProjector.ActivateForRun(state, started.Run);

        return projected with
        {
            Runs = Set(state.Runs, started.Run.Id, started.Run),
            ContextBriefWaivers = contextBriefWaivers
        };
    }

    internal static GovernedTaskState Complete(GovernedTaskState state, RunCompleted completed)
    {
        var run = RunCompletionProjection.Apply(state.Runs[completed.RunId], completed);
        var projected = WorkItemStateProjector.PauseAfterRun(state, run);

        return projected with
        {
            Runs = Set(state.Runs, completed.RunId, run)
        };
    }

    private static IReadOnlyDictionary<TKey, TValue> Set<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> source,
        TKey key,
        TValue value)
        where TKey : notnull
    {
        var copy = new Dictionary<TKey, TValue>(source)
        {
            [key] = value
        };
        return copy;
    }
}
