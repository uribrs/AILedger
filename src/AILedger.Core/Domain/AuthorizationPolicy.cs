using AILedger.Core.Contracts;

namespace AILedger.Core.Domain;

public sealed class AuthorizationPolicy
{
    public RoleAssignment Authorize(GovernedTaskState state, LedgerCommand command)
    {
        if (!state.Roles.TryGetValue(command.ActorId, out var assignment))
        {
            throw new GovernanceException($"Actor '{command.ActorId}' has no assigned role.");
        }

        if (command is AssignRoleCommand && assignment.Role != RoleKind.Operator)
        {
            throw new GovernanceException("Only an operator can assign roles or capabilities.");
        }


        if (command is AddWorkItemCommand && assignment.Role != RoleKind.Operator)
        {
            throw new GovernanceException("Only an operator can define governed resource scope.");
        }

        foreach (var capability in RequiredCapabilities(command))
        {
            if (!assignment.Capabilities.Contains(capability))
            {
                throw new GovernanceException($"Actor '{command.ActorId}' lacks capability '{capability}'.");
            }
        }

        return assignment;
    }

    private static IReadOnlyList<Capability> RequiredCapabilities(LedgerCommand command) => command switch
    {
        AssignRoleCommand => [Capability.ManageRoles],
        AddClaimCommand => [Capability.AddClaim],
        ResolveClaimCommand => [Capability.ResolveClaim],
        AddEvidenceCommand => [Capability.AddEvidence],
        ProposeDecisionCommand => [Capability.ProposeDecision],
        ResolveDecisionCommand => [Capability.ResolveDecision],
        RaiseChallengeCommand => [Capability.RaiseChallenge],
        DisposeChallengeCommand => [Capability.DisposeChallenge],
        AddWorkItemCommand => [Capability.ManageWork, Capability.ManageScope],
        StartRunCommand or CompleteRunCommand => [Capability.ManageRuns],
        RequestStageTransitionCommand => [Capability.RequestTransition],
        OpenTaskCommand => [],
        _ => throw new GovernanceException($"Unsupported command '{command.GetType().Name}'.")
    };
}
