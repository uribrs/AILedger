using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Roles;

// Replay counterpart: RoleEventValidator.
internal static class RoleAssignmentRules
{
    internal static void ValidateEnums(AssignRoleCommand command)
    {
        RequireDefined(command.Role, nameof(command.Role));
        foreach (var capability in command.Capabilities)
        {
            RequireDefined(capability, nameof(command.Capabilities));
        }
    }

    internal static void EnsureAuthorized(RoleAssignment assignment)
    {
        if (assignment.Role != RoleKind.Operator)
        {
            throw new GovernanceException("Only an operator can assign roles or capabilities.");
        }
    }

    internal static IReadOnlyList<LedgerEventData> AssignRole(
        GovernedTaskState state,
        AssignRoleCommand command,
        DateTimeOffset now)
    {
        RequireId(command.TargetActorId.Value, nameof(command.TargetActorId));
        if (command.TargetActorId == command.ActorId)
        {
            throw new GovernanceException("Actors cannot assign or expand their own authority.");
        }
    
        var capabilities = DistinctCapabilities(command.Capabilities);
        if (command.Role != RoleKind.Operator &&
            capabilities.Any(capability => capability is Capability.ManageRoles or Capability.ManageScope))
        {
            throw new GovernanceException("Only an operator role can receive role-management or scope-management authority.");
        }
        var assignment = new RoleAssignment(
            command.TargetActorId,
            command.Role,
            capabilities,
            new Provenance(command.ActorId, now, "actor.assign-role"));
    
        return [new RoleAssigned(assignment)];
    }

    internal static bool IsOperator(GovernedTaskState state, ActorId actorId) =>
        state.Roles.TryGetValue(actorId, out var assignment) && assignment.Role == RoleKind.Operator;

    private static IReadOnlyList<Capability> DistinctCapabilities(IReadOnlyList<Capability> capabilities)
    {
        EnsureUnique(capabilities, "Capabilities");
        return capabilities.OrderBy(value => value).ToArray();
    }
}
