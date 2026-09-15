using AILedger.Core.Contracts;

namespace AILedger.Core.CoordinatorSessions;

// Applies the two bracket events to aggregate state and owns no command or replay policy.
internal static class CoordinatorSessionStateProjector
{
    internal static GovernedTaskState Start(GovernedTaskState state, SessionStarted started) =>
        state with
        {
            CoordinatorSessions = Set(
                state.CoordinatorSessions,
                started.Session.Id,
                started.Session)
        };

    internal static GovernedTaskState Complete(GovernedTaskState state, SessionCompleted completed)
    {
        var session = state.CoordinatorSessions[completed.SessionId] with
        {
            EndedAt = completed.EndedAt
        };

        return state with
        {
            CoordinatorSessions = Set(state.CoordinatorSessions, completed.SessionId, session)
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
