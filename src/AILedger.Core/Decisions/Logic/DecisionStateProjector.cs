using AILedger.Core.Contracts;

namespace AILedger.Core.Decisions;

// Applies Decision lifecycle events to the aggregate projection. It does not decide whether an
// event is legal; command policy and historical-event admissibility stay at their own boundaries.
internal static class DecisionStateProjector
{
    internal static GovernedTaskState Add(GovernedTaskState state, DecisionProposed proposed) =>
        state with { Decisions = Set(state.Decisions, proposed.Decision.Id, proposed.Decision) };

    internal static GovernedTaskState Resolve(GovernedTaskState state, DecisionResolved resolved) =>
        SetStatus(state, resolved.DecisionId, resolved.Status);

    internal static GovernedTaskState Invalidate(GovernedTaskState state, DecisionInvalidated invalidated) =>
        SetStatus(state, invalidated.DecisionId, DecisionStatus.Invalidated);

    internal static GovernedTaskState Overturn(GovernedTaskState state, DecisionOverturned overturned) =>
        SetStatus(state, overturned.DecisionId, DecisionStatus.Invalidated);

    private static GovernedTaskState SetStatus(
        GovernedTaskState state,
        DecisionId decisionId,
        DecisionStatus status)
    {
        var decision = state.Decisions[decisionId] with { Status = status };
        return state with { Decisions = Set(state.Decisions, decisionId, decision) };
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
