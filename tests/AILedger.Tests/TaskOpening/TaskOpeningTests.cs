using AILedger.Core.Application;
using AILedger.Core.Contracts;

namespace AILedger.Tests.TaskOpening;

public sealed class TaskOpeningTests
{
    // Roles/task-opening plan attention R4: command time grants the opening operator every
    // capability currently defined.
    [Fact]
    public void OpeningTaskEstablishesOperatorWithEveryCapabilityAndCausalEvents()
    {
        var handler = new CommandHandler();
        var actor = new ActorId("operator");
        var now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

        var outcome = handler.Handle(
            null,
            new OpenTaskCommand(actor, null, "open-1", new TaskId("T1"), " Title ", " Goal "),
            now);

        Assert.Equal(2, outcome.Events.Count);
        Assert.IsType<TaskOpened>(outcome.Events[0].Data);
        Assert.IsType<RoleAssigned>(outcome.Events[1].Data);
        Assert.Equal(outcome.Events[0].EventId, outcome.Events[1].CausationId);
        Assert.Equal("Title", outcome.State.Title);
        Assert.Equal("Goal", outcome.State.Goal);
        Assert.Equal(
            Enum.GetValues<Capability>().OrderBy(value => value),
            outcome.State.Roles[actor].Capabilities.OrderBy(value => value));
    }
}
