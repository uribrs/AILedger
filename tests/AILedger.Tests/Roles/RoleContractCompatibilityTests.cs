using System.Reflection;
using System.Text.Json.Serialization;
using AILedger.Core.Contracts;

namespace AILedger.Tests.Roles;

public sealed class RoleContractCompatibilityTests
{
    // R1: concept ownership may become explicit; the public and persisted identities stay fixed.
    [Fact]
    public void PublicRoleContractsKeepNamespacesShapesOrdinalsAndDiscriminators()
    {
        AssertPublicNamespace(
            typeof(RoleKind), typeof(Capability), typeof(RoleAssignment),
            typeof(AssignRoleCommand), typeof(RoleAssigned));

        Assert.Equal(Enumerable.Range(0, 7), Enum.GetValues<RoleKind>().Select(value => (int)value));
        Assert.Equal(Enumerable.Range(0, 18), Enum.GetValues<Capability>().Select(value => (int)value));
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
