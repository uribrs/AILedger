using AILedger.Core.Contracts;

namespace AILedger.Core.Constraints;

// Applies Constraint events to aggregate state. Decisions stay in ConstraintRules and historical
// admissibility stays in ConstraintEventValidator; this class only changes the projection.
internal static class ConstraintStateProjector
{
    internal static GovernedTaskState Add(GovernedTaskState state, ConstraintAdded added) =>
        state with { Constraints = Set(state.Constraints, added.Constraint.Id, added.Constraint) };

    internal static GovernedTaskState Supersede(
        GovernedTaskState state,
        ConstraintSuperseded superseded)
    {
        var constraint = state.Constraints[superseded.ConstraintId] with
        {
            Status = ConstraintStatus.Superseded
        };

        return state with { Constraints = Set(state.Constraints, superseded.ConstraintId, constraint) };
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
