using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Tests.Core;

public sealed class ReducerTests
{
    [Fact]
    public void ReplayRejectsNonOpeningFirstEvent()
    {
        var @event = Event(new TaskId("T1"), new RoleAssigned(new RoleAssignment(
            new ActorId("actor"), RoleKind.Worker, [], new Provenance(new ActorId("operator"), DateTimeOffset.UnixEpoch, null))));

        Assert.Throws<GovernanceException>(() => new TaskReducer().Apply(null, @event));
    }

    [Fact]
    public void ReplayRejectsEventForDifferentTask()
    {
        var reducer = new TaskReducer();
        var state = reducer.Apply(null, Event(new TaskId("T1"), new TaskOpened("Task", "Goal")));

        Assert.Throws<GovernanceException>(() =>
            reducer.Apply(state, Event(new TaskId("T2"), new ClaimAdded(new Claim(
                new ClaimId("C1"), "Claim", ClaimStatus.Open, [], null,
                new Provenance(new ActorId("actor"), DateTimeOffset.UnixEpoch, null))))));
    }

    [Fact]
    public void ReplayAdvancesVersionExactlyOncePerEvent()
    {
        var actor = new ActorId("operator");
        var recordedAt = DateTimeOffset.UnixEpoch;
        var reducer = new TaskReducer();
        var state = reducer.Apply(null, Event(new TaskId("T1"), new TaskOpened("Task", "Goal"), actor, recordedAt));
        state = reducer.Apply(state, Event(new TaskId("T1"), new RoleAssigned(new RoleAssignment(
            actor, RoleKind.Operator, Enum.GetValues<Capability>(),
            new Provenance(actor, recordedAt, "task.open"))), actor, recordedAt));

        Assert.Equal(2, state.Version);
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

    [Fact]
    public void ReplayRejectsEventWithUnknownReference()
    {
        var taskId = new TaskId("T1");
        var operatorId = new ActorId("operator");
        var reducer = new TaskReducer();
        var state = Open(reducer, taskId, operatorId, DateTimeOffset.UnixEpoch);

        var exception = Assert.Throws<GovernanceException>(() => reducer.Apply(
            state,
            Event(taskId, new DecisionResolved(new DecisionId("missing"), DecisionStatus.Accepted), operatorId)));

        Assert.Contains("Unknown decision", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayRejectsIllegalStatusTransition()
    {
        var taskId = new TaskId("T1");
        var operatorId = new ActorId("operator");
        var reducer = new TaskReducer();
        var state = Open(reducer, taskId, operatorId, DateTimeOffset.UnixEpoch);
        state = reducer.Apply(state, Event(taskId, new DecisionProposed(new Decision(
            new DecisionId("D1"),
            "Decision",
            DecisionStatus.Proposed,
            "Rationale",
            [],
            null,
            new Provenance(operatorId, DateTimeOffset.UnixEpoch, "decision.propose"))), operatorId));
        state = reducer.Apply(
            state,
            Event(taskId, new DecisionResolved(new DecisionId("D1"), DecisionStatus.Accepted), operatorId));

        var exception = Assert.Throws<GovernanceException>(() => reducer.Apply(
            state,
            Event(taskId, new DecisionResolved(new DecisionId("D1"), DecisionStatus.Accepted), operatorId)));

        Assert.Contains("cannot transition", exception.Message, StringComparison.OrdinalIgnoreCase);
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
        ActorId? actorId = null,
        DateTimeOffset? recordedAt = null) => new(
        GovernedTaskState.CurrentSchemaVersion,
        new EventId(Guid.NewGuid().ToString("N")),
        taskId,
        actorId ?? new ActorId("actor"),
        recordedAt ?? DateTimeOffset.UnixEpoch,
        null,
        "test",
        data);
}
