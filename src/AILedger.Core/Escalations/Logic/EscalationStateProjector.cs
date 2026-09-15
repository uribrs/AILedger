using AILedger.Core.Contracts;

namespace AILedger.Core.Escalations;

// Applies Escalation events to aggregate state. Decisions stay in EscalationRules and historical
// admissibility stays in EscalationEventValidator; this class only changes the projection.
internal static class EscalationStateProjector
{
    internal static GovernedTaskState Add(GovernedTaskState state, EscalationRaised raised) =>
        state with { Escalations = Set(state.Escalations, raised.Escalation.Id, raised.Escalation) };

    internal static GovernedTaskState Resolve(GovernedTaskState state, EscalationResolved resolved)
    {
        var escalation = state.Escalations[resolved.EscalationId] with
        {
            Status = resolved.Status,
            Resolution = resolved.Resolution,
            ResolvedBy = resolved.ResolvedBy
        };

        return state with { Escalations = Set(state.Escalations, resolved.EscalationId, escalation) };
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
