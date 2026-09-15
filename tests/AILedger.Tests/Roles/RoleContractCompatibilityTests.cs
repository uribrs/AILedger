using System.Reflection;
using System.Text.Json.Serialization;
using AILedger.Core.Contracts;

namespace AILedger.Tests.Roles;

public sealed class RoleContractCompatibilityTests
{
    // Roles/task-opening plan attention R1: concept ownership may become explicit; the public and
    // persisted identities stay fixed.
    [Fact]
    public void PublicRoleContractsKeepNamespacesShapesOrdinalsAndDiscriminators()
    {
        AssertPublicNamespace(
            typeof(RoleKind), typeof(Capability), typeof(RoleAssignment),
            typeof(AssignRoleCommand), typeof(RoleAssigned));

        Assert.Equal(
            new (string Name, int Ordinal)[]
            {
                (nameof(RoleKind.Operator), 0),
                (nameof(RoleKind.PlanningLead), 1),
                (nameof(RoleKind.ImplementationLead), 2),
                (nameof(RoleKind.Researcher), 3),
                (nameof(RoleKind.Worker), 4),
                (nameof(RoleKind.Verifier), 5),
                (nameof(RoleKind.CodeReviewer), 6)
            },
            Enum.GetValues<RoleKind>().Select(value => (value.ToString(), (int)value)));
        Assert.Equal(
            new (string Name, int Ordinal)[]
            {
                (nameof(Capability.ManageRoles), 0),
                (nameof(Capability.ManageScope), 1),
                (nameof(Capability.AddClaim), 2),
                (nameof(Capability.ResolveClaim), 3),
                (nameof(Capability.AddEvidence), 4),
                (nameof(Capability.ProposeDecision), 5),
                (nameof(Capability.ResolveDecision), 6),
                (nameof(Capability.RaiseChallenge), 7),
                (nameof(Capability.DisposeChallenge), 8),
                (nameof(Capability.ManageWork), 9),
                (nameof(Capability.ManageRuns), 10),
                (nameof(Capability.RequestTransition), 11),
                (nameof(Capability.BuildContext), 12),
                (nameof(Capability.RaiseEscalation), 13),
                (nameof(Capability.ResolveEscalation), 14),
                (nameof(Capability.RecordAlternative), 15),
                (nameof(Capability.ManageConstraints), 16),
                (nameof(Capability.RecordArtifact), 17)
            },
            Enum.GetValues<Capability>().Select(value => (value.ToString(), (int)value)));
        AssertConstructor<RoleAssignment>(
            typeof(ActorId), typeof(RoleKind), typeof(IReadOnlyList<Capability>), typeof(Provenance));
        AssertConstructor<AssignRoleCommand>(
            typeof(ActorId), typeof(EventId?), typeof(string), typeof(ActorId),
            typeof(RoleKind), typeof(IReadOnlyList<Capability>));
        AssertConstructor<RoleAssigned>(typeof(RoleAssignment));

        AssertDiscriminator<LedgerCommand, AssignRoleCommand>("actor.assign-role");
        AssertDiscriminator<LedgerEventData, RoleAssigned>("actor.role-assigned");
    }

    private static void AssertPublicNamespace(params Type[] types) =>
        Assert.All(types, type => Assert.Equal("AILedger.Core.Contracts", type.Namespace));

    private static void AssertConstructor<T>(params Type[] parameterTypes) =>
        Assert.NotNull(typeof(T).GetConstructor(parameterTypes));

    private static void AssertDiscriminator<TBase, TDerived>(string expected)
    {
        var attribute = typeof(TBase).GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Single(candidate => candidate.DerivedType == typeof(TDerived));

        Assert.Equal(expected, attribute.TypeDiscriminator);
    }
}
