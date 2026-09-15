using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Tests.Roles;

public sealed class RoleReplayBoundaryTests
{
    // Roles/task-opening plan attention R2: replay must accept the capability set emitted before
    // RecordArtifact existed.
    [Fact]
    public void ReplayAcceptsTheLegacyOpeningCapabilityBaseline()
    {
        var taskId = new TaskId("T1");
        var operatorId = new ActorId("operator");
        var recordedAt = DateTimeOffset.UnixEpoch;
        var reducer = new TaskReducer();
        var state = reducer.Apply(null, Event(taskId, new TaskOpened("Task", "Goal"), operatorId, recordedAt));
        var legacyCapabilities = Enum.GetValues<Capability>()
            .Where(capability => capability != Capability.RecordArtifact)
            .ToArray();

        state = reducer.Apply(state, Event(taskId, new RoleAssigned(new RoleAssignment(
            operatorId,
            RoleKind.Operator,
            legacyCapabilities,
            new Provenance(operatorId, recordedAt, "task.open"))), operatorId, recordedAt));

        Assert.Equal(legacyCapabilities, state.Roles[operatorId].Capabilities);
    }

    [Fact]
    public void ReplayStillRefusesAnOpeningAssignmentBelowTheLegacyBaseline()
    {
        var taskId = new TaskId("T1");
        var operatorId = new ActorId("operator");
        var recordedAt = DateTimeOffset.UnixEpoch;
        var reducer = new TaskReducer();
        var state = reducer.Apply(null, Event(taskId, new TaskOpened("Task", "Goal"), operatorId, recordedAt));
        var narrowedCapabilities = Enum.GetValues<Capability>()
            .Where(capability => capability is not (Capability.RecordArtifact or Capability.ManageWork))
            .ToArray();

        var forged = Event(taskId, new RoleAssigned(new RoleAssignment(
            operatorId,
            RoleKind.Operator,
            narrowedCapabilities,
            new Provenance(operatorId, recordedAt, "task.open"))), operatorId, recordedAt);

        var exception = Assert.Throws<GovernanceException>(() => reducer.Apply(state, forged));

        Assert.Contains("legacy capability baseline", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayRejectsForgedOpeningRoleActor()
    {
        var taskId = new TaskId("T1");
        var operatorId = new ActorId("operator");
        var recordedAt = DateTimeOffset.UnixEpoch;
        var reducer = new TaskReducer();
        var state = reducer.Apply(null, Event(taskId, new TaskOpened("Task", "Goal"), operatorId, recordedAt));
        var forgedRole = new RoleAssignment(
            new ActorId("attacker"),
            RoleKind.Operator,
            Enum.GetValues<Capability>(),
            new Provenance(operatorId, recordedAt, "task.open"));

        var exception = Assert.Throws<GovernanceException>(() =>
            reducer.Apply(state, Event(taskId, new RoleAssigned(forgedRole), operatorId, recordedAt)));

        Assert.Contains("opening role", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReplayRejectsForgedOpeningRoleProvenance()
    {
        var taskId = new TaskId("T1");
        var operatorId = new ActorId("operator");
        var recordedAt = DateTimeOffset.UnixEpoch;
        var reducer = new TaskReducer();
        var state = reducer.Apply(null, Event(taskId, new TaskOpened("Task", "Goal"), operatorId, recordedAt));
        var forgedRole = new RoleAssignment(
            operatorId,
            RoleKind.Operator,
            Enum.GetValues<Capability>(),
            new Provenance(new ActorId("attacker"), recordedAt, "task.open"));

        var exception = Assert.Throws<GovernanceException>(() =>
            reducer.Apply(state, Event(taskId, new RoleAssigned(forgedRole), operatorId, recordedAt)));

        Assert.Contains("provenance", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReplayRejectsDuplicateCapabilities()
    {
        var taskId = new TaskId("T1");
        var operatorId = new ActorId("operator");
        var recordedAt = DateTimeOffset.UnixEpoch;
        var reducer = new TaskReducer();
        var state = Open(reducer, taskId, operatorId, recordedAt);
        var duplicateCapabilities = new RoleAssignment(
            new ActorId("worker"),
            RoleKind.Worker,
            [Capability.AddClaim, Capability.AddClaim],
            new Provenance(operatorId, recordedAt, "actor.assign-role"));

        var exception = Assert.Throws<GovernanceException>(() => reducer.Apply(
            state,
            Event(taskId, new RoleAssigned(duplicateCapabilities), operatorId, recordedAt)));

        Assert.Equal("Role capabilities cannot contain duplicates.", exception.Message);
    }

    // Roles/task-opening plan attention R5: replay must reject a direct authority-changing event
    // from a worker.
    [Fact]
    public void ReplayRejectsAuthorityChangingEventFromWorker()
    {
        var taskId = new TaskId("T1");
        var operatorId = new ActorId("operator");
        var workerId = new ActorId("worker");
        var recordedAt = DateTimeOffset.UnixEpoch;
        var reducer = new TaskReducer();
        var state = Open(reducer, taskId, operatorId, recordedAt);
        state = reducer.Apply(state, Event(taskId, new RoleAssigned(new RoleAssignment(
            workerId,
            RoleKind.Worker,
            [Capability.AddClaim],
            new Provenance(operatorId, recordedAt, "actor.assign-role"))), operatorId, recordedAt));
        var forgedRole = new RoleAssignment(
            new ActorId("accomplice"),
            RoleKind.Worker,
            [],
            new Provenance(workerId, recordedAt, "actor.assign-role"));

        var exception = Assert.Throws<GovernanceException>(() =>
            reducer.Apply(state, Event(taskId, new RoleAssigned(forgedRole), workerId, recordedAt)));

        Assert.Contains("operator", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static GovernedTaskState Open(
        TaskReducer reducer,
        TaskId taskId,
        ActorId actorId,
        DateTimeOffset recordedAt)
    {
        var state = reducer.Apply(null, Event(taskId, new TaskOpened("Task", "Goal"), actorId, recordedAt));
        return reducer.Apply(state, Event(taskId, new RoleAssigned(new RoleAssignment(
            actorId,
            RoleKind.Operator,
            Enum.GetValues<Capability>(),
            new Provenance(actorId, recordedAt, "task.open"))), actorId, recordedAt));
    }

    private static LedgerEvent Event(
        TaskId taskId,
        LedgerEventData data,
        ActorId actorId,
        DateTimeOffset recordedAt) => new(
        GovernedTaskState.CurrentSchemaVersion,
        new EventId(Guid.NewGuid().ToString("N")),
        taskId,
        actorId,
        recordedAt,
        null,
        "test",
        data);
}
