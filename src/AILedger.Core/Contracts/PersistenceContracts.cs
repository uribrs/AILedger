namespace AILedger.Core.Contracts;

public sealed record TaskWorkspaceLayout(
    string EventsFileName = "events.jsonl",
    string StateFileName = "state.json",
    string TaskProjectionFileName = "task.md",
    string AssumptionsProjectionFileName = "assumptions.md",
    string DecisionsProjectionFileName = "decisions.md",
    string LockFileName = ".writer.lock");

public interface IGovernedTaskService
{
    Task<CommandOutcome> ExecuteAsync(TaskId taskId, LedgerCommand command, CancellationToken cancellationToken);
    Task<GovernedTaskState?> GetStateAsync(TaskId taskId, CancellationToken cancellationToken);
    IAsyncEnumerable<LedgerEvent> GetHistoryAsync(TaskId taskId, CancellationToken cancellationToken);
}

public interface ITaskProjectionWriter
{
    Task WriteAsync(string taskDirectory, GovernedTaskState state, CancellationToken cancellationToken);
}
