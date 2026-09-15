using AILedger.Core.Contracts;

namespace AILedger.Core.TaskOpening;

internal static class TaskOpeningStateProjector
{
    internal static GovernedTaskState Open(LedgerEvent @event, TaskOpened opened) =>
        new()
        {
            TaskId = @event.TaskId,
            Title = opened.Title,
            Goal = opened.Goal,
            Tags = opened.Tags,
            // The next RoleAssigned is the one exceptional role event: it bootstraps the operator
            // before normal role authority exists. RoleStateProjector clears this marker.
            PendingOpeningActor = @event.ActorId
        };
}
