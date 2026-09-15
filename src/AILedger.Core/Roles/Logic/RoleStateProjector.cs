using AILedger.Core.Contracts;

namespace AILedger.Core.Roles;

internal static class RoleStateProjector
{
    internal static GovernedTaskState Assign(
        GovernedTaskState state,
        RoleAssignment assignment) =>
        state with
        {
            Roles = Set(state.Roles, assignment.ActorId, assignment),
            PendingOpeningActor = null
        };

    private static IReadOnlyDictionary<ActorId, RoleAssignment> Set(
        IReadOnlyDictionary<ActorId, RoleAssignment> source,
        ActorId key,
        RoleAssignment value)
    {
        var copy = new Dictionary<ActorId, RoleAssignment>(source)
        {
            [key] = value
        };
        return copy;
    }
}
