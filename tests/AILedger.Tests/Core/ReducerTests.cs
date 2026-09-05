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
        var reducer = new TaskReducer();
        var state = reducer.Apply(null, Event(new TaskId("T1"), new TaskOpened("Task", "Goal")));
        state = reducer.Apply(state, Event(new TaskId("T1"), new RoleAssigned(new RoleAssignment(
            new ActorId("operator"), RoleKind.Operator, [Capability.ManageRoles],
            new Provenance(new ActorId("operator"), DateTimeOffset.UnixEpoch, null)))));

        Assert.Equal(2, state.Version);
    }

    private static LedgerEvent Event(TaskId taskId, LedgerEventData data) => new(
        GovernedTaskState.CurrentSchemaVersion,
        new EventId(Guid.NewGuid().ToString("N")),
        taskId,
        new ActorId("actor"),
        DateTimeOffset.UnixEpoch,
        null,
        "test",
        data);
}
