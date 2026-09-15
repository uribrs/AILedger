using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Roles;

public sealed class RoleAssignmentTests
{
    [Fact]
    public void NonOperatorCannotAssignRoleOrCapabilities()
    {
        var task = new TestTask();
        var planningLead = new ActorId("planning-lead");
        task.Assign(planningLead, RoleKind.PlanningLead, Capability.AddClaim);

        var command = new AssignRoleCommand(
            planningLead,
            null,
            task.NextCorrelation(),
            new ActorId("worker"),
            RoleKind.Worker,
            [Capability.ManageRuns]);

        var exception = Assert.Throws<GovernanceException>(() => task.Apply(command));

        Assert.Contains("Only an operator", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(new ActorId("worker"), task.State.Roles.Keys);
    }

    [Theory]
    [InlineData(Capability.ManageRoles)]
    [InlineData(Capability.ManageScope)]
    public void OperatorCannotGrantPrivilegedCapabilityToNonOperatorRole(Capability capability)
    {
        var task = new TestTask();

        var exception = Assert.Throws<GovernanceException>(() => task.Assign(
            new ActorId("worker"), RoleKind.Worker, Capability.AddClaim, capability));

        Assert.Contains("Only an operator role", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ActorCannotExpandItsOwnAuthorityEvenWhenItIsOperator()
    {
        var task = new TestTask();
        var command = new AssignRoleCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            task.OperatorId,
            RoleKind.Operator,
            Enum.GetValues<Capability>());

        var exception = Assert.Throws<GovernanceException>(() => task.Apply(command));

        Assert.Contains("own authority", exception.Message, StringComparison.Ordinal);
    }
}
