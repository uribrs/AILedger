using AILedger.Core.Contracts;

namespace AILedger.Core.Alternatives;

// Applies Alternative events to aggregate state. Decisions stay in AlternativeRules and historical
// admissibility stays in AlternativeEventValidator; this class only changes the projection.
internal static class AlternativeStateProjector
{
    internal static GovernedTaskState Add(GovernedTaskState state, AlternativeRecorded recorded) =>
        state with
        {
            Alternatives = Set(state.Alternatives, recorded.Alternative.Id, recorded.Alternative)
        };

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
