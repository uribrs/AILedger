using AILedger.Core.Contracts;

namespace AILedger.Cli;

internal static class RoleDefaults
{
    public static IReadOnlyList<Capability> For(RoleKind role) => role switch
    {
        RoleKind.Operator => Enum.GetValues<Capability>(),
        RoleKind.PlanningLead =>
        [
            Capability.AddClaim, Capability.ResolveClaim, Capability.AddEvidence,
            Capability.ProposeDecision, Capability.RaiseChallenge, Capability.ManageRuns,
            Capability.RequestTransition, Capability.BuildContext,
            Capability.RaiseEscalation, Capability.RecordAlternative, Capability.RecordArtifact
        ],
        RoleKind.ImplementationLead =>
        [
            Capability.AddClaim, Capability.AddEvidence, Capability.ProposeDecision,
            Capability.RaiseChallenge, Capability.ManageRuns, Capability.RequestTransition,
            Capability.BuildContext, Capability.RaiseEscalation, Capability.RecordAlternative,
            Capability.RecordArtifact
        ],
        RoleKind.Verifier or RoleKind.CodeReviewer =>
        [Capability.AddClaim, Capability.AddEvidence, Capability.RaiseChallenge, Capability.BuildContext,
         Capability.RaiseEscalation, Capability.RecordArtifact],
        RoleKind.Researcher or RoleKind.Worker =>
        [Capability.AddClaim, Capability.AddEvidence, Capability.RaiseChallenge, Capability.BuildContext,
         Capability.RaiseEscalation],
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role.")
    };

    public static void EnsureSafe(RoleKind role, IReadOnlyList<Capability> capabilities)
    {
        if (role != RoleKind.Operator &&
            (capabilities.Contains(Capability.ManageRoles) ||
             capabilities.Contains(Capability.ManageScope) ||
             capabilities.Contains(Capability.ResolveEscalation) ||
             capabilities.Contains(Capability.ManageConstraints)))
        {
            throw new CliUsageException(
                "Non-operator roles cannot receive ManageRoles, ManageScope, ResolveEscalation or ManageConstraints.");
        }
    }
}
