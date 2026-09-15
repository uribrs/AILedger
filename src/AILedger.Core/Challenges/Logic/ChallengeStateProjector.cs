using AILedger.Core.Contracts;

namespace AILedger.Core.Challenges;

internal static class ChallengeStateProjector
{
    internal static GovernedTaskState Add(GovernedTaskState state, ChallengeRaised raised) =>
        state with
        {
            Challenges = Set(state.Challenges, raised.Challenge.Id, raised.Challenge)
        };

    internal static GovernedTaskState Dispose(GovernedTaskState state, ChallengeDisposed disposed)
    {
        var challenge = state.Challenges[disposed.ChallengeId] with { Status = disposed.Status };
        return state with
        {
            Challenges = Set(state.Challenges, disposed.ChallengeId, challenge)
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
