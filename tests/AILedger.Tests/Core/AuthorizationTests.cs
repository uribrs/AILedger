using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class AuthorizationTests
{
    [Fact]
    public void R2_NonOperatorCannotAssignRoleOrCapabilities()
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
    public void ScopeMutationRequiresOperatorRoleEvenIfStateWasExternallyConstructedWithCapability()
    {
        var task = new TestTask();
        var worker = new ActorId("worker");
        var roles = new Dictionary<ActorId, RoleAssignment>(task.State.Roles)
        {
            [worker] = new RoleAssignment(
                worker, RoleKind.Worker, [Capability.ManageWork, Capability.ManageScope],
                new Provenance(task.OperatorId, DateTimeOffset.UnixEpoch, "test"))
        };
        var state = task.State with { Roles = roles };

        var exception = Assert.Throws<GovernanceException>(() => new CommandHandler().Handle(
            state,
            new AddWorkItemCommand(worker, null, "scope", new WorkItemId("W1"), "Work", worker, [], [Path.GetFullPath("src")]),
            DateTimeOffset.UnixEpoch));

        Assert.Contains("Only an operator", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreRejectsRelativeResourceScope()
    {
        var task = new TestTask();

        var exception = Assert.Throws<GovernanceException>(() => task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work",
            task.OperatorId, [], ["relative-scope"])));

        Assert.Contains("absolute paths", exception.Message, StringComparison.Ordinal);
        Assert.Empty(task.State.WorkItems);
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

    [Fact]
    public void CommandRequiresExplicitCapability()
    {
        var task = new TestTask();
        var actor = new ActorId("researcher");
        task.Assign(actor, RoleKind.Researcher, Capability.AddEvidence);

        var command = new AddClaimCommand(
            actor,
            null,
            task.NextCorrelation(),
            new ClaimId("C1"),
            "A claim",
            null);

        var exception = Assert.Throws<GovernanceException>(() => task.Apply(command));

        Assert.Contains(nameof(Capability.AddClaim), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonOwnerCannotStartOwnedWork()
    {
        var task = new TestTask();
        var owner = new ActorId("owner");
        var otherWorker = new ActorId("other-worker");
        task.Assign(owner, RoleKind.Worker, Capability.ManageRuns);
        task.Assign(otherWorker, RoleKind.Worker, Capability.ManageRuns);
        var workItemId = new WorkItemId("W1");
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId, "Owned work",
            owner, [], [Path.GetFullPath("src")]));

        var exception = Assert.Throws<GovernanceException>(() => task.Apply(new StartRunCommand(
            otherWorker, null, task.NextCorrelation(), new RunId("R1"), workItemId, "codex", null)));

        Assert.Contains("Only work owner", exception.Message, StringComparison.Ordinal);
        Assert.Empty(task.State.Runs);
        Assert.Equal(WorkItemStatus.Proposed, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void ActorWithManageRunsCanStartUnownedWork()
    {
        var task = new TestTask();
        var worker = new ActorId("worker");
        task.Assign(worker, RoleKind.Worker, Capability.ManageRuns);
        var workItemId = new WorkItemId("W1");
        var runId = new RunId("R1");
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId, "Unowned work",
            null, [], [Path.GetFullPath("src")]));

        task.Apply(new StartRunCommand(
            worker, null, task.NextCorrelation(), runId, workItemId, "codex", null));

        Assert.Equal(worker, task.State.Runs[runId].ActorId);
        Assert.Equal(WorkItemStatus.Active, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void NonRunActorCannotCompleteRunButOperatorCan()
    {
        var task = new TestTask();
        var runActor = new ActorId("run-actor");
        var otherWorker = new ActorId("other-worker");
        task.Assign(runActor, RoleKind.Worker, Capability.ManageRuns);
        task.Assign(otherWorker, RoleKind.Worker, Capability.ManageRuns);
        var runId = new RunId("R1");
        task.Apply(new StartRunCommand(
            runActor, null, task.NextCorrelation(), runId, null, "codex", "session-1"));

        var exception = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            otherWorker, null, task.NextCorrelation(), runId, AgentRunStatus.Completed, "session-1")));

        Assert.Contains("Only run actor", exception.Message, StringComparison.Ordinal);
        Assert.Equal(AgentRunStatus.Active, task.State.Runs[runId].Status);

        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), runId, AgentRunStatus.Completed, "session-1"));

        Assert.Equal(AgentRunStatus.Completed, task.State.Runs[runId].Status);
    }

    [Fact]
    public void OperatorCanStartWorkOwnedByAnotherActor()
    {
        var task = new TestTask();
        var owner = new ActorId("owner");
        task.Assign(owner, RoleKind.Worker, Capability.ManageRuns);
        var workItemId = new WorkItemId("W1");
        var runId = new RunId("R1");
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId, "Owned work",
            owner, [], [Path.GetFullPath("src")]));

        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), runId, workItemId, "codex", null));

        Assert.Equal(task.OperatorId, task.State.Runs[runId].ActorId);
        Assert.Equal(WorkItemStatus.Active, task.State.WorkItems[workItemId].Status);
    }
}
