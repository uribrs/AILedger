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

        // Abandoning hands a directory area back for someone else to claim, which is the same
        // scope decision as handing it out in the first place.
        if (command is AbandonWorkItemCommand && assignment.Role != RoleKind.Operator)
        {
            throw new GovernanceException("Only an operator can abandon governed work.");
        }

        // An escalation exists to reach the operator, so only the operator closes one.
        if (command is ResolveEscalationCommand && assignment.Role != RoleKind.Operator)
        {
            throw new GovernanceException("Only an operator can resolve an escalation.");
        }

        if (command is AddConstraintCommand or SupersedeConstraintCommand && assignment.Role != RoleKind.Operator)
        {
            throw new GovernanceException("Only an operator can govern task constraints.");
        }

        if (command is MarkLessonBearingCommand &&
            assignment.Role is not (RoleKind.Operator or RoleKind.PlanningLead or RoleKind.ImplementationLead))
        {
            throw new GovernanceException("Only an operator or lead can mark a source lesson-bearing.");
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
        RaiseEscalationCommand => [Capability.RaiseEscalation],
        ResolveEscalationCommand => [Capability.ResolveEscalation],
        RecordAlternativeCommand => [Capability.RecordAlternative],
        MarkLessonBearingCommand => [],
        RecordArtifactCommand => [Capability.RecordArtifact],
        // The same capability the assembler already demands before it will build a manifest, so
        // recording the brief can refuse nothing that producing it did not refuse first.
        RecordContextBuiltCommand => [Capability.BuildContext],
        AddConstraintCommand or SupersedeConstraintCommand => [Capability.ManageConstraints],
        CompleteWorkItemCommand or BlockWorkItemCommand or UnblockWorkItemCommand
            or AbandonWorkItemCommand => [Capability.ManageWork],
        OpenTaskCommand => [],
        _ => throw new GovernanceException($"Unsupported command '{command.GetType().Name}'.")
    };
}
