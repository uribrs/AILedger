using System.Text.Json;
using AILedger.Core.Contracts;

namespace AILedger.Cli.Routing;

internal sealed class CliCommandExecutor(TextWriter output, JsonSerializerOptions json)
{
    public async Task ExecuteAsync(
        CliCommandInvocation invocation,
        LedgerCommand command,
        CancellationToken cancellationToken)
    {
        var outcome = await invocation.Service.ExecuteAsync(
            CliInput.Task(invocation.Input), command, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(new
        {
            outcome.State.TaskId,
            outcome.State.Version,
            outcome.State.Stage,
            Events = outcome.Events.Select(@event => @event.EventId)
        }).ConfigureAwait(false);
    }

    public Task WriteJsonAsync<T>(T value) =>
        output.WriteLineAsync(JsonSerializer.Serialize(value, json));

    public Task WriteLineAsync(string value) => output.WriteLineAsync(value);

    public Task WriteAsync(string value) => output.WriteAsync(value);

    public static async Task<GovernedTaskState> RequireStateAsync(
        CliCommandInvocation invocation,
        CancellationToken cancellationToken) =>
        await invocation.Service.GetStateAsync(
            CliInput.Task(invocation.Input), cancellationToken).ConfigureAwait(false)
        ?? throw new CliUsageException($"Task '{CliInput.Task(invocation.Input)}' was not found.");
}
