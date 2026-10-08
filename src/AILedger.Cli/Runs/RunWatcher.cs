using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Runs;

// Observes the same durable history as `history --follow`. It never closes or resumes a run.
internal sealed class RunWatcher(CliCommandExecutor output)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json =
        new(AILedger.Storage.LedgerJson.CreateOptions()) { WriteIndented = false };
    internal async Task WatchAsync(CliCommandInvocation invocation, CancellationToken token)
    {
        var task = Task(invocation.Input);
        var id = new RunId(invocation.Input.Required("run"));
        var cursor = ReadCursor(invocation.Input.Optional("since"));
        AgentRunStatus? previous = null;
        while (true)
        {
            var state = await CliCommandExecutor.RequireStateAsync(invocation, token).ConfigureAwait(false);
            if (!state.Runs.TryGetValue(id, out var run))
                throw new CliUsageException($"Run '{id}' is not recorded on task '{task}'. Watch after launch admission; use history --follow for task-wide activity.");
            if (cursor > state.Version)
                throw new CliUsageException("Since exceeds the current task version.");
            long version = 0;
            await foreach (var entry in invocation.Service.GetHistoryAsync(task, token).ConfigureAwait(false))
            {
                version++;
                // Do not mix a later history with this snapshot's terminal status.
                if (version > state.Version) break;
                if (version > cursor && BelongsTo(entry, run))
                    await WriteAsync(invocation, new("event", task.Value, id.Value, version,
                        entry.Data.GetType().Name, entry.EventId.Value, null)).ConfigureAwait(false);
            }
            cursor = state.Version;
            if (previous != run.Status)
                await WriteAsync(invocation, new("status", task.Value, id.Value, cursor,
                    null, null, run.Status)).ConfigureAwait(false);
            previous = run.Status;
            if (run.Status != AgentRunStatus.Active) return;
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(250), token).ConfigureAwait(false);
        }
    }

    private static bool BelongsTo(LedgerEvent entry, AgentRun run) => entry.Data switch
    {
        RunStarted started => started.Run.Id == run.Id,
        RunCompleted completed => completed.RunId == run.Id,
        _ => entry.ActorId == run.ActorId && entry.CorrelationId == run.Id.Value
    };

    private Task WriteAsync(CliCommandInvocation invocation, Observation row) => invocation.Input.Flag("json")
        ? output.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(row, Json))
        : output.WriteLineAsync($"{row.TaskId}/{row.RunId} v{row.Version}: " +
            (row.Kind == "status" ? $"ledger status {row.Status}" : $"{row.EventType} [{row.EventId}]"));

    private static long ReadCursor(string? value) => value is null ? 0 :
        long.TryParse(value, out var cursor) && cursor >= 0 ? cursor :
        throw new CliUsageException("Since must be a task version of zero or more.");

    private sealed record Observation(string Kind, string TaskId, string RunId, long Version,
        string? EventType, string? EventId, AgentRunStatus? Status);
}
