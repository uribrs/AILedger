using AILedger.Core.Contracts;

namespace AILedger.Core.WorkItems;

// Applies Work Item lifecycle events and run-driven status changes to the aggregate projection.
// Command decisions stay in WorkItemLifecycleRules; historical admissibility stays in
// WorkItemEventValidator.
internal static class WorkItemStateProjector
{
    internal static GovernedTaskState Add(
        GovernedTaskState state,
        WorkItemAdded added,
        IReadOnlyList<ContextBriefWaiver> contextBriefWaivers) =>
        state with
        {
            WorkItems = Set(state.WorkItems, added.WorkItem.Id, added.WorkItem),
            ContextBriefWaivers = contextBriefWaivers
        };

    internal static GovernedTaskState Invalidate(
        GovernedTaskState state,
        WorkItemInvalidated invalidated) =>
        UpdateStatus(state, invalidated.WorkItemId, invalidated.Status);

    internal static GovernedTaskState Complete(
        GovernedTaskState state,
        WorkItemCompleted completed) =>
        SetStatus(state, completed.WorkItemId, WorkItemStatus.Completed, blockReason: null);

    internal static GovernedTaskState Block(
        GovernedTaskState state,
        WorkItemBlocked blocked) =>
        SetStatus(state, blocked.WorkItemId, WorkItemStatus.Blocked, blocked.Reason);

    internal static GovernedTaskState Unblock(
        GovernedTaskState state,
        WorkItemUnblocked unblocked) =>
        SetStatus(state, unblocked.WorkItemId, WorkItemStatus.Paused, blockReason: null);

    internal static GovernedTaskState Abandon(
        GovernedTaskState state,
        WorkItemAbandoned abandoned)
    {
        var workItem = state.WorkItems[abandoned.WorkItemId] with
        {
            Status = WorkItemStatus.Abandoned,
            AbandonReason = abandoned.Reason
        };
        return state with { WorkItems = Set(state.WorkItems, abandoned.WorkItemId, workItem) };
    }

    internal static GovernedTaskState ActivateForRun(GovernedTaskState state, AgentRun run)
    {
        foreach (var member in WorkCoverage.Effective(run.WorkItemId, run.Assurance))
            state = UpdateStatus(state, member, WorkItemStatus.Active);
        return state;
    }

    internal static GovernedTaskState PauseAfterRun(GovernedTaskState state, AgentRun run)
    {
        foreach (var member in WorkCoverage.Effective(run.WorkItemId, run.Assurance))
        {
            var status = state.WorkItems[member].Status;
            if (run.Assurance is null
                ? status is not (WorkItemStatus.Blocked or WorkItemStatus.Stale or WorkItemStatus.Abandoned)
                : status == WorkItemStatus.Active)
                state = UpdateStatus(state, member, WorkItemStatus.Paused);
        }
        return state;
    }

    private static GovernedTaskState UpdateStatus(
        GovernedTaskState state,
        WorkItemId workItemId,
        WorkItemStatus status)
    {
        var workItem = state.WorkItems[workItemId] with { Status = status };
        return state with { WorkItems = Set(state.WorkItems, workItemId, workItem) };
    }

    private static GovernedTaskState SetStatus(
        GovernedTaskState state,
        WorkItemId workItemId,
        WorkItemStatus status,
        string? blockReason)
    {
        var workItem = state.WorkItems[workItemId] with { Status = status, BlockReason = blockReason };
        return state with { WorkItems = Set(state.WorkItems, workItemId, workItem) };
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
