using System.Text.Json;
using AILedger.Cli.Routing;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Storage;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Retrospectives;

internal sealed class RetrospectiveCliCommands(
    CliCommandExecutor executor,
    TextReader processStandardInput,
    Func<string> harnessTranscriptRoot)
{
    public IEnumerable<CliCommandRegistration> Registrations()
    {
        yield return new CliCommandRegistration(
            ["retrospective build"],
            CliCommandOptions.Set(
                "root", "task", "actor", "coordinator-session", "coordinator-transcript"),
            isReadOnly: true,
            BuildAsync);
        yield return new CliCommandRegistration(
            ["retrospective record"],
            CliCommandOptions.Set("root", "task", "actor", "id", "title", "body-stdin"),
            isReadOnly: false,
            RecordAsync);
    }

    private async Task BuildAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var input = invocation.Input;
        var taskId = Task(input);
        var state = await CliCommandExecutor.RequireStateAsync(invocation, cancellationToken).ConfigureAwait(false);
        var history = new List<LedgerEvent>();
        await foreach (var @event in invocation.Service.GetHistoryAsync(taskId, cancellationToken).ConfigureAwait(false))
        {
            history.Add(@event);
        }

        var refusals = await ReadRefusalsAsync(invocation.LedgerRoot, taskId).ConfigureAwait(false);
        var coordinatorUsage = await CoordinatorUsageReader.ReadAsync(
            state, input.Optional("coordinator-session"), input.Optional("coordinator-transcript"),
            harnessTranscriptRoot(), cancellationToken).ConfigureAwait(false);
        await executor.WriteJsonAsync(TaskRetrospective.Build(state, history, refusals, coordinatorUsage))
            .ConfigureAwait(false);
    }

    private async Task RecordAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var input = invocation.Input;
        _ = Task(input);
        if (!input.Flag("body-stdin"))
        {
            throw new CliUsageException("Retrospective record requires '--body-stdin'.");
        }
        if (!Console.IsInputRedirected && ReferenceEquals(Console.In, processStandardInput))
        {
            throw new CliUsageException(
                "Retrospective record requires redirected standard input for '--body-stdin'.");
        }

        var body = await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (body.Length == 0)
        {
            throw new CliUsageException("Retrospective body cannot be empty.");
        }

        await executor.ExecuteAsync(
            invocation,
            new RecordArtifactCommand(
                Actor(input), Cause(input), Correlation(input), new ArtifactId(input.Required("id")),
                GovernedArtifactKind.WorkflowRetrospective, input.Required("title"), body,
                WorkItemId: null, ProducerRunId: null, SupersedesArtifactId: null),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<RetrospectiveRefusalJournal?> ReadRefusalsAsync(
        string ledgerRoot,
        TaskId taskId)
    {
        var path = RefusalJournal.ResolvePath(new TaskWorkspacePathResolver(ledgerRoot).Resolve(taskId));
        if (!File.Exists(path))
        {
            return null;
        }

        var options = LedgerJson.CreateOptions();
        var refusals = new List<RetrospectiveRefusal>();
        var unreadable = 0;
        foreach (var line in await File.ReadAllLinesAsync(path).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize<RefusalRecord>(line, options) is { } record)
                {
                    refusals.Add(new RetrospectiveRefusal(
                        record.ActorId, record.Command, record.Site, record.Message,
                        record.KernelIdentity));
                }
                else
                {
                    unreadable++;
                }
            }
            catch (JsonException)
            {
                unreadable++;
            }
        }

        return new RetrospectiveRefusalJournal(refusals, unreadable);
    }
}
