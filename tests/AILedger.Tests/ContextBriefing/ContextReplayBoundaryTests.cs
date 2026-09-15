using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.ContextBriefing;

public sealed class ContextReplayBoundaryTests
{
    [Fact]
    public void ReplayAcceptsAHistoryThatAddsWorkWithNoContextBuiltEvent()
    {
        var task = new TestTask("legacy-task") { AutoBuildContext = false };
        Assert.Empty(task.State.ContextBuilds);

        var replayed = new TaskReducer().Apply(task.State, Event(
            task.TaskId, task.OperatorId, task.State.Version + 1, new WorkItemAdded(
                new WorkItem(
                    new WorkItemId("W1"), "Legacy work", task.OperatorId,
                    WorkItemStatus.Proposed, [], []))));

        Assert.Contains(new WorkItemId("W1"), replayed.WorkItems.Keys);
        Assert.Empty(replayed.ContextBuilds);
    }

    // R2: command-time policy may tighten, but replay must continue accepting older legal events.
    [Fact]
    public void TheCommandHandlerRefusesWhatReplayAccepts()
    {
        var task = new TestTask { AutoBuildContext = false };

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [])));

        Assert.Contains("has not built its context", refusal.Message, StringComparison.Ordinal);
    }

    private static LedgerEvent Event(
        TaskId taskId,
        ActorId actorId,
        long sequence,
        LedgerEventData data) =>
        new(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{taskId.Value}:{sequence:D10}"),
            taskId,
            actorId,
            new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero).AddMinutes(sequence),
            null,
            $"legacy-{sequence}",
            data);
}
