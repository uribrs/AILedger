using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Core.ContextBriefing;
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
            Events = outcome.Events.Select(@event => @event.EventId),
            NextActionContract = command is RecordArtifactCommand or RequestStageTransitionCommand
                ? NextActionContracts.Observe(outcome.State, command.ActorId,
                    command is RecordArtifactCommand artifact ? artifact.ProducerRunId : null) : null
        }).ConfigureAwait(false);
    }

    public async Task WriteProviderReturnAsync(IGovernedTaskService service, TaskId task, ActorId actor,
        RunId run, AgentRunResult? result, bool ordinaryLaunch)
    {
        if (!ordinaryLaunch)
        {
            if (result is not null) await WriteJsonAsync(result).ConfigureAwait(false);
            return;
        }
        var contract = await NextActionsAsync(service, task, actor, run).ConfigureAwait(false);
        // Preserve the existing full JSON contract unless the caller explicitly requests a summary.
        var body = result is null ? new JsonObject() : JsonSerializer.SerializeToNode(result, json)!.AsObject();
        if (contract is not null) body["nextActionContract"] = JsonSerializer.SerializeToNode(contract, json);
        if (result is not null || contract is not null) await WriteJsonAsync(body).ConfigureAwait(false);
    }

    public async Task WriteProviderSummaryAsync(IGovernedTaskService service, TaskId task, ActorId actor,
        RunId run, object summary)
    {
        var body = JsonSerializer.SerializeToNode(summary, json)!.AsObject();
        body["nextActionContract"] = JsonSerializer.SerializeToNode(
            await NextActionsAsync(service, task, actor, run).ConfigureAwait(false), json);
        await WriteJsonAsync(body).ConfigureAwait(false);
    }

    private static async Task<NextActionContract?> NextActionsAsync(IGovernedTaskService service,
        TaskId task, ActorId actor, RunId run)
    {
        try
        {
            // The caller has already attempted durable termination. Cancellation must not erase
            // that observation, but this read has a small independent time budget.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var state = await service.GetStateAsync(task, timeout.Token).ConfigureAwait(false);
            return state is null ? NextActionContracts.Unavailable(0) : NextActionContracts.Observe(state, actor, run);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return NextActionContracts.Unavailable(0);
        }
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
